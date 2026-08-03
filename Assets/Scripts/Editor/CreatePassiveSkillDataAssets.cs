using UnityEngine;
using UnityEditor;
using System.Collections.Generic;

/// <summary>
/// [Editor Tool] 批量创建 25 个 PassiveSkillData SO 资产
/// 5 线 × 5 层 = 25 个，数据来源：Docs/策划案_P2_被动.txt 数值表
/// 输出路径：Assets/Resources/Skills/Passive/
/// 命名格式：Passive_L{layer}_L{lineId}
/// Menu: Tools → Create All PassiveSkillData Assets
/// </summary>
public static class CreatePassiveSkillDataAssets
{
    const string OutputDir = "Assets/Resources/Skills/Passive";

    [MenuItem("Tools/Create All PassiveSkillData Assets")]
    public static void CreateAll()
    {
        // 确保输出目录存在
        if (!AssetDatabase.IsValidFolder(OutputDir))
        {
            EnsureFolderExists(OutputDir);
        }

        int created = 0;
        for (int layer = 1; layer <= 5; layer++)
        {
            for (int lineId = 0; lineId <= 4; lineId++)
            {
                CreateOne(layer, lineId);
                created++;
            }
        }

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log($"[CreatePassiveSkillDataAssets] Done. Created {created} SOs under {OutputDir}");
    }

    static void CreateOne(int layer, int lineId)
    {
        string assetName = $"Passive_L{layer}_L{lineId}";
        string assetPath = $"{OutputDir}/{assetName}.asset";

        // 若已存在则先删除
        var existing = AssetDatabase.LoadAssetAtPath<PassiveSkillData>(assetPath);
        if (existing != null)
        {
            AssetDatabase.DeleteAsset(assetPath);
        }

        var so = ScriptableObject.CreateInstance<PassiveSkillData>();
        so.layer = layer;
        so.lineId = lineId;
        so.type = SkillType.Passive;
        so.category = SkillCategory.Passive;
        so.skillName = GetSkillName(layer, lineId);
        so.description = GetDescription(layer, lineId);
        so.unlockLevel = GetUnlockLevel(layer);
        so.skillLevel = 1;
        so.maxLevel = 1; // 被动无等级提升
        so.cooldown = 0f;
        so.manaCost = 0f;
        so.effects = GetEffects(layer, lineId);

        AssetDatabase.CreateAsset(so, assetPath);
    }

    // ── 数据表 ──────────────────────────────────────────────────

    static string GetSkillName(int layer, int lineId)
    {
        string[] lineNames = { "HP恢复", "伤害+攻速", "移速+闪避", "减伤+控制", "法力+CD" };
        string[] tierNames = { "", "TI", "TII", "TIII", "TIV", "TV" };
        return $"{tierNames[layer]} {lineNames[lineId]}";
    }

    static string GetDescription(int layer, int lineId)
    {
        string[][] desc = new string[][]
        {
            // lineId 0: HP恢复线
            new[] { "", "生命上限+1%", "生命上限+2%", "生命上限+3%", "生命上限+4%", "生命上限+5%" },
            // lineId 1: 伤害+攻速线
            new[] { "", "伤害+8%", "伤害+15%", "伤害+22%, 攻速+10%", "伤害+28%, 攻速+15%", "伤害+35%, 攻速+20%" },
            // lineId 2: 移速+闪避线
            new[] { "", "移速+6%", "移速+12%", "移速+18%, 闪避+15%", "移速+24%, 闪避+20%", "移速+30%, 闪避+30%" },
            // lineId 3: 减伤+控制线
            new[] { "", "减伤+5%", "减伤+10%", "减伤+15%, 硬直-20%", "减伤+20%, 控制-25%", "减伤+25%, 低血加防(HP≤30%时额外减伤15%)" },
            // lineId 4: 法力+CD线
            new[] { "", "法力恢复+1%", "法力恢复+2%, 法力+20", "法力恢复+3%, 法力+22, CD-5%", "法力恢复+4%, 法力+25, CD-8%", "法力恢复+5%, 法力+30, CD-10%, 法力消耗-3%" },
        };
        return desc[lineId][layer];
    }

    static int GetUnlockLevel(int layer)
    {
        return layer switch
        {
            1 => 1,   // TI: Lv1
            2 => 5,   // TII: Lv5
            3 => 8,   // TIII: Lv8
            4 => 12,  // TIV: Lv12
            5 => 16,  // TV: Lv16
            _ => 1,
        };
    }

    static PassiveSkillData.PassiveEffect[] GetEffects(int layer, int lineId)
    {
        var list = new List<PassiveSkillData.PassiveEffect>();

        switch (lineId)
        {
            // ── Line 0: HP恢复线 ──
            case 0:
                // 全百分比叠加: HP+1% ~ HP+5%
                list.Add(Effect(StatId.MaxHealth, layer * 0.01f, ModifierType.Percent));
                break;

            // ── Line 1: 伤害+攻速线 ──
            case 1:
                list.Add(Effect(StatId.DamageMultiplier, DamageLineValues[layer], ModifierType.Percent));
                if (layer >= 3)
                    list.Add(Effect(StatId.AttackSpeedMultiplier, AttackSpeedValues[layer], ModifierType.Percent));
                break;

            // ── Line 2: 移速+闪避线 ──
            case 2:
                list.Add(Effect(StatId.MoveSpeed, MoveSpeedValues[layer], ModifierType.Percent));
                if (layer >= 3)
                    list.Add(Effect(StatId.DodgeChance, DodgeValues[layer], ModifierType.Flat));
                break;

            // ── Line 3: 减伤+控制线 ──
            case 3:
                list.Add(Effect(StatId.DamageReduction, DamageReductionValues[layer], ModifierType.Flat));
                if (layer == 3)
                    list.Add(Effect(StatId.StunReduction, 0.20f, ModifierType.Percent));    // 硬直-20%
                if (layer == 4)
                    list.Add(Effect(StatId.ControlReduction, 0.25f, ModifierType.Percent));  // 控制-25%
                // TV(layer=5) 低血加防由 PassiveEquipManager 侧条件处理，SO 不存额外值
                break;

            // ── Line 4: 法力+CD线 ──
            case 4:
                list.Add(Effect(StatId.ManaRegen, ManaRegenValues[layer], ModifierType.Percent));
                if (layer >= 2)
                    list.Add(Effect(StatId.MaxMana, ManaFlatValues[layer], ModifierType.Flat));
                if (layer >= 3)
                    list.Add(Effect(StatId.CooldownMultiplier, CdReductionValues[layer], ModifierType.Percent));
                if (layer == 5)
                    list.Add(Effect(StatId.ManaCostMultiplier, -0.03f, ModifierType.Percent));
                break;
        }

        return list.ToArray();
    }

    // ── 辅助方法 ──

    static PassiveSkillData.PassiveEffect Effect(string stat, float value, ModifierType type, string note = null)
    {
        return new PassiveSkillData.PassiveEffect
        {
            targetStat = stat,
            value = value,
            type = type,
        };
    }

    // ── 数值常量 ──

    static readonly float[] DamageLineValues    = { 0, 0.08f, 0.15f, 0.22f, 0.28f, 0.35f };
    static readonly float[] AttackSpeedValues   = { 0, 0,     0,     0.10f, 0.15f, 0.20f };
    static readonly float[] MoveSpeedValues     = { 0, 0.06f, 0.12f, 0.18f, 0.24f, 0.30f };
    static readonly float[] DodgeValues         = { 0, 0,     0,     0.15f, 0.20f, 0.30f };
    static readonly float[] DamageReductionValues = { 0, 0.05f, 0.10f, 0.15f, 0.20f, 0.25f };
    static readonly float[] ManaRegenValues     = { 0, 0.01f, 0.02f, 0.03f, 0.04f, 0.05f };
    static readonly float[] ManaFlatValues      = { 0, 0,     20,    22,    25,    30 };
    static readonly float[] CdReductionValues   = { 0, 0,     0,     -0.05f,-0.08f,-0.10f };

    // ── 目录工具 ──

    static void EnsureFolderExists(string path)
    {
        // 递归创建父目录
        string parent = System.IO.Path.GetDirectoryName(path).Replace("\\", "/");
        string folder = System.IO.Path.GetFileName(path);

        if (!AssetDatabase.IsValidFolder(parent))
            EnsureFolderExists(parent);

        AssetDatabase.CreateFolder(parent, folder);
    }
}
