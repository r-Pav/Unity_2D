using UnityEngine;

/// <summary>
/// 跳跃上升状态 — 进入时由 PlayerJump.TryJump 执行器施加跳跃力(owner.ExecuteJump)
/// 可二段跳(上升期再按空格:消耗次数并重新施加力,状态不切换) / vy&lt;0 转 Fall
/// 上升期保留空中水平加速(原 PlayerController.OnFixedUpdate 空中分支)
/// </summary>
public class PlayerJumpState : EntityState
{
    private readonly PlayerJump jump;
    private bool wasGrounded;   // 落地边沿检测:进入时地面可能未消失,先用上一帧 grounded 去抖

    public PlayerJumpState(CharacterBase owner, StateMachine stateMachine, Animator anim, PlayerJump jump)
        : base(owner, stateMachine, anim, new[] { AnimParams.IsJumping })
    {
        this.jump = jump;
    }

    public override void OnEnter()
    {
        // IsJumping=true
        // 跳跃力由 PlayerJump.TryJump(执行器)在 ChangeState 前施加:
        // 二段跳时 ChangeState(JumpState) 对当前状态是 no-op,OnEnter 不会再次触发,
        // 故力不能放这里,否则二段跳会丢力(与旧 PlayerJump.TryJump 行为保持一致)
        base.OnEnter();
        // 进入时取当前 grounded:首跳(从地面起跳)为 true,上升初期不误判落地;
        // 二段跳(从空中进入)为 false,落地边沿检测立即可用
        wasGrounded = owner.IsGrounded;
    }

    public override void OnUpdate()
    {
        var pc = (PlayerController)owner;

        // 落地检测:先离地再落地才判定(顶头/异常回落时 vy 可能未低于阈值但已贴地)
        // 用 wasGrounded 边沿,防止跳跃上升初期 grounded 未消失被误判为落地(吞跳)
        if (owner.IsGrounded && !wasGrounded)
        {
            jump.ResetJumps();
            float h = Input.GetAxisRaw("Horizontal");
            stateMachine.ChangeState(Mathf.Abs(h) > 0.1f ? pc.MoveState : pc.IdleState);
            return;
        }
        wasGrounded = owner.IsGrounded;

        // P3b:Shift → 空中冲刺(冷却好;验证点:空中也能冲;优先级与原 dash.OnPlayerUpdate 一致)
        if (Input.GetKeyDown(KeyCode.LeftShift) && pc.Dash != null && pc.Dash.CooldownReady)
        {
            stateMachine.ChangeState(pc.DashState);
            return;
        }

        // 二段跳:上升期再按空格 → 消耗次数并重新施加跳跃力(状态不变,OnEnter 不会触发;
        // 力由 PlayerJump.TryJump 执行器内部施加,此处只需保持状态;墙顶优先翻顶)
        if (Input.GetKeyDown(KeyCode.Space))
        {
            if (pc.TryVault())
                return;
            if (jump.TryJump(pc))
                return;
        }
        else if (jump.UpdateJumpBuffer(pc))
        {
            return;
        }

        // 空中左键 → 空中攻击(带冷却判断 + 一滞空一次限制;原 TryAttack 空中分支)
        if (Input.GetMouseButtonDown(0) && pc.Combat != null && pc.Combat.AttackCooldownReady
            && !jump.AirAttackUsed)
        {
            stateMachine.ChangeState(pc.AirAttackState);
            return;
        }

        // 下坠攻击:空中按 S 且高度够(原 PlayerGroundPound.HandleInput;冷却/高度/非贴墙检查在组件内)
        if (Input.GetKeyDown(KeyCode.S) && pc.GroundPound != null && pc.GroundPound.TryStartPound(pc))
        {
            stateMachine.ChangeState(pc.GroundPoundState);
            return;
        }

        // 上升期空中加速(原 OnFixedUpdate 空中分支:与下落同款,保持上升中可控)
        float h2 = Input.GetAxisRaw("Horizontal");
        float targetX = h2 * pc.AirMaxSpeed;
        float newX = Mathf.MoveTowards(owner.Rb.velocity.x, targetX, pc.AirAcceleration * Time.deltaTime);
        owner.SetVelocityPublic(x: newX);

        // 转为下落 → Fall
        if (owner.Rb != null && owner.Rb.velocity.y < -0.1f)
            stateMachine.ChangeState(pc.FallState);
    }
}
