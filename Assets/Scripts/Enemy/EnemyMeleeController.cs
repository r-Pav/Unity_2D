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
    [Tooltip("巡逻范围（左右各多少单位）")]
    [SerializeField] private float patrolRange = 3f;
    public float PatrolRange => patrolRange;

    // ============================================================
    // 抽象方法实现
    // ============================================================

    protected override IState GetInitialState() => new MeleeIdleState(this);
    public override IState CreateChaseState() => new MeleeChaseState(this);
    public override IState CreateFallbackState() => new MeleePatrolState(this);

    // ============================================================
    // 生命周期
    // ============================================================

    protected new void Start()
    {
        stunState = new EnemyStunState(this, fsm);
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
