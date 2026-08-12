using UnityEngine;

/// <summary>
/// 地面待机状态 — grounded 且 |h|&lt;0.1
/// 切换 → Move(输入) / Jump(空格) / Fall(离地)
/// 物理执行(刹停/移动)由 PlayerController.OnFixedUpdate 处理,本状态只做调度判断
/// </summary>
public class PlayerIdleState : EntityState
{
    private readonly PlayerJump jump;

    public PlayerIdleState(CharacterBase owner, StateMachine stateMachine, Animator anim, PlayerJump jump)
        : base(owner, stateMachine, anim)
    {
        this.jump = jump;
    }

    public override void OnUpdate()
    {
        var pc = (PlayerController)owner;

        // 离地(走出平台边缘/被击退) → Fall
        if (!owner.IsGrounded)
        {
            stateMachine.ChangeState(pc.FallState);
            return;
        }

        // P3b:Shift → 冲刺(冷却好;本状态只在非贴墙/非锁定时运行,天然满足非贴墙条件;
        // 优先级与原 dash.OnPlayerUpdate 一致:冲刺优先于跳跃/攻击/格挡)
        if (Input.GetKeyDown(KeyCode.LeftShift) && pc.Dash != null && pc.Dash.CooldownReady)
        {
            stateMachine.ChangeState(pc.DashState);
            return;
        }

        // 跳跃缓冲:锁定期间记录的跳跃意图,解锁后窗口内补跳
        if (jump.UpdateJumpBuffer(pc))
        {
            stateMachine.ChangeState(pc.JumpState);
            return;
        }

        float h = Input.GetAxisRaw("Horizontal");

        // 空格 → Jump(墙顶优先翻顶)
        if (Input.GetKeyDown(KeyCode.Space))
        {
            if (pc.NearWallTop() && pc.CanVault())
            {
                pc.WallClingState?.TriggerVault();
                return;
            }
            if (jump.TryJump(pc))
            {
                stateMachine.ChangeState(pc.JumpState);
                return;
            }
        }

        // 左键 → 攻击(带冷却判断;冲刺中不会进入本状态,天然禁止冲刺攻击)
        if (Input.GetMouseButtonDown(0) && pc.Combat != null && pc.Combat.AttackCooldownReady)
        {
            stateMachine.ChangeState(pc.AttackState);
            return;
        }

        // 右键 → 格挡
        if (Input.GetMouseButtonDown(1))
        {
            stateMachine.ChangeState(pc.BlockState);
            return;
        }

        // 有输入 → Move
        if (Mathf.Abs(h) > 0.1f)
            stateMachine.ChangeState(pc.MoveState);
    }
}
