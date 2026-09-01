using UnityEngine;

/// <summary>
/// 三连击攻击状态基类 — 地面/空中共用(PlayerAttackState / PlayerAirAttackState 继承)。
/// 公共:连击段推进(COMBO-CUT 直切/排队)、输入门(OnAttackInputOpen 事件帧)、预输入缓冲、
/// 超时兜底、动画事件转发(OnAnimStart/End/OnHitFrame/OnInputOpen)。
/// 差异由子类钩子承担:OnComboEnter / OnComboUpdate / OnComboExit / OnComboCut /
/// AdvanceComboOnExit(空中不推进连击段) / EndComboAndChangeState(空中先恢复重力再落态)。
/// </summary>
public abstract class PlayerComboState : EntityState
{
    protected readonly PlayerCombat combat;

    protected int comboIndex = 1;
    protected const int comboLimit = 3;
    protected float timeLastExit;
    protected bool comboQueued;          // 动画播放中按下攻击键 → 标记排队
    protected bool isComboCut;           // 是否为 COMBO-CUT 直切（跳过子机 Exit 推进）
    protected float _exitBufferTimer;    // 动画结束后的预输入缓冲（方案 7.4）
    protected float _stateTimer;         // 状态存活时长:超 MaxAttackDuration 强制退出(防动画事件链断裂永久锁死)

    /// <summary>攻击持续 VFX 锚点(attack_VFX 子物体上的 AttackVFXAnchor;未配置时为 null,空安全)</summary>
    private AttackVFXAnchor _vfx;

    /// <summary>输入门:攻击动画事件帧(OnAttackInputOpen)到达前 = false,此期间跳跃/冲刺输入只记录不执行</summary>
    public bool InputOpen { get; protected set; }
    protected bool _jumpQueued;   // 输入门前按下的跳跃意图(事件帧到达后自动执行)
    protected bool _dashQueued;   // 输入门前按下的冲刺意图(事件帧到达后自动执行)

    protected readonly float comboResetTimer;
    protected readonly float comboExitWindow;

    /// <summary>攻击状态最大存活时长(秒):动画事件丢失/Play 失败时兜底退出,防 LocksInput 永久锁死</summary>
    protected const float MaxAttackDuration = 2.5f;

    /// <summary>是否为空中攻击(伤害判定 OnMeleeHitFrame 的 isAirAttack 参数)</summary>
    protected abstract bool IsAirAttack { get; }

    /// <summary>当前连击段(伤害判定核心读取)</summary>
    public int ComboIndex => comboIndex;

    public override bool LocksInput => true;

    protected PlayerComboState(CharacterBase owner, StateMachine stateMachine, Animator anim,
        string[] animBoolNames, PlayerCombat combat, float comboResetTimer, float comboExitWindow)
        : base(owner, stateMachine, anim, animBoolNames)
    {
        this.combat = combat;
        this.comboResetTimer = comboResetTimer;
        this.comboExitWindow = comboExitWindow;
    }

    public override void OnEnter()
    {
        base.OnEnter();
        comboQueued = false;
        _stateTimer = 0f;
        InputOpen = false;     // 新一段攻击:输入门关闭,等事件帧打开
        _jumpQueued = false;
        _dashQueued = false;
        ResetComboIfNeeded();
        anim?.SetInteger(AnimParams.AttackIndex, comboIndex);
        OnComboEnter();
        combat?.ConsumeAttackCooldown();
        combat?.OnAttack?.Invoke();

        // 攻击持续 VFX:进入连击播第 1 段槽(空中 slot_Air;未配置锚点则跳过)
        if (_vfx == null) _vfx = owner.GetComponentInChildren<AttackVFXAnchor>(true);
        _vfx?.Show(IsAirAttack ? "slot_Air" : "slot_1");
    }

    public override void OnUpdate()
    {
        var pc = (PlayerController)owner;

        // 攻击中朝向跟随输入(连击转向灵敏:伤害判定 OnMeleeHitFrame 读 AttackDir,同步新方向)
        float h = Input.GetAxisRaw("Horizontal");
        if (Mathf.Abs(h) > 0.1f) pc.UpdateFacing(h);

        OnComboUpdate();

        // 攻击中 Shift → 打断攻击冲刺。输入门:事件帧前按 Shift 只记录意图,事件帧后自动执行
        if (Input.GetKeyDown(KeyCode.LeftShift) && pc.Dash != null && pc.Dash.CooldownReady)
        {
            if (!InputOpen)
                _dashQueued = true;
            else
            {
                stateMachine.ChangeState(pc.DashState);
                return;
            }
        }

        // 输入检测：动画播放中 或 预输入缓冲期内 按攻击键 → 排队/直切
        if (Input.GetMouseButtonDown(0) && comboIndex < comboLimit)
        {
            if (_exitBufferTimer > 0f)
                CutToNextCombo();   // 缓冲窗口内点击:立即直切下一段(COMBO-CUT,无 idle 间隙)
            else
                comboQueued = true; // 动画播放中 → 排队,末帧处理
        }

        // 预输入缓冲超时 → 连段结束退出
        if (_exitBufferTimer > 0f)
        {
            _exitBufferTimer -= Time.deltaTime;
            if (_exitBufferTimer <= 0f)
                EndComboAndChangeState(pc, h);
        }

        // 兜底:状态存活超时强制退出(动画事件链断裂/Play 失败时防止 LocksInput 永久锁死)
        _stateTimer += Time.deltaTime;
        if (_stateTimer > MaxAttackDuration)
        {
            Debug.LogWarning("[Combat] AttackState 超时兜底退出(动画事件可能丢失)");
            EndComboAndChangeState(pc, h);
        }
    }

    public override void OnExit()
    {
        base.OnExit();
        AdvanceComboOnExit();
        timeLastExit = Time.time;
        OnComboExit();

        // 攻击结束:收起持续特效(淡出)
        _vfx?.Hide();
    }

    // ── AnimationEvent 回调(经 PlayerCombat 薄转发) ──

    /// <summary>动画事件:进入攻击表现 — 朝向跟随当前输入</summary>
    public void OnAnimStart()
    {
        var pc = (PlayerController)owner;
        if (combat != null)
            pc.UpdateFacing(combat.AttackDir);
    }

    /// <summary>动画事件:comboQueued → 直切下一段;否则开 _exitBufferTimer 预输入缓冲(方案 7.2/7.4)</summary>
    public void OnAnimEnd()
    {
        if (comboQueued && comboIndex < comboLimit)
        {
            CutToNextCombo();   // 排队命中 → 直切下一段
            comboQueued = false;
            _exitBufferTimer = 0f;  // 进入下一段,关闭预输入缓冲
            return;
        }
        comboQueued = false;
        _exitBufferTimer = comboExitWindow;
    }

    /// <summary>动画命中帧:伤害判定(空中/地面按各自 IsAirAttack 结算)</summary>
    public void OnHitFrame()
    {
        combat?.OnMeleeHitFrame(comboIndex, comboLimit, IsAirAttack);
    }

    /// <summary>输入门事件帧(动画事件 OnAttackInputOpen):打开输入,消费门前记录的跳跃/冲刺意图</summary>
    public void OnInputOpen()
    {
        InputOpen = true;
        var pc = (PlayerController)owner;

        if (_dashQueued)
        {
            _dashQueued = false;
            if (pc.Dash != null && pc.Dash.CooldownReady)
            {
                stateMachine.ChangeState(pc.DashState);
                return;
            }
        }
        if (_jumpQueued)
        {
            _jumpQueued = false;
            if (pc.JumpComp != null && pc.JumpComp.TryJump(pc))
                stateMachine.ChangeState(pc.JumpState);
        }
    }

    /// <summary>输入门前按下跳跃:记录意图,事件帧到达后自动跳</summary>
    public void QueueJump() => _jumpQueued = true;

    // ── 子类差异钩子 ──

    protected virtual void OnComboEnter() { }
    protected virtual void OnComboUpdate() { }
    protected virtual void OnComboExit() { }

    /// <summary>连段切换(COMBO-CUT 直切/排队直切共走此路径):换对应槽特效(地面 slot_1/2/3,空中 slot_Air)</summary>
    protected virtual void OnComboCut()
    {
        if (_vfx == null) _vfx = owner.GetComponentInChildren<AttackVFXAnchor>(true);
        _vfx?.Show(IsAirAttack ? "slot_Air" : "slot_" + comboIndex);
    }

    /// <summary>连段段数推进(OnExit):地面推进续段(0.6s 内再攻击延续段数);空中不推进(一滞空一套)</summary>
    protected virtual void AdvanceComboOnExit()
    {
        if (!isComboCut)
        {
            comboIndex++;
            if (comboIndex > comboLimit) comboIndex = 1;
        }
    }

    /// <summary>连段结束退出(预输入超时/超时兜底):默认回 Idle/Move;空中先恢复重力,贴地/空中分落</summary>
    protected virtual void EndComboAndChangeState(PlayerController pc, float h)
    {
        stateMachine.ChangeState(Mathf.Abs(h) > 0.1f ? pc.MoveState : pc.IdleState);
    }

    // ── 内部 ──

    /// <summary>COMBO-CUT 直切下一段(缓冲窗口内点击 / 动画末帧排队命中共用):推进段数 + 直切动画 + 输入门重置</summary>
    private void CutToNextCombo()
    {
        isComboCut = true;
        comboIndex++;
        anim?.SetInteger(AnimParams.AttackIndex, comboIndex);
        InputOpen = false;   // 切段 = 新一段攻击的前摇,输入门重新关闭
        _jumpQueued = false;
        _dashQueued = false;
        anim?.Play("Attack" + comboIndex, 0, 0f);
        OnComboCut();
        isComboCut = false;
    }

    private void ResetComboIfNeeded()
    {
        if (Time.time > timeLastExit + comboResetTimer) comboIndex = 1;
        if (comboIndex > comboLimit) comboIndex = 1;
    }
}
