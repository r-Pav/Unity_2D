using UnityEngine;

/// <summary>
/// 空中攻击状态 — 三连击单状态(与地面同构,复用 Attack1/2/3 动画 clip),继承 PlayerComboState。
/// 差异:悬停(重力小值缓沉)/ 上挑(ApplyLift,越打越高)/ 一滞空一套(每次进入强制从第 1 段)/
/// OnEnter 强制直切 AirAttack 子机(坑 39 兜底)/ 连段结束恢复重力落态。
/// </summary>
public class PlayerAirAttackState : PlayerComboState
{
    private readonly PlayerJump jump;

    private float _airAttackOriginalGravity = 1f;   // 空中攻击前的重力(连段结束/退出时恢复)
    private bool _airAttackGravityRestored = true;  // 重力是否已恢复(防重复恢复)
    private float _hoverTimer;                      // 滞空计时:落地退出需先滞空最小时间(防低空攻击瞬间退出)

    private const float MinHoverTime = 0.25f;
    private const float InputOpenTimeout = 0.5f;    // 输入门事件帧兜底超时(动画事件丢失时自动开门+恢复重力)
    private const float AirLiftForce = 1f;          // 每段出手上挑力(累加,连段越打越高)
    private const float MinLiftSpeed = 1.5f;        // 出手上挑最低速度
    private const float AirControlScale = 0.3f;     // 攻击中水平微调系数(弱于普通空中移动)

    public PlayerAirAttackState(CharacterBase owner, StateMachine stateMachine, Animator anim,
        PlayerCombat combat, PlayerJump jump, float comboResetTimer, float comboExitWindow)
        : base(owner, stateMachine, anim, new[] { AnimParams.IsAirAttacking }, combat, comboResetTimer, comboExitWindow)
    {
        this.jump = jump;
    }

    protected override bool IsAirAttack => true;

    protected override void OnComboEnter()
    {
        var pc = (PlayerController)owner;
        comboIndex = 1;   // 一滞空一套:每次进入强制从第 1 段(空中不推进续段)

        // [2026-08-24 兜底] 强制直切 AirAttack 子机内对应段:绕过 Jump/Fall 的 Exit 过渡竞争
        // (坑 39 同款:Jump 的 IsJumping==false→Exit 优先于 IsAirAttacking→子机,动画层回 Entry 后
        //  Entry 无 IsAirAttacking 过渡 → 落 Locomotion 卡住。子机内状态名:Attack/Attack2/Attack3)
        if (anim != null)
        {
            string stateName = comboIndex == 1 ? "Attack" : "Attack" + comboIndex;
            anim.Play("Base Layer.AirAttack." + stateName, 0, 0f);
        }

        // 攻击朝向跟随当前输入
        if (combat != null)
            pc.UpdateFacing(combat.AttackDir);

        // 悬停:水平速度减半 + 垂直速度保留 30%(不硬切 0,保留惯性,过渡自然)
        // + 重力小值缓沉(0.15,不归零,视觉更活;连段结束才恢复原重力,期间不下坠)
        Rigidbody2D rb = pc.GetRigidbody();
        if (rb != null)
        {
            rb.velocity = new Vector2(rb.velocity.x * 0.5f, rb.velocity.y * 0.3f);
            _airAttackOriginalGravity = rb.gravityScale;
            rb.gravityScale = Mathf.Min(_airAttackOriginalGravity, 0.3f);
            _airAttackGravityRestored = false;
        }
        _hoverTimer = 0f;

        // 第一段出手:给上挑力
        ApplyLift();

        // 一滞空一套:标记本次滞空已用过空中攻击(落地 ResetJumps 时清)
        jump?.MarkAirAttackUsed();
    }

    protected override void OnComboUpdate()
    {
        var pc = (PlayerController)owner;
        _hoverTimer += Time.deltaTime;

        // 水平微调(弱化):攻击中左右可控,力度弱于普通空中移动;不控则保持惯性
        float h = Input.GetAxisRaw("Horizontal");
        if (Mathf.Abs(h) > 0.01f && pc.GetRigidbody() != null)
        {
            float targetX = h * pc.AirMaxSpeed * AirControlScale;
            float newX = Mathf.MoveTowards(pc.GetRigidbody().velocity.x, targetX,
                pc.AirAcceleration * AirControlScale * Time.deltaTime);
            pc.SetVelocityPublic(x: newX);
        }

        // 输入门事件帧兜底:动画事件未挂/丢失时,超时自动开门,防永久悬空锁输入
        // (悬停要持续到连段结束 EndComboAndChangeState,不在 OnInputOpen 恢复重力)
        if (!InputOpen && _hoverTimer > InputOpenTimeout)
            OnInputOpen();
    }

    protected override void OnComboCut()
    {
        ApplyLift();   // 切段出手:再给一次上挑力(累加,越打越高)
    }

    protected override void OnComboExit()
    {
        RestoreGravity();
    }

    protected override void AdvanceComboOnExit()
    {
        // 空中不推进连击段:一滞空一套,中断/打完后下次进入由 OnComboEnter 强制 comboIndex=1
    }

    protected override void EndComboAndChangeState(PlayerController pc, float h)
    {
        RestoreGravity();   // 开始下坠

        // 空中 → 落 FallState;已贴地(低空攻击)→ 直接落 Idle/Move
        if (pc.IsGrounded())
        {
            jump?.ResetJumps();
            stateMachine.ChangeState(Mathf.Abs(h) > 0.1f ? pc.MoveState : pc.IdleState);
        }
        else
        {
            stateMachine.ChangeState(pc.FallState);
        }
    }

    /// <summary>出手上挑:每段攻击开始时给一个向上的力(累加,连段越打越高)。
    /// 累加后若仍低于 MinLiftSpeed(如下落中攻击被 vy×0.3 削到负值),提升到最低上挑速度,
    /// 保证每段出手都有可见的上升感</summary>
    private void ApplyLift()
    {
        var pc = (PlayerController)owner;
        Rigidbody2D rb = pc != null ? pc.GetRigidbody() : null;
        if (rb != null)
        {
            float newVy = rb.velocity.y + AirLiftForce;
            if (newVy < MinLiftSpeed) newVy = MinLiftSpeed;
            rb.velocity = new Vector2(rb.velocity.x, newVy);
        }
    }

    /// <summary>恢复重力(带防重标志):连段结束/打断/受击/超时统一入口</summary>
    private void RestoreGravity()
    {
        if (_airAttackGravityRestored) return;
        var pc = (PlayerController)owner;
        Rigidbody2D rb = pc != null ? pc.GetRigidbody() : null;
        if (rb != null)
        {
            rb.gravityScale = _airAttackOriginalGravity;
            _airAttackGravityRestored = true;
        }
    }
}
