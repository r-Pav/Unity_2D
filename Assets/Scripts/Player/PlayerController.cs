using UnityEngine;

/// <summary>
/// 玩家控制器（主组件）— 移动 + 墙状态机
/// 跳跃 / 冲刺 / 生命值已拆为独立子组件：PlayerJump / PlayerDash / PlayerHealth
/// 子组件为必装依赖（RequireComponent），挂此脚本时 Unity 自动补齐
/// </summary>
[RequireComponent(typeof(PlayerJump))]
[RequireComponent(typeof(PlayerDash))]
[RequireComponent(typeof(PlayerHealth))]
public class PlayerController : PlayerCharacterBase
{
    // ============================================================
    // Singleton 注册表（场景内唯一 Player；调用方统一走 Instance，避免 FindObjectOfType 散布）
    // ============================================================

    private static PlayerController _instance;

    public static PlayerController Instance
    {
        get
        {
            if (_instance == null)
                _instance = FindObjectOfType<PlayerController>();
            return _instance;
        }
    }

    // ============================================================
    // 蹬墙跳
    // ============================================================

    [Header("蹬墙跳")]
    [Tooltip("远离墙弹出：水平力")]
    [SerializeField] private float wallKickForceX = 6f;

    [Tooltip("远离墙弹出：垂直力")]
    [SerializeField] private float wallKickForceY = 8f;

    public float WallKickForceX => wallKickForceX;
    public float WallKickForceY => wallKickForceY;

    // ============================================================
    // 空中移动
    // ============================================================

    [Header("空中移动")]
    [Tooltip("空中水平加速率（值越大越快到达目标速度）")]
    [SerializeField] private float airAcceleration = 20f;

    [Tooltip("空中最大水平速度")]
    [SerializeField] private float airMaxSpeed = 4f;

    // ============================================================
    // 子模块引用
    // ============================================================

    private PlayerCombat combat;
    private PlayerGroundPound groundPound;
    private PlayerStomp stomp;
    private SkillManager skillManager;
    private SkillPool skillPool;
    private SkillPointManager skillPointManager;
    private PassiveEquipManager passiveEquipManager;
    private PlayerJump jump;
    private PlayerDash dash;
    private PlayerHealth health;

    // [P4/P5] 武器技能联动 & 组合合成系统
    private WeaponSkillLink weaponSkillLink;
    private CombinationCraftSystem combinationCraftSystem;

    // ============================================================
    // 运行时状态
    // ============================================================

    /// <summary>墙上状态机</summary>
    public StateMachine WallStateMachine { get; private set; }
    public WallClingState WallClingState { get; private set; }

    /// <summary>翻顶/墙跳后短暂冻结输入的计时器（由状态类设置）</summary>
    public float FreezeTimer { get; set; }

    /// <summary>Whether gameplay input should be processed.</summary>
    public bool InputEnabled { get; set; } = true;

    /// <summary>
    /// UI 面板打开时设为 true，阻止滚轮切换攻击模式。
    /// 由 PanelManager 等 UI 控制器设置。
    /// </summary>
    public bool ScrollBlocked { get; set; }

    // ============================================================
    // 战斗态锁定（P2）
    // ============================================================

    /// <summary>脱离战斗计时器（攻击/受伤时重置，归零后退出战斗）</summary>
    private float combatTimer;
    /// <summary>脱离战斗等待秒数</summary>
    private const float CombatExitDelay = 3f;

    // ============================================================
    // 生命周期
    // ============================================================

    protected override void Awake()
    {
        base.Awake();
        combat = GetComponent<PlayerCombat>();
        groundPound = GetComponent<PlayerGroundPound>();
        stomp = GetComponent<PlayerStomp>();
        skillManager = GetComponent<SkillManager>();
        skillPool = GetComponent<SkillPool>();
        skillPointManager = GetComponent<SkillPointManager>();
        passiveEquipManager = GetComponent<PassiveEquipManager>();
        weaponSkillLink = GetComponent<WeaponSkillLink>();
        combinationCraftSystem = GetComponent<CombinationCraftSystem>();

        // 自动创建组件（优先获取已有，无则创建）
        if (detect == null) detect = gameObject.AddComponent<PlayerDetectionConfig>();
        jump = GetComponent<PlayerJump>();
        dash = GetComponent<PlayerDash>();
        health = GetComponent<PlayerHealth>();

        // ── 战斗态锁定：攻击/受伤时触发，timer 清零后退出 ──
        if (combat != null)
            combat.OnAttack += OnCombatAction;
        if (health != null)
            health.OnDamaged += OnCombatAction;

        // ── 创建墙状态机 + 贴墙状态 ──
        WallStateMachine = new StateMachine();
        WallClingState = new WallClingState(this, WallStateMachine);
    }

    private void Start()
    {
        Input.imeCompositionMode = IMECompositionMode.Off;

        // Start() 在所有 OnEnable() 之后执行，确保 HUD 已订阅事件
        EventBus.Trigger(new PlayerHealthChangedEvent(
            health != null ? health.CurrentHealth : 0f,
            health != null ? health.MaxHealth : 0f));
    }

    protected override void OnUpdate()
    {
        if (!InputEnabled) return;

        // AirHurt 落地检测独立处理（需在 IsActionLocked 之前）
        if (health != null && health.IsAirHurt)
        {
            if (grounded)
                health.ClearAirHurt();
            return;
        }

        // 计时器递减必须先于锁定判定：FreezeTimer 在 IsActionLocked 里被检查，
        // 若递减在锁定 return 之后则永远无法归零 → 永久锁死（蹬墙跳后卡下落动画）
        UpdateCooldowns();

        if (IsActionLocked()) return;

        // 攻击朝向跟随当前输入（Fix：UpdateFacing 在 FixedUpdate 里，攻击在 Update 里会慢一帧）
        float h = Input.GetAxisRaw("Horizontal");
        if (Mathf.Abs(h) > 0.1f) UpdateFacing(h);

        // 冲刺组件：dash 中提早 return（跳过移动/跳跃/墙逻辑）
        if (dash?.OnPlayerUpdate(this) == true) return;

        DetectWallCling();
        WallStateMachine.Update();

        // 跳跃组件
        jump?.OnPlayerUpdate(this);

        // 生命值组件（保持接口一致，当前无每帧逻辑）
        health?.OnPlayerUpdate(this);

        UpdateSubModules();
    }

    /// <summary>聚合所有输入锁定源。新增状态在此加条件即可。</summary>
    private bool IsActionLocked()
    {
        if (!InputEnabled) return true;
        if (health == null) return false;
        return health.IsHurt
            || (combat != null && combat.IsInputLocked)
            || FreezeTimer > 0f;
    }

    // ============================================================
    // OnUpdate 流水线方法
    // ============================================================

    /// <summary>冻结计时递减 + 战斗态计时</summary>
    private void UpdateCooldowns()
    {
        if (FreezeTimer > 0f)
        {
            FreezeTimer -= Time.deltaTime;
            if (FreezeTimer < 0f) FreezeTimer = 0f;
        }

        if (combatTimer > 0f)
        {
            combatTimer -= Time.deltaTime;
            if (combatTimer <= 0f)
            {
                combatTimer = 0f;
                passiveEquipManager?.SetCombatState(false);
            }
        }
    }

    /// <summary>贴墙入口检测：空中 + 碰墙 + 不在上升 → 进入贴墙</summary>
    private void DetectWallCling()
    {
        if (WallStateMachine == null || WallStateMachine.CurrentState != null) return;
        if (grounded) return;
        if (!isTouchingWall) return;
        if (FreezeTimer > 0f) return;
        if (rb.velocity.y > 0f) return;

        WallStateMachine.ChangeState(WallClingState);
    }

    /// <summary>调用子模块 OnPlayerUpdate</summary>
    private void UpdateSubModules()
    {
        combat?.OnPlayerUpdate(this);
        groundPound?.OnPlayerUpdate(this);
        stomp?.OnPlayerUpdate(this);
        skillManager?.OnPlayerUpdate(this);
    }

    protected override void OnFixedUpdate()
    {
        if (!InputEnabled) return;

        if (health != null && health.IsAirHurt) return;
        if (IsActionLocked()) return;
        if (dash != null && dash.IsDashing) return;

        float h = Input.GetAxisRaw("Horizontal");

        // 贴墙时阻止朝墙推（避免collider嵌入墙体）
        if (isTouchingWall && Mathf.Sign(h) == wallDirection && wallDirection != 0)
            h = 0f;

        // 墙状态活跃时跳过普通移动（由状态类自己处理物理）
        if (WallStateMachine.CurrentState != null)
            return;

        if (!grounded)
        {
            // 空中加速度控制
            float targetX = h * airMaxSpeed;
            float newX = Mathf.MoveTowards(rb.velocity.x, targetX, airAcceleration * Time.fixedDeltaTime);
            SetVelocityPublic(x: newX);
        }
        else
        {
            if (Mathf.Abs(h) > 0.1f) Move(h);
            else Move(0f);
        }
    }

    // ============================================================
    // 跳跃执行（覆写：优化速度处理，消除 grounded 闪烁干扰）
    // 公开给 PlayerJump 调用
    // ============================================================

    /// <summary>执行跳跃（供 PlayerJump 组件调用）</summary>
    public void ExecuteJump(float force)
    {
        Jump(force);
    }

    protected override void Jump(float force)
    {
        // 跳跃时归零 Y 速度再施加力，防止踩头/弹跳等外部速度叠加
        SetVelocity(y: 0f);
        rb.AddForce(Vector2.up * force, ForceMode2D.Impulse);
    }

    // ============================================================
    // 击退标志设置（供 PlayerHealth 组件调用）
    // ============================================================

    /// <summary>设置击退状态（供 PlayerHealth 组件调用）</summary>
    public void SetKnockedBack(bool value)
    {
        isKnockedBack = value;
    }

    // ============================================================
    // 受伤 / 死亡（转发到 PlayerHealth 组件）
    // ============================================================

    /// <summary>受到伤害（被敌人攻击组件调用）</summary>
    public void TakeDamage(float amount)
    {
        health?.TakeDamage(amount);
    }

    /// <summary>受到伤害并击退（传入攻击方向）</summary>
    public void TakeDamageWithKnockback(float amount, Vector2 attackDir)
    {
        health?.TakeDamageWithKnockback(amount, attackDir);
    }

    // ============================================================
    // 子类访问接口（部分转发到子组件）
    // ============================================================

    public int GetFacing() => facing;
    public bool IsDashing() => dash != null && dash.IsDashing;
    public new bool IsGrounded() => base.IsGrounded;
    public Rigidbody2D GetRigidbody() => rb;

    public float CurrentHealth => health != null ? health.CurrentHealth : 0f;
    public float MaxHealth => health != null ? health.MaxHealth : 0f;

    public PlayerCombat Combat => combat;
    public StatModifierManager StatModManager => statModManager;
    public SkillPointManager SkillPointManager => skillPointManager;
    public SkillPool SkillPool => skillPool;
    public PassiveEquipManager PassiveEquipManager => passiveEquipManager;
    public WeaponSkillLink WeaponSkillLink => weaponSkillLink;
    public CombinationCraftSystem CombinationCraftSystem => combinationCraftSystem;

    // ============================================================
    // 战斗态锁定（P2）
    // ============================================================

    /// <summary>攻击/受伤时重置战斗计时器并进入战斗态（防重：已在战斗态只刷新计时器）</summary>
    private void OnCombatAction()
    {
        if (combatTimer > 0f)
        {
            combatTimer = CombatExitDelay;  // 仅刷新计时器，不重复触发 refCount++
            return;
        }
        combatTimer = CombatExitDelay;
        passiveEquipManager?.SetCombatState(true);
    }

    // ============================================================
    // Gizmos
    // ============================================================

    protected override void OnDrawGizmosSelected()
    {
        base.OnDrawGizmosSelected();

        if (dash == null || dash.CooldownReady)
        {
            Gizmos.color = new Color(0f, 1f, 1f, 0.5f);
            Gizmos.DrawRay(
                transform.position + Vector3.up * 0.5f,
                Vector3.right * facing * 2f);
        }
    }
}
