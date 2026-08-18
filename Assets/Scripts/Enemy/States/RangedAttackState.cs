using UnityEngine;

/// <summary>
/// 远程敌人攻击状态 — 双攻击入口（由 Attack1/Attack2 动画事件驱动，IEnemyAttackState）：
///   OnEnter 判框：player 在近战框(attackWidth/Height)内 → attack1(IsAttack1)
///                 否则在远程框(rangedAttackWidth/Height)内 → attack2(IsAttack2)
///                 框外 → 直接切 RangedRushState（不播动画）
///   OnHitFrame：attack1 → EnemyMeleeAttack.PerformAttack（近战机制照搬）；attack2 → 空
///   OnCharge / OnFire：attack2 → EnemyRangedAttack.OnCharge / OnFire（蓄力/发射粒子）
///   OnAnimEnd：回 RangedPatrolState（核心入口判断）
/// 超时兜底照 MeleeAttackState：采样 Attack clip 时长 + 0.2s，事件链路断时强制退出防卡死。
/// </summary>
public class RangedAttackState : EntityState, IEnemyAttackState
{
    private EnemyMeleeAttack meleeAttack;   // attack1 近战（挂在敌人本体）
    private EnemyRangedAttack rangedAttack; // attack2 远程（挂在敌人本体）
    private bool isAttack1;                 // true = attack1 近战；false = attack2 远程

    // 攻击超时兜底计时（Attack clip 时长 + 0.2s，OnUpdate 递减；clip 采样失败回退 1.0s）
    private float attackTimeout;
    private bool timeoutInitialized;

    public RangedAttackState(CharacterBase owner, StateMachine stateMachine, Animator anim = null)
        : base(owner, stateMachine, anim, new[] { AnimParams.IsAttacking })
    {
    }

    public override void OnEnter()
    {
        base.OnEnter(); // IsAttacking=true（busy 聚合 → 动画器 Entry 路由进 Attack1/Attack2）

        var me = (EnemyRangedController)owner;
        me.moveInput = 0f;
        me.OnEnterCombatState();
        // 不再清水平速度：受击进入时由 OnHitBy 设置击退滑行窗口保留击退速度（近战 stun 路径同样保留），
        // 普通从 Chase/Rush 进入时由 OnFixedUpdate 的 Move(0) 兜底停住（仅晚一帧，可接受）
        // me.ApplyStateColor(new Color(1.0f, 0.7f, 0.0f));  // [状态色已移除]

        // 缓存攻击组件（GetComponent 仅在进入状态时查一次，不每帧查）
        meleeAttack = me.GetComponent<EnemyMeleeAttack>();
        rangedAttack = me.GetComponent<EnemyRangedAttack>();

        // 进入攻击状态立即面向玩家：防止从巡逻背对状态进攻击时全程背对
        // （attack2 无命中帧转朝向，若不加此行远程框攻击会全程背对玩家）
        me.UpdateFacing(me.DirectionToPlayer());

        // 判框选攻击
        if (me.PlayerInAttackRange())
        {
            isAttack1 = true;
            SetAttackBools(true);  // 近战框 → attack1
        }
        else if (me.InRangedRect())
        {
            isAttack1 = false;
            SetAttackBools(false); // 远程框 → attack2
        }
        else
        {
            // 框外：不播攻击动画，直接进入加速移动状态（OnExit 自动清 IsAttacking/攻击参数）
            stateMachine.ChangeState(new RangedRushState(owner, stateMachine, anim));
            return;
        }

        // 攻击超时兜底：初始 1.0s，采样到 Attack clip 后按 clip 时长 + 0.2s 修正
        attackTimeout = 1.0f;
        timeoutInitialized = false;
    }

    /// <summary>设置攻击路由参数（IsAttack1/IsAttack2 互斥）</summary>
    private void SetAttackBools(bool attack1)
    {
        if (anim == null) return;
        anim.SetBool(AnimParams.IsAttack1, attack1);
        anim.SetBool(AnimParams.IsAttack2, !attack1);
    }

    public override void OnUpdate()
    {
        var me = (EnemyRangedController)owner;

        // attack2 蓄力/发射期间持续面向玩家：OnEnter 只转了一次朝向，player 绕后时
        // 子弹方向在 OnFire 重算朝玩家，模型却还固定朝原方向（反向飞行的视觉 bug）
        if (!isAttack1)
            me.UpdateFacing(me.DirectionToPlayer());

        // 首次进入攻击子机后采样 Attack clip 时长初始化兜底计时（attack1/attack2 通用）
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
            Debug.LogWarning("[Enemy] RangedAttackState 超时兜底退出(动画事件可能丢失)");
            stateMachine.ChangeState(new RangedPatrolState(owner, stateMachine, anim));
        }
    }

    public override void OnExit()
    {
        base.OnExit(); // IsAttacking=false

        // 清攻击路由参数（防残留导致 Entry 误路由）
        if (anim != null)
        {
            anim.SetBool(AnimParams.IsAttack1, false);
            anim.SetBool(AnimParams.IsAttack2, false);
        }

        var me = (EnemyRangedController)owner;
        me.EndChargeFlash();  // 兜底：蓄力中被打断/超时退出/死亡时结束蓄力闪烁（fire 正常路径已结束，幂等）
        me.attackCooldownTimer = me.AttackCooldownDuration;
    }

    // ── IEnemyAttackState（Attack1/Attack2 动画事件驱动）──

    /// <summary>命中帧：attack1 → 朝向玩家 + 近战攻击；attack2 远程无命中帧（发射在 OnFire）</summary>
    public void OnHitFrame()
    {
        if (!isAttack1) return;
        var me = (EnemyRangedController)owner;
        me.UpdateFacing(me.DirectionToPlayer());
        if (meleeAttack != null)
            meleeAttack.PerformAttack(me);
    }

    /// <summary>蓄力帧：attack2 → 远程蓄力粒子（attack1 近战无蓄力）</summary>
    public void OnCharge()
    {
        if (isAttack1) return;
        var me = (EnemyRangedController)owner;
        me.BeginChargeFlash();  // 蓄力帧开始蓄力色闪烁（灭相位=原始材质色，频率随蓄力加速）
        if (rangedAttack != null)
            rangedAttack.OnCharge();
    }

    /// <summary>发射帧：attack2 → 远程发射子弹 + 粒子（attack1 近战无发射）</summary>
    public void OnFire()
    {
        if (isAttack1) return;
        var me = (EnemyRangedController)owner;
        me.EndChargeFlash();  // 发射帧结束蓄力闪烁
        if (rangedAttack != null)
            rangedAttack.OnFire();
    }

    /// <summary>攻击动画结束：回巡逻核心入口（CanSeePlayer → 攻击/加速；否则继续巡逻）</summary>
    public void OnAnimEnd()
    {
        stateMachine.ChangeState(new RangedPatrolState(owner, stateMachine, anim));
    }
}
