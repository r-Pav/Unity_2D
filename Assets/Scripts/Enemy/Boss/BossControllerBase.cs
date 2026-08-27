using UnityEngine;
using System.Collections;

// ============================================================
// Boss 事件（EventBus 触发，UI / 场景逻辑订阅）
// ============================================================

/// <summary>Boss 激活事件 — Player 进入战斗区域时触发，UI 显示血条</summary>
public readonly struct BossActivatedEvent
{
    public readonly BossControllerBase boss;
    public readonly float maxHp;
    public readonly float currentHp;

    public BossActivatedEvent(BossControllerBase boss, float maxHp, float currentHp)
    {
        this.boss = boss;
        this.maxHp = maxHp;
        this.currentHp = currentHp;
    }
}

/// <summary>Boss 击败事件 — Boss 死亡时触发，UI 隐藏血条、开门等</summary>
public readonly struct BossDefeatedEvent
{
    public readonly BossControllerBase boss;

    public BossDefeatedEvent(BossControllerBase boss)
    {
        this.boss = boss;
    }
}

/// <summary>Boss 血量变化事件 — UI 血条更新</summary>
public readonly struct BossHpChangedEvent
{
    public readonly BossControllerBase boss;
    public readonly float currentHp;
    public readonly float maxHp;
    public readonly float ratio;

    public BossHpChangedEvent(BossControllerBase boss, float cur, float max)
    {
        this.boss = boss;
        currentHp = cur;
        maxHp = max;
        ratio = max > 0f ? cur / max : 0f;
    }
}

/// <summary>Boss 阶段切换事件 — 特效/音效层订阅</summary>
public readonly struct BossPhaseChangedEvent
{
    public readonly BossControllerBase boss;
    public readonly int newPhase;

    public BossPhaseChangedEvent(BossControllerBase boss, int newPhase)
    {
        this.boss = boss;
        this.newPhase = newPhase;
    }
}

// ============================================================
// BossControllerBase — Boss 抽象基类
// ============================================================

/// <summary>
/// Boss 控制器抽象基类 — 继承 EnemyControllerBase，在现有 FSM/受伤/死亡基础上
/// 追加 Boss 专属能力：阶段系统、战斗区域管理、延迟死亡、击退抵抗、无敌帧。
/// 子类（FirstBoss 等）覆写 OnPhaseChanged 实现具体阶段行为。
/// </summary>
public abstract class BossControllerBase : EnemyControllerBase
{
    // ============================================================
    // Inspector — 阶段系统
    // ============================================================

    [Header("阶段系统")]
    [Tooltip("HP 阈值数组（比例 0~1），如 [0.6, 0.25] 表示 HP≤60% 进 P2，≤25% 进 P3")]
    [SerializeField] protected float[] hpThresholds = { 0.6f, 0.25f };

    [Tooltip("阶段切换无敌持续时间（秒）")]
    [SerializeField] protected float phaseTransitionDuration = 1.5f;

    // ============================================================
    // Inspector — Boss 通用特性
    // ============================================================

    [Header("Boss 特性")]
    [Tooltip("Boss 显示名称(血条 UI 用)")]
    [SerializeField] protected string bossName = "Boss";

    [Tooltip("击退抵抗系数 0~1，1=完全免疫击退")]
    [SerializeField] [Range(0f, 1f)] protected float knockbackResistance = 0.8f;

    [Tooltip("死亡延迟（秒），期间播死亡动画，之后 Destroy")]
    [SerializeField] protected float deathDelay = 2.0f;

    [Header("普攻间隔")]
    [Tooltip("普通普攻两次之间的最小间隔（秒），只约束普攻，不影响技能与重击")]
    [SerializeField] protected float meleeIntervalDuration = 5f;

    // ============================================================
    // Inspector — 战斗区域
    // ============================================================

    [Header("战斗区域")]
    [Tooltip("Boss 房间 Trigger 碰撞体，Player 进入时激活 Boss")]
    [SerializeField] protected Collider2D combatAreaTrigger;

    // ============================================================
    // Inspector — 技能系统
    // ============================================================

    [Header("技能系统")]
    [Tooltip("Boss 特殊技能槽组件(挂 BossSkillSlots)")]
    [SerializeField] protected BossSkillSlots skillSlots;

    [Tooltip("攻击编排组件(挂 BossAttackDirector,技能/普攻循环)")]
    [SerializeField] protected BossAttackDirector attackDirector;

    [Tooltip("普攻组件(普攻阶段伤害,挂 EnemyMeleeAttack)")]
    [SerializeField] protected EnemyMeleeAttack defaultMelee;

    [Header("VFX")]
    [Tooltip("Boss 死亡 VFX 预制体 — 死亡时 Instantiate")]
    [SerializeField] protected GameObject bossDeathVFXPrefab;

    // ============================================================
    // 运行时状态
    // ============================================================

    /// <summary>当前阶段（0 = P1, 1 = P2, ...）</summary>
    protected int currentPhase;

    /// <summary>是否已激活（Player 进入战斗区域）</summary>
    protected bool isActivated;

    /// <summary>是否处于阶段切换无敌中</summary>
    protected bool isPhaseTransitioning;

    /// <summary>初始最大血量（用于阈值比例比较）</summary>
    protected float initialMaxHealth;

    /// <summary>普攻间隔计时（>0 = 禁止普攻,不影响技能/重击）</summary>
    protected float meleeIntervalTimer;

    /// <summary>当前血量（覆写是因为我们需要在无敌期间也处理伤害为 0）</summary>
    public float CurrentHp => currentHealth;
    public float MaxHp => maxHealth;
    public int CurrentPhase => currentPhase;
    public bool IsActivated => isActivated;
    public string BossName => bossName;

    /// <summary>Boss 标记 — 命中本地冻结时读取 boss 专属卡帧时长</summary>
    public override bool IsBoss => true;

    // ============================================================
    // 生命周期
    // ============================================================

    protected override void Awake()
    {
        base.Awake();

        // [Boss 单独设计] EnemyConfigSO 已 Lv 收敛化（不含 Boss 专属字段），此覆盖块注释保留——
        // 后续剥离到独立 BossConfigSO 时恢复：bossName/hpThresholds/phaseTransitionDuration/
        // knockbackResistance/deathDelay 从 BossConfigSO 读取。
        // if (config != null)
        // {
        //     bossName = config.bossName;
        //     hpThresholds = config.hpThresholds;
        //     phaseTransitionDuration = config.phaseTransitionDuration;
        //     knockbackResistance = config.knockbackResistance;
        //     deathDelay = config.deathDelay;
        // }

        currentHealth = maxHealth;
        initialMaxHealth = maxHealth;
        currentPhase = 0;
    }

    protected override void OnUpdate()
    {
        base.OnUpdate();
        if (meleeIntervalTimer > 0f)
            meleeIntervalTimer -= Time.deltaTime;
    }

    protected override void OnEnable()
    {
        base.OnEnable();
        // 确保战斗区域 Trigger 启用
        if (combatAreaTrigger != null)
            combatAreaTrigger.enabled = true;
    }

    protected override void OnDisable()
    {
        base.OnDisable();
        var mgr = MusicPointManager.Instance;
        if (mgr != null)
            mgr.OnBossMainLoopStarted -= OnBossMainLoopStarted;
    }

    // ============================================================
    // 战斗区域触发
    // ============================================================

    /// <summary>
    /// 子类在 Start() 中应把 combatAreaTrigger 的 isTrigger 设为 true，
    /// 并把碰撞体挂到 Boss 自身或子对象上。此处通过 OnTriggerEnter2D 检测 Player 进入。
    /// </summary>
    protected virtual void OnTriggerEnter2D(Collider2D other)
    {
        if (isActivated) return;
        if (!other.CompareTag("Player")) return;

        ActivateBoss();
    }

    /// <summary>激活 Boss — 开始 AI,触发事件(由 BossRoomTrigger 调用)</summary>
    public virtual void ActivateBoss()
    {
        if (isActivated) return;
        isActivated = true;

        EventBus.Trigger(new BossActivatedEvent(this, maxHealth, currentHealth));

        // 音乐转阶段:前奏切主体循环时 → P2(两段式 Boss 曲;无前奏则不触发)
        var mgr = MusicPointManager.Instance;
        if (mgr != null)
        {
            mgr.OnBossMainLoopStarted -= OnBossMainLoopStarted;
            mgr.OnBossMainLoopStarted += OnBossMainLoopStarted;
        }

        // 切换到追击状态(子类实现)
        if (fsm != null)
            fsm.ChangeState(CreateChaseState());
    }

    /// <summary>音乐切到主体循环(转阶段点):转 P2</summary>
    private void OnBossMainLoopStarted()
    {
        ForcePhaseTransition(1);
    }

    // ============================================================
    // 受伤覆写 — 阶段检测 + 无敌 + 击退抵抗
    // ============================================================

    public override void TakeDamage(float amount, string attackType = "")
    {
        if (isDead) return;
        if (!isActivated) return;

        // 阶段切换无敌期间：不扣血不反馈
        if (isPhaseTransitioning)
            return;

        // 委托基类处理：扣血 + 受伤反馈 + 硬直 + 死亡检测 + VFX
        base.TakeDamage(amount, attackType);

        HandleHitCommon(false, Vector2.zero);
    }

    public override void TakeDamageFrom(float amount, Vector2 attackSource, string attackType = "")
    {
        if (isDead) return;
        if (!isActivated) return;

        // 阶段切换无敌期间
        if (isPhaseTransitioning)
            return;

        // 委托基类处理核心逻辑：扣血 + 受伤反馈 + 硬直 + 死亡检测 + VFX
        base.TakeDamage(amount, attackType);

        // 击退（带抵抗系数）：resistance=1 时完全不吃击退
        float knockMultiplier = 1f - knockbackResistance;
        if (knockMultiplier > 0.001f)
        {
            Vector2 knockDir = ((Vector2)transform.position - attackSource).normalized;
            knockDir.y = 0f;
            if (knockDir.magnitude < 0.01f) knockDir = Vector2.right;
            rb.AddForce(knockDir * 3f * knockMultiplier, ForceMode2D.Impulse);
        }

        HandleHitCommon(true, attackSource);
    }

    /// <summary>
    /// 受击统一处理（状态机入口）：中断技能 + 血量事件 + 回追击 + 阶段检测。
    /// TakeDamage / TakeDamageFrom / OnHitBy 共用，状态切换只经 fsm.ChangeState(状态机 API)。
    /// 受击清攻击冷却 → ChaseState.OnUpdate 的 CanAttack 立即可用 → 状态机自动切 Attack 反击(不额外写攻击代码)。
    /// </summary>
    private void HandleHitCommon(bool faceSource, Vector2 sourcePosition)
    {
        skillSlots?.Interrupt();
        EventBus.Trigger(new BossHpChangedEvent(this, currentHealth, maxHealth));

        if (isDead) return;

        if (faceSource)
        {
            float dir = (sourcePosition.x > transform.position.x) ? 1f : -1f;
            moveInput = dir;
        }

        fsm.ChangeState(CreateChaseState());
    }

    // ============================================================
    // ICombatant 覆写（P4b 玩家→敌人结算统一 — CombatResolver 直接调用时保留 Boss 专属规则）
    // 注：ApplyDamage 不覆写 — 直接调用路径(base.TakeDamage→ApplyDamage)与 Resolve 路径共用基类
    //     扣血/VFX 实现，避免虚调用导致 skillInterrupt/BossHpChangedEvent 在 TakeDamageFrom 中重复触发。
    // ============================================================

    /// <summary>可受击：非死亡 + 已激活 + 非阶段切换无敌</summary>
    public override bool CanBeDamaged => !isDead && isActivated && !isPhaseTransitioning;

    /// <summary>受击状态推送：中断技能 + 血量事件 + 回追击 + 阶段检测（统一走 HandleHitCommon）。</summary>
    public override void OnHitBy(DamageInfo info)
    {
        if (!isActivated) return;
        if (isPhaseTransitioning) return;

        // Boss 不吃硬直(不进入 EnemyStunState),统一回追击;击退抵抗已在 ApplyKnockback 处理
        HandleHitCommon(true, info.sourcePosition);
    }

    /// <summary>施加击退（带抵抗系数）：resistance=1 时完全不吃击退</summary>
    public override void ApplyKnockback(Knockback knockback)
    {
        if (rb == null || knockback.force <= 0f) return;
        float knockMultiplier = 1f - knockbackResistance;
        if (knockMultiplier <= 0.001f) return;

        Vector2 knockDir = knockback.direction;
        knockDir.y = 0f;  // Boss 不受上挑
        if (knockDir.magnitude < 0.01f) knockDir = Vector2.right;
        rb.AddForce(knockDir * knockback.force * knockMultiplier, ForceMode2D.Impulse);
    }

    // ============================================================
    // 阶段系统(音乐驱动:转阶段点 = 音乐切到主体循环,introSwitchTime)
    // ============================================================

    /// <summary>
    /// 强制转阶段(音乐切主体循环时调用)。无敌帧 → 触发回调 → 结束无敌。
    /// 替代旧 HP 阈值 CheckPhaseTransition(已废弃,不再按血量转阶段)。
    /// </summary>
    public void ForcePhaseTransition(int newPhase)
    {
        if (isDead || isPhaseTransitioning) return;
        if (newPhase <= currentPhase) return;
        StartCoroutine(PhaseTransitionRoutine(newPhase));
    }

    /// <summary>阶段切换协程：无敌 → 触发回调 → 结束无敌</summary>
    protected virtual IEnumerator PhaseTransitionRoutine(int newPhase)
    {
        isPhaseTransitioning = true;
        // Debug.Log($"[{name}] 阶段切换: P{currentPhase + 1} → P{newPhase + 1}");

        // 切换到新阶段
        currentPhase = newPhase;

        // 触发事件（特效/音效层订阅）
        EventBus.Trigger(new BossPhaseChangedEvent(this, newPhase));

        // 调用子类覆写的行为切换
        OnPhaseChanged(newPhase);

        // 无敌持续时间
        yield return new WaitForSeconds(phaseTransitionDuration);

        isPhaseTransitioning = false;
    }

    /// <summary>
    /// 阶段切换回调 — 子类覆写以更新 AI / 攻击模式。
    /// newPhase: 1 = P2, 2 = P3, ...
    /// </summary>
    protected virtual void OnPhaseChanged(int newPhase) { }

    // ============================================================
    // 死亡覆写 — 延迟销毁
    // ============================================================

    protected override void Die()
    {
        if (isDead) return;
        base.Die();  // isDead + 清冻结/停顿 + moveInput=0 + 切 EnemyDeadState(死亡动画) + 超时兜底
    }

    /// <summary>
    /// 死亡动画播完 — Boss 专属结算 + 基类结算(死亡 VFX / 掉落 / EnemyDeathEvent / Destroy)。
    /// 由 Death.anim 末帧事件或基类死亡超时兜底触发,状态机统一走 EnemyDeadState。
    /// </summary>
    public override void OnDeathAnimationEnd()
    {
        if (!isDead) return;

        // Boss 专属结算(基类结算之前)
        if (bossDeathVFXPrefab != null)
            VFXSpawner.SpawnOnBoss(bossDeathVFXPrefab, transform.position);
        EventBus.Trigger(new BossDefeatedEvent(this));

        base.OnDeathAnimationEnd();
    }

    // ============================================================
    // 公开接口
    // ============================================================

    /// <summary>强制激活 Boss（供外部调用，如过场动画后触发）</summary>
    public void ForceActivate()
    {
        ActivateBoss();
    }

    /// <summary>普攻开始:启动普攻间隔(技能/重击不受此间隔约束)。攻击状态进入时调用</summary>
    public void StartMeleeInterval()
    {
        meleeIntervalTimer = meleeIntervalDuration;
    }

    /// <summary>普攻间隔是否进行中(>0 = 禁止普攻)</summary>
    public bool IsMeleeIntervalActive => meleeIntervalTimer > 0f;

    /// <summary>攻击编排组件</summary>
    public BossAttackDirector AttackDirector => attackDirector;

    /// <summary>普攻伤害(即时判定,由攻击编排普攻阶段调用)</summary>
    public void PerformDefaultMelee()
    {
        if (defaultMelee != null) defaultMelee.PerformAttack(this);
    }

    /// <summary>创建普攻状态(子类覆写:FirstBoss → BossAttackState;默认 null = 无普攻动画)</summary>
    public virtual IState CreateAttackState() => null;

    // ============================================================
    // 技能系统接口
    // ============================================================

    /// <summary>
    /// 从可用技能中选择一个执行。默认实现：均匀随机。
    /// 子类（如 FirstBoss）覆写为加权随机或顺序选择。
    /// </summary>
    protected virtual int SelectSkillIndex(int[] available)
    {
        if (available == null || available.Length == 0) return -1;
        return available[Random.Range(0, available.Length)];
    }

    /// <summary>
    /// 强制执行指定 index 的技能（阶段切换时展示技等）。
    /// 会先中断当前技能，再立即执行新技能。
    /// </summary>
    protected void ForceSkill(int index)
    {
        if (skillSlots == null) return;
        skillSlots.Interrupt();
        skillSlots.Execute(index);
    }

    /// <summary>
    /// Boss 攻击循环协程：选择可用技能 → 执行 → fallback 普攻。
    /// 供 FirstBoss.BossAttackState 调用（提升独立文件后需 public）。
    /// </summary>
    public System.Collections.IEnumerator ExecuteBossSkillCycle()
    {
        if (skillSlots == null)
        {
            // 没有技能系统：回退普攻
            if (defaultMelee != null)
                defaultMelee.PerformAttack(this);
            yield return new WaitForSeconds(1f);
            yield break;
        }

        int[] available = skillSlots.GetAvailableSkills();
        if (available.Length > 0)
        {
            int chosen = SelectSkillIndex(available);
            skillSlots.Execute(chosen);
            // 等待技能执行完成
            yield return new WaitWhile(() => skillSlots != null && skillSlots.IsExecuting);
        }
        else
        {
            // 全部冷却中/无自动技能：打一次普攻
            if (defaultMelee != null)
                defaultMelee.PerformAttack(this);
            yield return new WaitForSeconds(0.5f);
        }
    }

    // ============================================================
    // Gizmos
    // ============================================================

#if UNITY_EDITOR
    protected override void OnDrawGizmosSelected()
    {
        base.OnDrawGizmosSelected();

        // 战斗区域 Trigger 绿色框
        if (combatAreaTrigger != null)
        {
            Gizmos.color = new Color(0f, 1f, 0f, 0.3f);
            Bounds b = combatAreaTrigger.bounds;
            Gizmos.DrawWireCube(b.center, b.size);
            Gizmos.color = new Color(0f, 1f, 0f, 0.08f);
            Gizmos.DrawCube(b.center, b.size);
        }
    }
#endif
}
