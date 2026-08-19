using UnityEngine;

/// <summary>
/// 伤害统计窗口（技能组阶段 5,lv3A-02 传送回血与后续合成技能的「伤害 50% 回血」共用）。
///
/// Begin() 后订阅 DamageDealtEvent，累计 source 归属 player 的一切伤害（含魔法弹/分裂弹/
/// 幻象攻击/落雷/传送弹；落雷虽 canTriggerElementProc=false 仍是 player 伤害,天然计入,手册 5.2 口径）。
/// End() 返回累计总量并退订；重复 Begin() 安全（旧窗口先 End 丢弃，防重复订阅/串统计）。
/// </summary>
public class DamageWindow
{
    private float total;
    private bool active;

    /// <summary>开启统计窗口（重复开启：先关闭旧窗口，丢弃旧数据）</summary>
    public void Begin()
    {
        if (active) End();
        total = 0f;
        active = true;
        EventBus.Subscribe<DamageDealtEvent>(OnDamageDealt);
    }

    /// <summary>结束统计窗口并返回累计总量（未开启时返回 0）</summary>
    public float End()
    {
        if (!active) return total;
        active = false;
        EventBus.Unsubscribe<DamageDealtEvent>(OnDamageDealt);
        return total;
    }

    private void OnDamageDealt(DamageDealtEvent e)
    {
        if (IsPlayerSource(e.source))
            total += e.finalAmount;
    }

    /// <summary>
    /// source 归属 player 判定：当前项目 player 侧伤害一律以 PlayerHealth 作为 ICombatant source
    /// （近战/魔法弹/冲刺/落雷/幻象 DoT/传送弹均 GetComponent&lt;ICombatant&gt;() 得到 PlayerHealth），
    /// 统一用组件类型识别，天然涵盖 player 本体与 player 侧衍生来源。
    /// 用类型判断而非 source.GameObject：敌人已销毁时访问 GameObject 抛 MissingReferenceException。
    /// </summary>
    private static bool IsPlayerSource(ICombatant source) => source is PlayerHealth;
}
