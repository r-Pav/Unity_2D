using UnityEngine;

/// <summary>
/// 装备槽位类型 — 定义装备可放入的槽位
/// 当前设计: 4 槽位（Weapon/Armor/Accessory0/Accessory1）
/// </summary>
public enum EquipmentSlotType
{
    /// <summary>武器槽</summary>
    Weapon,
    /// <summary>护甲槽</summary>
    Armor,
    /// <summary>饰品槽 0</summary>
    Accessory0,
    /// <summary>饰品槽 1</summary>
    Accessory1
}

/// <summary>
/// 物品分类 — 用于背包/仓库面板的 Tab 过滤
/// All 仅用于 UI 过滤，不用于 ItemSO 数据
/// </summary>
public enum ItemCategory
{
    /// <summary>全部（仅 UI 过滤，不用于物品数据）</summary>
    All = 0,
    /// <summary>消耗品（药水、卷轴等）</summary>
    Consumable = 1,
    /// <summary>装备（武器、护甲、饰品）</summary>
    Equipment = 2,
    /// <summary>材料（合成材料等）</summary>
    Material = 3
}

/// <summary>
/// 物品稀有度 — 影响掉落概率和视觉表现
/// </summary>
public enum ItemRarity
{
    /// <summary>普通（白色）</summary>
    Common = 0,
    /// <summary>稀有（蓝色）</summary>
    Rare = 1,
    /// <summary>史诗（紫色）</summary>
    Epic = 2,
    /// <summary>传说（橙色）</summary>
    Legendary = 3
}

/// <summary>
/// 稀有度对应的颜色常量 — 供 DropItem 边框渲染等使用
/// </summary>
public static class RarityColor
{
    public static readonly Color Common = new Color(0.8f, 0.8f, 0.8f, 1f);   // #CCCCCC 灰白
    public static readonly Color Rare = new Color(0.27f, 0.53f, 1f, 1f);       // #4488FF 蓝色
    public static readonly Color Epic = new Color(0.67f, 0.27f, 1f, 1f);        // #AA44FF 紫色
    public static readonly Color Legendary = new Color(1f, 0.67f, 0f, 1f);       // #FFAA00 橙色

    public static Color GetColor(ItemRarity rarity) => rarity switch
    {
        ItemRarity.Common => Common,
        ItemRarity.Rare => Rare,
        ItemRarity.Epic => Epic,
        ItemRarity.Legendary => Legendary,
        _ => Common
    };
}
