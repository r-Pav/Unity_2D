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
/// 物品数据模板基类（2026-09-26 拆分）。
///
/// 只保留所有物品通用字段 + 存档用 ID 注册表；具体类型走子类：
///   EquipmentItemSO（装备：槽位 + 装备属性）
///   ConsumableItemSO（消耗品：回血等效果）
///   MaterialItemSO（材料：无额外字段）
///
/// 读取一律走虚属性（Category / SlotType / EquipmentStats / HealAmount），
/// **不要在调用点做类型转换**；非装备类型读 SlotType 得到默认值 Weapon，
/// 与拆类前"读不到 slotType 字段"的行为一致。
///
/// category 字段名保持不变（旧资产 YAML 键名兼容），由各子类 OnValidate 自动写入，Inspector 不显示。
/// </summary>
[CreateAssetMenu(fileName = "Item_", menuName = "Game/物品/通用（旧，勿新建）")]
public class ItemSO : ScriptableObject
{
    // ============================================================
    // 静态 ID 注册表（供存档系统通过 ID 查找 SO 引用）
    // ============================================================

    /// <summary>所有已注册的 ItemSO，key = item.id</summary>
    private static Dictionary<string, ItemSO> _registry;

    /// <summary>
    /// 注册一个 ItemSO 到全局查找表
    /// 由 InventoryManager 在初始化时调用，将配置的 itemTemplates 全部注册
    /// </summary>
    public static void Register(ItemSO item)
    {
        if (item == null || string.IsNullOrEmpty(item.id)) return;
        if (_registry == null)
            _registry = new Dictionary<string, ItemSO>();
        _registry[item.id] = item;
    }

    /// <summary>通过物品 ID 查找 ItemSO 引用（存档加载时用于重建 ItemInstance）</summary>
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
    // 通用实例字段
    // ============================================================

    [Header("基础信息")]
    [Tooltip("物品唯一标识：由资产文件名自动填充（改文件名即改 ID），不用手填。存档按它查找模板")]
    public string id;

    [Tooltip("物品显示名称（如 铁剑、生命药水）")]
    public string itemName;

    [Tooltip("物品图标")]
    public Sprite icon;

    [Tooltip("物品描述文本")]
    [TextArea(2, 4)]
    public string description;

    [Header("分类与稀有度")]
    [Tooltip("分类由物品类型决定，这里只作存档/旧代码读取用，不要手改")]
    [HideInInspector]
    [SerializeField] protected ItemCategory category;

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

    // ============================================================
    // 类型相关的只读入口（子类 override）
    // ============================================================

    /// <summary>物品分类（UI 过滤 / 快捷槽校验 / 存档都用它）</summary>
    public virtual ItemCategory Category => category;

    /// <summary>装备类别（仅装备类有意义；其他类型返回默认 Weapon）</summary>
    public virtual EquipmentCategory EquipCategory => EquipmentCategory.Weapon;

    /// <summary>装备属性数据（仅装备类有；其他类型返回 null）</summary>
    public virtual EquipmentStats? EquipmentStats => null;

    /// <summary>使用后恢复的生命值（仅消耗品类有；其他类型返回 0）</summary>
    public virtual float HealAmount => 0f;
}
