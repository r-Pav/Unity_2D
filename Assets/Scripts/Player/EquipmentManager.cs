using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// [Phase3] 玩家装备管理器 — 管理 4 个装备槽位（Weapon/Armor/Accessory0/Accessory1）
/// 挂 Player GameObject，负责装备/卸下/死亡掉落/属性加成注入/移除
/// 
/// 关键衔接：
///   Equip 时 → StatModifierManager.AddModifier + PlayerAttributeSystem.SetEquipmentBonus
///   Unequip 时 → StatModifierManager.RemoveModifier + PlayerAttributeSystem.SetEquipmentBonus
///   DropAllOnDeath → 遍历4槽位 Unequip + DropItem.Spawn
/// 
/// 注意：当前 Phase3 直接操作 ItemInstance，Phase4 InventoryManager 建成后通过它间接操作背包
/// </summary>
public class EquipmentManager : MonoBehaviour
{
    // ============================================================
    // 配置
    // ============================================================

    [Header("掉落物")]
    [Tooltip("DropItem Prefab 引用（用于死亡掉落时生成世界掉落物）")]
    [SerializeField] private DropItem dropItemPrefab;

    [Header("组件引用（自动查找）")]
    [Tooltip("StatModifierManager 引用（自动从同一 GameObject 获取）")]
    [SerializeField] private StatModifierManager statModManager;

    [Tooltip("PlayerAttributeSystem 引用（自动从同一 GameObject 获取）")]
    [SerializeField] private PlayerAttributeSystem attrSystem;

    // ============================================================
    // 运行时状态
    // ============================================================

    /// <summary>4 个装备槽位数据，索引 = EquipmentSlotType 枚举值</summary>
    private readonly ItemInstance[] _slots = new ItemInstance[4];

    /// <summary>死亡标记</summary>
    private bool _isDead;

    /// <summary>复活后重置死亡标记，恢复装备操作</summary>
    public void ResetDeathFlag()
    {
        _isDead = false;
    }

    // ============================================================
    // 回调（供 UI 注册刷新，Phase4 使用）
    // ============================================================

    /// <summary>装备回调：参数为 (slotType, item)</summary>
    public System.Action<EquipmentSlotType, ItemInstance> OnEquipped;

    /// <summary>卸下回调：参数为 (slotType, item)</summary>
    public System.Action<EquipmentSlotType, ItemInstance> OnUnequipped;

    // ============================================================
    // 生命周期
    // ============================================================

    private void Awake()
    {
        // 自动查找同 GameObject 上的依赖组件
        if (statModManager == null)
            statModManager = GetComponent<StatModifierManager>();
        if (attrSystem == null)
            attrSystem = GetComponent<PlayerAttributeSystem>();

        if (statModManager == null)
            Debug.LogWarning("[EquipmentManager] StatModifierManager 未找到，装备属性加成将不会注入修饰器管线");
        if (attrSystem == null)
            Debug.LogWarning("[EquipmentManager] PlayerAttributeSystem 未找到，装备主属性加成将不会生效");
    }

    // ============================================================
    // 公开接口 — Equip / Unequip
    // ============================================================

    /// <summary>
    /// 将物品装备到指定槽位
    /// 若槽位已有装备，会自动先卸下旧装备（旧装备的处理由调用方决定）
    /// </summary>
    /// <param name="slot">目标装备槽位</param>
    /// <param name="item">要装备的物品实例</param>
    /// <returns>true = 装备成功；false = 死亡/物品无效</returns>
    public bool Equip(EquipmentSlotType slot, ItemInstance item)
    {
        if (_isDead)
        {
            Debug.LogWarning("[EquipmentManager] 玩家已死亡，无法装备");
            return false;
        }

        if (item == null || item.template == null)
        {
            Debug.LogWarning("[EquipmentManager] 装备失败：物品无效");
            return false;
        }

        if (item.template.category != ItemCategory.Equipment)
        {
            Debug.LogWarning($"[EquipmentManager] 装备失败：{item.DisplayName} 不是装备类物品");
            return false;
        }

        // 验证槽位匹配
        if (item.template.slotType != slot)
        {
            Debug.LogWarning($"[EquipmentManager] 装备失败：{item.DisplayName} 槽位类型为 {item.template.slotType}，目标槽位为 {slot}");
            return false;
        }

        int slotIdx = (int)slot;

        // 若槽位已有装备，先卸下
        if (_slots[slotIdx] != null)
        {
            ItemInstance oldItem = _slots[slotIdx];
            RemoveEquipmentModifiers(slot, oldItem);
            _slots[slotIdx] = null;
            OnUnequipped?.Invoke(slot, oldItem);
        }

        // 填入新装备
        _slots[slotIdx] = item;

        // 注入属性加成
        AddEquipmentModifiers(slot, item);
        RecalculateEquipmentBonuses();

        OnEquipped?.Invoke(slot, item);

        // Debug.Log($"[EquipmentManager] 装备成功：{item.DisplayName} → {slot}");
        return true;
    }

    /// <summary>
    /// 卸下指定槽位的装备，返回被卸下的 ItemInstance
    /// </summary>
    /// <param name="slot">要卸下的槽位</param>
    /// <returns>被卸下的物品实例（null = 槽位为空）</returns>
    public ItemInstance Unequip(EquipmentSlotType slot)
    {
        if (_isDead)
        {
            Debug.LogWarning("[EquipmentManager] 玩家已死亡，无法卸下装备");
            return null;
        }

        int slotIdx = (int)slot;
        ItemInstance item = _slots[slotIdx];

        if (item == null) return null;

        // 移除属性加成
        RemoveEquipmentModifiers(slot, item);

        // 清空槽位
        _slots[slotIdx] = null;

        // 重算剩余装备的主属性加成
        RecalculateEquipmentBonuses();

        OnUnequipped?.Invoke(slot, item);

        // Debug.Log($"[EquipmentManager] 卸下装备：{item.DisplayName} ← {slot}");
        return item;
    }

    // ============================================================
    // 公开接口 — 死亡掉落
    // ============================================================

    /// <summary>
    /// 死亡时全部槽位生成掉落物，清空所有装备
    /// 在 PlayerHealth.TakeDamage 死亡分支中调用（重置生命值之前）
    /// ownerMask = Player | Enemy（双方可拾取）
    /// </summary>
    public void DropAllOnDeath()
    {
        _isDead = true;

        int dropCount = 0;
        for (int i = 0; i < 4; i++)
        {
            if (_slots[i] == null) continue;

            EquipmentSlotType slot = (EquipmentSlotType)i;
            ItemInstance item = _slots[i];

            // 移除属性加成
            RemoveEquipmentModifiers(slot, item);

            // 生成世界掉落物
            SpawnDropItem(item, dropLevel: RarityToLevel(item.template.rarity),
                ownerMask: LayerMask.GetMask("Player", "Enemy"));

            // 清空槽位
            _slots[i] = null;
            OnUnequipped?.Invoke(slot, item);
            dropCount++;
        }

        // 清除装备主属性加成
        attrSystem?.ClearEquipmentBonus();

        // Debug.Log($"[EquipmentManager] 死亡掉落完成，共 {dropCount} 件装备");
    }

    // ============================================================
    // 公开接口 — 查询
    // ============================================================

    /// <summary>查询指定槽位的装备（null = 空）</summary>
    public ItemInstance GetEquipped(EquipmentSlotType slot)
    {
        return _slots[(int)slot];
    }

    /// <summary>检查指定槽位是否有装备</summary>
    public bool HasEquipped(EquipmentSlotType slot)
    {
        return _slots[(int)slot] != null;
    }

    /// <summary>获取所有槽位的装备快照（用于存档等）</summary>
    public ItemInstance[] GetAllSlots()
    {
        return (ItemInstance[])_slots.Clone();
    }

    /// <summary>
    /// 注册装备/卸下回调（UI 面板在 Phase4 订阅以刷新显示）
    /// </summary>
    public void RegisterCallbacks(System.Action<EquipmentSlotType, ItemInstance> onEquip,
        System.Action<EquipmentSlotType, ItemInstance> onUnequip)
    {
        OnEquipped += onEquip;
        OnUnequipped += onUnequip;
    }

    /// <summary>移除已注册的回调</summary>
    public void UnregisterCallbacks(System.Action<EquipmentSlotType, ItemInstance> onEquip,
        System.Action<EquipmentSlotType, ItemInstance> onUnequip)
    {
        OnEquipped -= onEquip;
        OnUnequipped -= onUnequip;
    }

    // ============================================================
    // 内部方法 — 属性加成注入/移除
    // ============================================================

    /// <summary>
    /// 装备时注入属性修饰器
    /// 对 EquipmentStats.bonuses 中每条加成，以 source="Equip_{slotType}_{statId}" 注入 StatModifierManager
    /// 武器槽额外触发 WeaponEquippedEvent
    /// </summary>
    private void AddEquipmentModifiers(EquipmentSlotType slot, ItemInstance item)
    {
        var stats = item.template.equipmentStats;
        if (stats == null || stats.Value.bonuses == null) return;

        foreach (var bonus in stats.Value.bonuses)
        {
            string source = GetEquipSource(slot, bonus.statId);
            var mod = new Modifier(bonus.statId, bonus.value, bonus.type, source, priority: 0);
            statModManager?.AddModifier(mod);
        }

        // 武器槽额外：发送 WeaponEquippedEvent（供 WeaponSkillLink 订阅激活武器技能）
        if (slot == EquipmentSlotType.Weapon)
        {
            EventBus.Trigger(new WeaponEquippedEvent(stats.Value.weaponType, stats.Value.weaponSkill));
        }
    }

    /// <summary>
    /// 卸下时移除属性修饰器（以 source 精确匹配）
    /// 武器槽额外触发 WeaponUnequippedEvent
    /// </summary>
    private void RemoveEquipmentModifiers(EquipmentSlotType slot, ItemInstance item)
    {
        var stats = item.template.equipmentStats;
        if (stats == null || stats.Value.bonuses == null)
        {
            // 即使无 bonuses，武器仍可能需要清理
            if (slot == EquipmentSlotType.Weapon && stats != null)
            {
                EventBus.Trigger(new WeaponUnequippedEvent(stats.Value.weaponType));
            }
            return;
        }

        foreach (var bonus in stats.Value.bonuses)
        {
            string source = GetEquipSource(slot, bonus.statId);
            statModManager?.RemoveModifier(source);
        }

        // 武器槽额外：发送 WeaponUnequippedEvent
        if (slot == EquipmentSlotType.Weapon)
        {
            EventBus.Trigger(new WeaponUnequippedEvent(stats.Value.weaponType));
        }
    }

    /// <summary>
    /// 重算所有槽位装备提供的主属性加成（str/int/agi），调用 PlayerAttributeSystem.SetEquipmentBonus
    /// </summary>
    private void RecalculateEquipmentBonuses()
    {
        if (attrSystem == null) return;

        var bonusDict = new Dictionary<string, int>();
        int totalStr = 0, totalInt = 0, totalAgi = 0;

        for (int i = 0; i < 4; i++)
        {
            if (_slots[i] == null) continue;

            var stats = _slots[i].template.equipmentStats;
            if (stats == null || stats.Value.bonuses == null) continue;

            foreach (var bonus in stats.Value.bonuses)
            {
                if (bonus.type != ModifierType.Flat) continue;
                switch (bonus.statId)
                {
                    case "str": totalStr += Mathf.RoundToInt(bonus.value); break;
                    case "int": totalInt += Mathf.RoundToInt(bonus.value); break;
                    case "agi": totalAgi += Mathf.RoundToInt(bonus.value); break;
                }
            }
        }

        bonusDict["str"] = totalStr;
        bonusDict["int"] = totalInt;
        bonusDict["agi"] = totalAgi;

        attrSystem.SetEquipmentBonus(bonusDict);
    }

    // ============================================================
    // 内部方法 — 掉落生成
    // ============================================================

    /// <summary>
    /// 生成世界掉落物
    /// 位置 = 玩家位置 + 随机小偏移（避免多个物品完全重叠）
    /// </summary>
    private void SpawnDropItem(ItemInstance item, int dropLevel, LayerMask ownerMask)
    {
        if (dropItemPrefab == null)
        {
            Debug.LogWarning($"[EquipmentManager] dropItemPrefab 未配置，跳过掉落：{item.DisplayName}");
            return;
        }

        // 随机偏移：X[-0.5, 0.5], Y[0, 0.3]
        Vector2 pos = (Vector2)transform.position + new Vector2(
            Random.Range(-0.5f, 0.5f),
            Random.Range(0f, 0.3f));

        DropItem.Spawn(dropItemPrefab, item, dropLevel, ownerMask, pos);

        // Debug.Log($"[EquipmentManager] 生成掉落物：{item.DisplayName} at {pos}");
    }

    // ============================================================
    // 辅助方法
    // ============================================================

    /// <summary>生成装备修饰器的唯一 source 标识符</summary>
    private static string GetEquipSource(EquipmentSlotType slot, string statId)
    {
        return $"Equip_{slot}_{statId}";
    }

    /// <summary>稀有度 → 掉落等级映射：Common=1, Rare=2, Epic=3, Legendary=4</summary>
    private static int RarityToLevel(ItemRarity rarity)
    {
        return (int)rarity + 1;
    }
}
