using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// 人物属性面板 — 挂 UI Canvas 上的 StatPanel GameObject
/// 实现 IPanel 接入 PanelManager（快捷键 C，FullScreen）
/// 事件驱动刷新：订阅属性/修饰器/血量/法力变化事件，不再持有 Player 引用
/// </summary>
public class PlayerStatPanel : MonoBehaviour, IPanel
{
    // ============================================================
    // IPanel 实现
    // ============================================================

    public PanelType PanelType => PanelType.FullScreen;
    public bool PauseGame => true;
    public bool LockInput => true;
    public bool ShowCursor => true;

    // ============================================================
    // Inspector 配置
    // ============================================================

    [Header("战斗属性")]
    [SerializeField] private TMP_Text valueAttack;
    [SerializeField] private TMP_Text valueCritRate;
    [SerializeField] private TMP_Text valueCritDmg;
    [SerializeField] private TMP_Text valueShots;
    [SerializeField] private TMP_Text valueInterval;
    [SerializeField] private TMP_Text valueDefense;
    [SerializeField] private TMP_Text valueDodge;

    [Header("生命法力")]
    [SerializeField] private TMP_Text valueHp;
    [SerializeField] private TMP_Text valueMp;
    [SerializeField] private TMP_Text valueHpRegen;
    [SerializeField] private TMP_Text valueMpRegen;

    [Header("其他派生")]
    [SerializeField] private TMP_Text valueMoveSpeed;
    [SerializeField] private TMP_Text valueArmor;
    [SerializeField] private TMP_Text valueCdReduce;

    [Header("主属性")]
    [SerializeField] private TMP_Text valueStr;
    [SerializeField] private TMP_Text valueInt;
    [SerializeField] private TMP_Text valueAgi;

    // ============================================================
    // 生命周期
    // ============================================================

    private void OnEnable()
    {
        EventBus.Subscribe<PlayerAttrChangedEvent>(OnAnyStatChanged);
        EventBus.Subscribe<StatModifiersChangedEvent>(OnAnyStatChanged);
        EventBus.Subscribe<PlayerHealthChangedEvent>(OnAnyStatChanged);
        EventBus.Subscribe<PlayerManaChangedEvent>(OnAnyStatChanged);
        RefreshUI();
    }

    private void OnDisable()
    {
        EventBus.Unsubscribe<PlayerAttrChangedEvent>(OnAnyStatChanged);
        EventBus.Unsubscribe<StatModifiersChangedEvent>(OnAnyStatChanged);
        EventBus.Unsubscribe<PlayerHealthChangedEvent>(OnAnyStatChanged);
        EventBus.Unsubscribe<PlayerManaChangedEvent>(OnAnyStatChanged);
    }

    // 任何相关事件触发 → 刷新面板（统一入口）
    private void OnAnyStatChanged<T>(T e) => RefreshUI();

    // ============================================================
    // 刷新方法
    // ============================================================

    /// <summary>
    /// 从注册表读取所有组件当前值，写入 19 个 Text.text。
    /// 百分比保留 0 位小数，浮点数保留 1 位小数。
    /// </summary>
    private void RefreshUI()
    {
        StatModifierManager statMod = StatModifierManager.Instance;
        PlayerAttributeSystem attrSystem = PlayerAttributeSystem.Instance;
        PlayerHealth health = PlayerHealth.Instance;
        SkillManager skill = SkillManager.Instance;
        PlayerCombat combat = PlayerController.Instance != null ? PlayerController.Instance.Combat : null;

        if (statMod == null) return;

        // ── 战斗属性 ──

        if (valueAttack != null)
        {
            float attack = statMod.GetFinalValue(1f, StatId.DamageMultiplier);
            valueAttack.text = $"攻击力 {attack:F2}";
        }

        if (valueCritRate != null)
        {
            float critRate = statMod.GetFinalValue(0f, StatId.CritRate) * 100f;
            valueCritRate.text = $"暴击率 {critRate:F0}%";
        }

        if (valueCritDmg != null)
        {
            float critDmg = statMod.GetFinalValue(0f, StatId.CritDamage) * 100f;
            valueCritDmg.text = $"暴击伤害 +{critDmg:F0}%";
        }

        if (valueShots != null)
        {
            int baseShots = combat != null ? combat.BaseShotsPerClick : 1;
            int extraShots = Mathf.RoundToInt(statMod.GetFinalValue(0f, StatId.ShotsPerClick));
            valueShots.text = $"发射数 {baseShots + extraShots}";
        }

        if (valueInterval != null)
        {
            float baseCD = combat != null ? combat.BaseAttackCooldown : 0.3f;
            float intervalMult = statMod.GetFinalValue(1f, StatId.AttackInterval);
            float interval = baseCD / Mathf.Max(0.1f, intervalMult);
            valueInterval.text = $"攻击间隔 {interval:F2}s";
        }

        if (valueDefense != null)
        {
            float defense = statMod.GetFinalValue(0f, StatId.DamageReduction) * 100f;
            valueDefense.text = $"减伤 {defense:F0}%";
        }

        if (valueDodge != null)
        {
            float dodge = statMod.GetFinalValue(0f, StatId.DodgeChance) * 100f;
            valueDodge.text = $"闪避 {dodge:F0}%";
        }

        // ── 生命法力 ──

        if (valueHp != null && health != null)
            valueHp.text = $"生命 {health.CurrentHealth:F0} / {health.MaxHealth:F0}";

        if (valueMp != null && skill != null)
            valueMp.text = $"法力 {skill.CurrentMana:F0} / {skill.MaxMana:F0}";

        if (valueHpRegen != null)
        {
            float hpRegen = statMod.GetFinalValue(0f, StatId.HpRegen);
            valueHpRegen.text = $"生命恢复 {hpRegen:F1}/s";
        }

        if (valueMpRegen != null)
        {
            float baseManaRegen = GetBaseManaRegen(skill);
            float mpRegen = statMod.GetFinalValue(baseManaRegen, StatId.ManaRegen);
            valueMpRegen.text = $"回蓝 {mpRegen:F1}/s";
        }

        // ── 其他派生 ──

        if (valueMoveSpeed != null)
        {
            float baseMS = GetBaseMoveSpeed();
            float moveSpeed = statMod.GetFinalValue(baseMS, StatId.MoveSpeed);
            valueMoveSpeed.text = $"移速 {moveSpeed:F1}";
        }

        if (valueArmor != null)
        {
            float armor = statMod.GetFinalValue(0f, StatId.Armor);
            valueArmor.text = $"护甲 {armor:F0}";
        }

        if (valueCdReduce != null)
        {
            float cdMult = statMod.GetFinalValue(1f, StatId.CooldownMultiplier);
            float cdReduce = (1f - cdMult) * 100f;
            valueCdReduce.text = $"冷却缩减 {cdReduce:F0}%";
        }

        // ── 主属性 ──

        if (valueStr != null && attrSystem != null)
            valueStr.text = $"力量 {attrSystem.GetStrength()}";

        if (valueInt != null && attrSystem != null)
            valueInt.text = $"智力 {attrSystem.GetIntelligence()}";

        if (valueAgi != null && attrSystem != null)
            valueAgi.text = $"敏捷 {attrSystem.GetAgility()}";
    }

    // ============================================================
    // 反射辅助（访问其他组件的 private 字段）
    // ============================================================

    /// <summary>从 SkillManager 反射读取 manaRegenPerSec 基值</summary>
    private static float GetBaseManaRegen(SkillManager skill)
    {
        if (skill == null) return 5f;
        var field = typeof(SkillManager).GetField("manaRegenPerSec",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        return field != null ? (float)field.GetValue(skill) : 5f;
    }

    /// <summary>从 CharacterBase 反射读取 baseMoveSpeed 基值</summary>
    private float GetBaseMoveSpeed()
    {
        var controller = PlayerController.Instance;
        if (controller == null) return 6f;
        var field = typeof(CharacterBase).GetField("baseMoveSpeed",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        if (field == null) return 6f;
        // 反射从继承链上的任意 MonoBehaviour 读取均可（字段在 CharacterBase 定义）
        return (float)field.GetValue(controller);
    }
}
