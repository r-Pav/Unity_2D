using UnityEngine;

/// <summary>
/// 地面受击状态 — 敌人命中(地面)进入,硬直 hurtDuration(0.3s) 后恢复移动
/// 受击动画由 Animator 的 Entry(IsHurt) 路由驱动,本状态只管超时退出
/// 击退物理由 PlayerHealth.TakeDamageWithKnockback → KnockbackRoutine 处理(不变)
/// </summary>
public class PlayerHurtState : EntityState
{
    private readonly float hurtDuration;   // 受击硬直时长(原 PlayerHealth 序列化值,由 PlayerController 注入)
    private float enterTime;               // 进入时间戳(超时判定用)

    public override bool LocksInput => true;

    public PlayerHurtState(CharacterBase owner, StateMachine stateMachine, Animator anim, float hurtDuration)
        : base(owner, stateMachine, anim, new[] { AnimParams.IsHurt })
    {
        this.hurtDuration = hurtDuration;
    }

    public override void OnEnter()
    {
        // IsHurt=true → Animator Entry 路由 Hit 动画
        base.OnEnter();
        enterTime = Time.time;
    }

    public override void OnUpdate()
    {
        var pc = (PlayerController)owner;

        // 硬直超时 → 恢复移动(Idle/Move)
        if (Time.time - enterTime >= hurtDuration)
        {
            float h = Input.GetAxisRaw("Horizontal");
            stateMachine.ChangeState(Mathf.Abs(h) > 0.1f ? pc.MoveState : pc.IdleState);
        }
    }
}
