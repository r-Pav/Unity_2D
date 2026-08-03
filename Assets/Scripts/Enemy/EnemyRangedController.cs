using UnityEngine;

/// <summary>
/// 远程敌人控制器 — 继承 EnemyControllerBase，3 状态 FSM（Idle → Chase → Attack，无 Patrol）。
/// 通过三级距离策略保持与玩家的射击距离，在 retreatWidth/Height~attackWidth/Height 之间远程射击。
/// </summary>
public class EnemyRangedController : EnemyControllerBase
{
    // ============================================================
    // 配置参数
    // ============================================================

    [Header("后退策略 — 矩形")]
    [Tooltip("后退矩形半宽（X 轴，低于此距离后退）")]
    [SerializeField] private float retreatWidth = 3f;
    [Tooltip("后退矩形半高（Y 轴，低于此距离后退）")]
    [SerializeField] private float retreatHeight = 3f;
    [Tooltip("恢复追击矩形半宽（X 轴，迟滞区间上限）")]
    [SerializeField] private float retreatRecoverWidth = 10f;
    [Tooltip("恢复追击矩形半高（Y 轴，迟滞区间上限）")]
    [SerializeField] private float retreatRecoverHeight = 6f;

    /// <summary>暴露后退矩形半宽给攻击组件（EnemyRangedAttack）读取</summary>
    public float RetreatWidth => retreatWidth;
    /// <summary>暴露后退矩形半高给攻击组件（EnemyRangedAttack）读取</summary>
    public float RetreatHeight => retreatHeight;

    // ============================================================
    // 抽象方法实现
    // ============================================================

    protected override IState GetInitialState() => new IdleState(this);
    public override IState CreateChaseState() => new ChaseState(this);
    public override IState CreateFallbackState() => new IdleState(this);

    // ============================================================
    // 生命周期
    // ============================================================

    protected new void Start()
    {
        attackWidth = 10f;
        attackHeight = 6f;
        stunState = new EnemyStunState(this, fsm);
        SetStunState(stunState);
        base.Start();
    }

    // ============================================================
    // 覆盖：基类攻击条件 + 不在后退矩形区内
    // ============================================================

    public override bool CanAttack()
    {
        if (!base.CanAttack()) return false;
        if (player == null) return false;

        float deltaX = player.position.x - transform.position.x;
        float deltaY = player.position.y - transform.position.y;
        // 玩家不在后退矩形内（X 或 Y 超出后退半边界）才能攻击
        return Mathf.Abs(deltaX) >= retreatWidth * 0.5f || Mathf.Abs(deltaY) >= retreatHeight * 0.5f;
    }

    // ============================================================
    // 辅助：检查玩家是否在指定矩形内
    // ============================================================

    private bool InRect(float w, float h)
    {
        if (player == null) return false;
        float dx = Mathf.Abs(player.position.x - transform.position.x);
        float dy = Mathf.Abs(player.position.y - transform.position.y);
        return dx <= w * 0.5f && dy <= h * 0.5f;
    }

    // ============================================================
    // FSM 状态定义
    // ============================================================

    public class IdleState : IState
    {
        private readonly EnemyRangedController owner;
        private float timer;

        public IdleState(EnemyRangedController owner) { this.owner = owner; }

        public void OnEnter()
        {
            timer = Random.Range(1f, 2.5f);
            owner.moveInput = 0f;
            owner.OnExitCombatState();
            owner.ApplyStateColor(new Color(0.6f, 0.6f, 0.6f)); // 灰白
        }

        public void OnUpdate()
        {
            timer -= Time.deltaTime;
            if (timer <= 0f)
                owner.fsm.ChangeState(new IdleState(owner));  // 原地循环，无 Patrol
            else if (owner.CanSeePlayer())
            {
                // Debug.Log($"[{owner.name}] 发现玩家，进入Chase");
                owner.fsm.ChangeState(owner.CreateChaseState());
            }
        }

        public void OnExit() { }
    }

    private class ChaseState : IState
    {
        private readonly EnemyRangedController owner;
        private float losePlayerTimer;
        private float debugCooldown;

        public ChaseState(EnemyRangedController owner) { this.owner = owner; }

        public void OnEnter()
        {
            losePlayerTimer = 3f;
            debugCooldown = 0f;
            owner.OnEnterCombatState();
            owner.ApplyStateColor(new Color(1.0f, 0.2f, 0.2f)); // 红色
            owner.moveInput = owner.DirectionToPlayer();
        }

        public void OnUpdate()
        {
            if (owner.isDead) return;

            if (owner.CanSeePlayer())
            {
                losePlayerTimer = 3f;
                TryTransitionToAttack();
            }
            else
            {
                losePlayerTimer -= Time.deltaTime;
                if (losePlayerTimer <= 0f)
                    owner.fsm.ChangeState(new IdleState(owner));
            }
        }

        private void TryTransitionToAttack()
        {
            // 三级矩形距离策略（带迟滞区间，防止 single-threshold 震荡）
            if (owner.InRect(owner.retreatWidth, owner.retreatHeight))
                owner.moveInput = -owner.DirectionToPlayer() * 0.5f;       // 太近，后退
            else if (owner.InRect(owner.retreatRecoverWidth, owner.retreatRecoverHeight))
                owner.moveInput = 0f;                                       // 迟滞区间，静止
            else
                owner.moveInput = owner.DirectionToPlayer();                // 足够远，追击

            if (owner.CanAttack())
            {
                // Debug.Log($"[{owner.name}] 进入攻击！");
                owner.fsm.ChangeState(new AttackState(owner));
            }
            else
            {
                debugCooldown -= Time.deltaTime;
                if (debugCooldown <= 0f)
                {
                    debugCooldown = 0.5f;
                    float dx = owner.player.position.x - owner.transform.position.x;
                    float dy = owner.player.position.y - owner.transform.position.y;
                    // Debug.Log($"[{owner.name}] CanAttack=false | CanSeePlayer={owner.CanSeePlayer()} | cooldown={owner.attackCooldownTimer:F2} | " +
                        // $"deltaX={dx:F2} deltaY={dy:F2} | atkW={owner.AttackWidth} atkH={owner.AttackHeight} | " +
                        // $"retW={owner.RetreatWidth} retH={owner.RetreatHeight}");
                }
            }
        }

        public void OnExit()
        {
        }
    }

    private class AttackState : IState
    {
        private readonly EnemyRangedController owner;
        private IEnemyAttack attackModule;
        private float timer;
        private bool attacked;

        public AttackState(EnemyRangedController owner) { this.owner = owner; }

        public void OnEnter()
        {
            timer = 0.5f;
            attacked = false;
            owner.moveInput = 0f;
            owner.OnEnterCombatState();
            owner.GetComponent<Rigidbody2D>().velocity = new Vector2(0f, owner.GetComponent<Rigidbody2D>().velocity.y);
            owner.ApplyStateColor(new Color(1.0f, 0.7f, 0.0f));
            attackModule = owner.GetComponent<IEnemyAttack>();
        }

        public void OnUpdate()
        {
            timer -= Time.deltaTime;

            if (!attacked && timer <= 0.3f)
            {
                attacked = true;
                // Debug.Log($"[{owner.name}] 执行远程攻击");
                attackModule?.PerformAttack(owner);
            }

            if (timer <= 0f)
            {
                if (owner.CanSeePlayer())
                    owner.fsm.ChangeState(owner.CreateChaseState());
                else
                    owner.fsm.ChangeState(new IdleState(owner));
            }
        }

        public void OnExit()
        {
            owner.attackCooldownTimer = owner.attackCooldownDuration;
        }
    }

    // ============================================================
    // Gizmos
    // ============================================================

#if UNITY_EDITOR
    protected override void OnDrawGizmosSelected()
    {
        base.OnDrawGizmosSelected();

        Vector3 pos = transform.position;
        float hw, hh;

        // 后退矩形（蓝色线框）
        hw = retreatWidth * 0.5f;
        hh = retreatHeight * 0.5f;
        DrawWireRect(pos, hw, hh, new Color(0f, 0.5f, 1f, 0.4f));

        // 恢复追击矩形（绿色线框，迟滞区间上限）
        hw = retreatRecoverWidth * 0.5f;
        hh = retreatRecoverHeight * 0.5f;
        DrawWireRect(pos, hw, hh, new Color(0f, 1f, 0f, 0.3f));
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
