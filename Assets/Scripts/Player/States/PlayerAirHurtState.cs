using UnityEngine;

/// <summary>
/// 空中受击状态 — 敌人命中(空中)进入,击退+下落
/// 落地(IsGrounded) → ClearAirHurt + 回 Idle/Move;超时兜底 airHurtTimeout(1.5s) → 同样清除
/// (被敌人顶着不落地也能恢复控制,防止永久锁死;超时后回 FallState 继续下落)
/// </summary>
public class PlayerAirHurtState : EntityState
{
    private readonly PlayerHealth health;
    private readonly PlayerJump jump;
    private readonly float airHurtTimeout;   // 空中受击最大时长(原 PlayerHealth 序列化值,由 PlayerController 注入)
    private float enterTime;                 // 进入时间戳(超时兜底判定用)

    public override bool LocksInput => true;

    public PlayerAirHurtState(CharacterBase owner, StateMachine stateMachine, Animator anim,
        PlayerHealth health, PlayerJump jump, float airHurtTimeout)
        : base(owner, stateMachine, anim, new[] { AnimParams.IsAirHurt })
    {
        this.health = health;
        this.jump = jump;
        this.airHurtTimeout = airHurtTimeout;
    }

    public override void OnEnter()
    {
        // IsAirHurt=true → AirHurt 动画
        base.OnEnter();
        enterTime = Time.time;
    }

    public override void OnUpdate()
    {
        var pc = (PlayerController)owner;

        // 落地 → 清除 AirHurt 恢复(Idle/Move)
        if (owner.IsGrounded)
        {
            health?.ClearAirHurt();
            jump?.ResetJumps();   // 修复:空中受击落地后不重置跳跃次数 → 之后按空格跳不了
            float h = Input.GetAxisRaw("Horizontal");
            stateMachine.ChangeState(Mathf.Abs(h) > 0.1f ? pc.MoveState : pc.IdleState);
            return;
        }

        // 超时兜底:被敌人顶着不落地 → 同样清除,回 FallState 继续下落
        if (Time.time - enterTime >= airHurtTimeout)
        {
            health?.ClearAirHurt();
            jump?.ResetJumps();
            stateMachine.ChangeState(pc.FallState);
        }
    }
}
