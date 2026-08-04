using UnityEngine;

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
public abstract class EnemyControllerBase : CharacterBase
{
    // ============================================================
    // 配置参数
    // ============================================================

    [Header("属性")]
    [SerializeField] protected float maxHealth = 3f;

    [Header("受伤反馈")]
    [SerializeField] protected Color hitColor = Color.white;  // 白色闪白更明显
    [SerializeField] protected float hitFlashDuration = 0.1f;

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
    [Tooltip("检测矩形半宽（X 轴）")]
    [SerializeField] protected float detectionWidth = 8f;
    [Tooltip("检测矩形半高（Y 轴）")]
    [SerializeField] protected float detectionHeight = 3f;

    [Header("攻击范围 — 矩形")]
    [Tooltip("攻击矩形半宽（X 轴）")]
    [SerializeField] protected float attackWidth = 1.5f;
    [Tooltip("攻击矩形半高（Y 轴）")]
    [SerializeField] protected float attackHeight = 1.5f;

    [Header("攻击冷却")]
    [SerializeField] protected float attackCooldownDuration = 1f;
    public float AttackCooldownDuration => attackCooldownDuration;

    [Header("击退")]
    [Tooltip("远程攻击击退力度（近战击退由 EnemyPoise 控制）")]
    [SerializeField] protected float rangedKnockbackForce = 5f;

    /// <summary>暴露攻击矩形半宽给攻击组件读取</summary>
    public float AttackWidth => attackWidth;
    /// <summary>暴露攻击矩形半高给攻击组件读取</summary>
    public float AttackHeight => attackHeight;
    /// <summary>暴露检测矩形半宽给攻击组件读取</summary>
    public float DetectionWidth => detectionWidth;
    /// <summary>暴露检测矩形半高给攻击组件读取</summary>
    public float DetectionHeight => detectionHeight;

    /// <summary>暴露缓存的 player Transform，供攻击组件使用（避免 FindObjectOfType）</summary>
    public Transform PlayerTarget => player;

    // ============================================================
    // 运行时状态
    // ============================================================

    protected float currentHealth;
    protected bool isDead;

    /// <summary>公开死亡状态（供外部组件读取）</summary>
    public bool IsDead => isDead;

    /// <summary>当前血量（供 HealthBar 等读取）</summary>
    public float CurrentHealth => currentHealth;
    /// <summary>最大血量</summary>
    public float MaxHealth => maxHealth;

    private Renderer[] renderers;
    private Color stateColor;         // 当前状态色，hit 恢复时用此值
    private float hitFlashTimer;

    // ── FSM ──
    protected StateMachine fsm;
    public StateMachine Fsm => fsm;
    protected Transform player;

    /// <summary>缓存的 PassiveEquipManager 引用（通过 FindObjectOfType 获取）</summary>
    protected PassiveEquipManager passiveEquipManager;
    /// <summary>缓存的 EnemyPoise 引用（霸体/击退组件）</summary>
    private EnemyPoise _poise;
    /// <summary>当前敌人是否处于战斗状态（Chase/Attack），防止重复触发 SetCombatState</summary>
    private bool isInCombatState;

    /// <summary>FSM 状态设置的移动输入（1 / -1 / 0），OnFixedUpdate 应用</summary>
    public float moveInput;

    /// <summary>攻击冷却计时器，攻击后一段时间内不进攻击（防止循环）</summary>
    public float attackCooldownTimer;

    /// <summary>是否处于攻击判定帧内 (供弹反系统查询)。当前由 PerformAttack 临时置位，后续由 AnimationEvent 驱动。</summary>
    public bool IsInAttackFrame { get; set; }

    protected EnemyStunState stunState;
    private float stunCooldownTimer;

    // ============================================================
    // 抽象方法 — 子类必须实现
    // ============================================================

    /// <summary>返回初始 FSM 状态（子类返回各自的 IdleState）</summary>
    protected abstract IState GetInitialState();

    /// <summary>创建追击状态（子类返回各自的 ChaseState 实现）</summary>
    public abstract IState CreateChaseState();

    /// <summary>创建晕眩结束的后备状态（近战→Patrol，远程→Idle）</summary>
    public abstract IState CreateFallbackState();

    // ============================================================
    // 生命周期
    // ============================================================

    protected override void Awake()
    {
        base.Awake();
        currentHealth = maxHealth;

        renderers = GetComponentsInChildren<Renderer>();
        Color firstColor = renderers.Length > 0 ? renderers[0].material.color : Color.white;
        stateColor = firstColor;

        fsm = new StateMachine();
        player = PlayerController.Instance?.transform;
        passiveEquipManager = PassiveEquipManager.Instance;
        _poise = GetComponent<EnemyPoise>();
    }

    protected virtual void OnEnable()
    {
        EventBus.Subscribe<GroundPoundEvent>(OnGroundPound);
    }

    protected virtual void OnDisable()
    {
        EventBus.Unsubscribe<GroundPoundEvent>(OnGroundPound);
        OnExitCombatState();  // 场景卸载/对象池回收时确保退出战斗计数
    }

    protected void Start()
    {
        fsm.ChangeState(GetInitialState());
    }

    protected override void Update()
    {
        base.Update();

        if (hitFlashTimer > 0f)
        {
            hitFlashTimer -= Time.deltaTime;
            if (hitFlashTimer <= 0f)
                RestoreColors();
        }

        if (attackCooldownTimer > 0f)
            attackCooldownTimer -= Time.deltaTime;

        if (stunCooldownTimer > 0f)
            stunCooldownTimer -= Time.deltaTime;
    }

    /// <summary>敌人动画参数更新。后续有动画 Clip 时可扩展 IsAttacking/IsDead。</summary>
    protected override void UpdateAnimation()
    {
        if (_animator == null) return;
        _animator.SetFloat(AnimParams.Speed, Mathf.Abs(rb.velocity.x));
    }

    protected override void OnUpdate()
    {
        if (isDead) return;
        fsm?.Update();
    }

    protected override void OnFixedUpdate()
    {
        // FSM 状态已经设好 moveInput，这里统一执行物理移动
        if (Mathf.Abs(moveInput) > 0.01f)
        {
            Move(moveInput);
            UpdateFacing(moveInput);
        }
        else if (fsm?.CurrentState != stunState)
        {
            // 硬直中不零速，让击退自然衰减
            Move(0f);
        }
    }

    // ============================================================
    // 事件订阅
    // ============================================================

    private void OnGroundPound(GroundPoundEvent e)
    {
        if (isDead) return;

        int selfLayer = 1 << gameObject.layer;
        if ((e.targetLayers & selfLayer) == 0) return;

        Vector2 toCenter = (Vector2)transform.position - e.center;
        toCenter.y = 0f;
        float dist = toCenter.magnitude;
        if (dist > e.radius) return;

        TakeDamage(e.damage);  // GroundPound 不传 attackType，使用默认 VFX

        if (dist > 0.01f)
        {
            Vector2 knockDir = toCenter.normalized;
            knockDir.y = 0f;
            if (rb != null)
                rb.velocity = knockDir * e.knockbackForce;
        }
    }

    // ============================================================
    // 受伤 / 死亡
    // ============================================================

    /// <summary>
    /// 造成伤害。attackType 可选，匹配到 hitVFXVariants 中的条目时使用对应 VFX，否则用默认 hitVFXPrefab。
    /// </summary>
    public virtual void TakeDamage(float amount, string attackType = "")
    {
        if (isDead) return;

        EnterStunState();
        if (ApplyDamage(amount, attackType)) Die();
    }

    public virtual void TakeDamageFrom(float amount, Vector2 attackSource, string attackType = "")
    {
        if (isDead) return;

        // 受击 VFX — 朝攻击来源方向偏移，更真实
        Vector2 hitOffset = ((Vector2)transform.position - attackSource).normalized * -0.15f;
        Vector2 vfxPos = (Vector2)transform.position + hitOffset;
        Vector2 hitDir = ((Vector2)transform.position - attackSource).normalized;

        if (ApplyDamage(amount, attackType, vfxPos, hitDir)) { Die(); return; }

        // ── 按攻击类型分流：近战走霸体控制 + stun 打断，远程保持原有逻辑 ──
        bool isMelee = _poise != null && _poise.IsMeleeAttack(attackType);

        if (isMelee)
        {
            // ── 近战路径：stun 硬直 + 霸体控制轻击退 ──

            // 1. FSM 打断：始终进入 stun 硬直（不受霸体影响）
            //    注意：不立即 fsm.ChangeState(CreateChaseState())，让 stun 真正执行 0.5s
            //          EnemyStunState.OnUpdate 会在 timer 归零后自动转 Chase/Fallback
            EnterStunState();

            // 2. 击退：RegisterMeleeHit 负责霸体累计 + 返回本次是否击退
            if (_poise.RegisterMeleeHit(attackType, out float kbForce) && kbForce > 0f)
            {
                Vector2 knockDir = hitDir;
                knockDir.y = 0f;
                if (knockDir.magnitude < 0.01f) knockDir = Vector2.right;
                rb.AddForce(knockDir * kbForce, ForceMode2D.Impulse);
            }
            // 注意：近战路径不再设置 moveInput 和 ChangeState(Chase)
            //       stun state 的 OnUpdate 会在 0.5s 后自动切换到 Chase/Fallback
        }
        else
        {
            // ── 远程路径：保持原有逻辑不变（3f 击退 + 立即追击）──
            Vector2 knockDir = hitDir;
            knockDir.y = 0f;
            if (knockDir.magnitude < 0.01f) knockDir = Vector2.right;
            rb.AddForce(knockDir * rangedKnockbackForce, ForceMode2D.Impulse);

            // 朝攻击源方向追击（Move 内部会乘以 moveSpeed，这里只设方向 ±1）
            float dir = (attackSource.x > transform.position.x) ? 1f : -1f;
            moveInput = dir;
            fsm.ChangeState(CreateChaseState());
        }
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

    protected virtual void Die()
    {
        isDead = true;
        OnExitCombatState();  // 死亡时退出战斗计数

        // 死亡 VFX
        if (deathVFXPrefab != null)
            VFXSpawner.SpawnOnEnemy(deathVFXPrefab, transform.position);

        // [Phase3] 死亡时装备生成掉落物（在 EnemyDeathEvent 和 Destroy 之前）
        GetComponent<EnemyEquipment>()?.DropOnDeath();

        EventBus.Trigger(new EnemyDeathEvent(this, (Vector2)transform.position));
        Destroy(gameObject);
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
        hitFlashTimer = hitFlashDuration;
        foreach (Renderer r in renderers) r.material.color = hitColor;
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

    public bool CanSeePlayer()
    {
        if (player == null) return false;
        if (!IsInDetectionRect()) return false;
        return HasLineOfSight();
    }

    private bool IsInDetectionRect()
    {
        float deltaX = player.position.x - transform.position.x;
        float deltaY = player.position.y - transform.position.y;
        return Mathf.Abs(deltaX) <= detectionWidth * 0.5f
            && Mathf.Abs(deltaY) <= detectionHeight * 0.5f;
    }

    private bool HasLineOfSight()
    {
        float dist = Vector2.Distance(transform.position, player.position);
        Vector2 dir = ((Vector2)(player.position - transform.position)).normalized;
        Vector2 origin = (Vector2)transform.position + Vector2.up * 0.5f;

        RaycastHit2D[] hits = Physics2D.RaycastAll(origin, dir, dist);
        foreach (RaycastHit2D hit in hits)
        {
            if (hit.transform == transform || hit.transform.IsChildOf(transform))
                continue;
            if (hit.transform.TryGetComponent(out PlayerController _))
                return true;
            return false;
        }
        return true;
    }

    public bool PlayerInAttackRange()
    {
        if (player == null) return false;
        float deltaX = player.position.x - transform.position.x;
        float deltaY = player.position.y - transform.position.y;
        return Mathf.Abs(deltaX) <= attackWidth * 0.5f && Mathf.Abs(deltaY) <= attackHeight * 0.5f;
    }

    public float DirectionToPlayer()
    {
        if (player == null) return 0f;
        return player.position.x > transform.position.x ? 1f : -1f;
    }

    /// <summary>是否可以对玩家发起攻击（综合所有条件）。子类可覆盖以添加额外条件（如远程后退区）。</summary>
    public virtual bool CanAttack()
    {
        if (player == null) return false;
        if (!CanSeePlayer()) return false;
        if (attackCooldownTimer > 0f) return false;

        // 玩家空中击飞时不攻击，避免无限连击
        var pc = player.GetComponent<PlayerController>();
        if (pc != null)
        {
            var ph = pc.GetComponent<PlayerHealth>();
            if (ph != null && ph.IsAirHurt) return false;
        }

        float deltaX = player.position.x - transform.position.x;
        float deltaY = player.position.y - transform.position.y;
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
