using UnityEngine;

/// <summary>
/// [P3] 主动分支技能数据 — 继承 SkillData
/// 包含 Lv1~Lv3 的分支选择信息（左右分支各 Lv2/Lv3 的数据引用）
/// chosenBranch 是运行时字段，不序列化到 SO 资产
/// </summary>
[CreateAssetMenu(fileName = "Skill_Active_", menuName = "Game/SkillData/Active")]
public class ActiveSkillData : SkillData
{
    [Header("Lv1 基础数据")]
    [Tooltip("Lv1 版本的技能参数（首次获得时的形态）")]
    public ActiveBranchData lv1Data;

    [Header("分支数据（Lv2）")]
    [Tooltip("Lv2 左分支参数")]
    public ActiveBranchData lv2Left;
    [Tooltip("Lv2 右分支参数")]
    public ActiveBranchData lv2Right;

    [Header("分支数据（Lv3）")]
    [Tooltip("Lv3 左分支参数（Lv2 左→Lv3 左）")]
    public ActiveBranchData lv3Left;
    [Tooltip("Lv3 右分支参数（Lv2 右→Lv3 右）")]
    public ActiveBranchData lv3Right;

    [Header("运行时状态（不保存到 SO 资产）")]
    [System.NonSerialized]
    public string chosenBranch; // null / "Left" / "Right"

    // ============================================================
    // 数据获取 — 根据等级和 chosenBranch 返回对应分支数据
    // ============================================================

    /// <summary>
    /// 获取指定等级对应的分支数据。
    /// Lv1 直接返回 lv1Data；Lv2/Lv3 根据 chosenBranch 返回 Left/Right。
    /// 未选分支时返回 null（Lv1 阶段）或对应侧数据。
    /// </summary>
    public ActiveBranchData GetBranchData(int level)
    {
        switch (level)
        {
            case 1: return lv1Data;
            case 2:
                if (chosenBranch == "Left")  return lv2Left;
                if (chosenBranch == "Right") return lv2Right;
                return null; // 尚未选择分支
            case 3:
                if (chosenBranch == "Left")  return lv3Left;
                if (chosenBranch == "Right") return lv3Right;
                return null;
            default: return null;
        }
    }

    /// <summary>
    /// 获取指定等级对应的图标。优先用分支自身的 icon，为空则回退到根 SkillData.icon。
    /// </summary>
    public Sprite GetIconForLevel(int level)
    {
        var branch = GetBranchData(level);
        if (branch != null && branch.icon != null) return branch.icon;
        return icon; // 根 SkillData.icon
    }

    /// <summary>
    /// 检查指定分支是否已被另一侧锁定（级联锁定规则）
    /// </summary>
    public bool IsBranchLocked(string branch)
    {
        if (string.IsNullOrEmpty(chosenBranch)) return false;
        return chosenBranch != branch;
    }

    // ============================================================
    // 分支数据子结构
    // ============================================================

    /// <summary>
    /// 单个分支节点的技能参数 — 每个 Lv 每个分支一组独立参数
    /// </summary>
    [System.Serializable]
    public class ActiveBranchData
    {
        [Tooltip("分支/等级显示名称（如「散射弹幕」「弹幕风暴」）")]
        public string branchName;

        [Tooltip("分支图标（可选，为空则使用根SkillData.icon）")]
        public Sprite icon;

        [Tooltip("伤害值")]
        public float damage;

        [Tooltip("冷却时间（秒）")]
        public float cooldown;

        [Tooltip("法力消耗")]
        public float manaCost;

        [TextArea(2, 4)]
        [Tooltip("技能效果描述（分支选择弹窗中显示）")]
        public string description;

        // 后续可按需扩展：range / projectileCount / pierceCount / knockback 等
    }
}
