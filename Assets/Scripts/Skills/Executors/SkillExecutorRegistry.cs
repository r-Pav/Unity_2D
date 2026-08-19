using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 技能执行器注册表（阶段 0.5 框架底座,决策 N7）— 挂在 Player 上。
/// 统一订阅 SkillActivatedEvent 并按「行为标识 / 技能名」分发,新增树/合成技能不改 SkillManager 主流程。
///
/// 双通道分发：
///   ① ActiveSkillData 树：e.skillName → 树缓存 → GetBranchData(e.skillLevel) → branch.behaviorId → behaviorId 字典执行
///   ② 未命中树（CombinationSkillData 等合成产物）：按 e.skillName 查 skillName 字典执行
///
/// 执行器注册：静态 Register(ISkillExecutor)/Unregister（可在注册表挂载前调用,自动缓冲）。
/// 本阶段注册表内无任何 executor（阶段 2 起才注册）,框架空跑不报错。
/// 未识别 behaviorId/skillName：Debug.LogWarning 一次并静默返回,不抛异常。
/// </summary>
public class SkillExecutorRegistry : MonoBehaviour
{
    // ============================================================
    // 静态注册入口
    // ============================================================

    private static SkillExecutorRegistry _instance;
    private static readonly List<ISkillExecutor> pendingRegistrations = new();

    /// <summary>注册执行器（树执行器按 BehaviorId 分发;合成技能执行器 BehaviorId = 产物 skillName,按 skillName 分发）</summary>
    public static void Register(ISkillExecutor executor)
    {
        if (executor == null) return;
        if (_instance != null)
            _instance.AddExecutor(executor);
        else
            pendingRegistrations.Add(executor); // 注册表尚未挂载,先缓冲,OnEnable 时冲刷
    }

    /// <summary>注销执行器</summary>
    public static void Unregister(ISkillExecutor executor)
    {
        if (executor == null) return;
        if (_instance != null)
            _instance.RemoveExecutor(executor);
        else
            pendingRegistrations.Remove(executor);
    }

    // ============================================================
    // 二次激活挂起（技能组阶段 5 传送弹：CD 期间允许再按技能键触发传送）
    // ============================================================

    private static readonly HashSet<string> pendingReactivationSkills = new();

    /// <summary>
    /// 设置技能「二次激活挂起」标记：true = 有未使用的传送弹，CD 期间允许再次触发；
    /// false = 清除。由 TeleportBoltExecutor 发射时置位、TeleportBolt 回池时清除（含玩家死亡）。
    /// </summary>
    public static void SetPendingReactivation(string skillName, bool pending)
    {
        if (string.IsNullOrEmpty(skillName)) return;
        if (pending) pendingReactivationSkills.Add(skillName);
        else pendingReactivationSkills.Remove(skillName);
    }

    /// <summary>该技能是否处于二次激活挂起（SkillManager 冷却检查据此放行）</summary>
    public static bool HasPendingReactivation(string skillName)
        => !string.IsNullOrEmpty(skillName) && pendingReactivationSkills.Contains(skillName);

    // ============================================================
    // 实例字典（behaviorId → executor;skillName → executor）
    // ============================================================

    private readonly Dictionary<string, ISkillExecutor> behaviorExecutors = new();
    private readonly Dictionary<string, ISkillExecutor> skillNameExecutors = new();

    // skillName → ActiveSkillData 树缓存（Resources.LoadAll 构建,路径与 CombinationCraftSystem L48 一致）
    private Dictionary<string, ActiveSkillData> activeTreeCache;
    // skillName → CombinationSkillData 组合技能缓存（通道②数据来源,同样走 Resources/Skills/Combo）
    private Dictionary<string, CombinationSkillData> comboCache;
    private bool cacheBuilt;

    // 已警告过的 key（避免未识别技能反复刷日志）
    private readonly HashSet<string> warnedKeys = new();

    // ============================================================
    // 生命周期
    // ============================================================

    private void OnEnable()
    {
        if (_instance != null && _instance != this)
            Debug.LogWarning("[SkillExecutorRegistry] 检测到多个注册表实例,将使用最后启用的一个");
        _instance = this;

        // 冲刷注册表挂载前的缓冲注册
        if (pendingRegistrations.Count > 0)
        {
            foreach (var executor in pendingRegistrations)
                AddExecutor(executor);
            pendingRegistrations.Clear();
        }

        EventBus.Subscribe<SkillActivatedEvent>(OnSkillActivated);
    }

    private void OnDisable()
    {
        EventBus.Unsubscribe<SkillActivatedEvent>(OnSkillActivated);
        if (_instance == this) _instance = null;
    }

    // ============================================================
    // 分发逻辑（双通道）
    // ============================================================

    private void OnSkillActivated(SkillActivatedEvent e)
    {
        // 通道①：命中 ActiveSkillData 树 → 分支 behaviorId 分发
        if (TryGetActiveTree(e.skillName, out ActiveSkillData tree))
        {
            ActiveSkillData.ActiveBranchData branch = tree.GetBranchData(e.skillLevel);
            if (branch == null) return; // 该等级无对应分支（如未选分支）,无行为

            string behaviorId = branch.behaviorId;
            if (string.IsNullOrEmpty(behaviorId)) return; // 未配 behaviorId = 该分支无行为（旧资产兼容,空跑不崩溃）

            if (behaviorExecutors.TryGetValue(behaviorId, out var executor))
            {
                executor.Execute(e, tree, branch);
            }
            else
            {
                WarnOnce($"[SkillExecutorRegistry] 未识别行为标识 behaviorId='{behaviorId}' (skillName='{e.skillName}'),已跳过");
            }
            return;
        }

        // 通道②：未命中 ActiveSkillData（CombinationSkillData 等）→ 按 skillName 分发
        if (skillNameExecutors.TryGetValue(e.skillName, out var comboExecutor))
        {
            // data 传解析到的组合技能数据（未找到时回退 null,不抛异常）
            comboCache.TryGetValue(e.skillName ?? string.Empty, out var comboData);
            comboExecutor.Execute(e, comboData, null);
        }
        else
        {
            WarnOnce($"[SkillExecutorRegistry] 未识别技能 skillName='{e.skillName}',已跳过");
        }
    }

    // ============================================================
    // 注册 / 注销内部实现
    // ============================================================

    private void AddExecutor(ISkillExecutor executor)
    {
        string key = executor.BehaviorId;
        if (string.IsNullOrEmpty(key))
        {
            Debug.LogWarning("[SkillExecutorRegistry] 忽略 BehaviorId 为空的执行器");
            return;
        }

        EnsureActiveCache();

        // 分类：BehaviorId 命中任意树分支 → 树执行器（behaviorId 字典）;否则 → 按 skillName 分发（合成技能执行器）
        if (ContainsBehaviorId(key))
            behaviorExecutors[key] = executor;
        else
            skillNameExecutors[key] = executor;
    }

    private void RemoveExecutor(ISkillExecutor executor)
    {
        string key = executor.BehaviorId;
        if (behaviorExecutors.TryGetValue(key, out var be) && be == executor)
            behaviorExecutors.Remove(key);
        if (skillNameExecutors.TryGetValue(key, out var se) && se == executor)
            skillNameExecutors.Remove(key);
    }

    // ============================================================
    // ActiveSkillData 缓存
    // ============================================================

    private void EnsureActiveCache()
    {
        if (cacheBuilt) return;
        cacheBuilt = true;
        activeTreeCache = new Dictionary<string, ActiveSkillData>();

        // 路径与 CombinationCraftSystem L48 的 Resources.LoadAll 用法一致
        ActiveSkillData[] all = Resources.LoadAll<ActiveSkillData>("Skills/Active");
        foreach (var tree in all)
        {
            if (tree == null || string.IsNullOrEmpty(tree.skillName)) continue;
            if (!activeTreeCache.ContainsKey(tree.skillName))
                activeTreeCache[tree.skillName] = tree;
        }

        // 组合技能缓存（通道② data 来源）— 路径与 CombinationCraftSystem L48 的 Combo 加载一致
        comboCache = new Dictionary<string, CombinationSkillData>();
        CombinationSkillData[] allCombos = Resources.LoadAll<CombinationSkillData>("Skills/Combo");
        foreach (var combo in allCombos)
        {
            if (combo == null || string.IsNullOrEmpty(combo.skillName)) continue;
            if (!comboCache.ContainsKey(combo.skillName))
                comboCache[combo.skillName] = combo;
        }
    }

    private bool TryGetActiveTree(string skillName, out ActiveSkillData tree)
    {
        EnsureActiveCache();
        return activeTreeCache.TryGetValue(skillName ?? string.Empty, out tree);
    }

    /// <summary>判断 behaviorId 是否出现在任一树的任一分支（用于注册分类）</summary>
    private bool ContainsBehaviorId(string behaviorId)
    {
        foreach (var tree in activeTreeCache.Values)
        {
            if (TreeHasBehaviorId(tree.lv1Data, behaviorId)
                || TreeHasBehaviorId(tree.lv2Left, behaviorId)
                || TreeHasBehaviorId(tree.lv2Right, behaviorId)
                || TreeHasBehaviorId(tree.lv3Left, behaviorId)
                || TreeHasBehaviorId(tree.lv3Right, behaviorId))
                return true;
        }
        return false;
    }

    private static bool TreeHasBehaviorId(ActiveSkillData.ActiveBranchData branch, string behaviorId)
        => branch != null && branch.behaviorId == behaviorId;

    /// <summary>同一 key 只警告一次,防止未识别技能反复刷日志</summary>
    private void WarnOnce(string message)
    {
        if (warnedKeys.Add(message))
            Debug.LogWarning(message);
    }
}
