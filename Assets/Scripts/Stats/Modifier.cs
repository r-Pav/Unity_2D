using System;

/// <summary>
/// 修饰器类型枚举 — 百分比叠加 vs 数值叠加
/// </summary>
public enum ModifierType
{
    /// <summary>百分比修饰：最终公式中累计到 (1 + ΣPercent)</summary>
    Percent,
    /// <summary>数值修饰：最终公式中直接加到 ΣFlat</summary>
    Flat
}

/// <summary>
/// [P1] 属性修饰器数据模型 — 纯数据，不包含逻辑
/// 每个修饰器是一个独立单元，描述对某个属性的单次加成/减益
/// </summary>
[Serializable]
public class Modifier
{
    /// <summary>目标属性标识符（如 "maxHealth"、"moveSpeed"、"damageMultiplier"）</summary>
    public string targetStat;
    /// <summary>修饰值（正=加成，负=减益）</summary>
    public float value;
    /// <summary>修饰类型：Percent（百分比）或 Flat（数值）</summary>
    public ModifierType type;
    /// <summary>来源标识（如 "Passive_T1_HP"、"Weapon_Sword"、"Buff_Potion"）— 同 source 覆盖旧值</summary>
    public string source;
    /// <summary>叠加优先级（同 priority 组内叠加）</summary>
    public int priority;
    /// <summary>条件生效规则（可选），如「低血加防」仅在 HP≤30% 时生效。null 表示无条件生效</summary>
    public Func<bool> condition;

    public Modifier(string targetStat, float value, ModifierType type, string source,
        int priority = 0, Func<bool> condition = null)
    {
        this.targetStat = targetStat;
        this.value = value;
        this.type = type;
        this.source = source;
        this.priority = priority;
        this.condition = condition;
    }

    /// <summary>返回当前条件是否满足（无 condition 视为始终满足）</summary>
    public bool IsActive() => condition == null || condition();
}

/// <summary>
/// [P1] 属性 ID 常量 — 统一管理所有属性标识符，避免字符串硬编码
/// </summary>
public static class StatId
{
    // ── 已有属性（改造自 serialized field）──
    public const string MoveSpeed = "moveSpeed";
    public const string MaxHealth = "maxHealth";

    // ── P1 新增属性 ──
    /// <summary>伤害倍率（默认 1.0）</summary>
    public const string DamageMultiplier = "damageMultiplier";
    /// <summary>减伤率 [0~1]（默认 0.0）</summary>
    public const string DamageReduction = "damageReduction";
    /// <summary>暴击率 [0~1]（默认 0.0）</summary>
    public const string CritRate = "critRate";
    /// <summary>暴击伤害倍率 [0~∞]（默认 0.0，即暴击时额外伤害比例）</summary>
    public const string CritDamage = "critDamage";
    /// <summary>单次发射子弹数加成（Flat，默认 0）</summary>
    public const string ShotsPerClick = "shotsPerClick";
    /// <summary>攻击间隔缩短比例（Percent，默认 0.0，填 10=缩短10%）</summary>
    public const string AttackInterval = "attackInterval";
    /// <summary>攻击速度倍率（默认 1.0）</summary>
    public const string AttackSpeedMultiplier = "attackSpeedMultiplier";
    /// <summary>闪避率 [0~1]（默认 0.0）</summary>
    public const string DodgeChance = "dodgeChance";
    /// <summary>控制减免 [0~1]（默认 0.0）</summary>
    public const string ControlReduction = "controlReduction";
    /// <summary>法力消耗倍率（默认 1.0）</summary>
    public const string ManaCostMultiplier = "manaCostMultiplier";

    // ── P2 新增属性 ──
    /// <summary>法力上限（默认按角色配置）</summary>
    public const string MaxMana = "maxMana";
    /// <summary>法力恢复倍率（默认 1.0）</summary>
    public const string ManaRegen = "manaRegen";
    /// <summary>冷却时间倍率（默认 1.0，值<1 表示 CD 缩短）</summary>
    public const string CooldownMultiplier = "cooldownMultiplier";
    /// <summary>硬直减免 [0~1]（默认 0.0，受击硬直时间 × (1-值)）</summary>
    public const string StunReduction = "stunReduction";

    // ── Phase2 属性系统新增 ──
    /// <summary>力量（主属性，int）</summary>
    public const string Str = "str";
    /// <summary>智力（主属性，int）</summary>
    public const string Int = "int";
    /// <summary>敏捷（主属性，int）</summary>
    public const string Agi = "agi";
    /// <summary>生命恢复/秒（派生，float）</summary>
    public const string HpRegen = "hpRegen";
    /// <summary>护甲（派生Bonus，每10力+1，float）</summary>
    public const string Armor = "armor";
    /// <summary>技能增强 [0~1]（派生Bonus，每10智+5%，float）</summary>
    public const string SkillEnhance = "skillEnhance";
    /// <summary>攻击速度（派生，基数100+AGI加成，float，使用时÷100转倍率）</summary>
    public const string AttackSpeed = "attackSpeed";
    /// <summary>冷却缩减 [0~1]（派生Bonus，每10敏+5%，float）</summary>
    public const string CooldownReduction = "cooldownReduction";
}
