using UnityEngine;
using UnityEditor;

/// <summary>
/// [Editor Tool] 创建 Q（能量球）和 E（冲进步）ActiveSkillData SO 资产
/// 数据来源：Docs/策划案_P3_主动.txt 数值表
/// 输出路径：Assets/Resources/Skills/Active/
/// 命名格式：Skill_Active_Q, Skill_Active_E
/// Menu: Tools → Create ActiveSkillData Assets (Q/E)
/// </summary>
public static class CreateActiveSkillDataAssets
{
    const string OutputDir = "Assets/Resources/Skills/Active";

    [MenuItem("Tools/Create ActiveSkillData Assets (Q/E)")]
    public static void CreateAll()
    {
        // 确保输出目录存在
        if (!AssetDatabase.IsValidFolder(OutputDir))
            EnsureFolderExists(OutputDir);

        CreateSkill_Q();
        CreateSkill_E();

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log($"[CreateActiveSkillDataAssets] Done. Created Q + E assets under {OutputDir}");
    }

    // ============================================================
    // Q 技能: 能量球 / 散射弹幕 / 穿透狙击 / 弹幕风暴 / 毁灭射线
    // ============================================================

    static void CreateSkill_Q()
    {
        string path = $"{OutputDir}/Skill_Active_Q.asset";
        DeleteIfExists(path);

        var so = ScriptableObject.CreateInstance<ActiveSkillData>();
        so.name = "Skill_Active_Q";
        so.skillName = "能量球";
        so.description = "发射一枚能量球攻击敌人";
        so.type = SkillType.Active;
        so.category = SkillCategory.Attack;
        so.hotkey = KeyCode.Q;
        so.unlockLevel = 0;   // 初始可用
        so.skillLevel = 1;
        so.maxLevel = 3;      // 主动技能 Lv3 封顶
        so.cooldown = 3f;     // Lv1 基础冷却
        so.manaCost = 10f;    // Lv1 基础法力消耗

        // Lv1: 能量球
        so.lv1Data = new ActiveSkillData.ActiveBranchData
        {
            branchName = "能量球",
            damage = 35f,
            cooldown = 3f,
            manaCost = 10f,
            description = "发射一枚能量球，对首个敌人造成 35 点伤害"
        };

        // Lv2 Left: 散射弹幕
        so.lv2Left = new ActiveSkillData.ActiveBranchData
        {
            branchName = "散射弹幕",
            damage = 25f,
            cooldown = 4f,
            manaCost = 15f,
            description = "发射 3 枚弹幕，每枚造成 25 点伤害"
        };

        // Lv2 Right: 穿透狙击
        so.lv2Right = new ActiveSkillData.ActiveBranchData
        {
            branchName = "穿透狙击",
            damage = 55f,
            cooldown = 5f,
            manaCost = 18f,
            description = "发射穿透弹，造成 55 点伤害，穿透 3 个目标"
        };

        // Lv3 Left: 弹幕风暴（散射弹幕升级）
        so.lv3Left = new ActiveSkillData.ActiveBranchData
        {
            branchName = "弹幕风暴",
            damage = 30f,
            cooldown = 3.5f,
            manaCost = 20f,
            description = "发射 5 枚弹幕，每枚造成 30 点伤害"
        };

        // Lv3 Right: 毁灭射线（穿透狙击升级）
        so.lv3Right = new ActiveSkillData.ActiveBranchData
        {
            branchName = "毁灭射线",
            damage = 90f,
            cooldown = 4.5f,
            manaCost = 25f,
            description = "发射毁灭射线，造成 90 点伤害，可反弹"
        };

        AssetDatabase.CreateAsset(so, path);
        Debug.Log($"  Created: {path}");
    }

    // ============================================================
    // E 技能: 冲进步 / 突进斩 / 灵巧闪避 / 双闪连袭 / 虚空步伐
    // ============================================================

    static void CreateSkill_E()
    {
        string path = $"{OutputDir}/Skill_Active_E.asset";
        DeleteIfExists(path);

        var so = ScriptableObject.CreateInstance<ActiveSkillData>();
        so.name = "Skill_Active_E";
        so.skillName = "冲进步";
        so.description = "朝前方冲刺一段距离";
        so.type = SkillType.Active;
        so.category = SkillCategory.Movement;
        so.hotkey = KeyCode.E;
        so.unlockLevel = 3;   // 玩家等级 Lv3 解锁
        so.skillLevel = 1;
        so.maxLevel = 3;      // 主动技能 Lv3 封顶
        so.cooldown = 3f;     // Lv1 基础冷却
        so.manaCost = 8f;     // Lv1 基础法力消耗

        // Lv1: 冲进步
        so.lv1Data = new ActiveSkillData.ActiveBranchData
        {
            branchName = "冲进步",
            damage = 0f,
            cooldown = 3f,
            manaCost = 8f,
            description = "朝前方冲刺 3 米距离"
        };

        // Lv2 Left: 突进斩
        so.lv2Left = new ActiveSkillData.ActiveBranchData
        {
            branchName = "突进斩",
            damage = 40f,
            cooldown = 3.5f,
            manaCost = 12f,
            description = "冲刺并对沿途敌人造成 40 点 AOE 伤害"
        };

        // Lv2 Right: 灵巧闪避
        so.lv2Right = new ActiveSkillData.ActiveBranchData
        {
            branchName = "灵巧闪避",
            damage = 0f,
            cooldown = 2f,
            manaCost = 6f,
            description = "冲刺附带无敌帧，冷却时间缩短"
        };

        // Lv3 Left: 双闪连袭（突进斩升级）
        so.lv3Left = new ActiveSkillData.ActiveBranchData
        {
            branchName = "双闪连袭",
            damage = 50f,
            cooldown = 4f,
            manaCost = 15f,
            description = "2 段充能冲刺，每段造成 50 点伤害"
        };

        // Lv3 Right: 虚空步伐（灵巧闪避升级）
        so.lv3Right = new ActiveSkillData.ActiveBranchData
        {
            branchName = "虚空步伐",
            damage = 60f,
            cooldown = 1.5f,
            manaCost = 10f,
            description = "冲刺极短冷却，下一次攻击必定暴击"
        };

        AssetDatabase.CreateAsset(so, path);
        Debug.Log($"  Created: {path}");
    }

    // ============================================================
    // 辅助方法
    // ============================================================

    static void DeleteIfExists(string path)
    {
        var existing = AssetDatabase.LoadAssetAtPath<ActiveSkillData>(path);
        if (existing != null)
            AssetDatabase.DeleteAsset(path);
    }

    static void EnsureFolderExists(string path)
    {
        string parent = System.IO.Path.GetDirectoryName(path).Replace("\\", "/");
        string folder = System.IO.Path.GetFileName(path);

        if (!AssetDatabase.IsValidFolder(parent))
            EnsureFolderExists(parent);

        AssetDatabase.CreateFolder(parent, folder);
    }
}
