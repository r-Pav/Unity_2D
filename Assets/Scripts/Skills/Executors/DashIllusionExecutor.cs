using UnityEngine;

/// <summary>
/// 树 B B-01 线（冲刺留嘲讽幻象）执行器（阶段 4）— 非 ISkillExecutor：
/// 树 B 是「升级解锁被动型」，不注册激活执行器（注册表不处理树 B，behaviorId 留空）。
/// 订阅两个事件（与 DashUpgradeExecutor 同款 RuntimeInitializeOnLoadMethod 模式）：
///   a. SkillLevelChangedEvent：TreeB_Dash 升到 Lv2 且 chosenBranch=="Left" → 启用「冲刺留嘲讽幻象」；
///      Lv3 且 branch=="Left" → 启用大范围 + DoT（参数从 lv2Left/lv3Left 资产读取）。
///   b. DashEndedEvent：启用后每次冲刺结束在原位生成嘲讽幻象（未启用忽略）。
/// 读档恢复：SkillManager.SetSlot 触发 SkillLevelChangedEvent → 自动重新应用（手册 8.1）。
/// 分支判断：Resources.Load&lt;ActiveSkillData&gt;("Skills/Active/Skill_Active_E") 与槽位共享同一 SO 实例
/// （BranchUpgradeSystem 先写 chosenBranch 再发事件，此处读到的是最新分支）。
/// </summary>
public class DashIllusionExecutor
{
    private const string TreeBDashSkillName = "TreeB_Dash";
    private const string TreeBAssetPath = "Skills/Active/Skill_Active_E";

    // 幻象寿命 / DoT 间隔（数值调优项，手册 11.6：幻象寿命与嘲讽时长待统一调）
    private const float IllusionLifetime = 5f;
    private const float DotInterval = 1f;

    /// <summary>是否已启用「冲刺留嘲讽幻象」（Lv2+Left 后 true；Init 时复位）</summary>
    private static bool dashIllusionEnabled;

    /// <summary>生成参数（Lv2/Lv3 由 SkillLevelChangedEvent 从分支资产读取）</summary>
    private static TauntIllusionConfig cachedConfig;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Init()
    {
        // 复位运行时状态（防 domain reload 关闭时上次运行的残留标记）
        dashIllusionEnabled = false;
        cachedConfig = default;

        // 与 DashUpgradeExecutor 同款：静态订阅，场景切换 EventBus.Clear() 后由本入口重新订阅
        EventBus.Subscribe<SkillLevelChangedEvent>(OnSkillLevelChanged);
        EventBus.Subscribe<DashEndedEvent>(OnDashEnded);
    }

    private static void OnSkillLevelChanged(SkillLevelChangedEvent e)
    {
        if (e.skillName != TreeBDashSkillName || e.newLevel < 2) return;

        // 分支判断：左分支（B-01 嘲讽幻象线）。chosenBranch 是 SO 共享实例运行时字段，槽位升级时已写入
        var tree = Resources.Load<ActiveSkillData>(TreeBAssetPath);
        if (tree == null || tree.chosenBranch != "Left") return;

        // 参数一律读分支资产（手册 0.5.4：执行器读 ActiveBranchData，不读顶层 cooldown/manaCost）
        var branch = tree.GetBranchData(e.newLevel); // Lv2 → lv2Left；Lv3 → lv3Left
        if (branch == null) return;

        dashIllusionEnabled = true;
        cachedConfig = new TauntIllusionConfig
        {
            tauntRadius = branch.range > 0f ? branch.range : 3f,      // lv3 资产 range 变大（大范围）
            tauntDuration = branch.duration > 0f ? branch.duration : 4f,
            lifetime = IllusionLifetime,
            dotEnabled = e.newLevel >= 3,                             // lv3B-01：解锁 DoT
            dotDamage = branch.damage,                                // DoT 单次伤害（lv3Left.damage）
            dotInterval = DotInterval
        };
    }

    private static void OnDashEnded(DashEndedEvent e)
    {
        if (!dashIllusionEnabled) return; // 未解锁 B-01（或未选左分支）：忽略

        // 原地生成嘲讽幻象（决策 N3 顶替逻辑在管理器内）
        var mgr = IllusionManager.EnsureInstance();
        if (mgr == null) return;
        mgr.SpawnTauntIllusion(e.position, cachedConfig);
    }
}
