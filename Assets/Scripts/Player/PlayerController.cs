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
[DefaultExecutionOrder(-10000)]   // 早于默认顺序的业务脚本 Awake：Instance 静态读取才有值（替代旧 Find 兜底）
public class PlayerController : PlayerCharacterBase
{
    // ============================================================
    // Singleton 注册表（场景内唯一 Player；调用方统一走 Instance，避免 FindObjectOfType 散布）
    // ============================================================

    private static PlayerController _instance;

    /// <summary>
    /// 当前实例。无 Find 兜底：靠 Awake 接管 + OnDestroy 自清维护，
    /// 避免兜底把"还没 Awake 的自己"提前写进静态字段导致自身被当重复实例销毁。
    /// </summary>
    public static PlayerController Instance => _instance;

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
    // 地图元素冲刺(S5) — 窗口内 F 的「先敌后元素」分流参数
    // ============================================================

    /// <summary>[S5] 元素查询的视口外扩余量(viewport 坐标 0~1,允许 -margin ~ 1+margin):
    /// 直接喂 MapDashPoint.TryFindNearest。0 = 元素必须完全在屏幕内(默认,符合规格「屏幕内」口径)。
    /// 敏感度太高/太低时在 Inspector 上微调,不需要改代码。</summary>
    [Header("地图元素冲刺(S5)")]
    [Tooltip("元素查询的视口外扩余量(0 = 必须完全在屏幕内)")]
    [SerializeField] private float mapDashViewportMargin = 0f;

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
    // 背刺追击窗口(backstab chase)— 背刺命中后窗口内按攻击 → 吸附 enemy 身边开打
    // 共享数据放玩家根(攻击输入分发侧):背刺状态命中帧写入,退出背刺状态后仍可读;
    // 无每帧轮询/无协程,只用过期时间戳(输入事件时校验)。清除由输入侧顺路做。
    // ============================================================

    private EnemyControllerBase _chaseTarget;   // 追击目标(背刺命中的那只 enemy;null = 无窗口)
    private float _chaseEndTime;                // 窗口过期时间戳(Time.time 基准)

    /// <summary>追击窗口当前是否有效(未过期 + 目标存活;输入分发快速判断用)</summary>
    public bool BackstabChaseActive
    {
        get
        {
            if (_chaseTarget == null || _chaseTarget.IsDead) return false;
            return Time.time <= _chaseEndTime;
        }
    }

    /// <summary>开启追击窗口(PlayerBackstabState 命中帧调用):写目标 + 过期时间戳。
    /// 调用侧已校验 WeaponThrow.BackstabChaseEnabled / window>0 / 目标存活</summary>
    public void BeginBackstabChase(EnemyControllerBase target, float windowSeconds)
    {
        _chaseTarget = target;
        _chaseEndTime = Time.time + Mathf.Max(0f, windowSeconds);
    }

    /// <summary>清除追击窗口(目标死亡/过期/追击已消费时;输入侧顺路清,防过期残留)</summary>
    public void ClearBackstabChase()
    {
        _chaseTarget = null;
        _chaseEndTime = 0f;
    }

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
        _instance = this;   // 先接管：本类 Awake 内 AddComponent 出的组件可能立刻访问 Instance
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
            backstabWeapon != null ? backstabWeapon.BackstabHoverDuration : 0.2f,
            backstabWeapon != null ? backstabWeapon.BackstabChaseWindow : 2f,
            backstabWeapon != null ? backstabWeapon.BackstabChaseEnabled : true);
        PlayerFsm.ChangeState(IdleState);
    }

    /// <summary>自清单例引用：Instance 不再需要 Find 兜底(销毁后静态字段不留脏引用)</summary>
    private void OnDestroy()
    {
        if (_instance == this) _instance = null;
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

        // F 键双消费方分发(石碑系统 T4,方案 §3.5):石碑传送优先于背刺。
        // HandleWaypointInput() 返回 true = 本帧 F 已被石碑消费(开传送页),不再走背刺;
        // 返回 false(非石碑/战斗中/锁定态/未按 F)→ 落回原背刺逻辑,行为与改前逐帧一致。
        // 重音背刺 F 键:任何状态都检测(窗口内可强制打断普攻/格挡/冲刺等;死亡/受击硬直/背刺中除外)。
        // 放在锁定分支之前,保证攻击等 LocksInput 状态下窗口内 F 仍能强制打断。
        if (!HandleWaypointInput())
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
    // 石碑 F 键入口(石碑系统 T4,方案 §3.5)— F 判定注入
    // 由 OnUpdate 在 HandleBackstabInput 之前调用:返回 true = 本帧 F 已被石碑消费(打开传送页)。
    // 门控:已激活石碑旁 + 非战斗(敌人仇恨) + 非锁定态;任一不满足 → false,F 落回原背刺逻辑,
    // 保证非石碑/战斗中的 F 行为与改前逐帧一致(背刺窗口/技能槽 3 照旧)。
    // ============================================================

    /// <summary>
    /// 石碑 F:站在已激活石碑 trigger 内 + 非战斗 → 开传送页并消费本帧 F。
    /// 战斗中不弹页(规则2:F 落回背刺,战斗中最需要背刺);锁定态(攻击/受击硬直)不弹页(风险 R12,本期不做打断)。
    /// </summary>
    private bool HandleWaypointInput()
    {
        if (!Input.GetKeyDown(KeyCode.F)) return false;

        var wp = WaypointSystem.Instance;
        if (wp == null) return false;
        var near = WaypointSystem.CurrentNearby;   // static 只读属性(避免经实例访问的 CS0176)
        if (near == null || !near.Activated) return false;   // 不在已激活石碑旁 → 零侵入

        // 战斗态(任意敌人仇恨)不弹传送页:F 落回背刺
        if (AttackingStat.Instance != null && AttackingStat.Instance.InCombat) return false;

        // 攻击/受击硬直等锁定态不弹页
        if (IsActionLocked()) return false;

        // 开面板(LockInput=true → PanelManager 置 InputEnabled=false,技能槽3 CheckHotkeys 同帧短路)
        wp.OpenTeleportPanel();
        return true;   // 消费 F:本帧不再走背刺
    }

    // ============================================================
    // 重音背刺 / 连音背刺 F 键入口(方案 v2 + P8 连音)
    // 窗口内 F:强制打断进 PlayerBackstabState;窗口外 F:无效,什么也不触发。
    // 两条判定路径(规格 P2「判定优先级」):
    //   ① 当前曲配了 PlayerBackstab 连音组(HasChain)→ 判定条件 = 本组内存在"活跃且未被消费"的点
    //      (IsInChainWindow,P2 产物);组内后续点由状态就地推进(见 TryEnterBackstab);
    //   ② 当前曲无连音组 → 原自动重音窗口路径(barIntervalSeconds>0 才有窗),行为与改前逐帧一致。
    // Boss 曲/未配置曲无连音组时 F 保持原行为(技能槽 3 由 SkillManager 处理、Boss 战判定由 PlayerBeatJudge 处理)。
    // ============================================================

    /// <summary>每帧 F 键分发:连音组曲按"连音点窗口内"触发背刺,无连音组曲按"自动重音窗口内"触发;窗口外按 F 无效果。
    /// [S5] 窗口内的分流:先敌后元素 —— 有可背刺敌人 → 原背刺;没敌人 + 面朝有就绪元素 + 非攻击态(连音还要"只认首音")
    ///   → 元素冲刺;都没有 → 走原背刺路径(状态内无目标兜底 = 普通空挥,不新增空挥逻辑)。</summary>
    private void HandleBackstabInput()
    {
        if (!Input.GetKeyDown(KeyCode.F)) return;

        var mgr = MusicPointManager.Instance;
        if (mgr == null || mgr.CurrentTrack == null) return;

        // ── ① 连音路径(P8):当前曲存在连音组 → 判定条件由"自动重音窗口"换成"本组内存在活跃且未消费的点"。
        // HasChain == _chainPoints.Count > 0,与 NextChainStartTime >= 0 等价(P2 产物);
        // 存在连音组时自动重音窗口不再接管 F(规格 P2:当前曲存在 PlayerBackstab 组 → 背刺走该组)。
        if (mgr.HasChain)
        {
            if (mgr.IsInChainWindow)
            {
                // [S5] 元素冲刺只认组内首音(见 TryMapDashInsteadOfBackstab);返回 false = 本帧该走背刺。
                // 组内后续点照旧由背刺 TryExecuteNextPoint 逐点推进,完全不受元素冲刺影响。
                if (!TryMapDashInsteadOfBackstab(chainMode: true, mgr))
                    TryEnterBackstab(chainMode: true);
            }
            // 连音窗口外 F:无效,什么都不触发(与下面自动重音路径同口径,不再普攻挥空)
            return;
        }

        // ── ② 无连音组:原自动重音路径,逐帧行为与改前完全一致 ──
        if (mgr.CurrentTrack.barIntervalSeconds <= 0f)
            return;   // 未启用自动重音(Boss 曲/普通曲未配置):F 保持原行为

        if (mgr.IsAutoBarWindow)
        {
            // [S5] 自动重音路径没有连音组 → 不涉及"只认首音";其余分流条件与连音路径同一套。
            if (!TryMapDashInsteadOfBackstab(chainMode: false, mgr))
                TryEnterBackstab(chainMode: false);
        }
        // 窗口外 F:无效,什么都不触发(2026-09-01 saika 确认,不再普攻挥空)
    }

    // ============================================================
    // [S5] 地图元素冲刺分流 — 窗口内 F 的「先敌后元素」判定
    // 只在按键那一帧跑:状态判定是纯属性读取,敌我查询复用背刺同一套搜索,元素查询遍历 MapDashPoint 注册表。
    // 没有任何每帧轮询/Update 内新增搜索。
    // ============================================================

    private PlayerMapDash _mapDash;              // 元素冲刺执行器(玩家根);懒缓存一次,未挂 = null → 元素冲刺整体跳过
    private bool _mapDashResolved;               // 是否已解析过(解析含"没挂"这个结果,不重复 GetComponent)

    /// <summary>
    /// [S5 地图元素冲刺] 判定本帧 F 是否改走「元素冲刺」:该走则执行并返回 true(调用方不再走背刺)。
    /// 返回 false = 保持原背刺路径(有敌人 / 攻击中 / 受击死亡 / 连音非首音 / 没元素 / 没挂执行器)。
    ///
    /// 判定顺序与依据(规格 §S5):
    ///   ① 攻击状态(`PlayerComboState`/`PlayerAttackState`/`PlayerAirAttackState`)不冲元素 —— 攻击中的 F 仍由背刺
    ///      强制打断(原优先级与打断语义一字不改),元素不抢攻击中的 F;
    ///   ② 受击/死亡不冲(沿用 TryEnterBackstab 的状态守卫口径);
    ///   ③ 背刺执行中不冲:此时 F 的原有语义是"连音逐点推进 / 非连音无效果",属于禁止项里的背刺打断语义,
    ///      保守排除,避免元素冲刺在背刺动画中途把玩家瞬移走;
    ///   ④ 连音路径只认首音(见 IsChainFirstPointActive):组内后续点即使活跃也不触发元素;
    ///   ⑤ 先敌后元素:「附近有没有可背刺敌人」复用背刺同一套搜索(BackstabState.FindNearestTarget,含搜索半径 /
    ///      存活判定 / 层级掩码),有敌人 → 让给背刺(含打断攻击语义);不新写一份搜索,防口径漂移;
    ///   ⑥ 无敌人 → 找「视口内 + 非 CD」的最近元素(MapDashPoint.TryFindNearest,相机用 S3 缓存的
    ///      CachedCamera,按键路径里不查 Camera.main);**口径与背刺目标分配一致,不判朝向**;
    ///      找到就执行,没找到 → 让给背刺(原地闪现=普通空挥)。
    /// 窗口消费口径不变:元素冲刺不调用 ConsumeAutoBarWindow、不 ConsumePoint —— 窗口/点的消费仍只由背刺路径完成。
    /// </summary>
    /// <param name="chainMode">true = 连音路径(需要"只认首音");false = 自动重音路径(无连音组)</param>
    /// <param name="mgr">当前曲的音乐点管理器(调用方已判非空)</param>
    private bool TryMapDashInsteadOfBackstab(bool chainMode, MusicPointManager mgr)
    {
        if (PlayerFsm == null) return false;

        var cur = PlayerFsm.CurrentState;

        // ① 攻击状态不冲元素(①②③ 全是纯状态判定,无副作用,顺序不影响结果)
        if (cur is PlayerComboState || cur is PlayerAttackState || cur is PlayerAirAttackState) return false;
        // ② 受击/死亡不冲(与 TryEnterBackstab 的守卫同口径)
        if (cur is PlayerDeadState || cur is PlayerHurtState || cur is PlayerAirHurtState) return false;
        // ③ 背刺执行中不冲(保持 F 在背刺中的原有语义)
        if (cur is PlayerBackstabState) return false;

        // ④ 连音只认首音:组内第一个点不在活跃窗口(或已被消费)时,元素冲刺让位给背刺
        if (chainMode && !IsChainFirstPointActive(mgr)) return false;

        // ⑤ 先敌后元素:同一套背刺搜索,有可背刺敌人就不抢
        if (BackstabState != null && BackstabState.FindNearestTarget() != null) return false;

        // ⑥ 找元素(视口/CD 都在 TryFindNearest 内部判;口径与背刺目标分配一致:**不判朝向**);
        //    没找到 → false,让背刺走空挥兜底
        var mapDash = ResolveMapDash();
        if (mapDash == null) return false;
        if (!MapDashPoint.TryFindNearest((Vector2)transform.position,
                mapDash.CachedCamera, mapDashViewportMargin, out var point))
            return false;

        // TryExecute 自带空引用/就绪双守卫;它返回 false(例如元素恰在本帧被别的单位消费)时仍落回背刺路径
        return mapDash.TryExecute(point);
    }

    /// <summary>
    /// [S5 连音只认首音] 当前连音组内是否正处在「首音」的活跃窗口(且未被消费)。
    /// 判据全部来自 MusicPointManager 的只读查询:`CurrentChainPoints` 是当前组的升序点数组(index 0 = 首音),
    /// 再要求这个首点自身 IsPointActive(处于活跃窗口)且 !IsPointConsumed(尚未被背刺消费)。
    /// 组内后续点即使处于活跃窗口也不触发元素冲刺 —— 它们照旧由背刺 TryExecuteNextPoint 逐点推进。
    /// </summary>
    private static bool IsChainFirstPointActive(MusicPointManager mgr)
    {
        float[] pts = mgr.CurrentChainPoints;
        if (pts == null || pts.Length == 0) return false;

        float first = pts[0];
        return mgr.IsPointActive(first) && !mgr.IsPointConsumed(first);
    }

    /// <summary>解析玩家根上的元素冲刺执行器(S3 挂点=玩家根);只解析一次,未挂则以后都直接跳过。</summary>
    private PlayerMapDash ResolveMapDash()
    {
        if (!_mapDashResolved)
        {
            _mapDashResolved = true;
            _mapDash = GetComponent<PlayerMapDash>();
        }
        return _mapDash;
    }

    /// <summary>窗口内 F:强制打断进背刺状态(死亡/受击硬直除外)。连音路径放开"背刺执行中重入"(P7 产物)。
    /// 背刺最高优先级:打断攻击连段前清掉其排队/缓冲输入,防旧点击在背刺后污染追击窗口(2026-09-03 saika)</summary>
    private void TryEnterBackstab(bool chainMode)
    {
        if (PlayerFsm == null || BackstabState == null) return;
        var cur = PlayerFsm.CurrentState;
        if (cur is PlayerDeadState
            || cur is PlayerHurtState
            || cur is PlayerAirHurtState) return;

        // ── 已在背刺状态中(P8 核心:放开重入,原实现直接 return → 连音第二拍被吞)──
        if (cur is PlayerBackstabState backstabState)
        {
            // 非连音路径(自动重音)保持原语义:背刺执行中按 F 不重入(该窗口进状态时已消费)
            if (!chainMode) return;

            // 连音路径:同一状态实例就地推进下一刀,不 ChangeState(FSM 对同实例直接 return,切不动也不重播动画)。
            //   返回 true = 这一刀已执行(状态内 ExecuteStrike 已按点 ConsumePoint);
            //   返回 false = 这一刀没执行(当前组已切走 = 本状态正在退出):不消费,留给下一拍 ——
            //     状态在最后一刀动画结束后自然 Exit,之后的 F 走上面的"全新进入"分支(ChangeState 生效)重新绑定新组。
            // 消费口径(P7 约定①):消费一律由 PlayerBackstabState 每刀执行时按点完成 —— 它要在同一时点取
            //   点序号/目标/环序号;入口层禁止 ConsumePoint,否则 OnEnter 读到的是"下一个点序号",目标与环全部错位。
            backstabState.TryExecuteNextPoint();
            return;
        }

        // 背刺最高优先级:当前若在连段中(地面/空中攻击),清掉待处理输入,打断前不留残留
        if (cur is PlayerComboState comboState)
            comboState.CancelPendingComboInput();
        PlayerFsm.ChangeState(BackstabState);
        // 消费:连音路径的按点消费由 PlayerBackstabState.OnEnter → ExecuteStrike 完成(同上,入口层不消费);
        // 自动重音路径消费当前窗口(本 bar 限一次背刺,防窗口内连按 F 连触发;空挥也消耗,miss 就过)。
        if (!chainMode)
            MusicPointManager.Instance?.ConsumeAutoBarWindow();
    }

    // ============================================================
    // 背刺追击攻击分发 — Idle/Move/Jump/Fall 状态"发起新攻击"分支最前调用
    // (左键按下 + 攻击冷却就绪已由调用方校验;返回 true = 本帧攻击已被追击消费)
    // ============================================================

    /// <summary>
    /// 追击攻击尝试:背刺追击窗口内按攻击 → 玩家瞬移到 enemy 侧旁(gap 复用 WeaponThrow.AirBlinkSideGap,
    /// y 对齐 enemy;enemy 贴地/低空时落点抬到"enemy 底 + 玩家半高"可站立高度防嵌地),
    /// 按 enemy 状态分流进新攻击:
    ///   - enemy 仍在空中 → AirAttackState(空中第 1 段,OnComboEnter 自带 comboIndex=1/悬停/第 1 段不闪;
    ///     玩家落空后攻击框可罩住 enemy → 第一击命中,后续正常空中三段闪击流);
    ///   - enemy 已落地(被击飞到平台/地面)→ AttackState(地面第 1 段,后续正常地面连段)。
    /// 窗口过期 / 目标死亡 / 两侧都堵 → 清窗口返回 false,调用方走原普通攻击(不硬追)。
    /// 只拦截"新攻击发起",combo 推进在攻击状态内部(PlayerComboState),不经过此入口,天然不受影响。
    /// </summary>
    public bool TryBackstabChaseAttack()
    {
        if (PlayerFsm == null) return false;

        // 窗口校验:过期 / 目标死亡 → 清窗口,走原逻辑(普通攻击)
        if (_chaseTarget == null || _chaseTarget.IsDead || Time.time > _chaseEndTime)
        {
            ClearBackstabChase();
            return false;
        }
        EnemyControllerBase target = _chaseTarget;

        // a. 吸附点:首选玩家对侧(玩家在左 → 落右侧,左右交替与空中闪击视觉统一);
        //    对侧被墙/管道堵(IsWallBlockedOnSide,含地面/管道)→ 翻另一侧;两侧都堵 → 放弃追击
        int side = transform.position.x >= target.transform.position.x ? -1 : 1;
        if (target.IsWallBlockedOnSide(side))
            side = -side;
        if (target.IsWallBlockedOnSide(side))
        {
            ClearBackstabChase();
            return false;
        }

        // 侧面间距复用 WeaponThrow.AirBlinkSideGap(与空中闪击同视觉);组件缺失/配置异常兜底 1.5
        var chaseWeapon = GetComponentInChildren<WeaponThrow>();
        float gap = chaseWeapon != null ? chaseWeapon.AirBlinkSideGap : 1.5f;
        if (gap <= 0f) gap = 1.5f;

        // dest.y 对齐 enemy(空中 enemy → 玩家落空;落地 enemy → 玩家落 enemy 所在平台/地面)。
        // 嵌地风险:enemy 贴地/低空时玩家落点会插进地面 → 抬到可站立高度(enemy 碰撞体底 + 玩家半高)
        Vector2 dest = new Vector2(target.transform.position.x + side * gap, target.transform.position.y);
        float enemyBottomY = target.transform.position.y
            - (target.Col != null ? target.Col.bounds.extents.y : 0.5f);
        float playerHalfY = col != null ? col.bounds.extents.y : 0.5f;
        float standY = enemyBottomY + playerHalfY;
        if (dest.y < standY) dest.y = standY;

        // b. 玩家瞬移:物理体位(同帧不读 transform.position 判朝向)+ 清水平速度(保留 y 视落地/空中由状态接管)
        Rigidbody2D rb2 = GetRigidbody();
        if (rb2 == null)
        {
            ClearBackstabChase();
            return false;
        }
        rb2.position = dest;
        SetVelocityPublic(x: 0f);

        // c. 玩家朝向 enemy(按 dest 与 enemy 相对位置,不能读 transform.position——rb.position 刚赋值未同步)
        UpdateFacing(target.transform.position.x >= dest.x ? 1f : -1f);

        // d. 分流进状态(PlayerFsm 上已持有的状态引用;地面可直接切空中攻击,AirAttackState 自带直切动画兜底)
        PlayerFsm.ChangeState(target.IsGrounded
            ? (IState)AttackState
            : (IState)AirAttackState);

        // e. 关窗口:追击单次消耗,防进入连段后再次误触发
        ClearBackstabChase();
        return true;
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
