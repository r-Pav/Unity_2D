using UnityEngine;

/// <summary>
/// 下落状态 — 下落 + 空中加速(原 PlayerController.OnFixedUpdate 空中分支迁入)
/// 落地转 Idle/Move;空中可二段跳(调 PlayerJump 执行器)
/// 注意:蹬墙跳/翻顶后进入本状态时可能仍在上冲,此时不开 Fall 动画(与旧逻辑一致)
/// </summary>
public class PlayerFallState : EntityState
{
    private readonly PlayerJump jump;

    public PlayerFallState(CharacterBase owner, StateMachine stateMachine, Animator anim, PlayerJump jump)
        : base(owner, stateMachine, anim, new[] { AnimParams.IsFalling })
    {
        this.jump = jump;
    }

    public override void OnEnter()
    {
        // 蹬墙跳/翻顶进入时可能仍在上冲:仅真正下落(vy<-0.1)时开 Fall 动画,
        // 否则保持移动动画(旧逻辑 IsFalling 只在 vy<-0.1 时置位)
        if (owner.Rb == null || owner.Rb.velocity.y < -0.1f)
            base.OnEnter();
    }

    public override void OnUpdate()
    {
        var pc = (PlayerController)owner;
        float h = Input.GetAxisRaw("Horizontal");

        // 落地 → Idle/Move(重置跳跃次数)
        if (owner.IsGrounded)
        {
            jump.ResetJumps();
            stateMachine.ChangeState(Mathf.Abs(h) > 0.1f ? pc.MoveState : pc.IdleState);
            return;
        }

        // P3b:Shift → 空中冲刺(冷却好;验证点:空中也能冲;优先级与原 dash.OnPlayerUpdate 一致)
        if (Input.GetKeyDown(KeyCode.LeftShift) && pc.Dash != null && pc.Dash.CooldownReady)
        {
            stateMachine.ChangeState(pc.DashState);
            return;
        }

        // 空中加速(原 OnFixedUpdate 空中分支迁入:目标速度 h×airMaxSpeed,按 airAcceleration 逼近)
        float targetX = h * pc.AirMaxSpeed;
        float newX = Mathf.MoveTowards(owner.Rb.velocity.x, targetX, pc.AirAcceleration * Time.deltaTime);
        owner.SetVelocityPublic(x: newX);

        // 真正下落时开 Fall 动画(进入时可能仍在上冲)
        if (owner.Rb != null && owner.Rb.velocity.y < -0.1f)
            anim?.SetBool(AnimParams.IsFalling, true);

        // 二段跳:空中按空格(墙顶优先翻顶)
        if (Input.GetKeyDown(KeyCode.Space))
        {
            if (pc.TryVault())
                return;
            if (jump.TryJump(pc))
            {
                stateMachine.ChangeState(pc.JumpState);
                return;
            }
        }
        else if (jump.UpdateJumpBuffer(pc))
        {
            stateMachine.ChangeState(pc.JumpState);
        }

        // 空中左键 → 空中攻击(带冷却判断;原 TryAttack 空中分支)
        if (Input.GetMouseButtonDown(0) && pc.Combat != null && pc.Combat.AttackCooldownReady)
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
    }
}
