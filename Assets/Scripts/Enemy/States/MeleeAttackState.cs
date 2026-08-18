using UnityEngine;

/// <summary>
/// 近战敌人攻击状态 — 由 Attack 动画事件驱动（IEnemyAttackState）：
///   OnHitFrame 命中帧执行攻击（朝向 + PerformAttack），OnAnimEnd 动画结束回 Idle 核心入口再判断。
/// 超时兜底：采样 Attack clip 时长 + 0.2s（采样失败回退 1.0s），OnUpdate 递减，事件链路断时强制退出防卡死。
/// </summary>
public class MeleeAttackState : EntityState, IEnemyAttackState
{
    private IEnemyAttack attackModule;

    // 攻击超时兜底计时（Attack clip 时长 + 0.2s，OnUpdate 递减；clip 采样失败回退 1.0s）
    private float attackTimeout;
    private bool timeoutInitialized;

    public MeleeAttackState(CharacterBase owner, StateMachine stateMachine, Animator anim = null)
        : base(owner, stateMachine, anim, new[] { AnimParams.IsAttacking })
    {
    }

    public override void OnEnter()
    {
        base.OnEnter(); // IsAttacking=true → Animator Entry 路由进 Attack

        var me = (EnemyMeleeController)owner;
        me.moveInput = 0f;
        me.OnEnterCombatState();
        if (me.Rb != null)
            me.Rb.velocity = new Vector2(0f, me.Rb.velocity.y);
        // me.ApplyStateColor(new Color(1.0f, 0.7f, 0.0f));  // [状态色已移除]
        attackModule = me.GetComponent<IEnemyAttack>();

        // 攻击超时兜底：初始 1.0s，采样到 Attack clip 后按 clip 时长 + 0.2s 修正
        attackTimeout = 1.0f;
        timeoutInitialized = false;
    }

    public override void OnUpdate()
    {
        var me = (EnemyMeleeController)owner;

        // 首次进入攻击子机后采样 Attack clip 时长初始化兜底计时
        if (!timeoutInitialized && anim != null)
        {
            var clips = anim.GetCurrentAnimatorClipInfo(0);
            if (clips.Length > 0 && clips[0].clip != null &&
                clips[0].clip.name.IndexOf("Attack", System.StringComparison.OrdinalIgnoreCase) >= 0)
            {
                attackTimeout = clips[0].clip.length + 0.2f;
                timeoutInitialized = true;
            }
        }

        // 超时兜底：动画事件链断裂时强制退出攻击状态，防卡死
        attackTimeout -= Time.deltaTime;
        if (attackTimeout <= 0f)
        {
            Debug.LogWarning("[Enemy] MeleeAttackState 超时兜底退出(动画事件可能丢失)");
            stateMachine.ChangeState(new MeleeIdleState(owner, stateMachine, anim));
        }
    }

    public override void OnExit()
    {
        base.OnExit(); // IsAttacking=false → Attack 状态 Exit，Entry 重判

        var me = (EnemyMeleeController)owner;
        me.attackCooldownTimer = me.AttackCooldownDuration;
    }

    // ── IEnemyAttackState（Attack 动画事件驱动）──

    /// <summary>命中帧：朝向玩家 + 执行攻击（原 0.3s 处逻辑）</summary>
    public void OnHitFrame()
    {
        var me = (EnemyMeleeController)owner;
        me.UpdateFacing(me.DirectionToPlayer());
        attackModule?.PerformAttack(me);
    }

    /// <summary>
    /// 蓄力帧：近战暂不启用蓄力，接口兼容空实现（远程 attack2 专用）。
    /// [预留] 后续近战需要蓄力时在此调 me.BeginChargeFlash()，发射/命中处调 me.EndChargeFlash()。
    /// </summary>
    public void OnCharge() { }

    /// <summary>
    /// 发射帧：近战暂不启用，接口兼容空实现（远程 attack2 专用）。
    /// [预留] 与 OnCharge 配对：蓄力闪烁结束点。
    /// </summary>
    public void OnFire() { }

    /// <summary>攻击动画结束：回 Idle 核心入口（timer→Patrol / CanSeePlayer→Chase），不在攻击状态堆逻辑链</summary>
    public void OnAnimEnd()
    {
        stateMachine.ChangeState(new MeleeIdleState(owner, stateMachine, anim));
    }
}
