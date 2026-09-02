using UnityEngine;

/// <summary>
/// 玩家控制器（主组件）— 移动 + 统一 FSM(PlayerFsm)
/// 跳跃 / 冲刺 / 生命值已拆为独立子组件：PlayerJump / PlayerDash / PlayerHealth
/// 子组件为必装依赖（RequireComponent），挂此脚本时 Unity 自动补齐
/// P1 改造: 由组件编排式改为 PlayerFsm 统一状态机驱动(Idle/Move/Jump/Fall/WallCling)
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

    public float AirAcceleration => airAcceleration;
    public float AirMaxSpeed => airMaxSpeed;

    // ============================================================
// 子模块引用
// ============================================================

    private PlayerCombat combat;
    private PlayerGroundPound groundPound;
    private SkillManager skillManager;
    private SkillPool skillPool;
    private SkillPointManager skillPointManager;
    private PassiveEquipManager passiveEquipManager;
    private PlayerJump jump;
    private PlayerDash dash;
    private PlayerHealth health;
    private PlayerTeleport teleport;

    // [P4/P5] 武器技能联动 & 组合合成系统
    private WeaponSkillLink weaponSkillLink;
    private CombinationCraftSystem combinationCraftSystem;

    // ============================================================
    // 玩家 FSM(统一状态机) — P1: Idle/Move/Jump/Fall + WallCling
    // ============================================================

    /// <summary>玩家统一状态机(原 WallStateMachine 已并入;P2 起 Attack/Block 等也挂入)</summary>
    public StateMachine PlayerFsm { get; private set; }

    public PlayerIdleState IdleState { get; private set; }
    public PlayerMoveState MoveState { get; private set; }
    public PlayerJumpState JumpState { get; private set; }
    public PlayerFallState FallState { get; private set; }

    /// <summary>贴墙状态(挂入 PlayerFsm;PlayerJump 翻顶/状态切换时调用)</summary>
    public WallClingState WallClingState { get; private set; }

    // P2:战斗状态挂入 PlayerFsm
    public PlayerAttackState AttackState { get; private set; }
    public PlayerAirAttackState AirAttackState { get; private set; }
    public PlayerBlockState BlockState { get; private set; }
    public PlayerGroundPoundState GroundPoundState { get; private set; }

    // P3a:受击/死亡状态挂入 PlayerFsm
    public PlayerHurtState HurtState { get; private set; }
    public PlayerAirHurtState AirHurtState { get; private set; }
    public PlayerDeadState DeadState { get; private set; }

    // P3b:冲刺/技能释放状态挂入 PlayerFsm
    public PlayerDashState DashState { get; private set; }
    public PlayerSkillCastState SkillCastState { get; private set; }

    // [阶段7] 瞄准选点状态（传送后慢动作选点；由 ComboLv3Executor 切入/退出）
    public PlayerAimingState AimingState { get; private set; }

    // [重音背刺] 自动重音窗口内 F 触发的背刺状态(方案 v2,无连打)
    public PlayerBackstabState BackstabState { get; private set; }

    // ============================================================
    // 状态转发属性 — 动画聚合 / 敌人 AI 查询统一走这里
    // ============================================================

    /// <summary>是否跳跃上升(FSM 当前状态为 PlayerJumpState)</summary>
    public bool IsJumping => PlayerFsm != null && PlayerFsm.CurrentState is PlayerJumpState;
    /// <summary>是否下落(FSM 当前状态为 PlayerFallState)</summary>
    public bool IsFalling => PlayerFsm != null && PlayerFsm.CurrentState is PlayerFallState;

    // P2:战斗状态已迁入 FSM,转发属性查 FSM 状态类型保持签名稳定(动画聚合/敌人 AI 查询)
    public bool IsAttacking => PlayerFsm != null && PlayerFsm.CurrentState is PlayerAttackState;
    public bool IsBlocking => PlayerFsm != null && PlayerFsm.CurrentState is PlayerBlockState;
    public bool IsAirAttacking => PlayerFsm != null && PlayerFsm.CurrentState is PlayerAirAttackState;
    // P3a:受击/死亡状态迁入 FSM,转发属性查 FSM 状态类型(签名不变 — EnemyControllerBase.CanAttack 读 ph.IsAirHurt 走 PlayerHealth 转发)
    public bool IsHurt => PlayerFsm != null && PlayerFsm.CurrentState is PlayerHurtState;
    public bool IsAirHurt => PlayerFsm != null && PlayerFsm.CurrentState is PlayerAirHurtState;
    public bool IsDead => PlayerFsm != null && PlayerFsm.CurrentState is PlayerDeadState;

    /// <summary>
    /// 翻顶/墙跳后短暂冻结输入的计时器（由 WallClingState/PlayerCharacterBase.TryVault 设置:墙跳 0.1s / 翻顶 0.15s）。
    /// P3a 曾计划删除并改 PlayerFreezeState,P3b 核对后保留:它由墙状态类按需写入、UpdateCooldowns 递减、
    /// IsActionLocked/DetectWallCling 读取,作为墙跳/翻顶后的输入冻结间隙仍被正常使用 → 保留并持续维护。
    /// 2026-08-14:基类 PlayerCharacterBase 增加虚属性,翻顶统一入口 TryVault 直接写入,此处改为 override。
    /// </summary>
    // 手动属性(不用自动属性):团结引擎会把自动属性 backing field <FreezeTimer>k__BackingField 纳入序列化检查,
    // 与基类曾定义的同名自动属性冲突(报 "serialized multiple times");手动 backing field 名不同,彻底规避。
    private float _freezeTimer;
    public float FreezeTimer { get => _freezeTimer; set => _freezeTimer = value; }

    /// <summary>翻顶执行后冻结输入(由 PlayerCharacterBase.OnVaultExecuted 钩子回调)</summary>
    protected override void OnVaultExecuted() => FreezeTimer = VaultFreezeTime;

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

    /// <summary>attackingStat(战斗状态标识):敌人仇恨全局状态位,管道实心由它驱动。Awake 自动创建。</summary>
    private AttackingStat attackingStat;
    /// <summary>公开访问 attackingStat 组件</summary>
    public AttackingStat AttackingStatComp => attackingStat;

    // ============================================================
    // 生命周期
    // ============================================================

    protected override void Awake()
    {
        base.Awake();
        combat = GetComponent<PlayerCombat>();
        groundPound = GetComponent<PlayerGroundPound>();
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
        // 传送组件(重音背刺复用 PlayerTeleport):优先获取已有,无则创建(与 PlayerDetectionConfig 同款)
        teleport = GetComponent<PlayerTeleport>();
        if (teleport == null) teleport = gameObject.AddComponent<PlayerTeleport>();

        // attackingStat(战斗状态标识):优先获取已有,无则创建(与 detect/teleport 同款)。
        // 由敌人仇恨上报驱动管道实心;组件不存在则管道永不锁,故必须确保创建。
        if (attackingStat == null) attackingStat = gameObject.AddComponent<AttackingStat>();

        // ── 战斗态锁定：攻击/受伤时触发，timer 清零后退出 ──
        if (combat != null)
            combat.OnAttack += OnCombatAction;
        if (health != null)
            health.OnDamaged += OnCombatAction;

        // ── 创建统一状态机 + 状态实例(含贴墙状态 + P2 战斗状态) ──
        PlayerFsm = new StateMachine();
        IdleState = new PlayerIdleState(this, PlayerFsm, _animator, jump);
        MoveState = new PlayerMoveState(this, PlayerFsm, _animator, jump);
        JumpState = new PlayerJumpState(this, PlayerFsm, _animator, jump);
        FallState = new PlayerFallState(this, PlayerFsm, _animator, jump);
        WallClingState = new WallClingState(this, PlayerFsm);
        AttackState = new PlayerAttackState(this, PlayerFsm, _animator, combat, GetComponentInChildren<WeaponThrow>(),
            combat != null ? combat.ComboResetTimer : 0.6f,
            combat != null ? combat.ComboExitWindow : 0.12f);
        AirAttackState = new PlayerAirAttackState(this, PlayerFsm, _animator, combat, jump,
            combat != null ? combat.ComboResetTimer : 0.6f,
            combat != null ? combat.ComboExitWindow : 0.12f);
        BlockState = new PlayerBlockState(this, PlayerFsm, _animator, combat, jump,
            combat != null ? combat.ParryMaxWindow : 0.2f);
        GroundPoundState = new PlayerGroundPoundState(this, PlayerFsm, _animator, groundPound, jump);
        HurtState = new PlayerHurtState(this, PlayerFsm, _animator,
            health != null ? health.HurtDuration : 0.3f);
        AirHurtState = new PlayerAirHurtState(this, PlayerFsm, _animator, health, jump,
            health != null ? health.AirHurtTimeout : 1.5f);
        DeadState = new PlayerDeadState(this, PlayerFsm, _animator);
        DashState = new PlayerDashState(this, PlayerFsm, _animator, dash, jump,
            dash != null ? dash.DashDuration : 0.15f);
        SkillCastState = new PlayerSkillCastState(this, PlayerFsm, _animator);
        AimingState = new PlayerAimingState(this, PlayerFsm, _animator);
        // 背刺参数统一从 WeaponThrow 读(和其他攻击的击退/位移配置放一起,Inspector 在武器上调)
        var backstabWeapon = GetComponentInChildren<WeaponThrow>();
        BackstabState = new PlayerBackstabState(this, PlayerFsm, _animator, combat, teleport,
            backstabWeapon != null ? backstabWeapon.BackstabSearchRadius : 6f,
            backstabWeapon != null ? backstabWeapon.BackstabBehindOffset : 1.5f,
            backstabWeapon != null ? backstabWeapon.BackstabDamageMultiplier : 3f,
            backstabWeapon != null ? backstabWeapon.BackstabKnockback : new Vector2(8f, 0f),
            backstabWeapon != null ? backstabWeapon.BackstabHoverDuration : 0.2f);
        PlayerFsm.ChangeState(IdleState);
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

        // P3a:AirHurt 落地检测由 AirHurtState.OnUpdate 管理(原顶部独立分支删除,避免重复处理)

        // 计时器递减必须先于锁定判定：FreezeTimer 在 IsActionLocked 里被检查，
        // 若递减在锁定 return 之后则永远无法归零 → 永久锁死（蹬墙跳后卡下落动画）
        UpdateCooldowns();

        // 重音背刺 F 键:任何状态都检测(窗口内可强制打断普攻/格挡/冲刺等;死亡/受击硬直/背刺中除外)。
        // 放在锁定分支之前,保证攻击等 LocksInput 状态下窗口内 F 仍能强制打断。
        HandleBackstabInput();

        if (IsActionLocked())
        {
            // 攻击/受击锁定期间:仍要处理跳跃输入(打断攻击/缓冲补跳),
            // 否则 PlayerJump 永远不被调用 → 攻击中按空格无效(吞键)
            jump?.OnLockedUpdate(this);
            // P2:锁定状态下 FSM 仍需驱动 — AttackState.OnUpdate 处理连击输入/预输入缓冲,
            // GroundPoundState.OnUpdate 处理落地检测;P3a:受击状态(Hurt/AirHurt)已迁入 FSM,
            // 必须驱动 FSM 才能让 HurtState 超时退出 / AirHurtState 落地检测(原 !IsHurt 排除已删除)
            if (PlayerFsm != null && PlayerFsm.CurrentState is EntityState es && es.LocksInput)
                PlayerFsm.Update();
            return;
        }

        // 攻击朝向跟随当前输入（Fix：UpdateFacing 在 FixedUpdate 里，攻击在 Update 里会慢一帧）
        float h = Input.GetAxisRaw("Horizontal");
        if (Mathf.Abs(h) > 0.1f) UpdateFacing(h);

        // 冲刺由 FSM 状态类检测(P3b:dash.OnPlayerUpdate 阻断调用删除,
        // Shift 检测迁至 Idle/Move/Jump/Fall/Block 状态 OnUpdate → ChangeState(DashState))

        // 贴墙入口检测:条件满足则挂入 PlayerFsm 的 WallClingState 接管
        // (DashState.LocksInput=true → IsActionLocked 提前 return,冲刺中不会进入本分支)
        DetectWallCling();

        // 统一状态机驱动(Idle/Move/Jump/Fall/WallCling 的 OnUpdate 处理输入与切换)
        PlayerFsm.Update();

        // 生命值组件（保持接口一致，当前无每帧逻辑）
        health?.OnPlayerUpdate(this);

        UpdateSubModules();
    }

    /// <summary>聚合所有输入锁定源:冻结计时 + FSM 当前状态 LocksInput(P3a:受击/死亡由 Hurt/AirHurt/DeadState.LocksInput 覆盖)</summary>
    private bool IsActionLocked()
    {
        if (!InputEnabled) return true;
        if (health == null) return false;
        return FreezeTimer > 0f
            // LocksInput 定义在 EntityState(状态基类),IState 接口无此成员,需向下转型
            || (PlayerFsm != null && PlayerFsm.CurrentState is EntityState es && es.LocksInput);
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
                // 管道恢复由 attackingStat(AttackingStat)驱动,不在此处(挥空攻击不再锁管道)
            }
        }

        // P2:战斗/下坠攻击冷却计时(原子组件 OnPlayerUpdate 迁出,统一在此递减)
        combat?.TickTimers();
        groundPound?.UpdateTimers();

        // P3b:冲刺冷却倒计时(原 PlayerDash.OnPlayerUpdate 内递减迁出,统一在此递减;
        // 放锁定判定前保证冷却持续走,与改造前"每帧调用一次"一致)
        dash?.TickCooldown();

        // 技能数值层(CD/充能/法力回复):放锁定判定前,攻击等 LocksInput 状态期间照常走。
        // 卡帧(timeScale=0)也不停:SkillManager 内用 unscaledDeltaTime,只冻视觉不冻数值。
        skillManager?.UpdateTimers();
    }

    /// <summary>贴墙入口检测：空中 + 碰墙 + 不在上升 + 非贴墙中 → 切换至 WallClingState</summary>
    private void DetectWallCling()
    {
        if (PlayerFsm == null || PlayerFsm.CurrentState is WallClingState) return;
        if (grounded) return;
        if (!isTouchingWall) return;
        if (FreezeTimer > 0f) return;
        if (rb.velocity.y > 0f) return;

        PlayerFsm.ChangeState(WallClingState);
    }

    /// <summary>非锁定态子模块更新:仅技能按键检测(数值层 CD/充能/法力已由锁定前的 UpdateCooldowns 跑,避免攻击等锁定期间停表)</summary>
    private void UpdateSubModules()
    {
        skillManager?.CheckHotkeys();
    }

    // ============================================================
    // 重音背刺 F 键入口(方案 v2)
    // 窗口内 F:强制打断进 PlayerBackstabState;窗口外 F:无效,什么也不触发。
    // 仅当当前曲启用自动重音(barIntervalSeconds>0)时接管 F;Boss 曲/未配置曲 F 保持原行为
    // (技能槽 3 由 SkillManager 处理、Boss 战判定由 PlayerBeatJudge 处理)。
    // ============================================================

    /// <summary>每帧 F 键分发:仅自动重音窗口内触发背刺;窗口外按 F 无效果</summary>
    private void HandleBackstabInput()
    {
        if (!Input.GetKeyDown(KeyCode.F)) return;

        var mgr = MusicPointManager.Instance;
        if (mgr == null || mgr.CurrentTrack == null || mgr.CurrentTrack.barIntervalSeconds <= 0f)
            return;   // 未启用自动重音(Boss 曲/普通曲未配置):F 保持原行为

        if (mgr.IsAutoBarWindow)
            TryEnterBackstab();
        // 窗口外 F:无效,什么都不触发(2026-09-01 saika 确认,不再普攻挥空)
    }

    /// <summary>窗口内 F:强制打断进背刺状态(死亡/受击硬直/背刺自身执行中除外,防重入)</summary>
    private void TryEnterBackstab()
    {
        if (PlayerFsm == null || BackstabState == null) return;
        var cur = PlayerFsm.CurrentState;
        if (cur is PlayerDeadState
            || cur is PlayerHurtState
            || cur is PlayerAirHurtState
            || cur is PlayerBackstabState) return;
        PlayerFsm.ChangeState(BackstabState);
    }

    protected override void OnFixedUpdate()
    {
        if (!InputEnabled) return;

        if (health != null && health.IsAirHurt) return;
        if (IsActionLocked()) return;        // 冲刺中(PlayerDashState.LocksInput=true)已被 IsActionLocked 覆盖,无需单独排除

        float h = Input.GetAxisRaw("Horizontal");

        // 贴墙时阻止朝墙推（避免collider嵌入墙体）
        if (isTouchingWall && Mathf.Sign(h) == wallDirection && wallDirection != 0)
            h = 0f;

        // 贴墙状态自己处理物理(下滑/攀爬/蹬墙跳)
        if (PlayerFsm.CurrentState is WallClingState)
            return;

        // 空中状态(PlayerJumpState/PlayerFallState)在各自 OnUpdate 里做空中加速,此处跳过避免双重施加
        if (!grounded)
            return;

        if (Mathf.Abs(h) > 0.1f) Move(h);
        else Move(0f);
    }

    // ============================================================
    // 跳跃执行（覆写：优化速度处理，消除 grounded 闪烁干扰）
    // 公开给 PlayerJump / 状态类 调用
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
    public bool IsDashing() => PlayerFsm != null && PlayerFsm.CurrentState is PlayerDashState;
    public new bool IsGrounded() => base.IsGrounded;
    public Rigidbody2D GetRigidbody() => rb;

    public float CurrentHealth => health != null ? health.CurrentHealth : 0f;
    public float MaxHealth => health != null ? health.MaxHealth : 0f;

    public PlayerCombat Combat => combat;
    public PlayerGroundPound GroundPound => groundPound;
    /// <summary>跳跃执行器(供 FSM 状态类/输入门查询跳跃次数/执行跳跃)</summary>
    public PlayerJump JumpComp => jump;
    /// <summary>冲刺执行器(供 FSM 状态类查询冷却/调 DoDash;P3b 起状态由 PlayerDashState 表达)</summary>
    public PlayerDash Dash => dash;
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
        // 管道实心由 attackingStat(AttackingStat,敌人仇恨)驱动,不在此处(挥空攻击不再锁管道)
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

        DrawBackstabLandingGizmos();
    }

    /// <summary>背刺落点可视化(选中玩家时):蓝圈 = 最近 enemy 背后落点 + 连线;
    /// 背后被挡(墙/管道)时绿圈 = 正面替代落点。仅运行时显示(物理查询编辑模式不稳定)。</summary>
    private void DrawBackstabLandingGizmos()
    {
        if (!Application.isPlaying) return;
        var weapon = GetComponentInChildren<WeaponThrow>();
        if (weapon == null) return;

        EnemyControllerBase nearest = null;
        float best = float.MaxValue;
        foreach (var e in FindObjectsOfType<EnemyControllerBase>())
        {
            if (e == null || e.IsDead) continue;
            float d = ((Vector2)e.transform.position - (Vector2)transform.position).sqrMagnitude;
            if (d < best) { best = d; nearest = e; }
        }
        if (nearest == null) return;

        float offset = weapon.BackstabBehindOffset;
        Vector2 behind = new Vector2(nearest.transform.position.x - nearest.Facing * offset, nearest.transform.position.y);
        Gizmos.color = Color.blue;
        Gizmos.DrawWireSphere(behind, 0.3f);
        Gizmos.DrawLine(nearest.transform.position, behind);

        Collider2D hit = Physics2D.OverlapPoint(behind);
        bool blocked = AreaChannelTrigger.IsPointInChannel(behind) || (hit != null && !hit.isTrigger);
        if (blocked)
        {
            Vector2 front = new Vector2(nearest.transform.position.x + nearest.Facing * offset, nearest.transform.position.y);
            Gizmos.color = Color.green;
            Gizmos.DrawWireSphere(front, 0.3f);
        }
    }
}
