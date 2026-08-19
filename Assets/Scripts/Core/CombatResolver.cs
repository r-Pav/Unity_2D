using UnityEngine;

/// <summary>
/// 战斗结算器 — 所有伤害交互的唯一入口
/// 替代现有 PlayerCombat.OnMeleeHitFrame 中直接调 enemy.TakeDamageFrom 的模式
/// </summary>
public static class CombatResolver
{
    /// <summary>
    /// 通用攻击结算
    /// 1. 闪避判定（受击方）
    /// 2. 格挡/弹反判定（受击方）
    /// 3. 护甲/减伤计算（受击方）— 水元素 proc 10% 概率跳过护甲（决策 D11）
    /// 4. 韧性/霸体判定（受击方 PoiseComponent）— Thunder_Strike 跳过韧性累计（决策 D8）
    /// 5. 击退施加（PoiseComponent 只做霸体累计/免疫判定，击退由调用方 Knockback 决定）
    /// 6. 元素 proc（ApplyDamage 之后、OnHitBy 之前）
    /// 7. FSM 状态推送（受击 → Hurt/AirHurt/Stun）
    /// 8. 事件触发
    /// </summary>
    public static float Resolve(ICombatant attacker, ICombatant defender, DamageInfo info)
    {
        if (!defender.CanBeDamaged) return 0f;
        if (defender.TryDodge(info)) return 0f;          // 闪避
        if (defender.TryParry(attacker, info)) return 0f; // 弹反

        // 水元素 proc：10% 概率本次跳过护甲（决策 D11；依赖 B1 enemy 护甲落地后才有实际效果）
        bool waterProc = info.canTriggerElementProc
            && info.element == ElementType.Water
            && Random.value < ElementProc.ProcChance;
        if (!waterProc)
            info.amount = defender.ApplyArmor(info.amount);   // 护甲
        else
            EventBus.Trigger(new ElementProcEvent(ElementType.Water, (Vector2)defender.Transform.position));
        info.amount = defender.ApplyReduction(info.amount); // 减伤
        if (info.amount <= 0f) return 0f;

        // 韧性/霸体：RegisterHit 只推进霸体累计/触发；霸体激活期间免疫击退。
        // 触发霸体的这一击：immuneBefore=false（触发前）→ 仍击退，与原设计一致。
        // Thunder_Strike（落雷）跳过韧性累计 → 霸体也强制硬直（决策 D8，硬直在 OnHitBy 落雷分支）。
        bool isThunderStrike = info.attackLabel == ThunderStrike.AttackLabel;
        if (isThunderStrike)
        {
            // 落雷：不走韧性/霸体判定；若配了击退则直接施加（当前落雷无击退，Knockback.None）
            if (info.knockback.force > 0f)
                defender.ApplyKnockback(info.knockback);
        }
        else if (defender.Poise != null)
        {
            bool immuneBefore = defender.Poise.IsPoiseActive;
            defender.Poise.RegisterHit(info.attackLabel);
            if (!immuneBefore && info.knockback.force > 0f)
                defender.ApplyKnockback(info.knockback);
        }
        else if (info.knockback.force > 0f)
        {
            defender.ApplyKnockback(info.knockback);
        }

        defender.ApplyDamage(info);  // 扣血+VFX+事件

        // 伤害结算事件（技能组阶段 5,伤害统计窗口订阅）— finalAmount = info.amount 已被护甲/减伤修改,用最终值
        EventBus.Trigger(new DamageDealtEvent(attacker, defender, info.amount));

        ElementProc.TryProc(info, defender); // 元素 proc（落雷衍生伤害 canTriggerElementProc=false 在此短路，防递归）
        defender.OnHitBy(info);      // 状态机推送 Hurt/AirHurt/Stun
        return info.amount;
    }
}
