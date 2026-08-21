using UnityEngine;

/// <summary>
/// 树 B 冲刺升级执行器(阶段 3 修正,决策 N1/D3/N6;saika 确认:树 B 跟随 Shift,不用 E 键激活)
/// 订阅 SkillLevelChangedEvent:树 B(TreeB_Dash)升到 Lv1 及以上时自动解锁:
///   1. 冲刺充能 1 → 2(PlayerDash.UnlockExtraCharge,内部标记幂等)
///   2. 启用冲刺伤害(PlayerDash.EnableDashDamage + SetDashDamage 注入 lv1Data.damage)
/// 冲刺本体在 Shift(PlayerDashState),本执行器只负责解锁,不依赖技能激活事件(E 键)。
/// 读档恢复:SkillManager.SetSlot 已补发 SkillLevelChangedEvent(阶段 3 程序侧修正,LevelUp 之外读档唯一入口)→ 自动重新应用(手册 8.1)。
/// 事件无 GameObject 引用,通过 PlayerController.Instance 获取 PlayerDash(与 EnemyControllerBase 同款获取方式)。
/// </summary>
public class DashUpgradeExecutor
{
    private const string TreeBDashSkillName = "TreeB_Dash";
    private const string TreeBAssetPath = "Skills/Active/Skill_Active_E";

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Init()
    {
        // EventBus.Clear() 在场景切换时清空订阅,AfterSceneLoad 重新订阅;同场景只触发一次
        EventBus.Subscribe<SkillLevelChangedEvent>(OnSkillLevelChanged);
    }

    private static void OnSkillLevelChanged(SkillLevelChangedEvent e)
    {
        if (e.skillName != TreeBDashSkillName || e.newLevel < 1) return;

        PlayerDash dash = PlayerController.Instance != null
            ? PlayerController.Instance.GetComponent<PlayerDash>()
            : null;
        if (dash == null) return;

        // 幂等:重复升级/读档恢复只是重新应用相同解锁,不会重复 +1 充能
        dash.UnlockExtraCharge();
        dash.EnableDashDamage();
        dash.SetDashDamage(GetBranchParam(b => b.damage, e.newLevel, 0f));
        // [2026-08-21] 冲刺距离/总时长随技能等级从分支数据注入(速度 = 距离 ÷ 时长 自动推导);
        // 当前等级分支未配置(0)时回退 lv1Data,仍为 0 则回退 PlayerDash 序列化
        dash.SetDashParams(
            GetBranchParam(b => b.dashDistance, e.newLevel, 0f),
            GetBranchParam(b => b.dashDuration, e.newLevel, 0f));
    }

    /// <summary>读树B 指定等级分支字段;分支缺失/未选分支时回退 lv1Data;再缺失返回 fallback</summary>
    private static float GetBranchParam(System.Func<ActiveSkillData.ActiveBranchData, float> selector,
        int level, float fallback)
    {
        var tree = Resources.Load<ActiveSkillData>(TreeBAssetPath);
        if (tree == null) return fallback;
        var branch = tree.GetBranchData(level) ?? tree.lv1Data; // 未选分支(理论不出现)回退基础
        if (branch == null) return fallback;
        float v = selector(branch);
        return v > 0f ? v : fallback;
    }
}
