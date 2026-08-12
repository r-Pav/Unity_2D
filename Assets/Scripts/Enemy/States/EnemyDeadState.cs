using UnityEngine;

/// <summary>
/// 敌人死亡状态 — HP<=0 进入（EnemyControllerBase.Die() 调 ChangeState）。
/// IsDead=true 触发死亡动画；Death 动画末帧 AnimationEvent → OnEnemyDeathEnd → OnDeathAnimationEnd 执行结算与销毁；
/// 事件链路断时由 EnemyControllerBase 的死亡超时兜底协程强制结束。
/// </summary>
public class EnemyDeadState : EntityState
{
    public EnemyDeadState(CharacterBase owner, StateMachine stateMachine, Animator anim)
        : base(owner, stateMachine, anim, new[] { AnimParams.IsDead })
    {
    }

    public override void OnUpdate()
    {
        // 等待动画事件 / 超时兜底，无每帧逻辑
    }
}
