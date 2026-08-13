using Cinemachine;
using System.Collections;
using UnityEngine;

/// <summary>
/// 管道触发器 — 挂管道两端各一个实例，单向配置：
/// 玩家从 sourceArea 侧进入本 trigger → 锁输入 → 镜头拉近 → 自动移动到对侧 trigger 的射线终点（管道外）
/// → 显示 targetArea、隐藏 sourceArea → 恢复 ortho + 输入。
/// 方向推导：本 trigger 的射线朝管道外（远离对侧 trigger），对侧 trigger 的射线终点 = 移动终点，
/// 不靠 ±1/手填坐标。未拖对侧 trigger 时降级：不移动，直接切地区 + 恢复输入（空引用安全）。
/// 不触发自动存档。
/// </summary>
public class AreaChannelTrigger : MonoBehaviour
{
    [Header("区域")]
    [Tooltip("来源区域:玩家进入本 trigger 前所在的地区(到达对侧后隐藏)")]
    [SerializeField] private GameObject sourceArea;

    [Tooltip("目标区域:玩家穿过管道后进入的地区(到达对侧时显示)")]
    [SerializeField] private GameObject targetArea;

    [Header("管道")]
    [Tooltip("对侧 trigger:管道另一端的 AreaChannelTrigger(移动终点 = 它的射线终点)")]
    [SerializeField] private AreaChannelTrigger otherTrigger;

    [Tooltip("管道内自动移动速度(默认 6:PlayerAnimation 的 runSpeedThreshold=5,若用 5 会卡在 Walk/Run 分档边界上导致动画闪烁)")]
    [SerializeField] private float channelMoveSpeed = 6f;

    [Tooltip("射线长度(编辑器可调):从本 trigger 中心朝管道外延伸,射线终点 = 出口外位置")]
    [SerializeField] private float channelLength = 5f;

    [Header("相机缩放")]
    [Tooltip("管道内拉近的 orthoSize(管道外正常 4)")]
    [SerializeField] private float orthoZoomIn = 3f;

    [Tooltip("缩放速度")]
    [SerializeField] private float zoomSpeed = 3f;

    /// <summary>管道外正常 orthoSize（相机简化方案固定 4）</summary>
    private const float DefaultOrthoSize = 4f;

    /// <summary>自动移动中标志（防重入：移动期间重复 OnTriggerEnter 忽略）</summary>
    private bool _isMoving;

    /// <summary>管道移动协程句柄（VCam 上运行；读档取消用）</summary>
    private Coroutine _moveRoutine;

    /// <summary>管道移动中的玩家引用（读档取消时恢复输入用）</summary>
    private PlayerController _movingPlayer;

    /// <summary>
    /// 管道移动中的存档位置（= 移动终点，对侧出口外）：管道内存档时 SaveSystem 优先用此位置，
    /// 而不是管道内当前位置。移动开始设置、结束清空（null = 无管道移动）。
    /// </summary>
    private static Vector3? _pendingSavePosition;

    /// <summary>管道移动中的存档位置（null = 不在管道移动中）；SaveSystem 存档时读取</summary>
    public static Vector3? PendingSavePosition => _pendingSavePosition;

    /// <summary>
    /// 取消管道移动（SaveSystem 读档时调用）：停协程 + 清存档位置 + 恢复玩家输入/速度。
    /// 管道内读档时移动协程仍在 VCam 上跑（菜单暂停只是 timeScale=0 空转），
    /// 读档恢复位置后协程继续推玩家 → 玩家自动 walk 被接管。读档必须先取消。
    /// </summary>
    public static void CancelMove()
    {
        foreach (var t in FindObjectsOfType<AreaChannelTrigger>())
            t.CancelMoveInternal();
    }

    private void CancelMoveInternal()
    {
        if (_moveRoutine != null)
        {
            if (_vcam != null) _vcam.StopCoroutine(_moveRoutine);
            _moveRoutine = null;
        }
        _pendingSavePosition = null;
        _isMoving = false;
        if (_movingPlayer != null)
        {
            _movingPlayer.SetMoveSpeedOverride(null);
            _movingPlayer.InputEnabled = true;
            _movingPlayer.SetVelocityPublic(x: 0f);
            _movingPlayer = null;
        }
    }

    /// <summary>场景唯一 VCam（懒查找缓存；相机简化后全场景仅一个 Virtual Camera）</summary>
    private CinemachineVirtualCamera _vcam;

    /// <summary>缩放协程句柄（新缩放打断旧的，避免并发竞争）</summary>
    private Coroutine _zoomRoutine;

    /// <summary>本 trigger 的射线终点（朝管道外）：中心 + 方向 × channelLength。对侧 trigger 用它作移动终点。</summary>
    public Vector3 ExitPoint
    {
        get
        {
            var col = GetComponent<Collider2D>();
            if (col == null) return transform.position;
            return (Vector2)col.bounds.center + RayDirection() * channelLength;
        }
    }

    /// <summary>射线方向 = 朝管道外 = 远离对侧 trigger；无对侧引用时默认水平朝右</summary>
    private Vector2 RayDirection()
    {
        if (otherTrigger != null)
            return new Vector2(Mathf.Sign(transform.position.x - otherTrigger.transform.position.x), 0f);
        return Vector2.right;
    }

    private void OnTriggerEnter2D(Collider2D other)
    {
        if (!other.CompareTag("Player")) return;

        var player = other.GetComponentInParent<PlayerController>();
        if (player == null) return;

        // 防重入(跨实例关键):玩家输入被锁 = 正在管道自动移动中,
        // 此时玩家会路过对侧 trigger(Entry→Exit),若不检查这里 Exit 也会启动自己的协程
        // 把玩家往回拉 → 两个协程抢 velocity → 落点错乱。锁输入状态 = 移动接管中,一律忽略。
        if (!player.InputEnabled) return;
        if (_isMoving) return; // 本实例防重入

        // 主协程挂 VCam(场景常驻)而非本 trigger——trigger 挂在地区下,HideArea 时随地区 SetActive(false),
        // 挂在本体的协程会被杀,导致 InputEnabled 永远不恢复(玩家卡死)。
        if (_vcam == null)
            _vcam = FindObjectOfType<CinemachineVirtualCamera>();
        if (_vcam == null) return;
        _movingPlayer = player; // 记录玩家引用(读档取消时恢复输入用)
        _moveRoutine = _vcam.StartCoroutine(AutoMoveChannel(player));
    }

    /// <summary>
    /// 管道自动移动接管流程:停 FSM+锁输入 → 镜头拉近 → SetVelocityPublic 物理驱动到对侧 trigger 的射线终点
    /// → 显示 targetArea / 隐藏 sourceArea → 恢复 ortho + 输入。
    /// 移动终点 = otherTrigger.ExitPoint（对侧射线终点 = 管道外）；未拖对侧 trigger 时降级(不移动,直接切地区+恢复)。
    /// </summary>
    private IEnumerator AutoMoveChannel(PlayerController player)
    {
        _isMoving = true;

        // 1. 禁用输入(ESC 除外——ESC 由 PanelManager 独立监听,不受 InputEnabled 影响):
        //    PlayerController.OnUpdate 第一行 `if (!InputEnabled) return;` 直接短路,
        //    FSM 不再跑 → 不再写 velocity,协程独享控制权。无需 player.enabled=false。
        player.InputEnabled = false;

        // 1a. 强制归位玩家状态:跳跃/攻击/受击等非 walk 状态进管道时,
        //     InputEnabled=false 只短路 Update,FSM 状态与 Animator 参数残留
        //     (卡在跳跃帧滑行过管道)。必须主动复位:
        //     - FSM 切回 Idle(ChangeState 立即执行,不依赖被冻结的 Update)
        //     - 清全部动画 Bool(IsJumping/IsFalling/IsAttacking/IsAirAttacking/IsHurt/IsAirHurt/IsDashing)
        //     - anim.Play 强制直切,绕过渡竞争(坑39:代码切状态时动画过渡竞争)
        //     - 恢复被空中攻击改过的 gravityScale
        var anim = player.Animator;
        if (anim != null)
        {
            anim.SetBool(AnimParams.IsJumping, false);
            anim.SetBool(AnimParams.IsFalling, false);
            anim.SetBool(AnimParams.IsAttacking, false);
            anim.SetBool(AnimParams.IsAirAttacking, false);
            anim.SetBool(AnimParams.IsHurt, false);
            anim.SetBool(AnimParams.IsAirHurt, false);
            anim.SetBool(AnimParams.IsDashing, false);
            anim.Play("Idle", 0, 0f); // 强制直切待机,绕过渡竞争
        }
        if (player.PlayerFsm != null && player.IdleState != null)
            player.PlayerFsm.ChangeState(player.IdleState);
        // 恢复重力:正常重力恒为 1(空中攻击瞬改 0.3 后自行恢复,贴墙状态无重力残留),
        // 直接设 1 兜底,防进管道瞬间被残留的低重力带飞
        if (player.Rb != null) player.Rb.gravityScale = 1f;
        // 重置跳跃次数:强制 ChangeState(Idle) 绕过了跳跃状态的落地分支,
        // jumpsLeft 残留为 0 → 出管道后跳不了。必须手动补 ResetJumps(落地副作用)
        var jumpComp = player.GetComponent<PlayerJump>();
        if (jumpComp != null) jumpComp.ResetJumps();

        // 1b. 降低速度到 4:SetMoveSpeedOverride 限速(玩家自身移动逻辑读 MoveSpeed 时生效,
        //     兜底防任何路径用原速);协程驱动也用同一速度
        player.SetMoveSpeedOverride(channelMoveSpeed);

        // 1c. 进管道立即加载对侧地区(ShowArea 提前):对侧 trigger 也随地区激活,
        //     玩家到达后原场景 HideArea 时,对侧 trigger 始终是活的——回来能再次触发。
        //     (若到达后才 ShowArea,原场景先关 → 原侧 trigger 失效 → 回程碰不到,场景不加载)
        var zm = ZoneManager.Instance;
        zm?.ShowArea(targetArea);
        // 2. 移动终点 = 对侧 trigger 的射线终点(管道外);空引用时降级(不移动,直接切地区+恢复)
        if (otherTrigger != null)
        {
            // 镜头拉近:orthoSize → orthoZoomIn(协程 Lerp,Time.unscaledDeltaTime)
            StartZoom(orthoZoomIn);

            Vector2 target = otherTrigger.ExitPoint;
            // 管道移动中:记录存档位置 = 移动终点(对侧出口外)——管道内存档不存管道内当前位置
            _pendingSavePosition = target;
            float moveSpeed = Mathf.Max(0.01f, channelMoveSpeed); // 防御:速度<=0 会死循环
            // 方向固定算一次:进入循环前确定朝哪走,循环内不再 Sign 翻转(接近目标时 x 微过冲会导致
            // Sign 正负交替 → 速度抖动 + walk/run 动画乱切)
            float dir = Mathf.Sign(target.x - player.transform.position.x);
            // 朝向同步:玩家面朝可能与移动方向相反(面朝右进右侧管道 = 往左走),
            // 不翻转朝向会倒着走。UpdateFacing 改 transform.localScale.x,子物体(剑)自动跟随
            player.UpdateFacing(dir);
            // 只比 x 轴:水平移动,忽略 y 波动(重力落地/弹跳)导致的 2D 距离抖动
            // 超时兜底:物理阻挡(目标在墙内/被 collider 卡住)时 5 秒强制结束,防死循环
            float elapsed = 0f;
            const float MaxMoveTime = 5f;
            while (Mathf.Abs(target.x - player.transform.position.x) > 0.1f && elapsed < MaxMoveTime)
            {
                elapsed += Time.deltaTime;
                // 每帧强制锁输入:PanelManager._ApplyInteractionState 在面板开/关时会重设
                // _player.InputEnabled = !shouldLockInput——管道内 ESC 开菜单再关闭后,
                // 栈空 → shouldLockInput=false → InputEnabled 被改回 true → 玩家输入与协程
                // 抢 velocity → 乱闪卡管道。这里每帧锁回,PanelManager 改多少次都无效。
                player.InputEnabled = false;
                // 物理驱动(SetVelocityPublic 写 rb.velocity,保持角色动画/物理一致);
                // 只覆盖 x,朝固定方向移动;y 交给物理(重力落地),每帧覆盖防玩家位移干扰
                player.SetVelocityPublic(x: dir * moveSpeed);
                yield return null;
            }
            player.SetVelocityPublic(x: 0f); // 到点停(或超时停)
            _pendingSavePosition = null; // 移动结束清空
        }

        // 3. 到达后关闭来源地区(原场景)
        zm?.HideArea(sourceArea);

        // 4. 恢复 orthoSize 4 + 恢复速度/输入
        StartZoom(DefaultOrthoSize);
        player.SetMoveSpeedOverride(null); // 恢复原速
        player.InputEnabled = true;
        _isMoving = false;
        _moveRoutine = null;
        if (_movingPlayer == player) _movingPlayer = null;
    }

    /// <summary>平滑缩放 VCam orthoSize(新缩放打断旧的,避免并发竞争)。
    /// 协程挂 VCam 而非本 trigger——trigger 随地区 HideArea 一起 SetActive(false) 时，
    /// 挂在本体的协程无法再启动(恢复缩放发生在切地区之后),VCam 常驻不受影响。</summary>
    private void StartZoom(float target)
    {
        if (_vcam == null)
            _vcam = FindObjectOfType<CinemachineVirtualCamera>();
        if (_vcam == null) return;

        if (_zoomRoutine != null) StopCoroutine(_zoomRoutine);
        _zoomRoutine = _vcam.StartCoroutine(ZoomRoutine(target));
    }

    private IEnumerator ZoomRoutine(float target)
    {
        float from = _vcam.m_Lens.OrthographicSize;
        float duration = Mathf.Abs(target - from) / Mathf.Max(0.05f, zoomSpeed);
        float elapsed = 0f;

        while (elapsed < duration)
        {
            elapsed += Time.unscaledDeltaTime; // 暂停(timeScale=0)时也能播完
            _vcam.m_Lens.OrthographicSize = Mathf.Lerp(from, target, Mathf.Clamp01(elapsed / duration));
            yield return null;
        }
        _vcam.m_Lens.OrthographicSize = target;
    }

    // ============================================================
    // Gizmos(编辑器可视化)
    // ============================================================

#if UNITY_EDITOR
    private void OnDrawGizmos()
    {
        var col = GetComponent<Collider2D>();
        if (col == null) return;

        // 触发器范围
        Gizmos.color = new Color(0.2f, 0.8f, 0.2f, 0.25f);
        Gizmos.DrawCube(col.bounds.center, col.bounds.size);
        Gizmos.color = new Color(0.2f, 0.8f, 0.2f, 0.6f);
        Gizmos.DrawWireCube(col.bounds.center, col.bounds.size);

        // 移动射线:从本 trigger 中心朝管道外(远离对侧 trigger),长度 channelLength;
        // 射线终点 = 本 trigger 的 ExitPoint = 玩家从对侧进入时的自动移动落点
        Vector2 dir = RayDirection();
        Gizmos.color = Color.yellow;
        Gizmos.DrawRay(col.bounds.center, dir * channelLength);

        // 射线终点标记(落点)
        Vector2 tip = (Vector2)col.bounds.center + dir * channelLength;
        Gizmos.color = Color.cyan;
        Gizmos.DrawWireSphere(tip, 0.3f);
    }
#endif
}
