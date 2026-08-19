using UnityEngine;

/// <summary>
/// 元素效果判定入口（技能组阶段 1）。按 info.element 分流：
/// - Fire   ：不在本类判定 —— 在 PlayerCombat.RollCrit 内作为暴击仲裁候选（避免二次判定）。
/// - Water  ：10% 概率无视护甲 —— 在 CombatResolver 护甲步骤处理（决策 D11），本类不重复判定。
/// - Thunder：10% 概率在 defender 位置生成落雷（ThunderStrike，决策 D8）。
///
/// 防递归纪律（手册 11.3）：任何元素衍生伤害构造 DamageInfo 时显式设
/// canTriggerElementProc=false + element=None，除非设计明确要求继承 ——
/// 落雷自身命中 enemy 不得再触发第二次雷（验收点 4）。
/// </summary>
public static class ElementProc
{
    /// <summary>元素 proc 触发概率（测试期 100% 必定触发;数值调优时改回 0.1f,决策 D10）</summary>
    public const float ProcChance = 1f;

    /// <summary>
    /// 元素 proc 主入口 — 由 CombatResolver.Resolve 在 ApplyDamage 之后、OnHitBy 之前调用。
    /// </summary>
    public static void TryProc(DamageInfo info, ICombatant defender)
    {
        // 防递归：衍生伤害显式 canTriggerElementProc=false，直接短路
        if (!info.canTriggerElementProc) return;
        if (defender == null) return;

        switch (info.element)
        {
            case ElementType.Water:
                // 护甲跳过已在 CombatResolver 护甲步骤完成，ElementProcEvent 已随判定触发
                break;

            case ElementType.Thunder:
                if (Random.value < ProcChance)
                {
                    EventBus.Trigger(new ElementProcEvent(ElementType.Thunder, (Vector2)defender.Transform.position));
                    ThunderStrike.SpawnAt(info, defender);
                }
                break;
        }
    }
}
