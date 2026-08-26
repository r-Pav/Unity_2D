using UnityEngine;
using System.Collections;

/// <summary>
/// 受击 VFX 变体条目 — 按攻击类型标签匹配不同特效。
/// attackType 后续可用于驱动受击音效、伤害类型（物理/元素）、弱点匹配、Buff 触发等。
/// </summary>
[System.Serializable]
public struct HitVFXVariant
{
    [Tooltip("攻击类型标签，如 Sword/Bow/Hammer/Fire/Ice 等")]
    public string attackType;
    [Tooltip("该攻击类型对应的受击 VFX（未配置时回退 hitVFXPrefab）")]
    public GameObject vfxPrefab;
}

/// <summary>
/// 敌人控制器抽象基类 — 继承 CharacterBase，管理共享的 AI、FSM 生命周期、受伤/死亡逻辑。
/// 子类（EnemyMeleeController / EnemyRangedController）负责实现具体的 FSM 状态。
/// </summary>
public abstract class EnemyControllerBase : CharacterBase, ICombatant
{
    // ============================================================
    // 配置参数
    // ============================================================

    [Header("数值配置 SO")]
    [Tooltip("敌人数值配置 ScriptableObject（Lv 收敛：内含 Lv1/2/3 三档；为空时仅用 Inspector/内置默认）")]
    [SerializeField] protected EnemyConfigSO config;

    [Header("等级")]
    [Tooltip("敌人等级 1~3，决定 EnemyConfigSO 取哪一档数值（Lv 收敛）")]
    [SerializeField] protected int level = 1;
    /// <summary>当前等级（消费组件/面板读取）</summary>
    public int Level => level;

    /// <summary>当前配置 SO（Inspector 运行时调试显示用）</summary>
    public EnemyConfigSO Config => config;

    /// <summary>当前移速（管线终值；Inspector 运行时调试显示用）</summary>
    public float CurrentMoveSpeed => MoveSpeed;

    [Header("属性")]
    [Tooltip("最大血量（0 = 未设置，用 SO 对应 Lv 档 / 内置默认兜底）")]
    [SerializeField] protected float maxHealth = 0f;

    [Header("受伤反馈")]
    [SerializeField] protected Color hitColor = Color.white;  // 白色闪白更明显
    [SerializeField] protected float hitFlashDuration = 0.1f;
    [Tooltip("受击停顿(秒):当前受击 enemy 在全局卡帧结束后,自己再冻结受击动画的时长。0/空 = 不启用")]
    [SerializeField] private float enemyHitPause = 0f;   // 显式 0 与"不设默认值"行为等价（0 = 不启用），避免 CS0649

    [Header("蓄力反馈")]
    [Tooltip("蓄力色 — 蓄力帧(OnCharge)开始闪烁、发射帧(OnFire)结束；灭相位恢复原始材质色")]
    [SerializeField] protected Color chargeColor = new Color(1f, 0.3f, 0f);
    [Tooltip("蓄力闪烁起始频率（每秒亮灭次数），随蓄力时间加速")]
    [SerializeField] protected float chargeFlashBaseFreq = 4f;
    [Tooltip("蓄力闪烁频率加速度（每秒增加的频率），蓄力越长闪得越快")]
    [SerializeField] protected float chargeFlashAccel = 6f;
    [Tooltip("蓄力闪烁频率上限（防长蓄力闪成震动）")]
    [SerializeField] protected float chargeFlashMaxFreq = 20f;

    // [预留] Boss 蓄力色独立配置：未来在 BossControllerBase 新增独立字段（与普通 enemy 分开设置），
    //       在 BossSkillSlots 各 Execute* 协程 windupTime 前摇段调 BeginChargeFlash()、判定帧调 EndChargeFlash()，
    //       Interrupt() 里兜底 EndChargeFlash()（协程被 Stop 后体内清理不会执行）。当前 Boss 不启用。

    [Header("VFX")]
    [Tooltip("受击 VFX 预制体 — 受伤时 Instantiate")]
    [SerializeField] protected GameObject hitVFXPrefab;
    [Tooltip("方向受击 VFX 预制体 — 有朝向的受击特效，朝向为攻击反方向（仅在 TakeDamageFrom 中额外生成）")]
    [SerializeField] protected GameObject directionalHitVFXPrefab;
    [Tooltip("死亡 VFX 预制体 — 死亡时 Instantiate")]
    [SerializeField] protected GameObject deathVFXPrefab;

    [Header("VFX 变体")]
    [Tooltip("按攻击类型匹配的受击 VFX 列表（匹配到时覆盖 hitVFXPrefab）")]
    [SerializeField] private HitVFXVariant[] hitVFXVariants;

    [Header("AI 检测 — 矩形")]
    [Tooltip("检测矩形半宽（X 轴；0 = 未设置）")]
    [SerializeField] protected float detectionWidth = 0f;
    [Tooltip("检测矩形半高（Y 轴；0 = 未设置）")]
    [SerializeField] protected float detectionHeight = 0f;

    [Header("攻击范围 — 矩形")]
    [Tooltip("攻击矩形半宽（X 轴；0 = 未设置）")]
    [SerializeField] protected float attackWidth = 0f;
    [Tooltip("攻击矩形半高（Y 轴；0 = 未设置）")]
    [SerializeField] protected float attackHeight = 0f;

    [Header("攻击冷却")]
    [Tooltip("攻击冷却时间（秒；0 = 未设置）")]
    [SerializeField] protected float attackCooldownDuration = 0f;
    public float AttackCooldownDuration => attackCooldownDuration;

    [Header("击退")]
    [Tooltip("远程攻击击退力度（近战击退由 PoiseComponent 控制；0 = 未设置）")]
    [SerializeField] protected float rangedKnockbackForce = 0f;

    [Header("空中受击")]
    [Tooltip("空中受击滞空时长(秒):击飞中再受击停住后继续击退轨迹;0 = 关闭")]
    [SerializeField] protected float airHitHangDuration = 0.3f;
    [Tooltip("空中受击吸附玩家速度(向玩家检测矩形中心拉 x,保持连段距离;0 = 关闭)")]
    [SerializeField] protected float airHitPullSpeed = 8f;
    [Tooltip("空中击退撞管道反弹系数(动漫撞墙:横向速度反向×此系数;0 = 撞上停住)")]
    [SerializeField] protected float airHitWallBounce = 0.6f;
    [Tooltip("撞墙形变强度(动漫挤压:水平压扁垂直拉长;0 = 关闭)")]
    [SerializeField] protected float airHitWallSquash = 0.2f;

    [Header("落地冲击")]
    [Tooltip("落地冲击触发速度阈值(y 速度低于此值触发,负值;如 -8。自然落地/走路不触发)")]
    [SerializeField] protected float groundImpactSpeedThreshold = -8f;
    [Tooltip("落地尘土/冲击特效 prefab(留空 = 无)")]
    [SerializeField] protected GameObject groundImpactVFX;
    [Tooltip("落地卡帧时长(秒;0 = 无)")]
    [SerializeField] protected float groundImpactHitStop = 0.05f;
    [Tooltip("落地震屏时长(秒;0 = 无)")]
    [SerializeField] protected float groundImpactShakeDuration = 0.1f;
    [Tooltip("落地震屏幅度(0 = 无;参考 0.1)")]
    [SerializeField] protected float groundImpactShakeMagnitude = 0.1f;
    [Tooltip("落地硬直时长(秒;0 = 关闭,落地直接恢复行动)")]
    [SerializeField] protected float groundImpactStun = 0.3f;
    [Tooltip("落地弹跳力度(沿击退方向弹出去;0 = 不弹)")]
    [SerializeField] protected float groundBounceForce = 3f;
    [Tooltip("落地形变强度(动漫挤压拉伸,0.3 = 压扁30%;0 = 关闭)")]
    [SerializeField] protected float groundImpactSquash = 0.3f;

    [Header("巡逻悬崖检测")]
    [Tooltip("前方偏移（X 轴）：前方多远处探脚下地面（0.8 = 角色前方约一个身位）")]
    [SerializeField] private float cliffCheckForward = 0.8f;
    [Tooltip("下探距离（Y 轴）：从脚底向下探多深，探不到 = 悬崖/空洞")]
    [SerializeField] private float cliffCheckDown = 0.8f;

    [Header("巡逻管道检测")]
    [Tooltip("管道检测层(Channel):巡逻边界用")]
    [SerializeField] private LayerMask channelLayer = 0;

    [Tooltip("管道检测射线长度(前方)")]
    [SerializeField] private float channelCheckForward = 1.5f;

    [Tooltip("射线发射高度偏移(腰部)")]
    [SerializeField] private float channelRayHeightOffset = 0.5f;

    /// <summary>管道检测射线命中缓冲（团结引擎 ContactFilter2D 重载需结果数组；静态复用防每帧 GC）</summary>
    private static readonly RaycastHit2D[] channelCheckHits = new RaycastHit2D[1];

    [Header("移动范围")]
    [Tooltip("活动范围半径（X 轴，以出生锚点为中心；0 = 不限制）")]
    [SerializeField] protected float homeRange = 0f;
    [Tooltip("出生锚点 X（活动范围中心；默认 = 编辑器摆放位置，可手动改）")]
    [SerializeField] protected float homeX;
    /// <summary>出生锚点是否已初始化（OnValidate/Awake 置位；置位后允许手动改 homeX 不被覆盖）</summary>
    [SerializeField] private bool homeXInitialized;

    /// <summary>暴露攻击矩形半宽给攻击组件读取</summary>
    public float AttackWidth => attackWidth;
    /// <summary>暴露攻击矩形半高给攻击组件读取</summary>
    public float AttackHeight => attackHeight;
    /// <summary>暴露检测矩形半宽给攻击组件读取</summary>
    public float DetectionWidth => detectionWidth;
    /// <summary>暴露检测矩形半高给攻击组件读取</summary>
    public float DetectionHeight => detectionHeight;

    /// <summary>
    /// 嘲讽目标抽象层（B11）— 所有 AI 读取目标位置统一走此属性：
    /// 嘲讽期间返回 OverrideTarget（幻象等实体），否则返回真实玩家。
    /// 追击/攻击/朝向/LOS/检测矩形全部跟随，enemy 追幻象即被牵引。
    /// </summary>
    public Transform PlayerTarget => OverrideTarget != null ? OverrideTarget : player;

    // ── 嘲讽状态（B11/阶段 4：SetTaunt 把仇恨拉到幻象）──

    /// <summary>嘲讽覆盖目标（SetTaunt 设置；null = 正常追玩家）</summary>
    public Transform OverrideTarget { get; private set; }

    /// <summary>嘲讽剩余时长（秒；>0 期间 OverrideTarget 生效，Update 归零自动 ClearTaunt）</summary>
    private float tauntTimer;

    /// <summary>
    /// 施加嘲讽 — 仇恨转移到 source 实体（幻象等）持续 duration 秒。
    /// Boss 嘲讽时长减半（决策：先做减半，免疫与否入数值调优清单）。
    /// 重复嘲讽：取当前剩余与本次时长较大者（防连续刷新导致提前结束），目标指向最新 source。
    /// </summary>
    public void SetTaunt(Transform source, float duration)
    {
        if (source == null || isDead || duration <= 0f) return;
        OverrideTarget = source;
        tauntTimer = Mathf.Max(tauntTimer, IsBoss ? duration * 0.5f : duration);
    }

    /// <summary>解除嘲讽 — 仇恨回到真实玩家（tauntTimer 归零时自动调用）</summary>
    public void ClearTaunt()
    {
        tauntTimer = 0f;
        OverrideTarget = null;
    }

    // ============================================================
    // 运行时状态
    // ============================================================

    protected float currentHealth;
    protected bool isDead;

    /// <summary>按 level 解析出的 SO 档（Awake 赋值；config 为空时为 null）— 子类/攻击组件读取</summary>
    protected EnemyLvStats lvStats;
    public EnemyLvStats LvStats => lvStats;

    /// <summary>管线前基础血量（Awake 存；装备修饰器变化时重算 maxHealth 用）</summary>
    private float _baseMaxHealth;

    /// <summary>公开死亡状态（供外部组件读取）</summary>
    public bool IsDead => isDead;

    /// <summary>是否为 Boss（BossControllerBase 重写为 true；普通怪默认 false）</summary>
    public virtual bool IsBoss => false;

    /// <summary>当前血量（供 HealthBar 等读取）</summary>
    public float CurrentHealth => currentHealth;
    /// <summary>最大血量</summary>
    public float MaxHealth => maxHealth;

    private Renderer[] renderers;
    private Color stateColor;         // 当前状态色，hit 恢复时用此值
    private float hitFlashTimer;
    private bool isChargeFlashing;       // 蓄力闪烁中（BeginChargeFlash 置位，EndChargeFlash 复位）
    private float chargeFlashStartTime;  // 蓄力闪烁开始时间（Time.time），驱动频率加速
    private float hitKnockbackWindow;    // 受击击退滑行窗口（>0 时 OnFixedUpdate 不 Move(0)，保留击退速度滑行；对齐 stun 豁免）
    private float hitPauseTimer;         // 受击停顿倒计时（>0 冻结受击动画；ApplyDamage 置 enemyHitPause，OnUpdate 倒数）

    // ── FSM ──
    protected StateMachine fsm;
    public StateMachine Fsm => fsm;
    protected Transform player;

    /// <summary>缓存的 PassiveEquipManager 引用（通过 FindObjectOfType 获取）</summary>
    protected PassiveEquipManager passiveEquipManager;
    /// <summary>缓存的 PoiseComponent 引用（霸体/击退组件）</summary>
    private PoiseComponent _poise;
    /// <summary>当前敌人是否处于战斗状态（Chase/Attack），防止重复触发 SetCombatState</summary>
    private bool isInCombatState;

    /// <summary>是否处于战斗状态（进入过 Chase/Attack；Patrol/Idle 时 OnExitCombatState 清 false）— 供状态类判断仇恨是否存在</summary>
    public bool IsInCombatState => isInCombatState;

    /// <summary>FSM 状态设置的移动输入（1 / -1 / 0），OnFixedUpdate 应用</summary>
    public float moveInput;

    /// <summary>攻击冷却计时器，攻击后一段时间内不进攻击（防止循环）</summary>
    public float attackCooldownTimer;

    /// <summary>是否处于攻击判定帧内 (供弹反系统查询)。当前由 PerformAttack 临时置位，后续由 AnimationEvent 驱动。</summary>
    public bool IsInAttackFrame { get; set; }

    /// <summary>当前攻击标签（敌人攻击时设置，弹反/结算用；P4c 由攻击组件写入）</summary>
    public string CurrentAttackLabel { get; set; }

    protected EnemyStunState stunState;
    private float stunCooldownTimer;

    // ── 命中本地冻结（独立卡帧）──
    /// <summary>本地冻结剩余时长（秒；>0 = 冻结中）。用 deltaTime 倒数 → 全局卡肉(timeScale=0)期间不倒数，天然叠加</summary>
    private float _localFreezeRemaining;
    /// <summary>冻结前暂存的速度（解除时恢复，保证击退速度不在冻结期间衰减）</summary>
    private Vector2 _localFreezeSavedVelocity;
    /// <summary>空中滞空冻结模式：结束恢复击退速度(正常击退轨迹),重力恢复</summary>
    private bool _airHangFreeze;
    /// <summary>本次受击 AddForce 目标速度(Impulse 延迟物理步应用,冻结前先算好;冻结恢复用它而非旧速度)</summary>
    private Vector2 _pendingKnockbackVelocity;
    /// <summary>上一帧是否在地面(落地上升沿检测用)</summary>
    private bool _wasGrounded;
    /// <summary>上一帧 y 速度(真正落地判定:下落快→落地瞬间速度归零)</summary>
    private float _lastFrameVy;
    /// <summary>最后击退的水平方向(落地弹跳方向;0 = 无记录)</summary>
    private float _lastKnockbackDirX;
    /// <summary>受击标记:被空中第三段(下砸)命中,落地时必触发落地冲击(不依赖速度阈值)</summary>
    private bool _pendingGroundImpact;
    /// <summary>空中击退中:移动系统完全让位(不清 x),让斜向击退速度自由飞,落地才恢复</summary>
    private bool _airKnockbackActive;
    /// <summary>空中吸附玩家中:向玩家检测矩形中心拉 x,保持连段距离(落地/死亡清除)</summary>
    private bool _pullToPlayer;

    /// <summary>是否正在本地冻结中</summary>
    public bool IsLocallyFrozen => _localFreezeRemaining > 0f;

    /// <summary>
    /// 命中本地冻结 — 只冻结本敌人自身：FSM 停更新、移动停止、动画停播。
    /// duration ≤ 0 忽略；冻结中再次调用取更长的剩余时长；已死亡忽略（死亡动画正常播放）。
    /// 敌人体感总冻结 = 全局卡肉时长（timeScale=0 使 deltaTime 停走）+ 本时长。
    /// </summary>
    public void ApplyLocalFreeze(float duration)
    {
        if (isDead || duration <= 0f) return;
        if (_localFreezeRemaining > 0f)
        {
            if (duration > _localFreezeRemaining) _localFreezeRemaining = duration;
            return;
        }
        _localFreezeRemaining = duration;
        if (_animator != null) _animator.speed = 0f;
        if (rb != null)
        {
            _localFreezeSavedVelocity = rb.velocity;
            rb.velocity = Vector2.zero;
        }
    }

    /// <summary>解除本地冻结：恢复动画速度与暂存的击退速度</summary>
    private void EndLocalFreeze()
    {
        if (_animator != null) _animator.speed = 1f;
        if (_airHangFreeze)
        {
            // 滞空模式:恢复重力(冻结期间关闭定身)
            if (rb != null) rb.gravityScale = 1f;
            Debug.Log($"[AirSlam] {name} 冻结结束恢复速度={_localFreezeSavedVelocity}");
            _airHangFreeze = false;
        }
        // 恢复击退速度(滞空 = 停住后继续正常击退轨迹;普通冻结 = 恢复原行为)
        if (rb != null) rb.velocity = _localFreezeSavedVelocity;
        _localFreezeSavedVelocity = Vector2.zero;
    }

    /// <summary>
    /// 空中滞空冻结 — 击飞中的敌人再受击:停住(动画/速度/FSM 停、关重力定身),结束恢复击退速度继续正常击退轨迹。
    /// 只对空中生效;地面受击走原硬直逻辑。冻结中再次调用取更长的剩余时长(空中连段滞空)。
    /// </summary>
    public void ApplyAirHangFreeze(float duration)
    {
        if (isDead || duration <= 0f || IsGrounded) return;
        if (_localFreezeRemaining > 0f)
        {
            if (duration > _localFreezeRemaining) _localFreezeRemaining = duration;
            // 冻结中再次受击:恢复速度必须换成新击退(下砸击退不能丢,否则恢复旧速度慢落)
            if (_pendingKnockbackVelocity.sqrMagnitude > 0.0001f)
            {
                _localFreezeSavedVelocity = _pendingKnockbackVelocity;
                _pendingKnockbackVelocity = Vector2.zero;
            }
            return;
        }
        _localFreezeRemaining = duration;
        _airHangFreeze = true;
        _pullToPlayer = true;   // 空中受击:吸附玩家检测矩形中心,保持连段距离
        if (_animator != null) _animator.speed = 0f;
        if (rb != null)
        {
            // 恢复速度优先用本次击退目标速度(AddForce 延迟物理步应用,冻结时 rb.velocity 还是旧值);
            // 无击退时退回当前速度
            _localFreezeSavedVelocity = _pendingKnockbackVelocity.sqrMagnitude > 0.0001f
                ? _pendingKnockbackVelocity
                : rb.velocity;
            _pendingKnockbackVelocity = Vector2.zero;
            Debug.Log($"[AirSlam] {name} 冻结存速度={_localFreezeSavedVelocity}, 当前vel={rb.velocity}, hang={duration}");
            rb.velocity = Vector2.zero;
            rb.gravityScale = 0f;   // 无全局卡帧时也必须定身:关重力防下落,结束恢复
        }
    }

    /// <summary>强制解除本地冻结 — 子类覆写 Die() 等场景调用，保证死亡动画/结算不被冻结卡住</summary>
    protected void ForceEndLocalFreeze()
    {
        _localFreezeRemaining = 0f;
        EndLocalFreeze();
    }

    // ============================================================
    // 抽象方法 — 子类必须实现
    // ============================================================

    /// <summary>返回初始 FSM 状态（子类返回各自的 IdleState）</summary>
    protected abstract IState GetInitialState();

    /// <summary>创建追击状态（子类返回各自的 ChaseState 实现）</summary>
    public abstract IState CreateChaseState();

    /// <summary>
    /// 创建攻击入口状态（受击/追击后按 player 所在框选攻击动画；默认返回 CreateChaseState()）。
    /// 远程 override CreateChaseState() 返回 RangedAttackState（判框入口），无需再覆盖本方法。
    /// </summary>
    public virtual IState CreateAttackEntryState() => CreateChaseState();

    /// <summary>创建晕眩结束的后备状态（近战→Patrol，远程→Idle）</summary>
    public abstract IState CreateFallbackState();

    // ============================================================
    // 生命周期
    // ============================================================

    // ── 内置默认值（Inspector 与 SO 均未设置时的兜底，与原代码默认值一致）──
    protected const float DefaultMaxHealth = 3f;
    protected const float DefaultDetectionWidth = 8f;
    protected const float DefaultDetectionHeight = 3f;
    protected const float DefaultAttackWidth = 1.5f;
    protected const float DefaultAttackHeight = 1.5f;
    protected const float DefaultAttackCooldown = 1f;
    protected const float DefaultRangedKnockback = 5f;

    /// <summary>
    /// 数值解析：Inspector 手填(>0) → SO 对应 Lv 档(>0) → 内置默认。
    /// 0 = 未设置（Lv 收敛取值链，子类/攻击组件复用）。
    /// </summary>
    protected float Resolve(float inspector, float soValue, float fallback)
        => inspector > 0f ? inspector : (soValue > 0f ? soValue : fallback);

    protected override void Awake()
    {
        base.Awake();

        // [移动范围] 出生锚点兜底：OnValidate 未跑过（动态生成 / 运行时实例化）时同步为当前摆放位置。
        // 已初始化（编辑器 OnValidate 同步过）则保留序列化值，允许手动改 homeX。
        if (!homeXInitialized)
        {
            homeX = transform.position.x;
            homeXInitialized = true;
        }

        // [Lv 收敛] 按 level 取 SO 对应档（config 为空 → 全 null → 全走 Inspector/内置默认）
        lvStats = config != null ? config.GetLvStats(level) : null;

        // 取值链：Inspector 手填(>0) → SO 对应 Lv 档(>0) → 内置默认（0 = 未设置）
        maxHealth = Resolve(maxHealth, lvStats?.maxHealth ?? 0f, DefaultMaxHealth);
        detectionWidth = Resolve(detectionWidth, lvStats?.detectionWidth ?? 0f, DefaultDetectionWidth);
        detectionHeight = Resolve(detectionHeight, lvStats?.detectionHeight ?? 0f, DefaultDetectionHeight);
        attackWidth = Resolve(attackWidth, lvStats?.attackWidth ?? 0f, DefaultAttackWidth);
        attackHeight = Resolve(attackHeight, lvStats?.attackHeight ?? 0f, DefaultAttackHeight);
        attackCooldownDuration = Resolve(attackCooldownDuration, lvStats?.attackCooldownDuration ?? 0f, DefaultAttackCooldown);
        rangedKnockbackForce = Resolve(rangedKnockbackForce, lvStats?.rangedKnockbackForce ?? 0f, DefaultRangedKnockback);

        // maxHealth 终值走管线（无 manager 回退 baseValue，对齐 CharacterBase.MoveSpeed 写法）
        // 注意：必须在 FirstBoss 的 maxHealth *= hpMultiplier 之前完成，保证 Boss 最终 = 基础值 × 倍率
        _baseMaxHealth = maxHealth;   // 存管线前基础值（装备修饰器变化时重算用）
        maxHealth = statModManager != null ? statModManager.GetFinalValue(_baseMaxHealth, StatId.MaxHealth) : _baseMaxHealth;
        currentHealth = maxHealth;

        renderers = GetComponentsInChildren<Renderer>();
        Color firstColor = renderers.Length > 0 ? renderers[0].material.color : Color.white;
        stateColor = firstColor;

        fsm = new StateMachine();
        player = PlayerController.Instance?.transform;
        passiveEquipManager = PassiveEquipManager.Instance;
        _poise = GetComponent<PoiseComponent>();
    }

#if UNITY_EDITOR
    /// <summary>
    /// 编辑器专用：首次加载/摆放 enemy 时把出生锚点 homeX 同步为当前摆放位置并置位 homeXInitialized。
    /// 只初始化一次——之后用户可手动改 homeX 覆盖（不会再被同步覆盖）。
    /// Awake 兜底覆盖动态生成 / 运行时实例化（OnValidate 未跑）的情况。
    /// </summary>
    protected override void OnValidate()
    {
        base.OnValidate();  // 基类：自动补齐 rb/col

        if (!homeXInitialized)
        {
            homeX = transform.position.x;
            homeXInitialized = true;
        }
    }
#endif

    protected virtual void OnEnable()
    {
        EventBus.Subscribe<GroundPoundEvent>(OnGroundPound);
        // [2026-08-10] 捡装备/卸下注入修饰器时重算 maxHealth（对齐玩家侧 PlayerHealth）
        EventBus.Subscribe<StatModifiersChangedEvent>(OnStatModifiersChanged);
    }

    protected virtual void OnDisable()
    {
        EventBus.Unsubscribe<GroundPoundEvent>(OnGroundPound);
        EventBus.Unsubscribe<StatModifiersChangedEvent>(OnStatModifiersChanged);
        OnExitCombatState();  // 场景卸载/对象池回收时确保退出战斗计数

        // 本地冻结清理：禁用/回收时强制解除，防止 animator.speed=0 残留
        if (_localFreezeRemaining > 0f)
        {
            _localFreezeRemaining = 0f;
            EndLocalFreeze();
        }

        // 受击停顿清理：禁用/回收时恢复动画速度，防止 animator.speed=0 残留冻结
        if (hitPauseTimer > 0f)
        {
            hitPauseTimer = 0f;
            if (_animator != null) _animator.speed = 1f;
        }
    }

    /// <summary>销毁兜底：恢复动画速度，防场景切换/销毁时 animator.speed=0 残留冻结</summary>
    protected virtual void OnDestroy()
    {
        if (_animator != null) _animator.speed = 1f;
    }

    protected void Start()
    {
        fsm.ChangeState(GetInitialState());
    }

    protected override void Update()
    {
        base.Update();

        // 命中本地冻结计时（deltaTime 倒数：全局卡肉 timeScale=0 期间不倒数 → 与全局冻结时长叠加）
        if (_localFreezeRemaining > 0f)
        {
            _localFreezeRemaining -= Time.deltaTime;
            if (_localFreezeRemaining <= 0f)
            {
                _localFreezeRemaining = 0f;
                EndLocalFreeze();
            }
        }

        // 落地冲击。
        // "真正落地"判定:grounded 且落地瞬间速度被地面处理(vy 接近 0)。
        // 不用 grounded 本身:基类是射线检测,落地前一段距离就提前 true,直接用它会在下落中途误触发。
        bool groundedNow = IsGrounded;
        float curVy = rb != null ? rb.velocity.y : 0f;
        bool landed = groundedNow && !IsLocallyFrozen && curVy > -1.5f;

        if (landed && _airKnockbackActive)
            _airKnockbackActive = false;   // 落地:移动系统接管(空中击退结束)
        if (landed)
            _pullToPlayer = false;         // 落地:空中吸附解除

        if (!isDead && _pendingGroundImpact && landed)
        {
            _pendingGroundImpact = false;
            Debug.Log($"[AirSlam] {name} 落地触发(标记), vy={curVy}");
            TriggerGroundImpact();
        }
        else if (!isDead && groundedNow && !_wasGrounded && _lastFrameVy < groundImpactSpeedThreshold && curVy > -1.5f)
        {
            Debug.Log($"[AirSlam] {name} 落地触发(速度阈值), 上帧vy={_lastFrameVy}, 当前vy={curVy}");
            TriggerGroundImpact();
        }
        _lastFrameVy = curVy;
        _wasGrounded = groundedNow;

        // 空中击退撞管道:向 x 方向射线检测 Channel 层(复用巡逻 channelLayer),
        // 命中动漫反弹(横向速度反向×系数 + 挤压形变),y 保留继续下落,防敌人被打进管道
        if (_airKnockbackActive && rb != null && channelLayer != 0 && Mathf.Abs(rb.velocity.x) > 0.1f)
        {
            float dir = Mathf.Sign(rb.velocity.x);
            float checkDist = Mathf.Abs(rb.velocity.x) * Time.deltaTime + 0.2f;
            RaycastHit2D hit = Physics2D.Raycast(rb.position, Vector2.right * dir, checkDist, channelLayer);
            if (hit.collider != null)
            {
                rb.velocity = new Vector2(-rb.velocity.x * airHitWallBounce, rb.velocity.y);
                if (airHitWallSquash > 0f)
                    StartCoroutine(WallBounceSquashRoutine(transform, airHitWallSquash));
                Debug.Log($"[AirSlam] {name} 空中击退撞管道,反弹 x={rb.velocity.x}");
            }
        }

        // 空中吸附:玩家空中连段时把敌人往检测矩形中心拉 x(只吸水平,不碰 y 下落),
        // 防止玩家前冲移动超过敌人导致错位/判定丢失。落地/死亡自动解除。
        if (_pullToPlayer && airHitPullSpeed > 0f && !isDead && rb != null)
        {
            var pc = PlayerController.Instance;
            if (pc != null && pc.Combat != null)
            {
                float targetX = pc.transform.position.x + pc.GetFacing() * pc.Combat.MeleeRangeOffset;
                Vector2 pos = rb.position;
                pos.x = Mathf.Lerp(pos.x, targetX, airHitPullSpeed * Time.deltaTime);
                rb.position = pos;
            }
        }

        if (hitFlashTimer > 0f)
        {
            hitFlashTimer -= Time.deltaTime;
            if (hitFlashTimer <= 0f)
                RestoreColors();
        }

        // 受击击退滑行窗口递减（归零后 OnFixedUpdate 恢复 Move(0) 正常停住）
        if (hitKnockbackWindow > 0f)
            hitKnockbackWindow -= Time.deltaTime;

        // 蓄力闪烁驱动：受击闪白期间(hitFlashTimer>0)让位，闪白优先
        if (isChargeFlashing && hitFlashTimer <= 0f)
            UpdateChargeFlash();

        if (attackCooldownTimer > 0f)
            attackCooldownTimer -= Time.deltaTime;

        if (stunCooldownTimer > 0f)
            stunCooldownTimer -= Time.deltaTime;

        // 嘲讽计时递减：归零自动解除（仇恨回到真实玩家；幻象销毁后 OverrideTarget 判空自动回退玩家）
        if (tauntTimer > 0f)
        {
            tauntTimer -= Time.deltaTime;
            if (tauntTimer <= 0f)
                ClearTaunt();
        }
    }

    /// <summary>
    /// 敌人动画参数更新 — 每帧聚合 Locomotion 双参数（IsIdle/IsMove 互斥）。
    /// busy（死亡/攻击/受击）时两者全 false → 当前 Locomotion 状态 Exit → Entry 重判命中 IsDead/IsAttacking/IsHurt。
    /// stun 无 hurt 动画：moveInput=0 → 自然回落 Idle。
    /// </summary>
    protected override void UpdateAnimation()
    {
        if (_animator == null) return;

        // 注：不设 Speed——melee controller 只有 Bool 参数（IsIdle/IsMove/IsAttacking/IsDead），无 Speed 参数，
        //      SetFloat 不存在的参数会每帧报错。需要速度档位的类型（如 ranged 的 Run）override 本方法自行设置。
        bool moving = Mathf.Abs(moveInput) > 0.01f;
        bool busy = isDead
            || _animator.GetBool(AnimParams.IsAttacking)
            || _animator.GetBool(AnimParams.IsHurt);
        _animator.SetBool(AnimParams.IsIdle, !moving && !busy);
        _animator.SetBool(AnimParams.IsMove, moving && !busy);
    }

    protected override void OnUpdate()
    {
        if (isDead) return;
        if (_localFreezeRemaining > 0f) return;   // 本地冻结：FSM 停更，AI/攻击全部暂停

        // 受击停顿：卡帧结束后的自身小冻结（不移动 + 冻结受击动画，只影响本 enemy）。
        // 用 Time.deltaTime 倒数 → 全局卡帧(timeScale=0)期间不走，卡帧结束后才开始计 = 总卡顿 = 卡帧 + 停顿
        if (hitPauseTimer > 0f)
        {
            hitPauseTimer -= Time.deltaTime;
            moveInput = 0f;                        // 停顿期间不移动
            if (_animator != null) _animator.speed = 0f;   // 冻结受击动画（停在当前帧）
            if (hitPauseTimer <= 0f && _animator != null) _animator.speed = 1f;   // 停顿结束恢复
            return;                                // 短路其余逻辑（攻击/状态切换等）
        }

        fsm?.Update();
    }

    protected override void OnFixedUpdate()
    {
        // 命中本地冻结：跳过全部移动 — moveInput 残留旧值，不拦会继续 Move() 滑步
        if (_localFreezeRemaining > 0f) return;

        // 空中击退中:移动系统完全让位,不清 x,让斜向击退速度自由飞(落地时清标志恢复)
        if (_airKnockbackActive) return;

        // [移动范围] 数学拦截：已在边界(|x-homeX| >= homeRange)且仍朝边界外走 → 停。
        // 朝范围中心方向（返回）不拦；homeRange=0 不限制。
        // 状态无关：Patrol/Chase/Rush 统一遵守，防止敌人跨区/进管道。fsm 状态不动，
        // 追击时停在边界面向玩家，玩家离开检测范围自然回巡逻。
        if (homeRange > 0f
            && Mathf.Abs(transform.position.x - homeX) >= homeRange
            && Mathf.Sign(moveInput) == Mathf.Sign(transform.position.x - homeX))
        {
            moveInput = 0f;
        }

        // FSM 状态已经设好 moveInput，这里统一执行物理移动
        if (Mathf.Abs(moveInput) > 0.01f)
        {
            Move(moveInput);
            UpdateFacing(moveInput);
        }
        else if (fsm?.CurrentState != stunState && hitKnockbackWindow <= 0f)
        {
            // 硬直中/击退滑行窗口内不零速，让击退自然衰减
            Move(0f);
        }
    }

    // ============================================================
    // 事件订阅
    // ============================================================

    /// <summary>
    /// 砸地攻击标签 — 复用重击近战标签（与 PlayerCombat.meleeFinisherAttackType 值一致，不跨类引用）。
    /// 命中 PoiseComponent.meleeAttackLabels 白名单 → OnHitBy 走近战 stun 路径（保留原晕眩行为）+ 计入霸体计数器。
    /// 禁止改空串：空串会走远程分支（不晕 + rangedKnockbackForce 击退 + 立即追击，行为变化）。
    /// </summary>
    private const string GroundPoundAttackLabel = "Sword_Heavy";
    /// <summary>空中第三段(下砸)攻击标签 — 玩家 PlayerCombat.airFinisherAttackType 默认同名,收到即标记落地冲击</summary>
    private const string AirSlamLabel = "AirSlam_Heavy";
    private void OnGroundPound(GroundPoundEvent e)
    {
        if (isDead) return;

        int selfLayer = 1 << gameObject.layer;
        if ((e.targetLayers & selfLayer) == 0) return;

        Vector2 toCenter = (Vector2)transform.position - e.center;
        toCenter.y = 0f;
        float dist = toCenter.magnitude;
        if (dist > e.radius) return;

        // P2a: 统一走 CombatResolver — 攻击标签用重击近战标签：
        //   掉血/闪白/VFX 由 ApplyDamage，晕眩由 OnHitBy(IsMelee=true)→EnterStunState，击退走 Poise 霸体判定
        Vector2 knockDir = toCenter.normalized;
        knockDir.y = 0f;
        if (knockDir.magnitude < 0.01f) knockDir = Vector2.right;

        CombatResolver.Resolve(null, this, new DamageInfo
        {
            amount = e.damage,
            source = null,                       // GroundPoundEvent 无攻击者字段，保持 null（defender 是 enemy，source 不影响结算）
            sourcePosition = e.center,
            attackLabel = GroundPoundAttackLabel,
            knockback = new Knockback
            {
                direction = knockDir,
                force = e.knockbackForce * (rb != null ? rb.mass : 1f),  // 原实现直接设 velocity=knockbackForce，改 Impulse 后乘 mass 等价保持手感
                duration = 0f,
                ignoreResistance = false
            }
        });
    }

    /// <summary>
    /// 修饰器变化（enemy 捡装备注入 / 卸下）时：MaxHealth 受影响则重算终值 + 等比缩放 currentHealth。
    /// 对齐玩家侧 PlayerHealth.OnStatModifiersChanged（保持当前血量百分比不变）。
    /// </summary>
    private void OnStatModifiersChanged(StatModifiersChangedEvent e)
    {
        if (isDead) return;
        foreach (var statId in e.affectedStatIds)
        {
            if (statId == StatId.MaxHealth)
            {
                float newMax = statModManager != null
                    ? statModManager.GetFinalValue(_baseMaxHealth, StatId.MaxHealth)
                    : _baseMaxHealth;
                // 等比缩放：保持当前血量百分比不变
                float ratio = maxHealth > 0f ? currentHealth / maxHealth : 1f;
                currentHealth = Mathf.Clamp(ratio * newMax, 0f, newMax);
                maxHealth = newMax;
                break;
            }
        }
    }

    // ============================================================
    // 受伤 / 死亡
    // ============================================================

    /// <summary>
    /// 造成伤害。attackType 可选，匹配到 hitVFXVariants 中的条目时使用对应 VFX，否则用默认 hitVFXPrefab。
    /// P4b:内部转 ApplyDamage(DamageInfo)，保留 EnterStunState 前置，外部调用方不受影响。
    /// </summary>
    public virtual void TakeDamage(float amount, string attackType = "")
    {
        if (isDead) return;

        EnterStunState();
        ApplyDamage(new DamageInfo
        {
            amount = amount,
            source = null,
            sourcePosition = transform.position,
            attackLabel = attackType,
            knockback = Knockback.None
        });
    }

    /// <summary>
    /// 造成伤害（含攻击来源）。attackType 匹配 VFX 变体。
    /// P4b:内部转 ApplyDamage(DamageInfo) + OnHitBy（扣血/闪白/VFX → 近战 stun / 远程击退+追击），外部调用方不受影响。
    /// </summary>
    public virtual void TakeDamageFrom(float amount, Vector2 attackSource, string attackType = "")
    {
        if (isDead) return;

        DamageInfo info = new DamageInfo
        {
            amount = amount,
            source = null,
            sourcePosition = attackSource,
            attackLabel = attackType,
            knockback = Knockback.None
        };

        // 扣血 + 受击 VFX（复用原 TakeDamageFrom 核心段）
        ApplyDamage(info);

        // 受击状态分流：近战 stun 硬直 / 远程击退 + 立即追击
        OnHitBy(info);
    }

    /// <summary>
    /// 扣血 + 受击闪白 + 受击 VFX（普通 + 可选方向）的公共段。返回是否死亡。
    /// </summary>
    /// <param name="vfxPos">VFX 生成位置；null 时用自身 transform.position（TakeDamage 路径）</param>
    /// <param name="hitDir">攻击来源方向；传入时额外生成方向受击 VFX（TakeDamageFrom 路径）</param>
    private bool ApplyDamage(float amount, string attackType, Vector2? vfxPos = null, Vector2? hitDir = null)
    {
        currentHealth -= amount;
        FlashHit();
        hitPauseTimer = enemyHitPause;   // 受击停顿：近战/远程受击都生效（0/空 = 不启用，行为不变）

        Vector2 pos = vfxPos ?? (Vector2)transform.position;

        // 普通受击 VFX（带 ±3° 随机旋转），挂到 Enemy 下跟随移动
        GameObject vfx = GetHitVFX(attackType);
        if (vfx != null)
        {
            float randomAngle = Random.Range(-3f, 3f);
            GameObject instance = VFXSpawner.Spawn(VFXCategory.EnemyVFX, vfx, pos, Quaternion.Euler(0, 0, randomAngle));
            if (instance != null) instance.transform.SetParent(transform);
        }

        // 方向受击 VFX — 朝向攻击反方向（通过翻转 scale.x），挂到 Enemy 下跟随移动
        if (hitDir.HasValue && directionalHitVFXPrefab != null)
        {
            GameObject instance = VFXSpawner.Spawn(VFXCategory.EnemyVFX, directionalHitVFXPrefab, pos, Quaternion.identity);
            if (instance != null)
            {
                Vector3 scale = instance.transform.localScale;
                scale.x = hitDir.Value.x < 0 ? -Mathf.Abs(scale.x) : Mathf.Abs(scale.x);
                instance.transform.localScale = scale;
                instance.transform.SetParent(transform);
            }
        }

        return currentHealth <= 0f;
    }

    /// <summary>
    /// 按 attackType 匹配受击 VFX。命中变体列表中的条目则返回对应 VFX，否则回退 hitVFXPrefab。
    /// </summary>
    private GameObject GetHitVFX(string attackType)
    {
        if (!string.IsNullOrEmpty(attackType))
        {
            foreach (var v in hitVFXVariants)
            {
                if (v.attackType == attackType && v.vfxPrefab != null)
                    return v.vfxPrefab;
            }
        }
        return hitVFXPrefab;
    }

    // ============================================================
    // 通用动画事件（AnimationRelay 转发入口）
    // ============================================================

    /// <summary>攻击命中帧事件 — 转发给当前攻击状态（IEnemyAttackState），ranged/boss 后续攻击状态实现同一接口</summary>
    public virtual void OnAttackHitFrame() => (fsm.CurrentState as IEnemyAttackState)?.OnHitFrame();

    /// <summary>攻击动画结束事件 — 转发给当前攻击状态</summary>
    public virtual void OnAttackAnimationEnd() => (fsm.CurrentState as IEnemyAttackState)?.OnAnimEnd();

    /// <summary>远程攻击蓄力事件（attack2 蓄力帧）— 转发给当前攻击状态</summary>
    public virtual void OnRangedCharge() => (fsm.CurrentState as IEnemyAttackState)?.OnCharge();

    /// <summary>远程攻击发射事件（attack2 发射帧）— 转发给当前攻击状态</summary>
    public virtual void OnRangedFire() => (fsm.CurrentState as IEnemyAttackState)?.OnFire();

    /// <summary>
    /// 死亡播放入口 — 置死亡标记 + 切死亡状态（旧状态 OnExit 自动清 IsAttacking）+ 启动超时兜底。
    /// 原 Die() 的结算内容（VFX/掉落/事件/Destroy）全部移到 OnDeathAnimationEnd()，由 Death 动画末帧事件触发。
    /// </summary>
    protected virtual void Die()
    {
        if (isDead) return;
        isDead = true;
        EndChargeFlash();  // 蓄力中死亡：结束蓄力闪烁（幂等）

        // 本地冻结中死亡 → 立即解除（死亡动画必须正常播放，死亡结算依赖动画末帧事件）
        if (_localFreezeRemaining > 0f)
        {
            _localFreezeRemaining = 0f;
            EndLocalFreeze();
        }

        // 受击停顿中死亡 → 立即解除（同上：animator.speed=0 会卡死死亡动画及其末帧事件）
        if (hitPauseTimer > 0f)
        {
            hitPauseTimer = 0f;
            if (_animator != null) _animator.speed = 1f;
        }

        // 死亡停住：清移动输入 + 水平速度（移动中被杀时 moveInput 残留 → 死亡动画期间会继续滑动）
        moveInput = 0f;
        if (rb != null) rb.velocity = new Vector2(0f, rb.velocity.y);

        fsm.ChangeState(new EnemyDeadState(this, fsm, _animator));

        // 死亡超时兜底：Death clip 时长 + 0.5s，事件链路断时强制 OnDeathAnimationEnd 防卡死
        StartCoroutine(DeathFallbackRoutine());
    }

    /// <summary>
    /// 死亡动画播完 — 执行原 Die() 全部结算内容（退出战斗计数 + 死亡 VFX + 掉落 + 事件 + 销毁）。
    /// 由 Death.anim 末帧事件 OnEnemyDeathEnd → AnimationRelay 转发，或死亡超时兜底触发。
    /// </summary>
    public virtual void OnDeathAnimationEnd()
    {
        // 守卫：只有 Die() 置过 isDead 才允许死亡结算。
        // 防误触发（如事件误挂到 Attack.anim / 重复触发）导致 enemy 无理由销毁。
        if (!isDead) return;

        OnExitCombatState();  // 死亡时退出战斗计数

        // 死亡 VFX
        if (deathVFXPrefab != null)
            VFXSpawner.SpawnOnEnemy(deathVFXPrefab, transform.position);

        // [Phase3] 死亡时装备生成掉落物（在 EnemyDeathEvent 和 Destroy 之前）
        GetComponent<EnemyEquipment>()?.DropOnDeath();

        EventBus.Trigger(new EnemyDeathEvent(this, (Vector2)transform.position));
        Destroy(gameObject);
    }

    /// <summary>
    /// 死亡超时兜底协程 — 采样死亡 clip 时长（+0.5s）作为兜底基准；采样失败（无 Animator / 未命名 Death）回退 1.0s。
    /// 动画事件正常时 OnEnemyDeathEnd 在 clip 末帧先到并销毁，本协程随物体销毁终止。
    /// </summary>
    private IEnumerator DeathFallbackRoutine()
    {
        // 等 Animator 过渡到死亡状态（最多 waitMax 秒），采样死亡 clip 时长
        float elapsed = 0f;
        float clipLen = 0f;
        const float waitMax = 0.4f;
        while (elapsed < waitMax)
        {
            elapsed += Time.deltaTime;
            if (_animator != null)
            {
                var clips = _animator.GetCurrentAnimatorClipInfo(0);
                if (clips.Length > 0 && clips[0].clip != null &&
                    clips[0].clip.name.IndexOf("Death", System.StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    clipLen = clips[0].clip.length;
                    break;
                }
            }
            yield return null;
        }

        float duration = clipLen > 0f ? clipLen + 0.5f : 1.0f;
        yield return new WaitForSeconds(duration);
        if (isDead)
            OnDeathAnimationEnd();
    }

    // ============================================================
    // ICombatant 接口实现（P4b 玩家→敌人结算统一）
    // ============================================================

    // ── 身份 ──
    public GameObject GameObject => gameObject;
    public Transform Transform => transform;

    // ── 受击方 ──
    public PoiseComponent Poise => _poise;
    public virtual bool CanBeDamaged => !isDead;

    /// <summary>
    /// 承受伤害（含击退信息），返回实际造成伤害量。
    /// 复用原 TakeDamageFrom 核心段：受击 VFX（普通+方向）+ 扣血 + 闪白，死亡时 Die()。
    /// 近战 stun 硬直 / 远程追击由 OnHitBy 推送（霸体累计已由 CombatResolver 调 RegisterHit 完成，此处不重复计数）。
    /// </summary>
    public float ApplyDamage(DamageInfo info)
    {
        if (isDead) return 0f;

        // 受击 VFX — 朝攻击来源方向偏移，更真实（与原 TakeDamageFrom 一致）
        Vector2 fromSource = (Vector2)transform.position - info.sourcePosition;
        bool hasDirection = fromSource.sqrMagnitude > 0.0001f;
        Vector2 hitOffset = hasDirection ? fromSource.normalized * -0.15f : Vector2.zero;
        Vector2 vfxPos = (Vector2)transform.position + hitOffset;
        Vector2? hitDir = hasDirection ? (Vector2?)fromSource.normalized : null;

        if (ApplyDamage(info.amount, info.attackLabel, vfxPos, hitDir)) Die();
        return info.amount;
    }

    // ── 结算管线钩子（P4b 敌人侧简单实现保证行为一致；弹反/闪避判定在 P4c 玩家侧接入）──

    /// <summary>闪避判定 — 敌人无闪避</summary>
    public bool TryDodge(DamageInfo info) => false;

    /// <summary>格挡/弹反判定 — 敌人无格挡弹反</summary>
    public bool TryParry(ICombatant attacker, DamageInfo info) => false;

    /// <summary>
    /// 护甲减免 — 伤害 - 护甲，保底 1 点（与玩家 PlayerHealth.ApplyArmorReduction 公式一致）。
    /// 护甲基础值来自 EnemyLvStats.armor（每档，B13），经 StatModifierManager 管线读取
    /// （enemy 已挂 StatModifierManager，参照 EnemyEquipment 同款 GetComponent 方式；
    /// 组件缺失时直接用基础值）。基础值默认 0 → 返回原值，现有战斗数值不变（回归保障）。
    /// </summary>
    public float ApplyArmor(float amount)
    {
        float armor = GetArmorValue();
        if (armor <= 0f) return amount;
        return Mathf.Max(1f, amount - armor);
    }

    /// <summary>护甲终值 = EnemyLvStats.armor 基础值经修饰器管线（Boss/精英可注入修饰器差异化）</summary>
    private float GetArmorValue()
    {
        float baseArmor = lvStats != null ? lvStats.armor : 0f;
        if (statModManager == null) return baseArmor;
        return statModManager.GetFinalValue(baseArmor, StatId.Armor);
    }

    /// <summary>减伤 — 敌人无减伤</summary>
    public float ApplyReduction(float amount) => amount;

    /// <summary>施加击退（CombatResolver 在霸体判定通过后调用；方向/力度由攻击方构造进 Knockback）
    /// 2026-08-18：放开 y 水平化 — 敌人统一按攻击方构造的完整 x/y 向量击退（武器每击配置的 y 生效，可上挑/击飞）。
    /// 空中受击不特殊处理:正常击退,滞空冻结只做短暂停住,结束恢复击退速度继续轨迹。</summary>
    public virtual void ApplyKnockback(Knockback knockback)
    {
        if (rb == null || knockback.force <= 0f) return;
        Vector2 knockDir = knockback.direction;
        if (knockDir.magnitude < 0.01f) knockDir = Vector2.right;
        _lastKnockbackDirX = Mathf.Sign(knockDir.x);   // 记录击退水平方向(落地弹跳用)
        // 空中/下落中击退:直接赋值速度(替代 AddForce 物理步延迟),立即生效无静止帧。
        // 判断用"下落中"(velocity.y 明显为负)而非 IsGrounded——grounded 射线会提前命中,
        // 敌人离地还有距离时 IsGrounded 已 true,会误走地面分支导致间歇性 x 被清。
        // 移动系统让位(不清 x),斜向击退速度自由飞。
        if (!IsGrounded || rb.velocity.y < -0.5f)
        {
            float targetX = rb.velocity.x + knockDir.x * (knockback.force / Mathf.Max(0.01f, rb.mass));
            float targetY = rb.velocity.y + knockDir.y * (knockback.force / Mathf.Max(0.01f, rb.mass));
            rb.velocity = new Vector2(targetX, targetY);
            _airKnockbackActive = true;
            return;
        }
        rb.AddForce(knockDir * knockback.force, ForceMode2D.Impulse);
    }

    /// <summary>撞墙形变:水平压扁 + 垂直拉长,再恢复(动漫撞墙挤压,与落地形变反向)</summary>
    private System.Collections.IEnumerator WallBounceSquashRoutine(Transform t, float amount)
    {
        Vector3 original = t.localScale;
        int dir = original.x >= 0f ? 1 : -1;   // 保留朝向符号
        float duration = 0.12f;
        float half = duration * 0.5f;

        for (float timer = 0f; timer < half; timer += Time.deltaTime)
        {
            float p = timer / half;
            t.localScale = new Vector3(
                Mathf.Abs(original.x) * dir * (1f - p * amount),
                original.y * (1f + p * amount),
                original.z);
            yield return null;
        }
        for (float timer = 0f; timer < half; timer += Time.deltaTime)
        {
            float p = timer / half;
            t.localScale = new Vector3(
                Mathf.Abs(original.x) * dir * (1f - (1f - p) * amount),
                original.y * (1f + (1f - p) * amount),
                original.z);
            yield return null;
        }
        t.localScale = original;
    }

    /// <summary>
    /// 落地冲击 — 被击退高速落地:尘土 VFX + 短卡帧震屏 + 落地硬直 + 往击退方向轻微弹跳。
    /// </summary>
    private void TriggerGroundImpact()
    {
        Debug.Log($"[AirSlam] {name} TriggerGroundImpact 执行, stun={groundImpactStun}, bounce={groundBounceForce}, vfx={(groundImpactVFX != null)}");
        if (groundImpactVFX != null)
            VFXSpawner.SpawnInWorld(groundImpactVFX, transform.position);

        if (groundImpactHitStop > 0f)
            HitStopController.Instance?.Trigger(groundImpactHitStop, groundImpactShakeDuration, groundImpactShakeMagnitude, Vector2.down);

        if (groundImpactStun > 0f && !isDead)
            EnterStunState();

        if (groundBounceForce > 0f && rb != null)
        {
            float dir = _lastKnockbackDirX != 0f ? _lastKnockbackDirX
                      : (rb.velocity.x >= 0f ? 1f : -1f);
            // 弹跳:沿击退方向水平弹出去(落地后垂直速度归零,y 交给重力)
            rb.velocity = new Vector2(dir * groundBounceForce, 0f);
        }

        // 动漫形变:落地压扁 → 恢复(player 快速落地同款 squash & stretch)
        if (groundImpactSquash > 0f)
            StartCoroutine(GroundImpactSquashRoutine(transform, groundImpactSquash));
    }

    /// <summary>落地形变:水平拉宽 + 垂直压扁,再恢复(参考 PlayerGroundPound.PoundSquash,动漫挤压拉伸)</summary>
    private System.Collections.IEnumerator GroundImpactSquashRoutine(Transform t, float amount)
    {
        Vector3 original = t.localScale;
        int dir = original.x >= 0f ? 1 : -1;   // 保留朝向符号
        float duration = 0.15f;
        float half = duration * 0.5f;

        for (float timer = 0f; timer < half; timer += Time.deltaTime)
        {
            float p = timer / half;
            t.localScale = new Vector3(
                Mathf.Abs(original.x) * dir * (1f + p * amount),
                original.y * (1f - p * amount),
                original.z);
            yield return null;
        }
        for (float timer = 0f; timer < half; timer += Time.deltaTime)
        {
            float p = timer / half;
            t.localScale = new Vector3(
                Mathf.Abs(original.x) * dir * (1f + (1f - p) * amount),
                original.y * (1f - (1f - p) * amount),
                original.z);
            yield return null;
        }
        t.localScale = original;
    }

    /// <summary>
    /// 受击状态推送 — 近战进 stun 硬直，远程击退+立即追击（原 TakeDamageFrom 分流逻辑）。
    /// 近战击退由 CombatResolver 统一在 ApplyKnockback 施加；RegisterHit 只做霸体累计/判定，不再叠加额外击退力。
    /// </summary>
    public virtual void OnHitBy(DamageInfo info)
    {
        if (isDead) return;

        // 空中第三段(下砸,AirSlam_Heavy)命中:标记落地冲击,落地时必触发(不依赖速度阈值)
        if (info.attackLabel == AirSlamLabel)
        {
            _pendingGroundImpact = true;
            _pullToPlayer = true;   // 空中第三击:吸附玩家检测矩形中心,保持连段距离
            // 结束进行中的滞空冻结(如空中第二击遗留):不恢复旧保存速度,
            // 第三击击退(已 ApplyKnockback/AddForce)独立生效,直接砸向地面
            if (_airHangFreeze)
            {
                _airHangFreeze = false;
                _localFreezeRemaining = 0f;
                _localFreezeSavedVelocity = Vector2.zero;
                _pendingKnockbackVelocity = Vector2.zero;
                if (_animator != null) _animator.speed = 1f;
                if (rb != null) rb.gravityScale = 1f;
                Debug.Log($"[AirSlam] {name} 清除遗留滞空,第三击击退独立生效");
            }
            Debug.Log($"[AirSlam] {name} 收到标记, 空中={!IsGrounded}, 冻结中={IsLocallyFrozen}");
        }

        // 空中受击:普通攻击走滞空冻结(停住),结束恢复击退速度继续正常击退轨迹。
        // 空中第三击(下砸)例外:不走滞空,直接按击退设置砸向地面(速度与力度相关),
        // 落地冲击/形变/弹跳全部等落地后再执行(标记触发)。
        bool airborne = !IsGrounded;
        bool isAirSlam = info.attackLabel == AirSlamLabel;
        if (airborne && airHitHangDuration > 0f && !isAirSlam)
            ApplyAirHangFreeze(airHitHangDuration);

        // 落雷（Thunder_Strike）：强制硬直，不区分近战/远程路径（决策 D8）。
        // 韧性判定已在 CombatResolver 跳过 Poise.RegisterHit → 霸体目标同样硬直。
        if (info.attackLabel == ThunderStrike.AttackLabel)
        {
            EnterStunState();
            return;
        }

        bool isMelee = _poise != null && _poise.IsMeleeAttack(info.attackLabel);

        if (isMelee)
        {
            // ── 近战路径：始终进入 stun 硬直（不受霸体影响）──
            //    注意：不立即 fsm.ChangeState(CreateChaseState())，让 stun 真正执行 0.5s
            //          EnemyStunState.OnUpdate 会在 timer 归零后自动转 Chase/Fallback
            EnterStunState();
        }
        else
        {
            // ── 远程路径：进入攻击入口状态 ──
            //    击退统一由 CombatResolver.ApplyKnockback 施加（方向/力度由攻击方构造进 Knockback，
            //    与近战 enemy 一致）；仅当攻击无击退配置（force<=0，如子弹/普通攻击段）时用
            //    rangedKnockbackForce 兜底，防止与 ApplyKnockback 双重叠加导致方向/力度混乱（P4b 后遗留）。
            //    空中受击不施加兜底击退(不要 x),滞空冻结接管。
            if (!airborne && info.knockback.force <= 0f)
            {
                Vector2 hitDir = ((Vector2)transform.position - info.sourcePosition).normalized;
                Vector2 knockDir = hitDir;
                knockDir.y = 0f;
                if (knockDir.magnitude < 0.01f) knockDir = Vector2.right;
                rb.AddForce(knockDir * rangedKnockbackForce, ForceMode2D.Impulse);
            }

            // 受击击退滑行窗口：攻击入口状态不再清速度，窗口内 OnFixedUpdate 不 Move(0)，
            // 让击退速度自然衰减（对齐近战 stun 路径的保留击退行为，否则远程 enemy 击退被吞）
            hitKnockbackWindow = 0.2f;

            fsm.ChangeState(CreateAttackEntryState());
        }
    }

    // ============================================================
    // 踩头硬直
    // ============================================================

    /// <summary>是否处于硬直保护中（踩头后的冷却期）</summary>
    public bool IsStunned => stunCooldownTimer > 0f;

    /// <summary>注入 EnemyStunState 实例（由子类在 Start() 中调用）</summary>
    public void SetStunState(EnemyStunState s) => stunState = s;

    /// <summary>进入硬直状态（由 PlayerCombat 弹反重击 / 外部调用）</summary>
    public void EnterStunState()
    {
        if (stunCooldownTimer > 0f || isDead) return;
        stunCooldownTimer = 0.5f;
        fsm.ChangeState(stunState);
    }

    // ============================================================
    // 战斗状态追踪（per-enemy guard，防止重复触发 PassiveEquipManager.SetCombatState）
    // ============================================================

    /// <summary>进入战斗状态（Chase/Attack）。仅首次进入时通知 PassiveEquipManager。</summary>
    public void OnEnterCombatState()
    {
        if (isInCombatState) return;
        isInCombatState = true;
        passiveEquipManager?.SetCombatState(true);
    }

    /// <summary>退出战斗状态（回到 Idle/Patrol 或死亡）。仅首次退出时通知 PassiveEquipManager。</summary>
    public void OnExitCombatState()
    {
        if (!isInCombatState) return;
        isInCombatState = false;
        passiveEquipManager?.SetCombatState(false);

        // 退出战斗时重置霸体计数器，确保每次战斗独立计算
        _poise?.ResetPoise();
    }

    // ============================================================
    // 受伤反馈
    // ============================================================

    private void FlashHit()
    {
        EndChargeFlash();  // 受击 = 中断蓄力闪烁（防止闪白结束后残留旧蓄力闪烁）
        hitFlashTimer = hitFlashDuration;
        foreach (Renderer r in renderers) r.material.color = hitColor;
    }

    /// <summary>蓄力闪烁开始（蓄力帧 OnCharge 调用；幂等，重复调用只重置开始时间）</summary>
    public void BeginChargeFlash()
    {
        isChargeFlashing = true;
        chargeFlashStartTime = Time.time;
    }

    /// <summary>
    /// 蓄力闪烁结束（发射帧 OnFire / 攻击状态 OnExit / 受击 / 死亡 调用；幂等）。
    /// 恢复原始材质色（状态色已注释后 stateColor = Awake 初始材质色）。
    /// </summary>
    public void EndChargeFlash()
    {
        if (!isChargeFlashing) return;
        isChargeFlashing = false;
        RestoreColors();
    }

    /// <summary>每帧闪烁：频率随蓄力时长线性加速（方波），灭相位=原始材质色</summary>
    private void UpdateChargeFlash()
    {
        float t = Time.time - chargeFlashStartTime;
        float freq = Mathf.Min(chargeFlashBaseFreq + chargeFlashAccel * t, chargeFlashMaxFreq);
        bool on = Mathf.Sin(t * freq * Mathf.PI) >= 0f;
        Color c = on ? chargeColor : stateColor;
        foreach (Renderer r in renderers)
            if (r != null)
                r.material.color = c;
    }

    /// <summary>设置所有渲染器为当前状态色</summary>
    public void ApplyStateColor(Color color)
    {
        stateColor = color;
        foreach (Renderer r in renderers)
            if (r != null)
                r.material.color = color;
    }

    /// <summary>短暂闪烁颜色后恢复状态色（用于攻击等瞬间反馈）</summary>
    public void FlashColor(Color color, float duration)
    {
        StopAllCoroutines();
        StartCoroutine(FlashRoutine(color, duration));
    }

    private System.Collections.IEnumerator FlashRoutine(Color color, float duration)
    {
        foreach (Renderer r in renderers)
            if (r != null) r.material.color = color;
        yield return new WaitForSeconds(duration);
        foreach (Renderer r in renderers)
            if (r != null) r.material.color = stateColor;
    }

    private void RestoreColors()
    {
        foreach (Renderer r in renderers)
            if (r != null)
                r.material.color = stateColor;
    }

    // ============================================================
    // 辅助方法（FSM 状态使用）
    // ============================================================

    /// <summary>当前面朝方向（1=右, -1=左），供外部组件读取</summary>
    public int Facing => facing;

    /// <summary>
    /// 目标是否存活（死亡后 enemy 停止检测/追击/攻击）。
    /// B11 语义确认：统一走 PlayerTarget —— 嘲讽目标为幻象时无 PlayerHealth（ph==null → 返回存活），
    /// 即嘲讽期间玩家死亡幻象仍拉仇恨（接受该行为）；tauntTimer 归零 OverrideTarget=null 后恢复查真实玩家。
    /// </summary>
    private bool IsPlayerAlive()
    {
        if (PlayerTarget == null) return false;
        var ph = PlayerTarget.GetComponent<PlayerHealth>();
        return ph == null || !ph.IsDead;
    }

    public bool CanSeePlayer()
    {
        if (!IsPlayerAlive()) return false;
        if (PlayerTarget == null) return false;
        if (!IsInDetectionRect()) return false;
        return HasLineOfSight();
    }

    private bool IsInDetectionRect()
    {
        float deltaX = PlayerTarget.position.x - transform.position.x;
        float deltaY = PlayerTarget.position.y - transform.position.y;
        return Mathf.Abs(deltaX) <= detectionWidth * 0.5f
            && Mathf.Abs(deltaY) <= detectionHeight * 0.5f;
    }

    private bool HasLineOfSight()
    {
        float dist = Vector2.Distance(transform.position, PlayerTarget.position);
        Vector2 dir = ((Vector2)(PlayerTarget.position - transform.position)).normalized;
        Vector2 origin = (Vector2)transform.position + Vector2.up * 0.5f;

        RaycastHit2D[] hits = Physics2D.RaycastAll(origin, dir, dist);
        foreach (RaycastHit2D hit in hits)
        {
            if (hit.transform == transform || hit.transform.IsChildOf(transform))
                continue;
            // 目标本体（玩家或嘲讽幻象）视为可见；幻象无碰撞体时射线自然穿透到目标位置
            if (hit.transform == PlayerTarget)
                return true;
            if (hit.transform.TryGetComponent(out PlayerController _))
                return true;
            return false;
        }
        return true;
    }

    public bool PlayerInAttackRange()
    {
        if (!IsPlayerAlive()) return false;
        if (PlayerTarget == null) return false;
        float deltaX = PlayerTarget.position.x - transform.position.x;
        float deltaY = PlayerTarget.position.y - transform.position.y;
        return Mathf.Abs(deltaX) <= attackWidth * 0.5f && Mathf.Abs(deltaY) <= attackHeight * 0.5f;
    }

    public float DirectionToPlayer()
    {
        if (PlayerTarget == null) return 0f;
        return PlayerTarget.position.x > transform.position.x ? 1f : -1f;
    }

    /// <summary>
    /// 巡逻悬崖检测 — 判断移动前方脚下是否还有地面（从脚底向下的探射线）。
    /// 前方无地面（悬崖/空洞）返回 false，巡逻状态应转向，防止敌人走下悬崖。
    /// 脚底优先用碰撞体底部 bounds.min.y（更贴合 pivot 偏移），无碰撞体时回退 transform 下方 0.5f。
    /// </summary>
    /// <param name="dir">巡逻方向（1=右, -1=左）</param>
    /// <returns>true = 前方脚下有地面（可继续走）</returns>
    public bool HasGroundAhead(int dir)
    {
        float footY = col != null ? col.bounds.min.y : transform.position.y - 0.5f;
        Vector2 origin = new Vector2(transform.position.x + dir * cliffCheckForward, footY);
        RaycastHit2D hit = Physics2D.Raycast(origin, Vector2.down, cliffCheckDown, groundLayer);
        return hit.collider != null;
    }

    /// <summary>巡逻管道检测 — 水平射线检测前方 Channel 层。命中返回 true，巡逻应转身。
    /// 注意:管道 collider 是 trigger,Physics2D.Raycast 默认忽略 trigger,必须用 ContactFilter2D useTriggers=true。
    /// 团结引擎的 ContactFilter2D 重载签名 = (origin, direction, filter, results[], distance),返回命中数。</summary>
    public bool HasChannelAhead(int dir)
    {
        Vector2 origin = new Vector2(transform.position.x + dir * 0.1f, transform.position.y + channelRayHeightOffset);
        var filter = new ContactFilter2D { useTriggers = true, layerMask = channelLayer };
        int count = Physics2D.Raycast(origin, Vector2.right * dir, filter, channelCheckHits, channelCheckForward);
        return count > 0;
    }

    /// <summary>是否可以对目标发起攻击（综合所有条件）。子类可覆盖以添加额外条件（如远程后退区）。
    /// B11：统一走 PlayerTarget —— 嘲讽幻象在攻击框内时正常出招（攻击打空=幻象不可被攻击）。</summary>
    public virtual bool CanAttack()
    {
        if (PlayerTarget == null) return false;
        if (!CanSeePlayer()) return false;
        if (attackCooldownTimer > 0f) return false;

        // 目标空中击飞时不攻击，避免无限连击（仅对真实玩家生效；幻象无 PlayerController 跳过）
        var pc = PlayerTarget.GetComponent<PlayerController>();
        if (pc != null)
        {
            var ph = pc.GetComponent<PlayerHealth>();
            if (ph != null && ph.IsAirHurt) return false;
        }

        float deltaX = PlayerTarget.position.x - transform.position.x;
        float deltaY = PlayerTarget.position.y - transform.position.y;
        return Mathf.Abs(deltaX) <= attackWidth * 0.5f && Mathf.Abs(deltaY) <= attackHeight * 0.5f;
    }

    // ============================================================
    // Gizmos
    // ============================================================

#if UNITY_EDITOR
    protected override void OnDrawGizmosSelected()
    {
        base.OnDrawGizmosSelected();

        Vector3 pos = transform.position;

        // 检测矩形（黄色半透明填充 + 线框）
        DrawRectGizmo(pos, detectionWidth, detectionHeight,
            new Color(1f, 1f, 0f, 0.08f), new Color(1f, 1f, 0f, 0.3f));

        // 攻击矩形（红色半透明填充 + 线框）
        DrawRectGizmo(pos, attackWidth, attackHeight,
            new Color(1f, 0f, 0f, 0.08f), new Color(1f, 0f, 0f, 0.5f));

        // 巡逻悬崖检测射线（橙色；仅辅助调试 Inspector 参数）
        float footY = col != null ? col.bounds.min.y : pos.y - 0.5f;
        Vector2 cliffOrigin = new Vector2(pos.x + Facing * cliffCheckForward, footY);
        Gizmos.color = new Color(1f, 0.6f, 0f, 0.9f);
        Gizmos.DrawLine(cliffOrigin, cliffOrigin + Vector2.down * cliffCheckDown);
        Gizmos.DrawSphere(cliffOrigin, 0.05f);

        // 移动范围边界（绿色竖线；以出生锚点 homeX 为中心 ± homeRange，与近战 patrolRange 蓝色竖线区分）
        // homeX 已由 OnValidate 同步为摆放位置（可手动改），编辑/运行态一致
        if (homeRange > 0f)
        {
            float h = col != null ? col.bounds.size.y : 2f;
            Gizmos.color = new Color(0f, 1f, 0f, 0.7f);
            Vector3 left = new Vector3(homeX - homeRange, footY, 0f);
            Vector3 right = new Vector3(homeX + homeRange, footY, 0f);
            Gizmos.DrawLine(left, left + Vector3.up * h);
            Gizmos.DrawLine(right, right + Vector3.up * h);
        }
    }

    /// <summary>绘制矩形 Gizmo：半透明填充 Cube + 四条边线框</summary>
    private static void DrawRectGizmo(Vector3 center, float width, float height, Color fillColor, Color wireColor)
    {
        // 填充：薄 Cube（z 忽略，2D 用）
        Gizmos.color = fillColor;
        Gizmos.DrawCube(center, new Vector3(width, height, 0.01f));

        // 线框：四条边
        Gizmos.color = wireColor;
        float hw = width * 0.5f;
        float hh = height * 0.5f;
        Vector3 tl = center + new Vector3(-hw,  hh, 0f);
        Vector3 tr = center + new Vector3( hw,  hh, 0f);
        Vector3 br = center + new Vector3( hw, -hh, 0f);
        Vector3 bl = center + new Vector3(-hw, -hh, 0f);
        Gizmos.DrawLine(tl, tr);
        Gizmos.DrawLine(tr, br);
        Gizmos.DrawLine(br, bl);
        Gizmos.DrawLine(bl, tl);
    }
#endif
}
