using UnityEngine;
using System.Collections;

/// <summary>
/// 玩家生命/受伤模块（子组件）— 由 PlayerController 自动查找并调用
/// 挂到 Player 对象上即可激活生命系统，不挂则无
/// 遵循组件模式：主组件控制流程，子组件实现具体功能
/// P1 改造：maxHealth → baseMaxHealth + 修饰器管线，TakeDamage 插入闪避/减伤
/// </summary>
public class PlayerHealth : MonoBehaviour
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
    private StatModifierManager statModManager;
    private CharacterBase _charBase;

    private Animator Anim => _charBase != null ? _charBase.Animator : null;
    private PlayerAttributeSystem attrSystem;

    /// <summary>受伤时触发（供 PlayerController 订阅，用于战斗态锁定）</summary>
    public System.Action OnDamaged;

    // ============================================================
    // 受击状态（C# 字段为状态源，Animator 只做单向输出）
    // ============================================================

    /// <summary>地面受击硬直中（Hurt 动画播放期间）</summary>
    public bool IsHurt { get; private set; }

    /// <summary>空中受击（AirHurt 动画，落地后清除）</summary>
    public bool IsAirHurt { get; private set; }

    /// <summary>清除空中受击状态（落地时由 PlayerController 调用）</summary>
    public void ClearAirHurt()
    {
        IsAirHurt = false;
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

    /// <summary>玩家复活（由 DeathPanel 按钮调用），清除死亡标记并回满血</summary>
    public void Revive()
    {
        _isDead = false;
        if (Anim != null)
            Anim.SetBool(AnimParams.IsDead, false);
        currentHealth = MaxHealth;
        GetComponent<EquipmentManager>()?.ResetDeathFlag();
        EventBus.Trigger(new PlayerHealthChangedEvent(currentHealth, MaxHealth));
    }

    // ============================================================
    // 生命周期
    // ============================================================

    private void Awake()
    {
        statModManager = GetComponent<StatModifierManager>();
        attrSystem = GetComponent<PlayerAttributeSystem>();
        _charBase = GetComponent<CharacterBase>();
        // 从 PlayerAttrConfigSO 统一切换初始生命值
        if (attrSystem != null && attrSystem.AttrConfig != null)
            baseMaxHealth = attrSystem.AttrConfig.initialHealth;
        currentHealth = MaxHealth;
        _lastMaxHealth = MaxHealth;
        hitFeedback = GetComponent<PlayerHitFeedback>();
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

    /// <summary>受到伤害（被敌人攻击组件调用）— P1改造：含闪避判定 + 减伤计算；P2改造：护甲减免</summary>
    public void TakeDamage(float amount)
    {
        if (_isDead) return;

        // ── 闪避判定 ──
        if (RollDodge())
        {
            // Debug.Log("[Player] 闪避成功");
            return;
        }

        OnDamaged?.Invoke();

        // ── P2 护甲减免：先减去护甲固定值，再走百分比减伤（保底1点伤害）──
        amount = ApplyArmorReduction(amount);

        // ── 减伤计算 ──
        amount = ApplyDamageReduction(amount);

        currentHealth -= amount;
        // Debug.Log($"[Player] 受伤，HP: {currentHealth}/{MaxHealth}");

        if (currentHealth <= 0f)
        {
            _isDead = true;

            // 死亡动画
            if (Anim != null)
            {
                Anim.SetBool(AnimParams.IsDead, true);
                Anim.SetTrigger(AnimParams.Death);
            }

            // [Phase3] 死亡时所有装备生成掉落物
            GetComponent<EquipmentManager>()?.DropAllOnDeath();

            // PlayerDeathEvent 由死亡动画末帧 AnimationEvent → OnDeathAnimationEnd() 触发
            return;
        }

        // 受击动画 — 空中/地面分流
        if (Anim != null)
        {
            bool inAir = owner != null && !owner.IsGrounded();
            if (inAir)
            {
                IsAirHurt = true;
                Anim.SetBool(AnimParams.IsAirHurt, true);
            }
            else
            {
                IsHurt = true;
                Anim.SetBool(AnimParams.IsHurt, true);
                StopCoroutine(nameof(ResetHurtRoutine));
                StartCoroutine(nameof(ResetHurtRoutine));
            }
        }

        // 受击反馈（闪红 + 震屏）
        hitFeedback?.OnTakeDamage();

        // 受击 VFX（由 VFXSpawner 统一管理，挂到 PlayerVFX 容器下）
        VFXSpawner.SpawnOnPlayer(hitVFXPrefab, (Vector2)transform.position + hitVFXOffset);

        // 命中停顿
        HitStopController.Instance?.Trigger(0.04f);

        // 生命值变化后通知 UI
        EventBus.Trigger(new PlayerHealthChangedEvent(currentHealth, MaxHealth));
    }

    /// <summary>受到伤害并击退（传入攻击方向）</summary>
    public void TakeDamageWithKnockback(float amount, Vector2 attackDir)
    {
        TakeDamage(amount);

        // 击退：水平方向受力，忽略 Y 轴
        Vector2 knockDir = attackDir.normalized;
        knockDir.y = 0f;
        if (knockDir.magnitude < 0.01f) knockDir = Vector2.right; // 默认向右

        Rigidbody2D rb = owner.GetRigidbody();
        rb.AddForce(knockDir * 10f, ForceMode2D.Impulse);

        // 击退期间 SetVelocity 自动跳过
        StartCoroutine(KnockbackRoutine());
    }

    private IEnumerator KnockbackRoutine()
    {
        owner.SetKnockedBack(true);
        float duration = 0.2f * (1f - GetControlReduction());
        yield return new WaitForSeconds(duration);
        owner.SetKnockedBack(false);
    }

    private IEnumerator ResetHurtRoutine()
    {
        yield return new WaitForSeconds(hurtDuration);
        IsHurt = false;
        if (Anim != null)
            Anim.SetBool(AnimParams.IsHurt, false);
    }

    /// <summary>获取控制减免值 [0~1]</summary>
    private float GetControlReduction()
    {
        if (statModManager == null) return 0f;
        float reduction = statModManager.GetFinalValue(0f, StatId.ControlReduction);
        return Mathf.Clamp01(reduction);
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

    /// <summary>死亡动画末帧回调 — 由 Animator AnimationEvent 触发，弹出 DeathPanel</summary>
    public void OnDeathAnimationEnd()
    {
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
