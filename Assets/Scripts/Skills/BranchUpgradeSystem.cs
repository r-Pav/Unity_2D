using UnityEngine;

/// <summary>
/// [P3] 主动技能分支升级系统
/// 职责：分支选择流程、升级消耗校验、路径锁定
/// 作为 SkillManager 的子模块（[System.Serializable]），不挂独立组件
/// 
/// 升级流程：
///   Lv1→Lv2：消耗 1 技能点 → 设置 pending 状态 → UI 弹窗供玩家选择分支
///   Lv2→Lv3：消耗 2 技能点 → 根据已记录的 chosenBranch 直接升级
///   分支选择不可逆，选后另一侧级联锁定
/// </summary>
[System.Serializable]
public class BranchUpgradeSystem
{
    // ============================================================
    // 配置参数
    // ============================================================

    [Header("升级消耗")]
    [Tooltip("Lv1→Lv2 消耗技能点数")]
    [SerializeField] private int pointCostToLv2 = 1;

    [Tooltip("Lv2→Lv3 消耗技能点数")]
    [SerializeField] private int pointCostToLv3 = 2;

    // ============================================================
    // 运行时引用（由 SkillManager.Initialize 注入）
    // ============================================================

    private SkillManager skillManager;
    private SkillPointManager pointManager;
    private int[] slotLevels; // 指向 SkillManager 的 slotLevels 数组
    private SkillSlot[] skillSlots; // 指向 SkillManager 的 skillSlots 数组

    // ============================================================
    // 运行时状态
    // ============================================================

    /// <summary>等待分支选择的槽位索引（-1 = 无等待）</summary>
    private int pendingSlotIndex = -1;

    // ============================================================
    // 公开属性
    // ============================================================

    /// <summary>是否有分支选择等待中（UI 轮询此状态决定是否弹窗）</summary>
    public bool IsWaitingForBranchChoice => pendingSlotIndex >= 0;

    /// <summary>等待分支选择的槽位索引</summary>
    public int PendingSlotIndex => pendingSlotIndex;

    // ============================================================
    // 初始化
    // ============================================================

    /// <summary>
    /// 由 SkillManager 在 Awake 中调用，注入依赖
    /// </summary>
    public void Initialize(
        SkillManager sm,
        SkillPointManager spm,
        int[] levels,
        SkillSlot[] slots)
    {
        skillManager = sm;
        pointManager = spm;
        slotLevels = levels;
        skillSlots = slots;
    }

    // ============================================================
    // 公开接口 — 升级入口
    // ============================================================

    /// <summary>解锁 Lv1：从未解锁状态进入基础技能。</summary>
    public bool UnlockLevel1(int slotIndex)
    {
        if (!TryGetActiveSkill(slotIndex, out ActiveSkillData data) || slotLevels[slotIndex] != 0)
            return false;
        if (!TrySpend(pointCostToLv2, data.skillName))
            return false;

        ApplyLevelUp(slotIndex, 1);
        Debug.Log($"[BranchUpgrade] {data.skillName} 解锁 Lv1");
        return true;
    }

    /// <summary>选择并解锁 Lv2 分支。</summary>
    public bool ChooseLevel2(int slotIndex, string branch)
    {
        if (!TryGetActiveSkill(slotIndex, out ActiveSkillData data) || slotLevels[slotIndex] != 1)
            return false;
        if (!IsValidBranch(branch) || !TrySpend(pointCostToLv2, data.skillName))
            return false;

        data.chosenBranch = branch;
        ApplyLevelUp(slotIndex, 2);
        EventBus.Trigger(new BranchChosenEvent(data.skillName, slotIndex, branch));
        Debug.Log($"[BranchUpgrade] {data.skillName} 选择分支 [{branch}] → Lv2");
        return true;
    }

    /// <summary>沿已选择的分支升级到 Lv3。</summary>
    public bool UpgradeLevel3(int slotIndex, string branch)
    {
        if (!TryGetActiveSkill(slotIndex, out ActiveSkillData data) || slotLevels[slotIndex] != 2)
            return false;
        if (!IsValidBranch(branch) || data.chosenBranch != branch)
            return false;
        if (data.GetBranchData(3) == null || !TrySpend(pointCostToLv3, data.skillName))
            return false;

        ApplyLevelUp(slotIndex, 3);
        Debug.Log($"[BranchUpgrade] {data.skillName} → Lv3 ({branch})");
        return true;
    }

    /// <summary>兼容旧入口，按当前等级推进。</summary>
    public bool TryUpgrade(int slotIndex)
    {
        if (!TryGetActiveSkill(slotIndex, out ActiveSkillData data)) return false;
        return slotLevels[slotIndex] switch
        {
            0 => UnlockLevel1(slotIndex),
            1 => false,
            2 => UpgradeLevel3(slotIndex, data.chosenBranch),
            _ => WarnAlreadyMax(data)
        };
    }

    /// <summary>Lv1→Lv2：消耗点数，设置 pending 等待分支选择</summary>
    private bool TryUpgradeToLevel2(int slotIndex, ActiveSkillData data)
    {
        // 已在等待分支选择，拒绝重复触发
        if (IsWaitingForBranchChoice && pendingSlotIndex == slotIndex)
        {
            Debug.LogWarning($"[BranchUpgrade] {data.skillName} 已在等待分支选择");
            return false;
        }

        if (!pointManager.CanSpend(pointCostToLv2))
        {
            Debug.LogWarning($"[BranchUpgrade] 技能点不足，需要 {pointCostToLv2} 点");
            return false;
        }

        pointManager.SpendPoints(pointCostToLv2);
        pendingSlotIndex = slotIndex;
        Debug.Log($"[BranchUpgrade] {data.skillName} Lv1→Lv2 等待分支选择...");
        return true;
    }

    /// <summary>Lv2→Lv3：校验分支已选+数据完整，消耗点数完成升级</summary>
    private bool TryUpgradeToLevel3(int slotIndex, ActiveSkillData data)
    {
        if (string.IsNullOrEmpty(data.chosenBranch))
        {
            Debug.LogWarning($"[BranchUpgrade] {data.skillName} 分支尚未选择，无法升级 Lv3");
            return false;
        }

        var lv3Data = data.GetBranchData(3);
        if (lv3Data == null)
        {
            Debug.LogWarning($"[BranchUpgrade] {data.skillName} Lv3 分支数据缺失");
            return false;
        }

        if (!pointManager.CanSpend(pointCostToLv3))
        {
            Debug.LogWarning($"[BranchUpgrade] 技能点不足，需要 {pointCostToLv3} 点");
            return false;
        }

        pointManager.SpendPoints(pointCostToLv3);
        ApplyLevelUp(slotIndex, 3);
        Debug.Log($"[BranchUpgrade] {data.skillName} → Lv3 ({data.chosenBranch})");
        return true;
    }

    /// <summary>预期外的等级（不可达），记录警告</summary>
    private static bool WarnAlreadyMax(ActiveSkillData data)
    {
        Debug.LogWarning($"[BranchUpgrade] {data.skillName} 已达 Lv3（最大等级）");
        return false;
    }

    /// <summary>
    /// 玩家在分支选择弹窗中选择分支后的回调（由 UI 调用）
    /// </summary>
    /// <param name="slotIndex">技能槽位索引</param>
    /// <param name="branch">"Left" 或 "Right"</param>
    /// <returns>是否成功确认</returns>
    public bool OnBranchChosen(int slotIndex, string branch)
    {
        // 校验 pending 状态
        if (slotIndex != pendingSlotIndex)
        {
            Debug.LogWarning($"[BranchUpgrade] OnBranchChosen 槽位不匹配: 期望 {pendingSlotIndex}, 收到 {slotIndex}");
            return false;
        }

        if (branch != "Left" && branch != "Right")
        {
            Debug.LogWarning($"[BranchUpgrade] 无效分支: {branch}（只能为 Left 或 Right）");
            return false;
        }

        var data = skillSlots[slotIndex]?.data as ActiveSkillData;
        if (data == null)
        {
            pendingSlotIndex = -1;
            return false;
        }

        // 记录分支选择（不可逆）
        data.chosenBranch = branch;

        // 完成 Lv2 升级
        ApplyLevelUp(slotIndex, 2);

        Debug.Log($"[BranchUpgrade] {data.skillName} 选择分支 [{branch}] → Lv2");

        // 触发分支确认事件（UI 可订阅以关闭弹窗、刷新技能树等）
        EventBus.Trigger(new BranchChosenEvent(data.skillName, slotIndex, branch));

        // 清除 pending 状态
        pendingSlotIndex = -1;

        return true;
    }

    /// <summary>
    /// 检查指定分支是否已被另一侧锁定
    /// </summary>
    public bool IsBranchLocked(int slotIndex, string branch)
    {
        var data = skillSlots[slotIndex]?.data as ActiveSkillData;
        return data != null && data.IsBranchLocked(branch);
    }

    /// <summary>
    /// 获取指定槽位升级所需技能点数
    /// </summary>
    public int GetUpgradeCost(int slotIndex)
    {
        int currentLevel = (slotIndex >= 0 && slotIndex < slotLevels.Length)
            ? slotLevels[slotIndex] : 0;

        return currentLevel switch
        {
            0 => pointCostToLv2,
            1 => pointCostToLv2,
            2 => pointCostToLv3,
            _ => 0
        };
    }

    /// <summary>
    /// 查询指定槽位是否可升级（满足点数且未达最大等级且非锁定分支）
    /// </summary>
    public bool CanUpgrade(int slotIndex)
    {
        if (!TryGetActiveSkill(slotIndex, out ActiveSkillData data)) return false;

        int currentLevel = slotLevels[slotIndex];
        int cost = GetUpgradeCost(slotIndex);
        if (currentLevel == 0) cost = pointCostToLv2;
        if (currentLevel >= data.maxLevel || pointManager == null) return false;
        return pointManager.CanSpend(cost);
    }

    // ============================================================
    // 内部方法
    // ============================================================

    private bool TryGetActiveSkill(int slotIndex, out ActiveSkillData data)
    {
        data = null;
        if (skillSlots == null || slotLevels == null || slotIndex < 0 ||
            slotIndex >= skillSlots.Length || slotIndex >= slotLevels.Length)
            return false;

        data = skillSlots[slotIndex]?.data as ActiveSkillData;
        return data != null;
    }

    private bool TrySpend(int amount, string skillName)
    {
        if (pointManager != null && pointManager.CanSpend(amount))
            return pointManager.SpendPoints(amount);

        Debug.LogWarning($"[BranchUpgrade] {skillName} 技能点不足，需要 {amount} 点");
        return false;
    }

    private static bool IsValidBranch(string branch) => branch == "Left" || branch == "Right";

    /// <summary>
    /// 执行实际的等级变更：设置运行时等级并触发事件。
    /// </summary>
    private void ApplyLevelUp(int slotIndex, int newLevel)
    {
        slotLevels[slotIndex] = newLevel;

        EventBus.Trigger(new SkillLevelChangedEvent(
            skillSlots[slotIndex]?.data?.skillName ?? "",
            slotIndex,
            newLevel
        ));

        // 通知 SkillManager 刷新联动（通过公开方法）
        skillManager.RefreshSynergy();
    }
}
