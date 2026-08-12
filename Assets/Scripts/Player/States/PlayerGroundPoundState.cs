using UnityEngine;

/// <summary>
/// 下坠攻击状态 — 空中按 S 且高度够进入(由 PlayerFallState/PlayerJumpState 检测后切换)
/// 进入时高速下坠(velocity.y = -poundSpeed),落地边沿触发 AOE(EventBus GroundPoundEvent)
/// 空中擦到敌人持续击退(原 PlayerGroundPound.HandleMidairEnemyCollisions)
/// isPounding 状态由本 FSM 状态类表达(原 PlayerGroundPound.isPounding)
/// </summary>
public class PlayerGroundPoundState : EntityState
{
    private readonly PlayerGroundPound groundPound;
    private readonly PlayerJump jump;
    private bool wasGrounded;   // 落地边沿检测:进入时必为空中,grounded 从 false→true 才判落地

    public override bool LocksInput => true;

    public PlayerGroundPoundState(CharacterBase owner, StateMachine stateMachine, Animator anim,
        PlayerGroundPound groundPound, PlayerJump jump)
        : base(owner, stateMachine, anim, new[] { AnimParams.IsFalling })
    {
        // 绑定 IsFalling:下坠期间维持 Fall 动画(与改造前一致:下坠时 FSM 状态为 Fall,IsFalling=true)
        this.groundPound = groundPound;
        this.jump = jump;
    }

    public override void OnEnter()
    {
        base.OnEnter();
        wasGrounded = false;
        groundPound?.StartPound((PlayerController)owner);
    }

    public override void OnUpdate()
    {
        var pc = (PlayerController)owner;
        bool grounded = pc.IsGrounded();

        // 落地边沿 → AOE + 重置跳跃次数 + 退出(原 HandlePoundState:grounded && !wasGrounded)
        if (grounded && !wasGrounded)
        {
            groundPound?.OnLand(pc);
            jump?.ResetJumps();   // 修复:下坠落地后不重置跳跃次数 → 之后按空格跳不了
            float h = Input.GetAxisRaw("Horizontal");
            stateMachine.ChangeState(Mathf.Abs(h) > 0.1f ? pc.MoveState : pc.IdleState);
            return;
        }
        wasGrounded = grounded;

        // 空中擦到敌人 → 击退(原 HandleMidairEnemyCollisions)
        groundPound?.HandleMidairEnemyCollisions(pc);
    }
}
