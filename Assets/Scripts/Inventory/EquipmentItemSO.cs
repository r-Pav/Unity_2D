using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 装备类物品模板（2026-09-26 从 ItemSO 拆出）。
/// 只含装备专属字段；通用字段（id / 名称 / 图标 / 描述 / 分类 / 稀有度 / 堆叠 / 价格）继承自 ItemSO。
/// 创建菜单：Game/物品/装备
/// </summary>
[CreateAssetMenu(fileName = "Equip_", menuName = "Game/物品/装备")]
public class EquipmentItemSO : ItemSO
{
    // ============================================================
    // 槽位与装备属性数据
    // ============================================================

    [Header("装备")]
    [Tooltip("装备类别：武器 / 护甲 / 饰品。饰品两个饰品位通用，装备时自动进空的那个")]
    public EquipmentCategory equipCategory;

    [Tooltip("装备属性数据（slot + bonuses[]）。由下面那些属性字段自动重建，一般不用手填")]
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
    // 基类虚属性
    // ============================================================

    public override ItemCategory Category => ItemCategory.Equipment;
    public override EquipmentCategory EquipCategory => equipCategory;
    public override EquipmentStats? EquipmentStats => equipmentStats;

    // ============================================================
    // Bonus 同步
    // ============================================================

    /// <summary>(statId, type) → 属性 setter 映射（Percent 类 ×100 还原为用户填的百分比值）</summary>
    private static readonly Dictionary<
        (string statId, ModifierType type), Action<EquipmentItemSO, float>> _bonusPopulators =
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
        var list = new List<Bonus>();
        EquipmentStats es = equipmentStats ?? new EquipmentStats();
        es.slot = EquipmentSlotUtility.GetSlots(equipCategory)[0];   // 仅作记录，实际装配槽由 EquipmentManager 传入

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
        category = ItemCategory.Equipment;   // 分类由类型决定，基类字段只是给旧代码/存档读
        id = name;                            // 物品 ID 自动取资产文件名（改文件名即改 ID）
        RebuildEquipmentBonuses();
        UnityEditor.EditorUtility.SetDirty(this);
    }
#endif
}
