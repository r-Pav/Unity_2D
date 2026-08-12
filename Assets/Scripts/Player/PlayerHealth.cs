using UnityEngine;
using System.Collections;

/// <summary>
/// 玩家生命/受伤模块（子组件）— 由 PlayerController 自动查找并调用
/// 挂到 Player 对象上即可激活生命系统，不挂则无
/// 遵循组件模式：主组件控制流程，子组件实现具体功能
/// P1 改造：maxHealth → baseMaxHealth + 修饰器管线，TakeDamage 插入闪避/减伤
/// P4c 改造：实现 ICombatant，敌人/Boss→玩家伤害统一走 CombatResolver.Resolve
/// </summary>
public class PlayerHealth : MonoBehaviour, ICombatant
{
    // ============================================================
    // Singleton 注册表（Player 子组件；调用方统一走 Instance）
    // ============================================================

    private static PlayerHealth _instance;

    public static PlayerHealth Instance
    {
        get
        {
            if (_instance == null)
                _instance = FindObjectOfType<PlayerHealth>();
            return _instance;
        }
    }

    // ============================================================
    // 配置参数
    // ============================================================

    [Header("生命")]
    [Tooltip("已移至 PlayerAttrConfigSO.initialHealth 统一配置；此处仅作运行时显示")]
    [SerializeField] private float baseMaxHealth = 5f;

    [Tooltip("受击硬直时长（秒），到期自动退出 Hit 动画")]
    [SerializeField] private float hurtDuration = 0.3f;

    [Header("VFX")]
    [Tooltip("受击特效 Prefab — 在 TakeDamage 命中反馈后自动生成")]
    [SerializeField] private GameObject hitVFXPrefab;
    [Tooltip("受击特效偏移（相对角色 pivot）")]
    [SerializeField] private Vector2 hitVFXOffset = Vector2.zero;

    // ============================================================
    // 运行时状态
    // ============================================================

    private float currentHealth;
    private bool _isDead;
    public bool IsDead => _isDead;
    private float _lastMaxHealth;  // 缓存上一次 maxHealth，用于等比缩放
    private PlayerController owner;
    private PlayerHitFeedback hitFeedback;
    private PlayerCombat combat;   // P4c:TryParry 弹反成功/CurrentAttackLabel 查询
    private StatModifierManager statModManager;
    private CharacterBase _charBase;

    private Animator Anim => _charBase != null ? _charBase.Animator : null;
    private PlayerAttributeSystem attrSystem;

    /// <summary>受伤时触发（供 PlayerController 订阅，用于战斗态锁定）</summary>
    public System.Action OnDamaged;

    // ============================================================
    // 受击状态（P3a:状态迁至 FSM — 属性查询 PlayerFsm 当前状态,动画由状态类 animBoolNames 驱动）
    // ============================================================

    /// <summary>地面受击硬直中（FSM 当前状态为 PlayerHurtState）</summary>
    public bool IsHurt =>
        owner != null && owner.PlayerFsm != null && owner.PlayerFsm.CurrentState is PlayerHurtState;

    /// <summary>空中受击（FSM 当前状态为 PlayerAirHurtState，落地/超时由状态类管理）</summary>
    public bool IsAirHurt =>
        owner != null && owner.PlayerFsm != null && owner.PlayerFsm.CurrentState is PlayerAirHurtState;

    [Tooltip("空中受击最大时长(秒)— 超时强制恢复控制,防止被敌人顶着不落地导致永久锁死")]
    [SerializeField] private float airHurtTimeout = 1.5f;

    /// <summary>受击硬直时长(秒)，注入 PlayerHurtState 做超时退出</summary>
    public float HurtDuration => hurtDuration;

    /// <summary>空中受击超时兜底时长(秒)，注入 PlayerAirHurtState 做超时退出</summary>
    public float AirHurtTimeout => airHurtTimeout;

    /// <summary>清除空中受击状态（由 PlayerAirHurtState 落地/超时退出时调用：清 Anim Bool，状态本身由 FSM 切换清除）</summary>
    public void ClearAirHurt()
    {
        if (Anim != null)
            Anim.SetBool(AnimParams.IsAirHurt, false);
    }

    // ============================================================
    // 公开属性
    // ============================================================

    public float CurrentHealth => currentHealth;
    /// <summary>基础最大生命值（未经过修饰器管线，SO 配置值）</summary>
    public float BaseMaxHealth => baseMaxHealth;
    /// <summary>当前最大生命值（如有 StatModifierManager 则走修饰器管线）</summary>
    public float MaxHealth => statModManager != null
        ? statModManager.GetFinalValue(baseMaxHealth, StatId.MaxHealth)
        : baseMaxHealth;

    // ============================================================
    // 复活
    // ============================================================

    /// <summary>玩家复活（由 DeathPanel 按钮调用），清除死亡标记、切回 FSM Idle 并回满血</summary>
    public void Revive()
    {
        _isDead = false;
        if (Anim != null)
            Anim.SetBool(AnimParams.IsDead, false);
        currentHealth = MaxHealth;
        GetComponent<EquipmentManager>()?.ResetDeathFlag();
        EventBus.Trigger(new PlayerHealthChangedEvent(currentHealth, MaxHealth));

        // P3a:死亡状态迁入 FSM — 复活时切回 Idle(PlayerDeadState.OnExit 会清 IsDead 动画参数)
        if (owner != null && owner.PlayerFsm != null)
            owner.PlayerFsm.ChangeState(owner.IdleState);
    }

    // ============================================================
    // 生命周期
    // ============================================================

    private void Awake()
    {
        statModManager = GetComponent<StatModifierManager>();
        attrSystem = GetComponent<PlayerAttributeSystem>();
        _charBase = GetComponent<CharacterBase>();
        // P3a:提前缓存 owner(IsHurt/IsAirHurt 查询 + FSM 状态切换依赖;OnPlayerUpdate 每帧也会刷新)
        owner = GetComponent<PlayerController>();
        // 从 PlayerAttrConfigSO 统一切换初始生命值
        if (attrSystem != null && attrSystem.AttrConfig != null)
            baseMaxHealth = attrSystem.AttrConfig.initialHealth;
        currentHealth = MaxHealth;
        _lastMaxHealth = MaxHealth;
        hitFeedback = GetComponent<PlayerHitFeedback>();
        combat = GetComponent<PlayerCombat>();
    }

    private void OnEnable()
    {
        EventBus.Subscribe<StatModifiersChangedEvent>(OnStatModifiersChanged);
    }

    private void OnDisable()
    {
        EventBus.Unsubscribe<StatModifiersChangedEvent>(OnStatModifiersChanged);
    }

    // ============================================================
    // 父类调用接口
    // ============================================================

    /// <summary>每帧被 PlayerController 调用（当前无需每帧逻辑，保留接口）</summary>
    public void OnPlayerUpdate(PlayerController pc)
    {
        owner = pc;
    }

    // ============================================================
    // 受伤 / 死亡
    // ============================================================

    /// <summary>受到伤害（被敌人攻击组件调用）— P4c：内部走 CombatResolver.Resolve 统一结算（闪避→弹反→护甲→减伤→扣血→状态推送）</summary>
    public void TakeDamage(float amount)
    {
        if (_isDead) return;

        CombatResolver.Resolve(null, this, new DamageInfo
        {
            amount = amount,
            source = null,
            sourcePosition = (Vector2)transform.position,
            attackLabel = "",
            knockback = Knockback.None
        });
    }

    /// <summary>受到伤害并击退（传入攻击方向）— P4c：内部走 CombatResolver.Resolve（击退力度/时长按原硬编码 10f/0.2s 构造进 Knockback）</summary>
    public void TakeDamageWithKnockback(float amount, Vector2 attackDir)
    {
        if (_isDead) return;

        // 击退：水平方向受力，忽略 Y 轴（原逻辑，构造进 Knockback.direction）
        Vector2 knockDir = attackDir.normalized;
        knockDir.y = 0f;
        if (knockDir.magnitude < 0.01f) knockDir = Vector2.right; // 默认向右

        CombatResolver.Resolve(null, this, new DamageInfo
        {
            amount = amount,
            source = null,
            sourcePosition = (Vector2)transform.position,
            attackLabel = "",
            knockback = new Knockback
            {
                direction = knockDir,
                force = 10f,     // 原 TakeDamageWithKnockback 硬编码击退力度
                duration = 0.2f, // 原 KnockbackRoutine 硬编码硬直时长
                ignoreResistance = false
            }
        });
    }

    private IEnumerator KnockbackRoutine(float duration)
    {
        owner.SetKnockedBack(true);
        float wait = duration * (1f - GetControlReduction());
        yield return new WaitForSeconds(wait);
        owner.SetKnockedBack(false);
    }

    /// <summary>获取控制减免值 [0~1]</summary>
    private float GetControlReduction()
    {
        if (statModManager == null) return 0f;
        float reduction = statModManager.GetFinalValue(0f, StatId.ControlReduction);
        return Mathf.Clamp01(reduction);
    }

    // ============================================================
    // ICombatant 接口实现（P4c 敌人/Boss→玩家结算统一）
    // 玩家侧照 EnemyControllerBase 同款样式：TryDodge/TryParry/ApplyArmor/ApplyReduction
    // 由 CombatResolver.Resolve 在 ApplyDamage 前按 5.2 顺序执行，行为与改造前一致
    // ============================================================

    // ── 身份 ──
    public GameObject GameObject => gameObject;
    public Transform Transform => transform;

    // ── 攻击方 ──
    /// <summary>当前攻击标签（Sword/Sword_Heavy）— 玩家攻击标签由 PlayerCombat 构造 DamageInfo 传入(P4b)，此处按 FSM 攻击状态返回</summary>
    public string CurrentAttackLabel
    {
        get
        {
            if (owner == null || owner.PlayerFsm == null || !(owner.PlayerFsm.CurrentState is PlayerAttackState atk))
                return null;
            if (combat == null) return null;
            return atk.ComboIndex >= 3 ? combat.MeleeFinisherAttackType : combat.MeleeAttackType;
        }
    }

    // ── 受击方 ──
    /// <summary>韧性组件 — 玩家不挂 PoiseComponent（P4 方案），返回 null → CombatResolver 走 fallback 直接击退</summary>
    public PoiseComponent Poise => null;

    /// <summary>是否处于可被攻击的状态</summary>
    public bool CanBeDamaged => !_isDead;

    /// <summary>是否处于攻击判定帧（弹反查询用；玩家无敌人弹反，按攻击状态近似返回）</summary>
    public bool IsInAttackFrame =>
        owner != null && owner.PlayerFsm != null
        && (owner.PlayerFsm.CurrentState is PlayerAttackState
            || owner.PlayerFsm.CurrentState is PlayerAirAttackState);

    /// <summary>
    /// 承受伤害（含击退信息），返回实际造成伤害量。
    /// 复用原 TakeDamage 核心段（扣血→死亡→VFX→事件）；闪避/弹反/护甲/减伤/击退已由
    /// CombatResolver.Resolve 在本方法前完成，此处不重复计算。
    /// </summary>
    public float ApplyDamage(DamageInfo info)
    {
        if (_isDead) return 0f;

        OnDamaged?.Invoke();

        currentHealth -= info.amount;
        // Debug.Log($"[Player] 受伤，HP: {currentHealth}/{MaxHealth}");

        if (currentHealth <= 0f)
        {
            _isDead = true;

            // [2026-08-10] 装备掉落移到死亡动画播完（OnDeathAnimationEnd）— 死亡瞬间不掉，能看到死亡动画 + 掉落过程
            // 原 DropAllOnDeath() 调用已迁移到 OnDeathAnimationEnd()

            // P3a:死亡状态迁入 FSM — PlayerDeadState.OnEnter 触发死亡动画(SetBool IsDead + SetTrigger Death)
            if (owner != null && owner.PlayerFsm != null)
                owner.PlayerFsm.ChangeState(owner.DeadState);

            // PlayerDeathEvent 由死亡动画末帧 AnimationEvent → OnDeathAnimationEnd() 触发
            return info.amount;
        }

        // 受击反馈（闪红 + 震屏）
        hitFeedback?.OnTakeDamage();

        // 受击 VFX（由 VFXSpawner 统一管理，挂到 PlayerVFX 容器下）
        VFXSpawner.SpawnOnPlayer(hitVFXPrefab, (Vector2)transform.position + hitVFXOffset);

        // 命中停顿
        HitStopController.Instance?.Trigger(0.04f);

        // 生命值变化后通知 UI
        EventBus.Trigger(new PlayerHealthChangedEvent(currentHealth, MaxHealth));

        return info.amount;
    }

    // ── 结算管线钩子（P4c 玩家侧接入，行为与改造前一致）──

    /// <summary>闪避判定 — 复用现有 RollDodge</summary>
    public bool TryDodge(DamageInfo info) => RollDodge();

    /// <summary>
    /// 格挡/弹反判定 — 命中时玩家处于 BlockState 弹反窗口（按下时长 ≤ parryMaxWindow）且存在攻击者 → 弹反成功：
    /// 免疫伤害 + 授予弹反重击 Buff（复用 PlayerCombat 弹反成功逻辑）。
    /// 环境/投射物（无攻击者）不弹反，与改造前一致。
    /// </summary>
    public bool TryParry(ICombatant attacker, DamageInfo info)
    {
        if (attacker == null) return false;
        if (owner == null || owner.PlayerFsm == null) return false;
        if (!(owner.PlayerFsm.CurrentState is PlayerBlockState block)) return false;
        if (!block.IsInParryWindow) return false;

        combat?.OnParrySuccess();
        return true;
    }

    /// <summary>护甲减免 — 复用现有 ApplyArmorReduction</summary>
    public float ApplyArmor(float amount) => ApplyArmorReduction(amount);

    /// <summary>减伤 — 复用现有 ApplyDamageReduction</summary>
    public float ApplyReduction(float amount) => ApplyDamageReduction(amount);

    /// <summary>施加击退（CombatResolver 按 fallback 判定通过后调用；力度/时长由攻击方构造进 Knockback）</summary>
    public void ApplyKnockback(Knockback knockback)
    {
        if (knockback.force <= 0f) return;

        // 击退：水平方向受力，忽略 Y 轴（原 TakeDamageWithKnockback 逻辑）
        Vector2 knockDir = knockback.direction.normalized;
        knockDir.y = 0f;
        if (knockDir.magnitude < 0.01f) knockDir = Vector2.right; // 默认向右

        Rigidbody2D rb = owner.GetRigidbody();
        rb.AddForce(knockDir * knockback.force, ForceMode2D.Impulse);

        // 击退期间 SetVelocity 自动跳过（硬直时长由 Knockback.duration 注入，行为与原 0.2s 一致）
        StartCoroutine(KnockbackRoutine(knockback.duration));
    }

    /// <summary>受击状态推送 — 空中→AirHurtState / 地面→HurtState（原 TakeDamage 分流逻辑）</summary>
    public void OnHitBy(DamageInfo info)
    {
        if (_isDead) return;

        // P3a:受击状态迁入 FSM — 空中→AirHurtState / 地面→HurtState
        // (动画由状态类 animBoolNames 驱动,超时退出由状态类管理;原 IsHurt/IsAirHurt 字段 + 协程已删除)
        bool inAir = owner != null && !owner.IsGrounded();
        if (owner != null && owner.PlayerFsm != null)
            owner.PlayerFsm.ChangeState(inAir ? owner.AirHurtState : owner.HurtState);
    }

    // ============================================================
    // P1 — 修饰器变化响应（maxHealth 变更时 clamp + 通知 HUD）
    // ============================================================

    /// <summary>修饰器增删时：若 maxHealth 受影响则等比缩放 currentHealth 并刷新 HUD</summary>
    private void OnStatModifiersChanged(StatModifiersChangedEvent e)
    {
        foreach (var statId in e.affectedStatIds)
        {
            if (statId == StatId.MaxHealth)
            {
                float newMax = MaxHealth;
                // 等比缩放：保持当前血量百分比不变
                float ratio = _lastMaxHealth > 0f ? currentHealth / _lastMaxHealth : 1f;
                currentHealth = Mathf.Clamp(ratio * newMax, 0f, newMax);
                _lastMaxHealth = newMax;
                EventBus.Trigger(new PlayerHealthChangedEvent(currentHealth, newMax));
                break;
            }
        }
    }

    // ============================================================
    // P1 — 闪避 / 减伤判定
    // ============================================================

    /// <summary>闪避判定：取 dodgeChance 做随机判定，成功返回 true</summary>
    private bool RollDodge()
    {
        if (statModManager == null) return false;
        float dodgeChance = statModManager.GetFinalValue(0f, StatId.DodgeChance);
        if (dodgeChance <= 0f) return false;
        return Random.value < dodgeChance;
    }

    /// <summary>减伤计算：actualDamage = incomingDamage × (1 - damageReduction)</summary>
    private float ApplyDamageReduction(float incomingDamage)
    {
        if (statModManager == null) return incomingDamage;
        float reduction = statModManager.GetFinalValue(0f, StatId.DamageReduction);
        reduction = Mathf.Clamp01(reduction);
        return incomingDamage * (1f - reduction);
    }

    /// <summary>[P2] 护甲减免：伤害 - 护甲值，保底 1 点伤害</summary>
    private float ApplyArmorReduction(float incomingDamage)
    {
        if (statModManager == null) return incomingDamage;
        float armor = statModManager.GetFinalValue(0f, StatId.Armor);
        if (armor <= 0f) return incomingDamage;
        return Mathf.Max(1f, incomingDamage - armor);
    }

    // ============================================================
    // AnimationEvent 回调
    // ============================================================

    /// <summary>死亡动画末帧回调 — 由 Animator AnimationEvent 触发，先掉装备再弹 DeathPanel</summary>
    public void OnDeathAnimationEnd()
    {
        // [2026-08-10] 掉落时机：死亡动画播完才掉（原 ApplyDamage 死亡瞬间掉，看不到过程）
        GetComponent<EquipmentManager>()?.DropAllOnDeath();

        EventBus.Trigger(new PlayerDeathEvent());
    }

#if UNITY_EDITOR
    private void OnDrawGizmosSelected()
    {
        if (hitVFXPrefab == null) return;
        Vector3 pos = (Vector3)((Vector2)transform.position + hitVFXOffset);
        Gizmos.color = new Color(1f, 0.3f, 0.3f, 0.4f);
        Gizmos.DrawCube(pos, Vector3.one * 0.3f);
        Gizmos.color = new Color(1f, 0.3f, 0.3f, 0.8f);
        Gizmos.DrawWireCube(pos, Vector3.one * 0.35f);
    }
#endif
}
