using UnityEngine;

/// <summary>
/// 地面攻击状态 — 三连击单状态(方案 7.2 + 7.4)
/// comboIndex 推进唯一入口:OnExit(COMBO-CUT 已在 OnAnimEnd/OnUpdate 中推进,OnExit 跳过重复推进,见方案 7.5)
/// 预输入缓冲(_exitBufferTimer = comboExitWindow 0.12s)是手感核心,禁止删除(见方案 7.4)
/// 动画事件经 PlayerCombat 薄转发:OnAnimStart/OnAnimEnd/OnHitFrame
/// </summary>
public class PlayerAttackState : EntityState
{
    private readonly PlayerCombat combat;
    private readonly WeaponThrow weaponThrow;

    private int comboIndex = 1;
    private const int comboLimit = 3;
    private float timeLastExit;
    private bool comboQueued;          // 动画播放中按下攻击键 → 标记排队
    private bool isComboCut;           // 是否为 COMBO-CUT 直切（跳过子机 Exit）
    private float _exitBufferTimer;    // 动画结束后的预输入缓冲（方案 7.4）
    private float _stateTimer;         // 状态存活时长:超 MaxAttackDuration 强制退出(防动画事件链断裂永久锁死)

    /// <summary>攻击状态最大存活时长(秒):动画事件丢失/Play 失败时兜底退出,防 LocksInput 永久锁死</summary>
    private const float MaxAttackDuration = 2.5f;

    // 配置(原 PlayerCombat 序列化值:连击重置 0.6s / 后摇缓冲 0.12s,由 PlayerController 注入)
    private readonly float comboResetTimer;
    private readonly float comboExitWindow;

    /// <summary>当前连击段(伤害判定核心读取,原 PlayerCombat.comboIndex)</summary>
    public int ComboIndex => comboIndex;

    public override bool LocksInput => true;

    public PlayerAttackState(CharacterBase owner, StateMachine stateMachine, Animator anim,
        PlayerCombat combat, WeaponThrow weaponThrow, float comboResetTimer, float comboExitWindow)
        : base(owner, stateMachine, anim, new[] { AnimParams.IsAttacking })
    {
        this.combat = combat;
        this.weaponThrow = weaponThrow;
        this.comboResetTimer = comboResetTimer;
        this.comboExitWindow = comboExitWindow;
    }

    public override void OnEnter()
    {
        // IsAttacking=true → 攻击子机 Entry 路由(原 TriggerAttack 非 CUT 路径)
        base.OnEnter();

        comboQueued = false;   // 修复:中断后重进时清残留排队标记(否则 OnAnimEnd 误直切导致 Play 越界)
        _stateTimer = 0f;

        ResetComboIfNeeded();
        anim?.SetInteger(AnimParams.AttackIndex, comboIndex);

        // 记录攻击起始:消耗冷却 + 战斗态锁定(原 TryAttack/ExecuteMeleeAttack)
        combat?.ConsumeAttackCooldown();
        combat?.OnAttack?.Invoke();
    }

    public override void OnUpdate()
    {
        var pc = (PlayerController)owner;

        // 输入检测：动画播放中 或 预输入缓冲期内 按攻击键 → 排队/直切
        if (Input.GetMouseButtonDown(0) && comboIndex < comboLimit)
        {
            if (_exitBufferTimer > 0f)
            {
                // 缓冲窗口内点击:立即直切下一段(COMBO-CUT,无 idle 间隙)
                isComboCut = true;
                comboIndex++;
                anim?.SetInteger(AnimParams.AttackIndex, comboIndex);
                anim?.Play("Attack" + comboIndex, 0, 0f);
                _exitBufferTimer = 0f;
                isComboCut = false;
            }
            else
            {
                comboQueued = true;  // 动画播放中 → 排队,末帧处理
            }
        }

        // 预输入缓冲超时 → 退出攻击状态
        if (_exitBufferTimer > 0f)
        {
            _exitBufferTimer -= Time.deltaTime;
            if (_exitBufferTimer <= 0f)
            {
                float h = Input.GetAxisRaw("Horizontal");
                stateMachine.ChangeState(Mathf.Abs(h) > 0.1f ? pc.MoveState : pc.IdleState);
            }
        }

        // 兜底:状态存活超时强制退出(动画事件链断裂/Play 失败时防止 LocksInput 永久锁死)
        _stateTimer += Time.deltaTime;
        if (_stateTimer > MaxAttackDuration)
        {
            Debug.LogWarning("[Combat] AttackState 超时兜底退出(动画事件可能丢失)");
            float h = Input.GetAxisRaw("Horizontal");
            stateMachine.ChangeState(Mathf.Abs(h) > 0.1f ? pc.MoveState : pc.IdleState);
        }
    }

    public override void OnExit()
    {
        // IsAttacking=false → 攻击子机 Exit,回 Locomotion
        base.OnExit();

        // 只有正常退出才推进 comboIndex(COMBO-CUT 已在 Play() 前推进)
        if (!isComboCut)
        {
            comboIndex++;
            if (comboIndex > comboLimit) comboIndex = 1;
        }
        timeLastExit = Time.time;

        // 武器投掷重生判定:攻击链结束(原 ExitComboChain/OnAttackAnimationEnd/CancelAttackForJump)
        weaponThrow?.OnAttackEnd();
    }

    // ── AnimationEvent 回调(经 PlayerCombat.OnAttackAnimationStart/End 薄转发) ──

    /// <summary>动画事件:进入攻击表现 — 朝向跟随当前输入(原 EnterAttack)</summary>
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
            // 排队命中 → 直切下一段(与现 COMBO-CUT 同步推进)。
            // limit 检查:comboIndex=3(第三段)不直切,否则 Play("Attack4") 越界报错
            isComboCut = true;
            comboIndex++;
            anim?.SetInteger(AnimParams.AttackIndex, comboIndex);
            anim?.Play("Attack" + comboIndex, 0, 0f);
            comboQueued = false;
            isComboCut = false;
            _exitBufferTimer = 0f;  // 进入下一段,关闭预输入缓冲
            return;
        }
        comboQueued = false;

        // 无排队 → 打开预输入缓冲窗口(0.12s):窗口内点击仍响应并直切下一段,
        // 窗口超时才退出到 Idle/Move(保留动画结束后的预输入手感,见方案 7.4)
        _exitBufferTimer = comboExitWindow;
    }

    /// <summary>动画命中帧:伤害判定 — 调用 PlayerCombat 保留的伤害核心(原 OnMeleeHitFrame),不重复实现。
    /// P2 动画事件仍经 Relay 走 combat.OnMeleeHitFrame(),本方法为 P5 事件直连后的入口</summary>
    public void OnHitFrame()
    {
        combat?.OnMeleeHitFrame(comboIndex, comboLimit, isAirAttack: false);
    }

    private void ResetComboIfNeeded()
    {
        if (Time.time > timeLastExit + comboResetTimer) comboIndex = 1;
        if (comboIndex > comboLimit) comboIndex = 1;
    }
}
