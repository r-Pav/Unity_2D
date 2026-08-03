// ============================================================
// BossSkillType — Boss 特殊技能类型枚举
// ============================================================

/// <summary>
/// Boss 特殊技能类型。
/// Charge/Slam/Shockwave 为 Boss 专属机制，由 BossSkillSlots 内部实现执行逻辑。
/// MeleeWrap/RangedWrap 复用现有 IEnemyAttack 组件。
/// Combo 引用其他 BossAttackSO 实现多段连击。
/// </summary>
public enum BossSkillType
{
    /// <summary>冲撞：Boss 位移 + 持续碰撞判定</summary>
    Charge,

    /// <summary>砸地 AOE：以 Boss 为中心的圆形范围伤害</summary>
    Slam,

    /// <summary>地面波：发射沿地面飞行的 ShockwaveProjectile</summary>
    Shockwave,

    /// <summary>近战包装：复用现有 EnemyMeleeAttack 组件</summary>
    MeleeWrap,

    /// <summary>远程包装：复用现有 EnemyRangedAttack 组件</summary>
    RangedWrap,

    /// <summary>连击组合：引用子 BossAttackSO[] 实现多段攻击</summary>
    Combo
}
