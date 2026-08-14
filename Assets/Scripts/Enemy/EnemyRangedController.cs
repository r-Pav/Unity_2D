using UnityEngine;

/// <summary>
/// 远程敌人控制器 — 继承 EnemyControllerBase，5 状态 FSM（Idle → Patrol → Rush → Attack 判框双攻击）。
/// 巡逻(patrolRange 来回) + 加速移动(Rush, SetMoveSpeedOverride) + 判框攻击：
///   attack1 近战（attackWidth/Height 框，EnemyMeleeAttack）
///   attack2 远程（rangedAttackWidth/Height 框，EnemyRangedAttack 蓄力+发射）
/// 不再使用后退/追击逻辑（retreat 矩形已删除）。
/// </summary>
public class EnemyRangedController : EnemyControllerBase
{
    // ============================================================
    // 配置参数
    // ============================================================

    [Header("巡逻")]
    [Tooltip("巡逻范围（左右各多少单位；0 = 未设置，用 SO 对应 Lv 档 / 内置 3f 兜底）")]
    [SerializeField] private float patrolRange = 0f;
    public float PatrolRange => patrolRange;

    /// <summary>巡逻范围内置默认（Inspector 与 SO 均未设置时兜底）</summary>
    private const float DefaultPatrolRange = 3f;

    [Header("远程攻击框 — 矩形（attack2；attack1 近战框用基类 attackWidth/Height）")]
    [Tooltip("远程攻击矩形半宽（X 轴；0 = 未设置，用 SO 对应 Lv 档 / 内置 8f 兜底）")]
    [SerializeField] private float rangedAttackWidth = 0f;
    [Tooltip("远程攻击矩形半高（Y 轴；0 = 未设置，用 SO 对应 Lv 档 / 内置 5f 兜底）")]
    [SerializeField] private float rangedAttackHeight = 0f;

    /// <summary>远程框内置默认（Inspector 与 SO 均未设置时兜底）</summary>
    private const float DefaultRangedAttackWidth = 8f;
    private const float DefaultRangedAttackHeight = 5f;

    /// <summary>暴露远程攻击矩形半宽给状态类（RangedAttackState 判框 attack2）</summary>
    public float RangedAttackWidth => rangedAttackWidth;
    /// <summary>暴露远程攻击矩形半高给状态类（RangedAttackState 判框 attack2）</summary>
    public float RangedAttackHeight => rangedAttackHeight;

    // ============================================================
    // 抽象方法实现
    // ============================================================

    protected override IState GetInitialState() => new RangedIdleState(this, Fsm, Animator);
    /// <summary>攻击入口 — 返回 RangedAttackState（OnEnter 判框：近战框→attack1 / 远程框→attack2 / 框外→Rush）</summary>
    public override IState CreateChaseState() => new RangedAttackState(this, Fsm, Animator);
    /// <summary>晕眩/丢失仇恨后的后备状态 — 回巡逻</summary>
    public override IState CreateFallbackState() => new RangedPatrolState(this, Fsm, Animator);

    // ============================================================
    // 生命周期
    // ============================================================

    protected new void Start()
    {
        // [Lv 收敛] 取值链：Inspector(>0) → SO 对应 Lv 档(>0) → 内置默认（0 = 未设置）
        patrolRange = Resolve(patrolRange, LvStats?.patrolRange ?? 0f, DefaultPatrolRange);
        rangedAttackWidth = Resolve(rangedAttackWidth, LvStats?.rangedAttackWidth ?? 0f, DefaultRangedAttackWidth);
        rangedAttackHeight = Resolve(rangedAttackHeight, LvStats?.rangedAttackHeight ?? 0f, DefaultRangedAttackHeight);

        stunState = new EnemyStunState(this, Fsm);
        SetStunState(stunState);
        base.Start();
    }

    // ============================================================
    // 辅助：玩家位置判定（供状态类判框）
    // ============================================================

    /// <summary>玩家是否在远程攻击矩形内（attack2 远程框；与近战 attackWidth/Height 不混淆）</summary>
    public bool InRangedRect()
    {
        if (player == null) return false;
        float dx = Mathf.Abs(player.position.x - transform.position.x);
        float dy = Mathf.Abs(player.position.y - transform.position.y);
        return dx <= rangedAttackWidth * 0.5f && dy <= rangedAttackHeight * 0.5f;
    }

    /// <summary>玩家是否在任一攻击框内（近战 attackWidth/Height 或远程 rangedAttackWidth/Height）</summary>
    public bool PlayerInAnyAttackRect() => PlayerInAttackRange() || InRangedRect();

    // ============================================================
    // Gizmos
    // ============================================================

#if UNITY_EDITOR
    protected override void OnDrawGizmosSelected()
    {
        base.OnDrawGizmosSelected();

        // 远程攻击矩形（青色线框）— 与基类近战矩形（红）区分
        Vector3 pos = transform.position;
        float hw = rangedAttackWidth * 0.5f;
        float hh = rangedAttackHeight * 0.5f;
        DrawWireRect(pos, hw, hh, new Color(0f, 1f, 1f, 0.4f));
    }

    private static void DrawWireRect(Vector3 center, float halfW, float halfH, Color color)
    {
        Gizmos.color = color;
        Vector3 tl = center + new Vector3(-halfW,  halfH, 0f);
        Vector3 tr = center + new Vector3( halfW,  halfH, 0f);
        Vector3 br = center + new Vector3( halfW, -halfH, 0f);
        Vector3 bl = center + new Vector3(-halfW, -halfH, 0f);
        Gizmos.DrawLine(tl, tr);
        Gizmos.DrawLine(tr, br);
        Gizmos.DrawLine(br, bl);
        Gizmos.DrawLine(bl, tl);
    }
#endif
}
