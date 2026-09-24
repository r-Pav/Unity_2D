using System.Collections;
using Cinemachine;
using UnityEngine;

/// <summary>
/// [框架二状态位] 玩家全局战斗状态标识 attackingStat — 任意敌人对玩家有仇恨 = 战斗中。
/// 由 EnemyControllerBase.OnEnter/OnExitCombatState 上报(per-enemy guard 已防重),管道只订阅它。
/// 职责:维护 refCount + 翻转时切管道实心(AreaChannelTrigger.SetAllSolid) + 切战斗相机缩放 + 广播事件。
/// 不含玩家攻击路径 → 挥空不置位。
/// </summary>
public class AttackingStat : MonoBehaviour
{
    private static AttackingStat _instance;

    public static AttackingStat Instance
    {
        get
        {
            if (_instance == null)
                _instance = PlayerController.Instance?.GetComponent<AttackingStat>();
            return _instance;
        }
    }

    /// <summary>战斗状态变化事件(true=进入战斗,false=脱离)。UI/被动等预留,当前可无订阅</summary>
    public event System.Action<bool> OnCombatChanged;

    /// <summary>是否处于战斗中(任意敌人仇恨)</summary>
    public bool InCombat => _combatRefCount > 0;

    /// <summary>仇恨敌人计数:多个敌人同时战斗时,最后一个退出才清 false(对齐 passiveEquipManager refCount 思想)</summary>
    private int _combatRefCount;

    // ── 战斗相机缩放(2026-09-24 saika 定):进战斗拉远 / 脱战回平常 ──
    // 与「战斗 → 管道实心」同一处副作用,所以并进本状态位,不再单独挂组件。
    // 缩放只能改 VCam 的 Lens(Brain 每帧用 VCam 覆盖真实相机,直接改 Camera 无效)。

    [Header("战斗相机缩放")]
    [Tooltip("玩家相机:被拉远的那台 VCam(场景里驱动玩家的那台)")]
    [SerializeField] private CinemachineVirtualCamera playerVcam;

    [Tooltip("平常正交大小:脱战后恢复到的值(与 VCam 上的初始值一致,如 7)")]
    [SerializeField] private float normalOrthoSize = 7f;

    [Tooltip("战斗正交大小:进入战斗拉远到的值(如 7.5)")]
    [SerializeField] private float combatOrthoSize = 7.5f;

    [Tooltip("过渡时长(秒):0 = 立即切换")]
    [SerializeField] private float zoomDuration = 0.35f;

    private Coroutine _zoomRoutine;

    private void Start()
    {
        if (playerVcam == null)
        {
            Debug.LogWarning("[AttackingStat] 战斗相机没引用 VCam,战斗相机缩放不生效(不影响战斗状态位与管道实心)");
            return;
        }

        // 中途进场景(读档/传送)时可能已经在战斗中:只在这种情况立即对齐,平常不碰 VCam 现值
        if (InCombat) SetCombatZoom(true, immediate: true);
    }

    private void OnDestroy()
    {
        _zoomRoutine = null;   // 协程随物体销毁自动停;置空防销毁后再被引用
    }

    /// <summary>切战斗相机缩放:immediate = 不走过渡直接设值;新的一次切换会打断上一次没播完的过渡</summary>
    private void SetCombatZoom(bool inCombat, bool immediate, EnemyControllerBase source = null)
    {
        if (playerVcam == null) return;

        float target = inCombat ? combatOrthoSize : normalOrthoSize;
        float from = playerVcam.m_Lens.OrthographicSize;

        if (_zoomRoutine != null)
        {
            StopCoroutine(_zoomRoutine);
            _zoomRoutine = null;
        }

        if (immediate || zoomDuration <= 0f)
            playerVcam.m_Lens.OrthographicSize = target;
        else
            _zoomRoutine = StartCoroutine(ZoomRoutine(target));

        // [CombatDBG 钩子] 镜头距离真的变了才打一条:不每帧(过渡插值不打),不随仇恨上报刷屏
        if (!Mathf.Approximately(from, target))
            LogCombatState(inCombat, source);
    }

    /// <summary>过渡:从当前值 Lerp 到目标(Time.unscaledDeltaTime,不受卡帧 timeScale 影响)</summary>
    private IEnumerator ZoomRoutine(float target)
    {
        float from = playerVcam.m_Lens.OrthographicSize;
        float elapsed = 0f;
        while (elapsed < zoomDuration)
        {
            elapsed += Time.unscaledDeltaTime;
            playerVcam.m_Lens.OrthographicSize = Mathf.Lerp(from, target, Mathf.Clamp01(elapsed / zoomDuration));
            yield return null;
        }
        playerVcam.m_Lens.OrthographicSize = target;
        _zoomRoutine = null;
    }

    /// <summary>
    /// 敌人仇恨上报(true=该敌人进入战斗;false=该敌人脱战/死亡)。
    /// 管道实心只在 0→1 翻转时切,恢复只在 →0 时切。
    /// </summary>
    public void Notify(bool enterCombat, EnemyControllerBase source = null)
    {
        if (enterCombat)
        {
            _combatRefCount++;
            if (_combatRefCount == 1)
            {
                AreaChannelTrigger.SetAllSolid(true);   // 进入战斗:管道变空气墙(物理挡玩家+敌人)
                SetCombatZoom(true, immediate: false, source);  // 进入战斗:玩家相机拉远
                OnCombatChanged?.Invoke(true);
            }
        }
        else
        {
            _combatRefCount = Mathf.Max(0, _combatRefCount - 1);
            if (_combatRefCount == 0)
            {
                AreaChannelTrigger.SetAllSolid(false);  // 脱离战斗:管道恢复 trigger(可传送)
                SetCombatZoom(false, immediate: false, source); // 脱战:玩家相机拉回平常值
                OnCombatChanged?.Invoke(false);
            }
        }
    }

    /// <summary>
    /// [CombatDBG 2026-09-24] 钩子 = 镜头距离变化(SetCombatZoom 里 from != target 才调)。
    /// 内容只看战斗状态:仇恨来源/计数/该敌人 FSM 与可见性;不打相机值,不走每帧。排查完整块删。
    /// </summary>
    private void LogCombatState(bool inCombat, EnemyControllerBase source)
    {
        string src;
        if (source == null)
        {
            src = "来源=无(进场景对齐/非敌人触发)";
        }
        else
        {
            var pc = PlayerController.Instance;
            Vector3 ep = source.transform.position;
            float dx = pc != null ? pc.transform.position.x - ep.x : 0f;
            float dy = pc != null ? pc.transform.position.y - ep.y : 0f;
            src = $"来源={source.name} 敌人FSM={source.Fsm?.CurrentState?.GetType().Name ?? "-"}" +
                  $" isInCombat={source.IsInCombatState} canSee={source.CanSeePlayer()}" +
                  $" 距离dx={dx:F2} dy={dy:F2} 死亡={source.IsDead}";
        }
        Debug.Log($"[CombatDBG] 镜头{(inCombat ? "拉远(进战斗)" : "拉回(脱战)")} refCount={_combatRefCount} {src}");
    }
}
