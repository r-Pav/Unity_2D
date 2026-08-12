using UnityEngine;

/// <summary>
/// 死亡状态 — HP<=0 进入(PlayerHealth.TakeDamage 触发 ChangeState)
/// 触发死亡动画(SetBool IsDead)；退出:仅由 PlayerHealth.Revive() 调 ChangeState(IdleState)(UI DeathPanel 按钮 → Revive)
/// PlayerDeathEvent 仍由死亡动画末帧 AnimationEvent → PlayerHealth.OnDeathAnimationEnd() 触发(不变)
/// [2026-08-10] 入口纯 Bool(IsDead) 驱动，去掉 Death Trigger——与 enemy 死亡统一；Trigger 消费残留会导致入口失效。
/// </summary>
public class PlayerDeadState : EntityState
{
    public override bool LocksInput => true;

    public PlayerDeadState(CharacterBase owner, StateMachine stateMachine, Animator anim)
        : base(owner, stateMachine, anim, new[] { AnimParams.IsDead })
    {
    }

    public override void OnEnter()
    {
        // IsDead=true → Entry 路由；但 Entry 过渡只在状态机启动时评估（运行中不检查），
        // 玩家在 Locomotion/攻击中死亡时动画层不会自动切 → Play 直切强制播死亡动画。
        // Death 状态在 Base Layer，短名可靠（子状态机内短名不可靠，勿改）。
        base.OnEnter();
        anim?.Play("Death", 0, 0f);
    }

    public override void OnUpdate()
    {
        // 死亡后无每帧逻辑,等待 Revive() 切回 Idle
    }
}
