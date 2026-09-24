// [废弃 2026-09-24] 战斗相机缩放已并进 AttackingStat.cs（与「战斗 → 管道实心」同一处副作用，零新增组件）。
// 保留原因：文件与 .meta 的 GUID 稳定，防外部引用断裂。
// 彻底删除前请先在编辑器里把场景中挂的该组件移除（选中 Player → 右键该组件 → 移除组件），
// 否则场景里会留下 Missing Script。

/* 旧实现（注释保留）：
using System.Collections;
using Cinemachine;
using UnityEngine;

/// <summary>
/// 战斗相机缩放(2026-09-24 saika 定):进入战斗把玩家相机拉远到战斗值,脱战回到平常值。
/// 数据源 = AttackingStat.OnCombatChanged(任意敌人对玩家有仇恨:refCount 0→1 进战斗,→0 脱战),
/// 不自建战斗判定 —— 与「战斗中管道变实心」「背刺不冲地图元素」同一口径,挥空不置位。
/// 缩放只能改 VCam 的 Lens(Brain 每帧用 VCam 覆盖真实相机,直接改 Camera 无效)。
/// 与管道拉近(AreaChannelTrigger)不并发:战斗中管道是实心 collider,玩家进不去管道。
/// 挂点 = 玩家根(AttackingStat 同物体);PlayerVCam 由 Inspector 拖引用,不 Find、不每帧轮询。
/// </summary>
public class CombatCameraZoom : MonoBehaviour
{
    [Tooltip("玩家相机:被拉远的那台 VCam(PlayerVCam)")]
    [SerializeField] private CinemachineVirtualCamera playerVcam;

    [Tooltip("平常正交大小:脱战后恢复到的值(与 VCam 上的初始值一致,如 7)")]
    [SerializeField] private float normalSize = 7f;

    [Tooltip("战斗正交大小:进入战斗拉远到的值(如 7.5)")]
    [SerializeField] private float combatSize = 7.5f;

    [Tooltip("过渡时长(秒):0 = 立即切换")]
    [SerializeField] private float transitionDuration = 0.35f;

    private AttackingStat _attackingStat;
    private Coroutine _zoomRoutine;
    private bool _subscribed;

    private void Start()
    {
        _attackingStat = GetComponent<AttackingStat>();
        if (_attackingStat == null)
        {
            Debug.LogWarning("[CombatCameraZoom] 本物体上没有 AttackingStat,战斗相机缩放不生效(请挂到玩家根)");
            return;
        }

        if (playerVcam == null)
        {
            Debug.LogWarning("[CombatCameraZoom] 玩家相机未引用,战斗相机缩放不生效:" +
                             "请把场景里驱动玩家的那台 VCam(优先级更高的那台)拖进「玩家相机」字段");
            return;
        }

        _attackingStat.OnCombatChanged += OnCombatChanged;
        _subscribed = true;

        // 中途进场景(读档/传送)时可能已经在战斗中:只在这种情况立即对齐;平常不碰 VCam 现值,
        // 避免把你在 Inspector 里调的相机值覆盖掉
        if (_attackingStat.InCombat) Apply(true, true);
    }

    private void OnDestroy()
    {
        if (_subscribed && _attackingStat != null)
            _attackingStat.OnCombatChanged -= OnCombatChanged;
    }

    private void OnCombatChanged(bool inCombat) => Apply(inCombat, false);

    /// <summary>切换缩放:immediate = 不走过渡直接设值;新的一次切换会打断上一次还没播完的过渡</summary>
    private void Apply(bool inCombat, bool immediate)
    {
        if (playerVcam == null) return;

        float target = inCombat ? combatSize : normalSize;

        if (_zoomRoutine != null)
        {
            StopCoroutine(_zoomRoutine);
            _zoomRoutine = null;
        }

        if (immediate || transitionDuration <= 0f)
        {
            playerVcam.m_Lens.OrthographicSize = target;
            return;
        }
        _zoomRoutine = StartCoroutine(ZoomRoutine(target));
    }

    /// <summary>过渡:从当前值 Lerp 到目标(Time.unscaledDeltaTime,不受卡帧 timeScale 影响)</summary>
    private IEnumerator ZoomRoutine(float target)
    {
        float from = playerVcam.m_Lens.OrthographicSize;
        float elapsed = 0f;
        while (elapsed < transitionDuration)
        {
            elapsed += Time.unscaledDeltaTime;
            playerVcam.m_Lens.OrthographicSize = Mathf.Lerp(from, target, Mathf.Clamp01(elapsed / transitionDuration));
            yield return null;
        }
        playerVcam.m_Lens.OrthographicSize = target;
        _zoomRoutine = null;
    }
}
*/
