using UnityEngine;

/// <summary>
/// Boss 攻击状态 — 动画驱动（独立于普通 enemy 的事件链）。
/// 进入:IsAttacking=true → 动画器 Entry 路由进 Attack 状态播放动画。
/// 保持:玩家在攻击范围内 → 保持攻击状态,Attack 动画循环播放(持续攻击,无冷却站桩)。
/// 退出:玩家离开攻击范围 → 回追击。受击/死亡由外部状态切换接管。
/// 不做伤害判定(当前阶段只验证状态动画流转;伤害/技能后续接入)。
/// </summary>
public class BossAttackState : EntityState
{
    public BossAttackState(CharacterBase owner, StateMachine stateMachine, Animator anim = null)
        : base(owner, stateMachine, anim, new[] { AnimParams.IsAttacking })
    {
    }

    public override void OnEnter()
    {
        base.OnEnter(); // IsAttacking=true → 动画器 Entry 路由进 Attack
        var boss = (FirstBoss)owner;
        boss.moveInput = 0f;

        // 面朝玩家
        float dir = boss.DirectionToPlayer();
        if (dir != 0f)
            boss.UpdateFacing(dir);
    }

    public override void OnUpdate()
    {
        var boss = (FirstBoss)owner;
        if (boss.IsDead) return;

        // 玩家不在攻击范围(子 obj)内 → 回追击;范围内保持攻击状态(Attack 动画循环播放 = 持续攻击)
        if (!boss.IsPlayerInBossAttackRange())
        {
            ReturnToChase(boss);
        }
    }

    /// <summary>Boss 独立动画事件(经 BossAnimationRelay 转发):Attack 动画结束帧 → 回追击(事件未挂时不影响,由 OnUpdate 范围判断接管)</summary>
    public void OnAnimEnd()
    {
        var boss = (FirstBoss)owner;
        if (boss.IsDead) return;
        ReturnToChase(boss);
    }

    /// <summary>攻击结束统一出口:回追击(不设冷却 — 范围内持续攻击由 Attack 动画循环控制)</summary>
    private void ReturnToChase(FirstBoss boss)
    {
        boss.Fsm.ChangeState(boss.CreateChaseState());
    }

    public override void OnExit()
    {
        base.OnExit(); // IsAttacking=false → 动画器 Exit,Entry 重判
        var boss = (FirstBoss)owner;
        boss.moveInput = 0f;
    }
}
