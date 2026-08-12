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
    /// 3. 护甲/减伤计算（受击方）
    /// 4. 韧性/霸体判定（受击方 PoiseComponent）
    /// 5. 击退施加（通过 PoiseComponent.RegisterHit 返回是否击退）
    /// 6. FSM 状态推送（受击 → Hurt/AirHurt/Stun）
    /// 7. 事件触发
    /// </summary>
    public static float Resolve(ICombatant attacker, ICombatant defender, DamageInfo info)
    {
        if (!defender.CanBeDamaged) return 0f;
        if (defender.TryDodge(info)) return 0f;          // 闪避
        if (defender.TryParry(attacker, info)) return 0f; // 弹反
        info.amount = defender.ApplyArmor(info.amount);   // 护甲
        info.amount = defender.ApplyReduction(info.amount); // 减伤
        if (info.amount <= 0f) return 0f;

        // 韧性/霸体：RegisterHit 只推进霸体累计/触发；霸体激活期间免疫击退。
        // 非重击不再否决击退——击退力度由调用方 info.knockback 决定（武器每击配置的 x/y 生效）。
        // 触发霸体的这一击：immuneBefore=false（触发前）→ 仍击退，与原设计一致。
        if (defender.Poise != null)
        {
            bool immuneBefore = defender.Poise.IsPoiseActive;
            defender.Poise.RegisterHit(info.attackLabel, out _);
            if (!immuneBefore && info.knockback.force > 0f)
                defender.ApplyKnockback(info.knockback);
        }
        else if (info.knockback.force > 0f)
        {
            defender.ApplyKnockback(info.knockback);
        }

        defender.ApplyDamage(info);  // 扣血+VFX+事件
        defender.OnHitBy(info);      // 状态机推送 Hurt/AirHurt/Stun
        return info.amount;
    }
}
