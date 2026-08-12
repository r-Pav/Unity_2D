using UnityEngine;

/// <summary>
/// 空中攻击状态 — 进入时滞空(水平速度半速 + 垂直速度归零 + 重力 0.3 倍),退出恢复重力
/// 退出条件(方案三):动画结束(OnAirAttackEnd 事件) / 落地
/// 动画事件经 PlayerCombat 薄转发:OnAirAttackHitFrame / OnAirAttackEnd
/// </summary>
public class PlayerAirAttackState : EntityState
{
    private readonly PlayerCombat combat;
    private readonly WeaponThrow weaponThrow;
    private readonly PlayerJump jump;

    private float _airAttackOriginalGravity = 1f;   // 空中攻击前的重力(结束时恢复)
    private bool _airAttackGravityRestored = true;  // 重力是否已恢复(防重复恢复)
    private float _hoverTimer;                      // 滞空计时:落地退出需先滞空最小时间(防低空攻击瞬间退出)

    /// <summary>最小滞空时长(秒):进入后至少悬浮这么久才允许落地退出,与 AirAttack 动画时长同量级</summary>
    private const float MinHoverTime = 0.25f;

    public override bool LocksInput => true;

    public PlayerAirAttackState(CharacterBase owner, StateMachine stateMachine, Animator anim,
        PlayerCombat combat, WeaponThrow weaponThrow, PlayerJump jump)
        : base(owner, stateMachine, anim, new[] { AnimParams.IsAirAttacking })
    {
        this.combat = combat;
        this.weaponThrow = weaponThrow;
        this.jump = jump;
    }

    public override void OnEnter()
    {
        // IsAirAttacking=true → Entry→AirAttack
        base.OnEnter();

        var pc = (PlayerController)owner;

        // 强制直切 AirAttack 动画:跳过 Jump/Fall→AirAttack 的过渡竞争
        // (OnExit 清 IsJumping/IsFalling 与 OnEnter 设 IsAirAttacking 同帧,动画层 Exit 过渡优先
        // 会落回 Locomotion 显示 idle/walk,且 Locomotion 无到 AirAttack 的过渡 → 卡住。Play 强制切绕过)
        anim?.Play("AirAttack", 0, 0f);

        // 攻击朝向跟随当前输入(原 ExecuteAirAttack)
        if (combat != null)
            pc.UpdateFacing(combat.AttackDir);

        // 滞空:水平速度减半 + 垂直速度归零 + 重力减小(原 ExecuteAirAttack)
        Rigidbody2D rb = pc.GetRigidbody();
        if (rb != null)
        {
            rb.velocity = new Vector2(rb.velocity.x * 0.5f, 0f);
            _airAttackOriginalGravity = rb.gravityScale;
            rb.gravityScale = Mathf.Max(0.3f, _airAttackOriginalGravity * 0.3f);
            _airAttackGravityRestored = false;
        }
        _hoverTimer = 0f;

        // 记录攻击起始:消耗冷却 + 战斗态锁定(原 ExecuteAirAttack)
        combat?.ConsumeAttackCooldown();
        combat?.OnAttack?.Invoke();
    }

    public override void OnUpdate()
    {
        var pc = (PlayerController)owner;
        _hoverTimer += Time.deltaTime;

        // 落地 → 退出(方案三:AirAttack 退出条件 = 动画结束/落地)。
        // 需先滞空 MinHoverTime:低空攻击时贴地瞬间不立即退出,保留滞空/动画表现(原版事件驱动退出);
        // 动画事件正常时 OnAirAttackEnd 先触发(→FallState),此处仅作事件丢失/低空兜底
        if (pc.IsGrounded() && _hoverTimer >= MinHoverTime)
        {
            jump?.ResetJumps();   // 修复:空中攻击落地后不重置跳跃次数 → 之后按空格跳不了
            float h = Input.GetAxisRaw("Horizontal");
            stateMachine.ChangeState(Mathf.Abs(h) > 0.1f ? pc.MoveState : pc.IdleState);
        }
    }

    public override void OnExit()
    {
        // IsAirAttacking=false → AirAttack→Exit,回 Locomotion
        base.OnExit();

        var pc = (PlayerController)owner;

        // 恢复重力(原 OnAirAttackEnd / CancelAttackForJump)
        Rigidbody2D rb = pc.GetRigidbody();
        if (rb != null && !_airAttackGravityRestored)
        {
            rb.gravityScale = _airAttackOriginalGravity;
            _airAttackGravityRestored = true;
        }

        // 武器投掷重生判定:空中攻击结束(原 OnAirAttackEnd)
        weaponThrow?.OnAttackEnd();
    }

    // ── AnimationEvent 回调(经 PlayerCombat.OnAirAttackHitFrame/OnAirAttackEnd 薄转发) ──

    /// <summary>空中攻击命中帧:伤害判定(复用近战命中核心) + 触发空中武器投掷(原 OnAirAttackHitFrame)</summary>
    public void OnAirAttackHitFrame()
    {
        // 空中攻击按第 1 段结算(不参与地面连击推进)
        combat?.OnMeleeHitFrame(comboIndex: 1, comboLimit: 3, isAirAttack: true);
        weaponThrow?.OnAirAttackStart();
    }

    /// <summary>空中攻击结束(动画事件):退回下落状态(重力恢复在 OnExit)</summary>
    public void OnAirAttackEnd()
    {
        stateMachine.ChangeState(((PlayerController)owner).FallState);
    }
}
