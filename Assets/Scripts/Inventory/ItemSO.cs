using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 属性加成条目 — 装备携带的一条属性加成
/// 注意：与运行时 Modifier 不同，这个是纯数据，用于 SO 中序列化
/// </summary>
[Serializable]
public struct Bonus
{
    [Tooltip("目标属性 ID，对应 StatId 常量（如 maxHealth, moveSpeed）")]
    public string statId;

    [Tooltip("加成值")]
    public float value;

    [Tooltip("加成类型：Percent（百分比叠加）或 Flat（数值叠加）")]
    public ModifierType type;
}

/// <summary>
/// 装备属性数据 — 仅装备类物品有效，定义装备后提供的属性加成
/// </summary>
[Serializable]
public struct EquipmentStats
{
    [Tooltip("所属装备槽位")]
    public EquipmentSlotType slot;

    [Tooltip("属性加成列表")]
    public Bonus[] bonuses;

    [Tooltip("武器类型（仅 Weapon 槽位有效）")]
    public WeaponType weaponType;

    [Tooltip("武器技能数据引用（仅 Weapon 槽位有效，可选）")]
    public WeaponSkillData weaponSkill;
}

/// <summary>
/// 物品数据模板 ScriptableObject
/// 策划在 Inspector 中配置物品的静态数据，运行时通过 ItemInstance 引用
/// </summary>
[CreateAssetMenu(fileName = "Item_", menuName = "Game/ItemSO")]
public class ItemSO : ScriptableObject
{
    // ============================================================
    // 静态 ID 注册表（供存档系统通过 ID 查找 SO 引用）
    // ============================================================

    /// <summary>所有已注册的 ItemSO，key = item.id</summary>
    private static Dictionary<string, ItemSO> _registry;

    /// <summary>
    /// [Phase5] 注册一个 ItemSO 到全局查找表
    /// 由 InventoryManager 在初始化时调用，将配置的 itemTemplates 全部注册
    /// </summary>
    public static void Register(ItemSO item)
    {
        if (item == null || string.IsNullOrEmpty(item.id)) return;
        if (_registry == null)
            _registry = new Dictionary<string, ItemSO>();
        _registry[item.id] = item;
    }

    /// <summary>
    /// [Phase5] 通过物品 ID 查找 ItemSO 引用
    /// 存档加载时用于重建 ItemInstance
    /// </summary>
    public static ItemSO FindById(string id)
    {
        if (_registry == null || string.IsNullOrEmpty(id)) return null;
        _registry.TryGetValue(id, out ItemSO item);
        return item;
    }

    /// <summary>清空注册表（用于重新加载场景时重置）</summary>
    public static void ClearRegistry()
    {
        _registry?.Clear();
    }

    // ============================================================
    // 实例字段
    // ============================================================

    [Header("基础信息")]
    [Tooltip("物品唯一标识（如 sword_01, potion_hp_small）")]
    public string id;

    [Tooltip("物品显示名称（如 铁剑、生命药水）")]
    public string itemName;

    [Tooltip("物品图标")]
    public Sprite icon;

    [Tooltip("物品描述文本")]
    [TextArea(2, 4)]
    public string description;

    [Header("分类与稀有度")]
    [Tooltip("物品大类：消耗品/装备/材料")]
    public ItemCategory category;

    [Tooltip("装备槽位类型（仅 category==Equipment 时有效）")]
    public EquipmentSlotType slotType;

    [Tooltip("稀有度（影响边框颜色和掉落概率）")]
    public ItemRarity rarity;

    [Header("堆叠与价格")]
    [Tooltip("最大堆叠数（非装备类物品可堆叠，装备类通常为 1）")]
    [Min(1)]
    public int maxStack = 1;

    [Tooltip("购买价格（商店）")]
    [Min(0)]
    public int buyPrice;

    [Tooltip("出售价格（卖给商店）")]
    [Min(0)]
    public int sellPrice;

    [Header("装备属性（仅装备类物品填写）")]
    [Tooltip("装备属性数据 — 仅 category==Equipment 时有效")]
    public EquipmentStats? equipmentStats;

    // ============================================================
    // 装备属性（在 Inspector 直接可见，填好值后 OnValidate 自动写入 Bonus 数组）
    // 不填 = 无加成，填了自动生效
    // ============================================================

    [Header("=== 攻击属性 ===")]
    [Tooltip("攻击力加成%，10 = 10%")]
    public float attackDamage;

    [Header("=== 近战属性 ===")]
    [Tooltip("暴击率%，10 = 10%")]
    public float critRate;

    [Tooltip("暴击伤害加成%，50 = 暴击时额外+50%伤害")]
    public float critDamage;

    [Header("=== 远程属性 ===")]
    [Tooltip("每次多发射子弹数")]
    public int shotsPerClick;

    // 攻速已移除(远程攻击取消后无意义) — 需要时取消注释恢复
    // [Tooltip("攻击间隔缩短%，10 = 间隔缩短10%")]
    // public float attackInterval;

    [Header("=== 防具属性 ===")]
    [Tooltip("减伤%，10 = 10%")]
    public float defense;

    [Tooltip("生命上限固定加成")]
    public float maxHealth;

    [Header("=== 通用属性 ===")]
    [Tooltip("移速加成%，5 = 5%")]
    public float moveSpeed;

    [Tooltip("闪避率%，10 = 10%")]
    public float dodgeRate;

    [Tooltip("法力上限固定加成")]
    public float maxMana;

    [Tooltip("回蓝加成%，10 = 10%")]
    public float manaRegen;

    [Header("=== 主属性 ===")]
    public int strength;
    public int intelligence;
    public int agility;

    // ============================================================
    // Bonus 同步
    // ============================================================

    /// <summary>(statId, type) → 属性 setter 映射（Percent 类 ×100 还原为用户填的百分比值）</summary>
    private static readonly System.Collections.Generic.Dictionary<
        (string statId, ModifierType type), System.Action<ItemSO, float>> _bonusPopulators =
        new()
        {
            [(StatId.DamageMultiplier, ModifierType.Percent)] = (item, v) => item.attackDamage = v * 100f,
            [(StatId.CritRate, ModifierType.Percent)] = (item, v) => item.critRate = v * 100f,
            [(StatId.CritDamage, ModifierType.Percent)] = (item, v) => item.critDamage = v * 100f,
            [(StatId.ShotsPerClick, ModifierType.Flat)] = (item, v) => item.shotsPerClick = (int)v,
            // 攻速已移除
            // [(StatId.AttackInterval, ModifierType.Percent)] = (item, v) => item.attackInterval = v * 100f,
            [(StatId.DamageReduction, ModifierType.Percent)] = (item, v) => item.defense = v * 100f,
            [(StatId.MaxHealth, ModifierType.Flat)] = (item, v) => item.maxHealth = v,
            [(StatId.MoveSpeed, ModifierType.Percent)] = (item, v) => item.moveSpeed = v * 100f,
            [(StatId.DodgeChance, ModifierType.Percent)] = (item, v) => item.dodgeRate = v * 100f,
            [(StatId.MaxMana, ModifierType.Flat)] = (item, v) => item.maxMana = v,
            [(StatId.ManaRegen, ModifierType.Percent)] = (item, v) => item.manaRegen = v * 100f,
            [(StatId.Str, ModifierType.Flat)] = (item, v) => item.strength = (int)v,
            [(StatId.Int, ModifierType.Flat)] = (item, v) => item.intelligence = (int)v,
            [(StatId.Agi, ModifierType.Flat)] = (item, v) => item.agility = (int)v,
        };

    /// <summary>从装备属性字段重建 equipmentStats.Bonuses 数组</summary>
    public void RebuildEquipmentBonuses()
    {
        if (category != ItemCategory.Equipment) return;

        var list = new System.Collections.Generic.List<Bonus>();
        EquipmentStats es = equipmentStats ?? new EquipmentStats();
        es.slot = slotType;

        // Percent 类：用户填 10 = 10%，写入时 /100 → 0.10
        // Flat 类：用户填 20 = +20，直接写入
        void Add(string statId, float val, ModifierType type, bool percent)
        {
            if (val != 0)
                list.Add(new Bonus { statId = statId, value = percent ? val / 100f : val, type = type });
        }

        Add(StatId.DamageMultiplier, attackDamage, ModifierType.Percent, percent: true);
        Add(StatId.CritRate, critRate, ModifierType.Percent, percent: true);
        Add(StatId.CritDamage, critDamage, ModifierType.Percent, percent: true);
        Add(StatId.ShotsPerClick, shotsPerClick, ModifierType.Flat, percent: false);
        // 攻速已移除
        // Add(StatId.AttackInterval, attackInterval, ModifierType.Percent, percent: true);
        Add(StatId.DamageReduction, defense, ModifierType.Percent, percent: true);
        Add(StatId.MaxHealth, maxHealth, ModifierType.Flat, percent: false);
        Add(StatId.MoveSpeed, moveSpeed, ModifierType.Percent, percent: true);
        Add(StatId.DodgeChance, dodgeRate, ModifierType.Percent, percent: true);
        Add(StatId.MaxMana, maxMana, ModifierType.Flat, percent: false);
        Add(StatId.ManaRegen, manaRegen, ModifierType.Percent, percent: true);
        Add(StatId.Str, strength, ModifierType.Flat, percent: false);
        Add(StatId.Int, intelligence, ModifierType.Flat, percent: false);
        Add(StatId.Agi, agility, ModifierType.Flat, percent: false);

        es.bonuses = list.ToArray();
        equipmentStats = es;
    }

    /// <summary>
    /// 从旧 bonuses 反填充属性字段
    /// 仅当所有属性字段为 0 时执行，避免覆盖手动填的值
    /// </summary>
    public void PopulateFromBonuses()
    {
        if (equipmentStats?.bonuses == null) return;

        if (attackDamage != 0 || critRate != 0 || critDamage != 0
            || shotsPerClick != 0 || defense != 0
            || maxHealth != 0 || moveSpeed != 0 || dodgeRate != 0
            || maxMana != 0 || manaRegen != 0
            || strength != 0 || intelligence != 0 || agility != 0)
            return;

        foreach (var b in equipmentStats.Value.bonuses)
        {
            var key = (b.statId, b.type);
            if (_bonusPopulators.TryGetValue(key, out var setter))
                setter(this, b.value);
        }
    }

#if UNITY_EDITOR
    private void OnValidate()
    {
        RebuildEquipmentBonuses();
        UnityEditor.EditorUtility.SetDirty(this);
    }
#endif

    // 注：consumableEffect 字段将在后续 Phase 中根据需要扩展
    // 参考整合方案中的 ItemEffectDataSO 设计
}
