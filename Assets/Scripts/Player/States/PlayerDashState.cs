using UnityEngine;

/// <summary>
/// 冲刺状态 — Shift 进入(Idle/Move/Jump/Fall 状态类检测后切换),dashDuration(0.15s) 计时后退出
/// OnEnter:清速度 + 设冲刺速度(facing × dashSpeed) + 启动冷却(由 PlayerDash.DoDash 执行)
/// 超时:落地 → Idle/Move,空中 → Fall(与改造前 PlayerDash.OnPlayerUpdate 超时分支一致)
/// dashCooldown 倒计时统一在 PlayerController.UpdateCooldowns 调 PlayerDash.TickCooldown 递减
/// </summary>
public class PlayerDashState : EntityState
{
    private readonly PlayerDash dash;
    private readonly float dashDuration;   // 冲刺时长(原 PlayerDash 序列化值,由 PlayerController 注入)
    private float dashTimer;               // 冲刺剩余计时

    public override bool LocksInput => true;

    public PlayerDashState(CharacterBase owner, StateMachine stateMachine, Animator anim,
        PlayerDash dash, float dashDuration)
        : base(owner, stateMachine, anim, new[] { AnimParams.IsDashing })
    {
        this.dash = dash;
        this.dashDuration = dashDuration;
    }

    public override void OnEnter()
    {
        // IsDashing=true → Dash 动画(控制器参数存在但未用于路由,保持设置)
        base.OnEnter();
        dashTimer = dashDuration;
        dash?.DoDash((PlayerController)owner);
    }

    public override void OnUpdate()
    {
        var pc = (PlayerController)owner;

        dashTimer -= Time.deltaTime;
        if (dashTimer <= 0f)
        {
            // 冲刺结束:落地 → Idle/Move;空中 → Fall(原 PlayerDash.OnPlayerUpdate 超时分支)
            float h = Input.GetAxisRaw("Horizontal");
            if (owner.IsGrounded)
                stateMachine.ChangeState(Mathf.Abs(h) > 0.1f ? pc.MoveState : pc.IdleState);
            else
                stateMachine.ChangeState(pc.FallState);
        }
    }
}
