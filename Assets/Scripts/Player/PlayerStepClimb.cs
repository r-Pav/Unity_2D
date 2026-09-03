using UnityEngine;

/// <summary>
/// 矮台阶自动翻越（纯增量旁路组件，挂 Player，自驱动）。
/// 解决矮楼梯卡住玩家移动的问题：玩家面朝楼梯按住方向键时，前方是"矮台阶"
/// （低射线命中 climbLayers 且高射线未命中）→ 自动短促上跳，翻上台阶；
/// 连续台阶落地后继续检测下一级。不动 PlayerController / FSM / PlayerJump。
///
/// 射线约定（相对玩家本地，pivot 在脚底）：
///   lowerRayOrigin = 脚踝附近，命中 = 前方有台阶立面
///   upperRayOrigin = 头顶附近，命中 = 前方是高墙（超过可翻高度）→ 不触发
///
/// 防抖：起跳→落地高度差 < minStepHeight（原地小跳没上台阶）→ antiJitterWindow 秒内不触发；
///       高度差 >= minStepHeight（真上了台阶）→ 立即可扫下一级。
/// 冷却：距上次翻越 < detectCooldown 不触发（落地前不重复扫）。
/// </summary>
public class PlayerStepClimb : MonoBehaviour
{
    // ============================================================
    // 检测射线
    // ============================================================

    [Header("检测射线")]
    [Tooltip("低射线原点（相对玩家本地坐标；x 自动乘 facing 朝前）")]
    [SerializeField] private Vector2 lowerRayOrigin = new Vector2(0.3f, 0.2f);

    [Tooltip("高射线原点（相对玩家本地坐标；高于此高度的台阶视为高墙不翻）")]
    [SerializeField] private Vector2 upperRayOrigin = new Vector2(0.3f, 1.2f);

    [Tooltip("水平射线长度")]
    [SerializeField] private float rayLength = 0.6f;

    [Tooltip("可攀爬层（Ground 等实体台阶层）")]
    [SerializeField] private LayerMask climbLayers = 1 << 3;   // 默认 Ground 层(3)，Inspector 可改

    // ============================================================
    // 翻越动作
    // ============================================================

    [Header("翻越动作")]
    [Tooltip("短促上跳初速（只给 y，不清 x；水平速度保持现有输入驱动）")]
    [SerializeField] private float stepJumpVelocity = 4f;

    [Tooltip("起跳后冷却秒数（落地前不重复扫）")]
    [SerializeField] private float detectCooldown = 0.15f;

    // ============================================================
    // 同台阶防抖
    // ============================================================

    [Header("同台阶防抖")]
    [Tooltip("起跳→落地高度差低于此值 = 没真上台阶（原地小跳），进入防抖")]
    [SerializeField] private float minStepHeight = 0.3f;

    [Tooltip("同台阶防抖窗口秒数（防同一级原地反复跳）")]
    [SerializeField] private float antiJitterWindow = 0.5f;

    // ============================================================
    // 内部常量（非序列化，仅兜底防卡死）
    // ============================================================

    /// <summary>翻越中超过该时长仍未落地视为异常（被撞/被打断），强制复位防永久锁</summary>
    private const float MaxClimbDuration = 1.0f;

    /// <summary>触发后最早允许结算的间隔（给物理一帧反应时间，防触发当帧误结算）</summary>
    private const float MinSettleInterval = 0.08f;

    // ============================================================
    // 组件引用与运行时状态
    // ============================================================

    private PlayerController _pc;
    private Rigidbody2D _rb;

    /// <summary>单元素命中缓冲复用（带 filter 的 Raycast 重载需要结果数组，防每帧 GC）</summary>
    private readonly RaycastHit2D[] _raycastHits = new RaycastHit2D[1];

    /// <summary>翻越中：触发后置 true，落地结算后复位；期间不检测</summary>
    private bool _climbing;

    /// <summary>本次起跳时的 y（防抖高度差基准；用物理体位置防插值抖动）</summary>
    private float _takeoffY;

    /// <summary>上次触发时刻（冷却）</summary>
    private float _lastTriggerTime = float.NegativeInfinity;

    /// <summary>同台阶防抖锁到期时刻（Time.time）</summary>
    private float _jitterLockUntil = float.NegativeInfinity;

    /// <summary>本次翻越开始时刻(超时兜底)</summary>
    private float _climbStartTime;

    private void Awake()
    {
        _pc = GetComponent<PlayerController>();
        _rb = GetComponent<Rigidbody2D>();
    }

    private void LateUpdate()
    {
        if (_pc == null || _rb == null) return;

        // 翻越中：等落地结算；异常超时强制复位
        if (_climbing)
        {
            TickClimbingSettle();
            return;
        }

        // 门控：冷却 + 防抖锁
        if (Time.time - _lastTriggerTime < detectCooldown) return;
        if (Time.time < _jitterLockUntil) return;

        // 门控：grounded + 水平输入朝前 + 未被锁定
        if (!_pc.IsGrounded()) return;
        float h = Input.GetAxisRaw("Horizontal");
        if (Mathf.Abs(h) <= 0.1f) return;
        int facing = _pc.GetFacing();
        if (facing == 0 || Mathf.Sign(h) != facing) return;
        if (IsLocked()) return;

        // 双水平射线判定矮台阶：低命中 && 高未命中
        if (!IsShortStepAhead(facing)) return;

        // 触发：只给 y 速度，不清 x（水平速度保持输入驱动）
        Vector2 vel = _rb.velocity;
        vel.y = stepJumpVelocity;
        _rb.velocity = vel;

        _takeoffY = _rb.position.y;
        _lastTriggerTime = Time.time;
        _climbStartTime = Time.time;
        _climbing = true;
    }

    /// <summary>
    /// 翻越中逐帧：等到落地（grounded 恢复且垂直速度已停止上升）结算高度差；
    /// 超过 MaxClimbDuration 仍未落地 → 按未成功处理进入防抖，防永久锁死。
    /// </summary>
    private void TickClimbingSettle()
    {
        // 异常超时：无论当前是否落地都强制结算（按失败走防抖）
        if (Time.time - _climbStartTime > MaxClimbDuration)
        {
            _climbing = false;
            _jitterLockUntil = Time.time + antiJitterWindow;
            return;
        }

        // 需给物理一点反应时间，防止触发当帧（速度刚给上、仍贴地）误结算
        if (Time.time - _lastTriggerTime < MinSettleInterval) return;
        // 落地判定：地面检测恢复 且 不再上升（被地面支撑/下落接触）
        if (!_pc.IsGrounded()) return;
        if (_rb.velocity.y > 0.01f) return;

        _climbing = false;

        // 高度差 = 当前 y - 起跳 y
        float heightDiff = _rb.position.y - _takeoffY;
        if (heightDiff < minStepHeight)
        {
            // 原地小跳没上台阶（同层级反复蹭）：锁 antiJitterWindow，防抖
            _jitterLockUntil = Time.time + antiJitterWindow;
        }
        // 高度差 >= minStepHeight（真上了台阶）：不设锁 → 冷却通过后即可扫下一级
    }

    /// <summary>复制 PlayerController.IsActionLocked 的公开等价判定：输入禁用 / 冻结 / FSM 锁定态</summary>
    private bool IsLocked()
    {
        if (_pc == null) return true;
        if (!_pc.InputEnabled) return true;
        if (_pc.FreezeTimer > 0f) return true;
        if (_pc.PlayerFsm != null
            && _pc.PlayerFsm.CurrentState is EntityState es
            && es.LocksInput)
            return true;
        return false;
    }

    /// <summary>
    /// 矮台阶判定：低射线命中 climbLayers（前方有台阶立面）且高射线未命中（台阶不高，可翻）。
    /// 检测 mask 排除玩家自身层（防射线起点在自身碰撞体内误命中）。
    /// </summary>
    private bool IsShortStepAhead(int dir)
    {
        Vector2 lowOrigin = (Vector2)transform.position
            + new Vector2(lowerRayOrigin.x * dir, lowerRayOrigin.y);
        Vector2 highOrigin = (Vector2)transform.position
            + new Vector2(upperRayOrigin.x * dir, upperRayOrigin.y);
        Vector2 rayDir = Vector2.right * dir;

        bool lowHit = RayHitLayer(lowOrigin, rayDir);
        bool highHit = RayHitLayer(highOrigin, rayDir);
        return lowHit && !highHit;
    }

    /// <summary>水平射线只命中实体(忽略 trigger,防管道等 trigger 误判为台阶)</summary>
    private bool RayHitLayer(Vector2 origin, Vector2 dir)
    {
        // 排除自身层：层配置为 Everything(~0) 时也不会把玩家自己当台阶
        LayerMask mask = climbLayers & ~(1 << gameObject.layer);
        if (mask.value == 0) return false;

        var filter = new ContactFilter2D { useTriggers = false, layerMask = mask };
        if (Physics2D.Raycast(origin, dir, filter, _raycastHits, rayLength) <= 0) return false;
        var hit = _raycastHits[0];
        if (hit.collider == null) return false;

        // 命中自己(玩家根或其子物体,如武器/特效挂点带 collider)不算台阶。
        // 团结引擎 m_QueriesStartInColliders=0 时射线会命中起点所在的自身 collider,
        // 冲刺高速帧低射线起点可能落入自己 Capsule → 误判台阶 → 平地小跳(2026-09-03 日志实测)
        if (hit.collider.transform.root == transform.root) return false;

        // 命中活体角色(EnemyControllerBase 及其子物体 collider)不算台阶:敌人是移动单位
        // (可被击飞/会走动),不是静态可攀爬立面;player 从 enemy 身边走过时低射线扫到其
        // collider、高射线在其头顶之上 → 误判"台阶立面"→ 平地小跳(2026-09-03 用户复现,与自身 collider 同族)
        if (hit.collider.GetComponentInParent<EnemyControllerBase>() != null) return false;

        return true;
    }

    // ============================================================
    // Gizmos（选中玩家时可视化两条射线，便于 Inspector 调参）
    // ============================================================

#if UNITY_EDITOR
    private void OnDrawGizmosSelected()
    {
        if (_pc == null) _pc = GetComponent<PlayerController>();
        int dir = _pc != null ? _pc.GetFacing() : 1;
        if (dir == 0) dir = 1;

        Vector2 lowOrigin = (Vector2)transform.position
            + new Vector2(lowerRayOrigin.x * dir, lowerRayOrigin.y);
        Vector2 highOrigin = (Vector2)transform.position
            + new Vector2(upperRayOrigin.x * dir, upperRayOrigin.y);

        Gizmos.color = Color.green;
        Gizmos.DrawRay(lowOrigin, Vector2.right * dir * rayLength);
        Gizmos.color = Color.yellow;
        Gizmos.DrawRay(highOrigin, Vector2.right * dir * rayLength);
    }
#endif
}
