using UnityEngine;

/// <summary>
/// 近战敌人控制器 — 继承 EnemyControllerBase，4 状态 FSM（Idle → Patrol → Chase → Attack）。
/// 在 patrolRange 内来回巡逻，发现玩家后直接追击并发起近战攻击。
/// </summary>
public class EnemyMeleeController : EnemyControllerBase
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

    // ============================================================
    // 抽象方法实现
    // ============================================================

    protected override IState GetInitialState() => new MeleeIdleState(this, Fsm, Animator);
    public override IState CreateChaseState() => new MeleeChaseState(this, Fsm, Animator);
    public override IState CreateFallbackState() => new MeleePatrolState(this, Fsm, Animator);

    // ============================================================
    // 生命周期
    // ============================================================

    protected new void Start()
    {
        // [Lv 收敛] patrolRange：Inspector(>0) → SO 对应 Lv 档 → 内置 3f（0 = 未设置）
        patrolRange = Resolve(patrolRange, LvStats?.patrolRange ?? 0f, DefaultPatrolRange);

        stunState = new EnemyStunState(this, Fsm);
        SetStunState(stunState);
        base.Start();
    }

    // ============================================================
    // Gizmos
    // ============================================================

#if UNITY_EDITOR
    protected override void OnDrawGizmosSelected()
    {
        base.OnDrawGizmosSelected();

        // 巡逻范围（蓝色竖线表示左右边界）
        Gizmos.color = Color.blue;
        float height = 1f;
        Vector3 left = transform.position + Vector3.left * patrolRange;
        Vector3 right = transform.position + Vector3.right * patrolRange;
        Gizmos.DrawLine(left, left + Vector3.up * height);
        Gizmos.DrawLine(right, right + Vector3.up * height);
    }
#endif
}
