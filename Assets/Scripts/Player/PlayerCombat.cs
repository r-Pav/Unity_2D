using UnityEngine;
using System.Collections;

/// <summary>
/// 玩家战斗模块（子组件）— 由 PlayerController 自动查找
/// P2 改造:攻击/格挡/空中攻击/下坠攻击的输入入口已迁移至 FSM 状态类
/// (PlayerAttackState / PlayerAirAttackState / PlayerBlockState / PlayerGroundPoundState),
/// 本组件保留:伤害判定核心(OnMeleeHitFrame)、暴击/闪避/减伤查询、
/// 格挡减伤修饰器 + 弹反 Buff + 弹反闪烁视觉、OnAttack 战斗态锁定事件、
/// OnAttackAnimationStart/End 等动画事件薄转发(正式事件迁移在 P5)
/// </summary>
public class PlayerCombat : MonoBehaviour
{
    // ============================================================
    // 配置参数 —— 伤害 & 层级
    // ============================================================

    [Header("伤害")]
    [Tooltip("每次攻击基础伤害（实际伤害 = 基础值 × 伤害倍率）")]
    [SerializeField] private float attackDamage = 1f;

    [Tooltip("敌人的 Layer")]
    [SerializeField] private LayerMask enemyLayer = ~0;

    // ============================================================
    // 配置参数 —— 近战
    // ============================================================

    [Header("近战")]
    [Tooltip("近战攻击类型标签 — 传给 Enemy TakeDamage 用于匹配 VFX 变体")]
    [SerializeField] private string meleeAttackType = "Sword";

    [Tooltip("近战第三段攻击类型标签 — 单独标记用于霸体计数")]
    [SerializeField] private string meleeFinisherAttackType = "Sword_Heavy";

    [Header("近战范围指示器")]
    [Tooltip("拖入 Player 下的攻击范围 Sprite（挂 MeleeRangeIndicator）")]
    [SerializeField] private MeleeRangeIndicator rangeIndicator;
    [Tooltip("攻击范围中心在 Player 前方的距离")]
    [SerializeField] private float meleeRangeOffset = 1.5f;

    [Tooltip("剑的 BoxCollider2D（挂在武器模板上,本体保持 disabled,clone 投掷时自动启用）。命中帧用 clone 的 bounds 做检测,不开物理碰撞")]
    [SerializeField] private BoxCollider2D swordCollider;

    [Header("近战 VFX")]
    [Tooltip("近战挥砍命中特效 Prefab — 在 OverlapBox 检测到敌人时生成")]
    [SerializeField] private GameObject slashVFXPrefab;

    [Header("近战")]
    [Tooltip("近战伤害")]
    [SerializeField] private float meleeDamage = 1f;

    [Tooltip("近战攻击冷却（秒）— 需短于 Attack1 动画时长，保证连击排队窗口存在")]
    [SerializeField] private float meleeAttackCooldown = 0.15f;

    [Tooltip("近战击退力度（直接施加到敌人 Rigidbody2D）")]
    [SerializeField] private float meleeKnockbackForce = 4f;

    [Tooltip("近战击退上挑力度（Y 轴最小值，保证敌人浮空）")]
    [SerializeField] private float meleeKnockbackUpForce = 0.3f;

    [Tooltip("剑碰撞(clone)第三段击退力度 — 独立于方框,可单独调大")]
    [SerializeField] private float swordKnockbackForce = 8f;

    [Tooltip("近战命中卡肉时长（秒）")]
    [SerializeField] private float meleeHitStopDuration = 0.08f;

    [Header("格挡/弹反")]
    [Tooltip("弹反判定最大时长(秒) — 短按松手 ≤ 此值判定为弹反")]
    [SerializeField] private float parryMaxWindow = 0.2f;
    [Tooltip("格挡减伤率 [0~1]")]
    [SerializeField] private float blockDamageReduction = 0.5f;
    [Tooltip("子弹 Layer（用于近战消除子弹的 OverlapBox 检测）")]
    [SerializeField] private LayerMask projectileLayer = 0;

    [Header("格挡/弹反 — 视觉（测试用，后续替换为 Animator）")]
    [Tooltip("玩家渲染器（测试用换色）")]
    [SerializeField] private SpriteRenderer playerRenderer;
    [Tooltip("格挡时颜色")]
    [SerializeField] private Color blockColor = new Color(0.3f, 0.5f, 1f, 1f);
    [Tooltip("弹反成功闪烁颜色")]
    [SerializeField] private Color parrySuccessColor = Color.yellow;
    [Tooltip("弹反闪烁时长（秒）")]
    [SerializeField] private float parryFlashDuration = 0.2f;
    [Tooltip("[预留] 弹反成功 Animator Trigger 名")]
    [SerializeField] private string parrySuccessAnimTrigger = "";
    [Tooltip("[预留] 格挡开始 Animator Trigger 名")]
    [SerializeField] private string blockStartAnimTrigger = "";
    [Tooltip("[预留] 格挡结束 Animator Trigger 名")]
    [SerializeField] private string blockEndAnimTrigger = "";

    [Header("连击")]
    [Tooltip("连击重置时间（秒）— 超过此时间未连击则 comboIndex 重置为 1")]
    [SerializeField] private float comboResetTimer = 0.6f;

    [Tooltip("动画结束后此窗口内(秒)点击直接接下一段,不落 idle。窗口内无点击则正常退出回 idle")]
    [SerializeField] private float comboExitWindow = 0.12f;

    // ============================================================
    // 运行时状态
    // ============================================================

    private float attackCooldownTimer;
    private PlayerController _owner;
    private StatModifierManager statModManager;
    private WeaponThrow _weaponThrow;   // 武器投掷(挂在 Player 子物体武器上)

    /// <summary>攻击时触发（供 PlayerController 订阅，用于战斗态锁定）</summary>
    public System.Action OnAttack;

    /// <summary>公开近战基础冷却（供 PlayerStatPanel 读取）</summary>
    public float BaseMeleeAttackCooldown => meleeAttackCooldown;

    // ── 格挡/弹反 ──
    /// <summary>是否正在格挡（按住右键）</summary>
    private bool isBlocking;

    /// <summary>是否持有弹反 Buff（OnMeleeHitFrame 重击分支读取）</summary>
    private bool hasParryBuff;
    /// <summary>玩家原始颜色（格挡/弹反恢复用）</summary>
    private Color _playerOriginalColor;
    private Coroutine _parryFlashRoutine;

    /// <summary>格挡减伤修饰器 source 标识</summary>
    private const string BlockModSource = "Blocking";

    // ============================================================
    // 生命周期
    // ============================================================

    private void Awake()
    {
        _owner = GetComponent<PlayerController>();
        statModManager = GetComponent<StatModifierManager>();
        // 武器投掷(挂在 Player 子物体武器上),攻击结束事件顺带触发其重生判定
        _weaponThrow = GetComponentInChildren<WeaponThrow>();

        if (playerRenderer != null)
            _playerOriginalColor = playerRenderer.color;

        // 近战范围指示器始终激活
        if (rangeIndicator != null)
            rangeIndicator.gameObject.SetActive(true);
    }

    private void OnEnable()
    {
        EventBus.Subscribe<PlayerDeathEvent>(OnPlayerDeath);
    }

    private void OnDisable()
    {
        EventBus.Unsubscribe<PlayerDeathEvent>(OnPlayerDeath);
        // 组件禁用时清理格挡状态
        CancelBlock();
    }

    // ============================================================
    // 攻击冷却 — 由 PlayerController.UpdateCooldowns 驱动递减,状态类进入攻击前查询
    // ============================================================

    /// <summary>攻击冷却是否就绪（Idle/Move/Jump/Fall 状态进入 Attack/AirAttack 前判断）</summary>
    public bool AttackCooldownReady => attackCooldownTimer <= 0f;

    /// <summary>消耗攻击冷却：记录一次攻击起始（AttackState/AirAttackState.OnEnter 调用）</summary>
    public void ConsumeAttackCooldown()
    {
        attackCooldownTimer = GetEffectiveAttackCooldown(meleeAttackCooldown);
    }

    /// <summary>每帧递减攻击冷却（原 OnPlayerUpdate → TickTimers 迁出,由 PlayerController.UpdateCooldowns 调用）</summary>
    public void TickTimers()
    {
        if (attackCooldownTimer > 0f)
            attackCooldownTimer -= Time.deltaTime;
    }

    // ============================================================
    // 连击配置 — 供 PlayerController 创建 PlayerAttackState 时注入
    // ============================================================

    /// <summary>连击重置时间（秒）</summary>
    public float ComboResetTimer => comboResetTimer;
    /// <summary>动画结束后预输入缓冲窗口（秒）</summary>
    public float ComboExitWindow => comboExitWindow;
    /// <summary>弹反判定最大时长（秒）</summary>
    public float ParryMaxWindow => parryMaxWindow;

    /// <summary>近战攻击标签（供 ICombatant.CurrentAttackLabel 查询）</summary>
    public string MeleeAttackType => meleeAttackType;

    /// <summary>终结重击攻击标签（供 ICombatant.CurrentAttackLabel 查询）</summary>
    public string MeleeFinisherAttackType => meleeFinisherAttackType;

    // ============================================================
    // 动画事件薄转发 — 正式事件迁移在 P5,P2 先转发给 FSM 当前状态类
    // ============================================================

    /// <summary>AttackN.anim 首帧触发 → 转发 AttackState.OnAnimStart（进入攻击表现:朝向）</summary>
    public void OnAttackAnimationStart()
    {
        if (_owner != null && _owner.PlayerFsm?.CurrentState is PlayerAttackState atk)
            atk.OnAnimStart();
    }

    /// <summary>AttackN.anim 末帧触发 → 转发 AttackState.OnAnimEnd（排队直切/预输入缓冲）</summary>
    public void OnAttackAnimationEnd()
    {
        if (_owner != null && _owner.PlayerFsm?.CurrentState is PlayerAttackState atk)
            atk.OnAnimEnd();
    }

    /// <summary>AirAttack.anim 命中帧 → 转发 AirAttackState.OnAirAttackHitFrame（伤害+武器投掷）</summary>
    public void OnAirAttackHitFrame()
    {
        if (_owner != null && _owner.PlayerFsm?.CurrentState is PlayerAirAttackState air)
            air.OnAirAttackHitFrame();
    }

    /// <summary>AirAttack.anim 结束 → 转发 AirAttackState.OnAirAttackEnd（退回下落）</summary>
    public void OnAirAttackEnd()
    {
        if (_owner != null && _owner.PlayerFsm?.CurrentState is PlayerAirAttackState air)
            air.OnAirAttackEnd();
    }

    /// <summary>AnimationEvent 入口（P2 动画事件仍经 Relay 调用）：从 FSM 当前状态读取连击参数后走伤害核心</summary>
    public void OnMeleeHitFrame()
    {
        // 兜底:非攻击状态收到命中帧(切换竞态/延迟事件),按第 1 段处理
        int idx = 1;
        if (_owner != null && _owner.PlayerFsm?.CurrentState is PlayerAttackState atk)
            idx = atk.ComboIndex;
        OnMeleeHitFrame(idx, 3, false);
    }

    /// <summary>攻击朝向：优先当前输入,否则取玩家朝向（供状态类/伤害核心读取）</summary>
    public int AttackDir
    {
        get
        {
            float h = Input.GetAxisRaw("Horizontal");
            if (h > 0.1f) return 1;
            if (h < -0.1f) return -1;
            return _owner != null ? _owner.GetFacing() : 1;
        }
    }

    // ============================================================
    // 近战伤害判定核心（保留,由 AttackState.OnHitFrame / 动画事件调用）
    // ============================================================

    /// <summary>挥砍命中帧伤害判定 — OverlapBox 检测/卡肉/击退/闪白（原 OnMeleeHitFrame 核心,参数化连击上下文）</summary>
    public void OnMeleeHitFrame(int comboIndex, int comboLimit, bool isAirAttack)
    {

        float damage = GetEffectiveDamage() * meleeDamage;

        if (hasParryBuff)
        {
            ExecuteHeavyMeleeAttack(damage);
            return;
        }

        LayerMask damageMask = enemyLayer;
        if (projectileLayer != 0)
            damageMask = enemyLayer | projectileLayer;

        // 方框范围 + 剑碰撞范围(攻击范围延伸):合并检测,统一走伤害
        Collider2D[] boxHits = MeleeHitDetector.Detect(rangeIndicator, damageMask);
        Collider2D[] swordHits = GetSwordColliderHits(damageMask);
        Collider2D[] hits = MergeHits(boxHits, swordHits);

        // 剑命中的 collider 集合(用于区分击退力度:剑第三段用 swordKnockbackForce)
        var swordHitSet = new System.Collections.Generic.HashSet<Collider2D>();
        foreach (var c in swordHits)
            if (c != null) swordHitSet.Add(c);

        bool hitAnything = false;

        foreach (var col in hits)
        {
            var enemy = col.GetComponent<EnemyControllerBase>();
            if (enemy != null)
            {
                float dmg = RollCrit(damage);
                bool isFinisher = comboIndex >= comboLimit;

                // 基础击退:第三段才有,沿"远离 player"方向(剑命中用更大力度)
                float baseForce = isFinisher ? (swordHitSet.Contains(col) ? swordKnockbackForce : meleeKnockbackForce) : 0f;

                // 武器击退:每击独立配 (x, y) 向量,x 按朝向镜像,方向由武器决定(不依赖 player 位置)
                Vector2 weaponForce = Vector2.zero;
                if (_weaponThrow != null)
                    weaponForce = _weaponThrow.GetKnockbackBonus(comboIndex, isAirAttack);
                if (weaponForce.x != 0f) weaponForce.x *= AttackDir;

                // 实际击退 = 基础方向 × 基础力度 + 武器向量(向量相加)
                Vector2 knockDir = ((Vector2)(enemy.transform.position - transform.position)).normalized;
                if (knockDir.magnitude < 0.01f) knockDir = Vector2.right * AttackDir;
                knockDir.y = Mathf.Max(knockDir.y, meleeKnockbackUpForce);
                Vector2 totalForce = knockDir * baseForce + weaponForce;

                string atkType = isFinisher ? meleeFinisherAttackType : meleeAttackType;

                // P4b:统一走 CombatResolver 结算(不再直接 enemy.TakeDamageFrom + enemyRb.AddForce)。
                // 击退向量(含 facing 镜像/上挑)构造进 Knockback,由敌人侧 ApplyKnockback 施加;
                // source = 玩家 ICombatant 实现(P4c PlayerHealth 接入后 GetComponent 自动取到;当前为 null 不影响敌人侧结算)
                ICombatant source = GetComponent<ICombatant>();
                DamageInfo info = new DamageInfo
                {
                    amount = dmg,
                    source = source,
                    sourcePosition = (Vector2)transform.position,
                    attackLabel = atkType,
                    knockback = new Knockback
                    {
                        direction = totalForce.sqrMagnitude > 0.0001f ? totalForce.normalized : Vector2.right * AttackDir,
                        force = totalForce.sqrMagnitude > 0.0001f ? totalForce.magnitude : 0f,
                        duration = 0f,
                        ignoreResistance = false
                    }
                };
                CombatResolver.Resolve(source, enemy, info);
                hitAnything = true;

                VFXSpawner.SpawnOnPlayer(slashVFXPrefab, col.transform.position);
            }
            else
            {
                var proj = col.GetComponent<Projectile>();
                if (proj != null && proj.CanBeDestroyedByMelee)
                {
                    proj.ReturnToPool();
                    hitAnything = true;
                }
            }
        }

        if (hitAnything)
            HitStopController.Instance?.Trigger(meleeHitStopDuration);

        rangeIndicator.Flash();
    }

    /// <summary>剑碰撞检测:用当前在飞 clone 的 BoxCollider2D 的 bounds 做 OverlapBox。
    /// 本体(默认剑)的 collider 保持 disabled 不参与;只有 clone 投掷时被 WeaponThrow 启用。
    /// swordCollider 字段只是"是否启用该功能"的标记 + 模板引用。</summary>
    private Collider2D[] GetSwordColliderHits(LayerMask mask)
    {
        if (swordCollider == null) return new Collider2D[0];  // 没拖字段 = 功能关闭
        if (_weaponThrow == null || _weaponThrow.ActiveCloneCollider == null) return new Collider2D[0];

        Bounds b = _weaponThrow.ActiveCloneCollider.bounds;
        return Physics2D.OverlapBoxAll(b.center, b.size, 0f, mask);
    }

    /// <summary>合并两组命中,按 collider 实例去重(剑范围与方框重叠时只算一次)</summary>
    private Collider2D[] MergeHits(Collider2D[] a, Collider2D[] b)
    {
        if (a.Length == 0) return b;
        if (b.Length == 0) return a;

        var merged = new System.Collections.Generic.List<Collider2D>(a.Length + b.Length);
        var seen = new System.Collections.Generic.HashSet<Collider2D>();
        foreach (var c in a)
        {
            if (c != null && seen.Add(c)) merged.Add(c);
        }
        foreach (var c in b)
        {
            if (c != null && seen.Add(c)) merged.Add(c);
        }
        return merged.ToArray();
    }

    // ============================================================

    /// <summary>
    /// 弹反重击 — 高击退力 + 强制硬直，消耗弹反 Buff。
    /// 由 OnMeleeHitFrame 在检测到 hasParryBuff 时调用。
    /// P6:统一走 CombatResolver 结算(原 enemy.TakeDamage + enemyRb.AddForce(8f) + EnterStunState 三处直接调用删除)。
    /// 数值保持:伤害=RollCrit(baseDamage)、击退 8f(构造进 Knockback.force,原 3f→8f)、强制硬直(重击标签 → OnHitBy 近战路径 → EnterStunState)。
    /// </summary>
    private void ExecuteHeavyMeleeAttack(float baseDamage)
    {
        hasParryBuff = false;
        EventBus.Trigger(new ParryBuffConsumedEvent());

        Collider2D[] hits = MeleeHitDetector.Detect(rangeIndicator, enemyLayer);

        bool hitAnything = false;

        foreach (var col in hits)
        {
            var enemy = col.GetComponent<EnemyControllerBase>();
            if (enemy != null)
            {
                float dmg = RollCrit(baseDamage);
                hitAnything = true;

                // 强化击退方向：水平远离玩家（原逻辑，Y 轴归零）
                Vector2 knockDir = ((Vector2)(enemy.transform.position - transform.position)).normalized;
                knockDir.y = 0f;
                if (knockDir.magnitude < 0.01f) knockDir = Vector2.right;

                // 与其他玩家→敌人路径一致：attackLabel 用重击标签(meleeFinisherAttackType)，
                // Poise.IsMeleeAttack("Sword_Heavy")=true → OnHitBy 近战路径 → EnterStunState 强制硬直；
                // 敌人 hitVFXVariants 均为空数组 → GetHitVFX 一律回退默认,标签切换不影响受击 VFX。
                ICombatant source = GetComponent<ICombatant>();
                DamageInfo info = new DamageInfo
                {
                    amount = dmg,
                    source = source,
                    sourcePosition = (Vector2)transform.position,
                    attackLabel = meleeFinisherAttackType,
                    knockback = new Knockback
                    {
                        direction = knockDir,
                        force = 8f,          // 原强化击退 8f(原 3f),构造进 Knockback.force
                        duration = 0f,
                        ignoreResistance = false
                    }
                };
                CombatResolver.Resolve(source, enemy, info);
            }
        }

        if (hitAnything)
            HitStopController.Instance?.Trigger(meleeHitStopDuration);

        rangeIndicator.Flash();
    }

    // ============================================================
    // 属性修饰器查询
    // ============================================================

    /// <summary>受攻击冷却(攻速已移除,直接返回基础冷却)</summary>
    private float GetEffectiveAttackCooldown(float baseCD)
    {
        return baseCD;
        // 攻速已移除 — 原逻辑:冷却 = baseCD / intervalMult
        // if (statModManager == null) return baseCD;
        // float mult = statModManager.GetFinalValue(1f, StatId.AttackInterval);
        // return baseCD / Mathf.Max(0.1f, mult);
    }

    /// <summary>暴击判定：受 CritRate/CritDamage 修饰</summary>
    private float RollCrit(float baseDamage)
    {
        if (statModManager == null) return baseDamage;
        float critChance = statModManager.GetFinalValue(0f, StatId.CritRate);
        if (critChance <= 0f || Random.value >= critChance)
            return baseDamage;
        float critDmg = statModManager.GetFinalValue(0f, StatId.CritDamage);
        return baseDamage * (1f + critDmg);
    }

    /// <summary>获取当前有效伤害（基础值 × 伤害倍率）</summary>
    private float GetEffectiveDamage()
    {
        if (statModManager == null) return attackDamage;
        float multiplier = statModManager.GetFinalValue(1f, StatId.DamageMultiplier);
        float dmg = attackDamage * multiplier;
        if (dmg < 0f) dmg = 0f;
        return dmg;
    }

    /// <summary>闪避判定：取 dodgeChance 做随机判定，成功返回 true</summary>
    public bool RollDodge()
    {
        if (statModManager == null) return false;
        float dodgeChance = statModManager.GetFinalValue(0f, StatId.DodgeChance);
        if (dodgeChance <= 0f) return false;
        return Random.value < dodgeChance;
    }

    /// <summary>减伤计算：actualDamage = incomingDamage × (1 - damageReduction)</summary>
    public float ApplyDamageReduction(float incomingDamage)
    {
        if (statModManager == null) return incomingDamage;
        float reduction = statModManager.GetFinalValue(0f, StatId.DamageReduction);
        reduction = Mathf.Clamp01(reduction);
        return incomingDamage * (1f - reduction);
    }

    // ============================================================
    // 格挡/弹反 — 减伤修饰器 + 弹反检测 + 视觉(由 PlayerBlockState 驱动时机)
    // ============================================================

    /// <summary>开始格挡：注入减伤修饰器 + 换色（PlayerBlockState.OnEnter 调用）</summary>
    public void StartBlocking()
    {
        isBlocking = true;

        if (statModManager != null)
        {
            statModManager.AddModifier(new Modifier(
                targetStat: StatId.DamageReduction,
                value: blockDamageReduction,
                type: ModifierType.Percent,
                source: BlockModSource,
                priority: 500
            ));
        }

        // [测试] 格挡颜色 / [预留] Animator.SetTrigger(blockStartAnimTrigger)
        if (playerRenderer != null)
            playerRenderer.color = blockColor;
    }

    /// <summary>取消格挡：移除修饰器，重置状态，恢复颜色（PlayerBlockState.OnExit / Dash 打断 / 死亡调用）</summary>
    public void CancelBlock()
    {
        if (!isBlocking) return;
        isBlocking = false;
        RemoveBlockModifier();
        RestorePlayerColor();
    }

    /// <summary>移除格挡减伤修饰器</summary>
    private void RemoveBlockModifier()
    {
        if (statModManager != null)
            statModManager.RemoveModifier(BlockModSource);
    }

    /// <summary>
    /// 弹反判定：OverlapBox 检测范围内是否有正在攻击帧的敌人。
    /// 成功 → OnParrySuccess()；失败 → 无惩罚。PlayerBlockState 松手(短按)时调用。
    /// </summary>
    public void AttemptParry()
    {
        if (rangeIndicator == null)
        {
            RestorePlayerColor();
            return;
        }

        Collider2D[] hits = MeleeHitDetector.Detect(rangeIndicator, enemyLayer);

        foreach (var col in hits)
        {
            var enemy = col.GetComponent<EnemyControllerBase>();
            if (enemy != null && enemy.IsInAttackFrame)
            {
                OnParrySuccess();
                return;
            }
        }

        // 弹反失败：恢复颜色
        RestorePlayerColor();
    }

    /// <summary>弹反成功：设置 Buff，闪烁，触发事件（P4c:PlayerHealth.TryParry 弹反成功时也调用，免疫伤害+授予弹反重击）</summary>
    public void OnParrySuccess()
    {
        hasParryBuff = true;

        if (playerRenderer != null)
        {
            if (_parryFlashRoutine != null)
                StopCoroutine(_parryFlashRoutine);
            _parryFlashRoutine = StartCoroutine(ParryFlashRoutine());
        }

        EventBus.Trigger(new ParrySuccessEvent());
    }

    private IEnumerator ParryFlashRoutine()
    {
        playerRenderer.color = parrySuccessColor;
        yield return new WaitForSeconds(parryFlashDuration);
        // 弹反后若仍在格挡中则保持格挡色，否则恢复原色
        playerRenderer.color = isBlocking ? blockColor : _playerOriginalColor;
        _parryFlashRoutine = null;
    }

    /// <summary>恢复玩家原始颜色</summary>
    private void RestorePlayerColor()
    {
        if (playerRenderer != null)
            playerRenderer.color = _playerOriginalColor;
    }

    /// <summary>玩家死亡时取消格挡</summary>
    private void OnPlayerDeath(PlayerDeathEvent _)
    {
        CancelBlock();
    }

#if UNITY_EDITOR
    private void OnDrawGizmosSelected()
    {
        if (rangeIndicator == null) return;
        var pc = GetComponent<PlayerController>();
        if (pc == null) return;
        Vector2 center = (Vector2)transform.position + Vector2.right * pc.GetFacing() * meleeRangeOffset;
        Vector2 size = rangeIndicator.Size;
        Gizmos.color = new Color(1f, 0f, 0f, 0.4f);
        Gizmos.DrawCube(center, size);
        Gizmos.color = Color.red;
        Gizmos.DrawWireCube(center, size);
    }
#endif
}
