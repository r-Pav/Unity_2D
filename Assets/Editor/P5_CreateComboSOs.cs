using UnityEditor;
using UnityEngine;

/// <summary>
/// [P5] 一次性脚本 — 创建 3 个组合技能 SO 资产
/// 菜单: Tools > P5 > Create Combo Skill SOs
/// </summary>
public static class P5_CreateComboSOs
{
    private const string OutputDir = "Assets/Resources/Skills/Combo";

    [MenuItem("Tools/P5/Create Combo Skill SOs")]
    public static void CreateAll()
    {
        EnsureDirectory();

        CreateAsset(
            "Skill_Combo_DualSynergy",
            "双重协同",
            "2 连 AOE 攻击 — 双重打击，范围伤害",
            "AOE连击"
        );

        CreateAsset(
            "Skill_Combo_LawDomain",
            "法则领域·极",
            "领域展开 + 引爆 + 眩晕 — 范围内敌人受困、引爆并眩晕",
            "领域展开"
        );

        CreateAsset(
            "Skill_Combo_FinalJudgment",
            "终焉审判·灭",
            "全屏 180 伤害 + 击飞 — 毁灭性全屏打击，击飞所有敌人",
            "全屏AOE"
        );

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        Debug.Log("[P5] 3 个组合技能 SO 创建完成: Assets/Resources/Skills/Combo/");
    }

    private static void CreateAsset(string fileName, string skillName, string description, string effectType)
    {
        var asset = ScriptableObject.CreateInstance<CombinationSkillData>();
        asset.skillName = skillName;
        asset.description = description;
        asset.type = SkillType.Active;
        asset.category = SkillCategory.Attack;
        asset.cooldown = 12f;
        asset.manaCost = 35f;
        asset.skillLevel = 2;
        asset.maxLevel = 2;
        asset.combinationLevel = 2;
        asset.effectType = effectType;
        asset.destroyOnUse = false;

        string path = $"{OutputDir}/{fileName}.asset";
        AssetDatabase.CreateAsset(asset, path);
        Debug.Log($"  [P5] Created: {path}");
    }

    private static void EnsureDirectory()
    {
        if (!AssetDatabase.IsValidFolder("Assets/Resources"))
            AssetDatabase.CreateFolder("Assets", "Resources");
        if (!AssetDatabase.IsValidFolder("Assets/Resources/Skills"))
            AssetDatabase.CreateFolder("Assets/Resources", "Skills");
        if (!AssetDatabase.IsValidFolder(OutputDir))
            AssetDatabase.CreateFolder("Assets/Resources/Skills", "Combo");
    }
}
