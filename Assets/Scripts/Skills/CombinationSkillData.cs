using UnityEngine;

/// <summary>
/// [P5] 组合技能数据 — 继承 SkillData
/// 组合技能是消耗两个材料技能（主动/武器）合成出的强力复合效果技能。
/// 配方直接写在 SO 里，指定材料技能 SO + 所需等级。
/// 等级固定 Lv2，不可升级。
/// </summary>
[CreateAssetMenu(fileName = "Skill_Combo_", menuName = "Game/SkillData/Combination")]
public class CombinationSkillData : SkillData
{
    [Header("配方 — 合成材料")]
    [Tooltip("材料技能 A 的 SO")]
    public SkillData materialSkillA;
    [Tooltip("材料技能 A 要求的等级")]
    [Range(1, 5)] public int materialLevelA = 1;

    [Tooltip("材料技能 B 的 SO")]
    public SkillData materialSkillB;
    [Tooltip("材料技能 B 要求的等级")]
    [Range(1, 5)] public int materialLevelB = 1;

    [Header("组合技能参数")]
    [Tooltip("组合产出等级（固定 Lv2，不可升级）")]
    public int combinationLevel = 2;

    [Tooltip("效果类型描述（如「领域展开」「全屏AOE」）")]
    public string effectType;

    [Tooltip("使用后是否销毁（默认 false，作为永久技能存在）")]
    public bool destroyOnUse = false;
}
