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
        dash.SetDashDamage(GetLv1Damage());
    }

    /// <summary>树 B lv1 分支伤害值(数值一律读 ActiveBranchData,手册 0.5.4)</summary>
    private static float GetLv1Damage()
    {
        var tree = Resources.Load<ActiveSkillData>(TreeBAssetPath);
        return tree != null && tree.lv1Data != null ? tree.lv1Data.damage : 0f;
    }
}
