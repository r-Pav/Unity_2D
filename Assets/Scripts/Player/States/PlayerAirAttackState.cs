using UnityEngine;

/// <summary>
/// 空中攻击状态 — 三连击单状态(与地面 PlayerAttackState 同构,复用 Attack1/2/3 动画 clip)。
/// 连段期间悬停(垂直速度归零 + 重力 0),连段结束(预输入超时 / 一套打完 / 受击)恢复重力下坠。
/// 一滞空一套:AirAttackUsed 标记(落地 ResetJumps 清),COMBO-CUT 不重进 OnEnter → 只标一次。
/// 动画事件经 PlayerCombat 薄转发:OnAnimStart / OnAnimEnd / OnHitFrame / OnInputOpen
/// </summary>
public class PlayerAirAttackState : EntityState
{
    private readonly PlayerCombat combat;
    private readonly PlayerJump jump;

    private int comboIndex = 1;
    private const int comboLimit = 3;
    private float timeLastExit;
    private bool comboQueued;          // 动画播放中按下攻击键 → 标记排队
    private bool isComboCut;           // 是否为 COMBO-CUT 直切（跳过子机 Exit）
    private float _exitBufferTimer;    // 动画结束后的预输入缓冲（方案 7.4,同地面）
    private float _stateTimer;         // 状态存活时长:超 MaxAttackDuration 强制退出(防动画事件链断裂永久锁死)

    /// <summary>输入门:攻击动画事件帧(OnAttackInputOpen)到达前 = false,此期间跳跃/冲刺输入只记录不执行</summary>
    public bool InputOpen { get; private set; }
    private bool _jumpQueued;   // 输入门前按下的跳跃意图(事件帧到达后自动执行)
    private bool _dashQueued;   // 输入门前按下的冲刺意图(事件帧到达后自动执行)

    private float _airAttackOriginalGravity = 1f;   // 空中攻击前的重力(连段结束/退出时恢复)
    private bool _airAttackGravityRestored = true;  // 重力是否已恢复(防重复恢复)
    private float _hoverTimer;                      // 滞空计时:落地退出需先滞空最小时间(防低空攻击瞬间退出)

    /// <summary>最小滞空时长(秒):进入后至少悬浮这么久才允许落地退出,与攻击动画时长同量级</summary>
    private const float MinHoverTime = 0.25f;

    /// <summary>输入门事件帧兜底超时(秒):动画事件未挂/丢失时自动开门+恢复重力,防永久悬空</summary>
    private const float InputOpenTimeout = 0.5f;

    /// <summary>每段攻击出手时的上挑力(累加):连段中越打越高,配合小重力缓沉形成节奏</summary>
    private const float AirLiftForce = 1f;

    /// <summary>出手上挑最低速度:累加后仍低于此值则提升到该值(保证下落中攻击也有上挑)</summary>
    private const float MinLiftSpeed = 1.5f;

    /// <summary>攻击中水平微调系数(0~1):空中攻击中左右可控,但力度弱于普通空中移动</summary>
    private const float AirControlScale = 0.3f;

    /// <summary>空中攻击状态最大存活时长(秒):动画事件丢失/Play 失败时兜底退出,防 LocksInput 永久锁死</summary>
    private const float MaxAttackDuration = 2.5f;

    // 配置(与地面同源:连击重置 0.6s / 后摇缓冲 0.12s,由 PlayerController 注入)
    private readonly float comboResetTimer;
    private readonly float comboExitWindow;

    /// <summary>当前连击段(伤害判定核心读取)</summary>
    public int ComboIndex => comboIndex;

    public override bool LocksInput => true;

    public PlayerAirAttackState(CharacterBase owner, StateMachine stateMachine, Animator anim,
        PlayerCombat combat, PlayerJump jump, float comboResetTimer, float comboExitWindow)
        : base(owner, stateMachine, anim, new[] { AnimParams.IsAirAttacking })
    {
        this.combat = combat;
        this.jump = jump;
        this.comboResetTimer = comboResetTimer;
        this.comboExitWindow = comboExitWindow;
    }

    public override void OnEnter()
    {
        // IsAirAttacking=true → Jump/Fall 普通过渡进 AirAttack 子机
        base.OnEnter();

        var pc = (PlayerController)owner;

        comboQueued = false;   // 中断后重进时清残留排队标记
        _stateTimer = 0f;
        InputOpen = false;     // 新一套空中攻击:输入门关闭,等事件帧打开
        _jumpQueued = false;
        _dashQueued = false;

        ResetComboIfNeeded();
        anim?.SetInteger(AnimParams.AttackIndex, comboIndex);

        // [2026-08-24 兜底] 强制直切 AirAttack 子机内对应段:绕过 Jump/Fall 的 Exit 过渡竞争
        // (坑 39 同款:Jump 的 IsJumping==false→Exit 优先于 IsAirAttacking→子机,动画层回 Entry 后
        //  Entry 无 IsAirAttacking 过渡 → 落 Locomotion 卡住。子机内状态名:Attack/Attack2/Attack3)
        if (anim != null)
        {
            string stateName = comboIndex == 1 ? "Attack" : "Attack" + comboIndex;
            string clipName = "Base Layer.AirAttack." + stateName;
            anim.Play(clipName, 0, 0f);
            var st = anim.GetCurrentAnimatorStateInfo(0);
            string clip = "?";
            if (anim.GetCurrentAnimatorClipInfo(0).Length > 0)
                clip = anim.GetCurrentAnimatorClipInfo(0)[0].clip != null ? anim.GetCurrentAnimatorClipInfo(0)[0].clip.name : "null";
            bool isLoc = st.IsName("Base Layer.Locomotion");
            bool isJump = st.IsName("Base Layer.Jump");
            bool isAir = st.IsName("Base Layer.AirAttack");
            bool isAtt = st.IsName("Base Layer.Attack");
            Debug.Log($"[AirAttack] OnEnter Play={clipName} IsName={st.IsName(clipName)} clip={clip} time={st.normalizedTime} | Loc={isLoc} Jump={isJump} Air={isAir} Att={isAtt}");
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

        // 第一段出手:给上挑力(每次攻击出手累加,越打越高)
        ApplyLift();

        // 一滞空一套:标记本次滞空已用过空中攻击(落地 ResetJumps 时清)
        jump?.MarkAirAttackUsed();

        // 记录攻击起始:消耗冷却 + 战斗态锁定
        combat?.ConsumeAttackCooldown();
        combat?.OnAttack?.Invoke();
    }

    public override void OnUpdate()
    {
        var pc = (PlayerController)owner;
        _hoverTimer += Time.deltaTime;

        // 空中攻击中朝向跟随输入(与地面一致)
        float h = Input.GetAxisRaw("Horizontal");
        if (Mathf.Abs(h) > 0.1f) pc.UpdateFacing(h);

        // 水平微调(弱化):攻击中左右可控,力度弱于普通空中移动;不控则保持惯性
        if (Mathf.Abs(h) > 0.01f && pc.GetRigidbody() != null)
        {
            float targetX = h * pc.AirMaxSpeed * AirControlScale;
            float newX = Mathf.MoveTowards(pc.GetRigidbody().velocity.x, targetX,
                pc.AirAcceleration * AirControlScale * Time.deltaTime);
            pc.SetVelocityPublic(x: newX);
        }

        // 空中攻击中 Shift → 打断攻击冲刺(与地面同款;ChangeState 先调 OnExit 恢复重力)
        // 输入门:事件帧前按 Shift 只记录意图,事件帧(OnAttackInputOpen)后自动执行
        if (Input.GetKeyDown(KeyCode.LeftShift) && pc.Dash != null && pc.Dash.CooldownReady)
        {
            if (!InputOpen)
            {
                _dashQueued = true;
            }
            else
            {
                stateMachine.ChangeState(pc.DashState);
                return;
            }
        }

        // 输入检测:动画播放中 或 预输入缓冲期内 按攻击键 → 排队/直切(与地面同款)
        if (Input.GetMouseButtonDown(0) && comboIndex < comboLimit)
        {
            if (_exitBufferTimer > 0f)
            {
                // 缓冲窗口内点击:立即直切下一段(COMBO-CUT,无 idle 间隙)
                isComboCut = true;
                comboIndex++;
                anim?.SetInteger(AnimParams.AttackIndex, comboIndex);
                InputOpen = false;   // 切段 = 新一段攻击的前摇,输入门重新关闭
                _jumpQueued = false;
                _dashQueued = false;
                anim?.Play("Attack" + comboIndex, 0, 0f);
                ApplyLift();   // 切段出手:再给一次上挑力(累加,越打越高)
                _exitBufferTimer = 0f;
                isComboCut = false;
            }
            else
            {
                comboQueued = true;  // 动画播放中 → 排队,末帧处理
            }
        }

        // 输入门事件帧兜底:动画事件未挂/丢失时,超时自动开门+恢复重力,防永久悬空锁输入
        if (!InputOpen && _hoverTimer > InputOpenTimeout)
            OnInputOpen();

        // 预输入缓冲超时 → 连段结束:恢复重力下坠退出
        if (_exitBufferTimer > 0f)
        {
            _exitBufferTimer -= Time.deltaTime;
            if (_exitBufferTimer <= 0f)
            {
                EndComboAndFall(pc, h);
            }
        }

        // 兜底:状态存活超时强制退出(动画事件链断裂/Play 失败时防止 LocksInput 永久锁死)
        _stateTimer += Time.deltaTime;
        if (_stateTimer > MaxAttackDuration)
        {
            Debug.LogWarning("[Combat] AirAttackState 超时兜底退出(动画事件可能丢失)");
            EndComboAndFall(pc, h);
        }
    }

    public override void OnExit()
    {
        // IsAirAttacking=false → 子机 Attack1/2/3 → Exit,回 Locomotion
        base.OnExit();

        // 恢复重力(连段结束/打断/受击退出都会走到这里;已恢复时防重跳过)
        RestoreGravity();
    }

    // ── AnimationEvent 回调(经 PlayerCombat 薄转发) ──

    /// <summary>动画事件:进入攻击表现 — 朝向跟随当前输入(与地面 OnAnimStart 同义)</summary>
    public void OnAnimStart()
    {
        var pc = (PlayerController)owner;
        if (combat != null)
            pc.UpdateFacing(combat.AttackDir);
    }

    /// <summary>动画事件:comboQueued → 直切下一段;否则开 _exitBufferTimer 预输入缓冲(与地面同款)</summary>
    public void OnAnimEnd()
    {
        if (comboQueued && comboIndex < comboLimit)
        {
            // 排队命中 → 直切下一段(与现 COMBO-CUT 同步推进)
            isComboCut = true;
            comboIndex++;
            anim?.SetInteger(AnimParams.AttackIndex, comboIndex);
            InputOpen = false;   // 切段 = 新一段攻击的前摇,输入门重新关闭
            _jumpQueued = false;
            _dashQueued = false;
            anim?.Play("Attack" + comboIndex, 0, 0f);
            ApplyLift();   // 切段出手:再给一次上挑力(累加,越打越高)
            comboQueued = false;
            isComboCut = false;
            _exitBufferTimer = 0f;
            return;
        }
        comboQueued = false;

        // 无排队 → 打开预输入缓冲窗口:窗口内点击直切下一段,窗口超时 = 连段结束下坠
        _exitBufferTimer = comboExitWindow;
    }

    /// <summary>动画命中帧:伤害判定 — 空中按当前连击段结算</summary>
    public void OnHitFrame()
    {
        combat?.OnMeleeHitFrame(comboIndex, comboLimit, isAirAttack: true);
    }

    /// <summary>输入门事件帧(动画事件 OnAttackInputOpen):打开输入,消费门前记录的跳跃/冲刺意图。
    /// 注意:空中不在此处恢复重力——悬停要持续到连段结束(EndComboAndFall),否则第二三段在下坠中打</summary>
    public void OnInputOpen()
    {
        InputOpen = true;

        // 门前按下的冲刺:直接执行(打断空中攻击 → OnExit 恢复重力)
        if (_dashQueued)
        {
            _dashQueued = false;
            var pc = (PlayerController)owner;
            if (pc.Dash != null && pc.Dash.CooldownReady)
            {
                stateMachine.ChangeState(pc.DashState);
                return;
            }
        }

        // 门前按下的跳跃:直接执行(打断空中攻击 → OnExit 恢复重力)
        if (_jumpQueued)
        {
            _jumpQueued = false;
            var pc = (PlayerController)owner;
            if (jump != null && jump.TryJump(pc))
            {
                stateMachine.ChangeState(pc.JumpState);
            }
        }
    }

    /// <summary>输入门前按下跳跃:记录意图,事件帧到达后自动跳</summary>
    public void QueueJump() => _jumpQueued = true;

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

    // ============================================================
    // 内部
    // ============================================================

    /// <summary>连段结束统一出口:恢复重力开始下坠 + 退出空中攻击状态。</summary>
    private void EndComboAndFall(PlayerController pc, float h)
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

    private void ResetComboIfNeeded()
    {
        if (Time.time > timeLastExit + comboResetTimer) comboIndex = 1;
        if (comboIndex > comboLimit) comboIndex = 1;
    }
}
