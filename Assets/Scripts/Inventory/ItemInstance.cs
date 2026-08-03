using System;
using UnityEngine;

/// <summary>
/// 运行时物品实例
/// 非 MonoBehaviour 纯数据类，由 InventoryManager 等持有和管理
/// 每个 ItemInstance 对应背包/仓库/装备槽中的一个物品条目
/// </summary>
[Serializable]
public class ItemInstance
{
    /// <summary>物品模板引用（ScriptableObject，静态数据）</summary>
    public ItemSO template;

    /// <summary>当前堆叠数量</summary>
    public int stackSize;

    /// <summary>当前耐久度（仅装备类物品有效，-1 表示不适用）</summary>
    public int currentDurability = -1;

    /// <summary>最大耐久度（仅装备类物品有效，-1 表示不适用）</summary>
    public int maxDurability = -1;

    // ── 后续 Phase 扩展字段（暂不实现逻辑，仅预留数据结构）──

    /// <summary>[预留] 属性加成列表 — 装备实例化的修饰器，将在 Phase 3 EquipmentManager 中使用</summary>
    /// <remarks>
    /// 与 ItemSO.EquipmentStats.bonuses 不同：
    /// - ItemSO.bonuses 是模板数据（静态）
    /// - ItemInstance.modifiers 是实例化后的运行时 Modifier（可被等级缩放等修改）
    /// 当前 Phase 1 不填充，Phase 3 装备时由 EquipmentManager 从 template 生成并注入 StatModifierManager
    /// </remarks>
    // public Modifier[] modifiers; // Phase 3 启用

    /// <summary>[预留] 物品效果数据引用（消耗品/武器技能），Phase 2+ 扩展</summary>
    // public ItemEffectDataSO itemEffect; // Phase 2+ 启用

    // ── 构造与工具方法 ──

    /// <summary>创建一个新的物品实例</summary>
    public ItemInstance(ItemSO template, int stackSize = 1)
    {
        this.template = template;
        this.stackSize = Mathf.Clamp(stackSize, 1, template != null ? template.maxStack : 1);

        // 装备类物品初始化耐久度
        if (template != null && template.category == ItemCategory.Equipment)
        {
            this.maxDurability = 100; // TODO: 后续从配置读取
            this.currentDurability = this.maxDurability;
        }
    }

    /// <summary>是否还有剩余堆叠（stackSize > 0）</summary>
    public bool IsValid => template != null && stackSize > 0;

    /// <summary>是否可继续堆叠（未达到最大堆叠数）</summary>
    public bool CanStack => template != null && stackSize < template.maxStack;

    /// <summary>剩余可堆叠数量</summary>
    public int RemainingStackSpace => template != null ? template.maxStack - stackSize : 0;

    /// <summary>尝试堆叠指定数量，返回实际堆叠成功的数量</summary>
    /// <param name="amount">尝试堆叠数量</param>
    /// <returns>实际堆叠成功的数量（不超过剩余空间）</returns>
    public int TryStack(int amount)
    {
        if (!CanStack || amount <= 0) return 0;
        int space = RemainingStackSpace;
        int actual = Mathf.Min(amount, space);
        stackSize += actual;
        return actual;
    }

    /// <summary>尝试移除指定数量，返回实际移除的数量</summary>
    public int TryRemove(int amount)
    {
        if (amount <= 0) return 0;
        int actual = Mathf.Min(amount, stackSize);
        stackSize -= actual;
        return actual;
    }

    /// <summary>是否为同一物品模板（用于堆叠判断）</summary>
    public bool IsSameItem(ItemInstance other)
    {
        return other != null && other.template == this.template;
    }

    /// <summary>获取显示用名称</summary>
    public string DisplayName => template != null ? template.itemName : "(空)";

    public override string ToString()
    {
        if (template == null) return "ItemInstance(null)";
        string durabilityStr = currentDurability >= 0 ? $" 耐久:{currentDurability}/{maxDurability}" : "";
        return $"{template.itemName} x{stackSize}{durabilityStr}";
    }
}
