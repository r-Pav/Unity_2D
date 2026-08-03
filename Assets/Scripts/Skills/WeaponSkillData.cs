using UnityEngine;

/// <summary>武器类型标识</summary>
public enum WeaponType
{
    Sword,      // 剑
    Bow,        // 弓
    Staff,      // 法杖
    Hammer,     // 大锤
    DualBlades  // 双刀
}

/// <summary>
/// [P4] 武器技能数据 — 继承 SkillData
/// 装备武器时自动获得对应专属技能，卸下时自动移除。
/// 等级固定 Lv1，不参与分支升级。
/// </summary>
[CreateAssetMenu(fileName = "Skill_Weapon_", menuName = "Game/SkillData/Weapon")]
public class WeaponSkillData : SkillData
{
    [Header("武器专属参数")]
    [Tooltip("武器类型，决定装备哪类武器时激活此技能")]
    public WeaponType weaponType;

    [Tooltip("基础伤害（用于粗略数值预览和战斗计算）")]
    public float damageBase;

    [TextArea(2, 4)]
    [Tooltip("效果描述文本（用于 UI 技能面板展示，如「近战连斩，逐次增伤」）")]
    public string effectDescription;

    // 武器技能等级固定 Lv1，不可升级
    // SkillData.skillLevel / maxLevel 在 Inspector 中默认 1/1
    // SkillData.type 建议设为 Active
    // SkillData.category 建议设为 Attack
}
