using UnityEngine;

/// <summary>
/// [Phase2] 玩家属性配置 ScriptableObject
/// 主属性基础值 + 所有衍生属性公式参数 + 升级配置
/// 策划在 Inspector 中调整数值，运行时 PlayerAttributeSystem 读取
/// </summary>
[CreateAssetMenu(fileName = "PlayerAttrConfig", menuName = "Game/PlayerAttributeConfig")]
public class PlayerAttrConfigSO : ScriptableObject
{
    // ============================================================
    // 主属性基础值（角色初始属性）
    // ============================================================

    [Header("主属性基础值")]
    [Tooltip("初始力量")]
    public int baseStr = 5;

    [Tooltip("初始智力")]
    public int baseInt = 5;

    [Tooltip("初始敏捷")]
    public int baseAgi = 5;

    // ============================================================
    // 力量派生系数
    // ============================================================

    [Header("力量派生系数")]
    [Tooltip("每点力量 + 最大生命")]
    public float str_hpPerPoint = 10f;

    [Tooltip("每点力量 + 生命恢复/秒")]
    public float str_hpRegenPerP = 0.5f;

    [Tooltip("每10点力量 + 护甲")]
    public float str_armorPer10 = 1f;

    // ============================================================
    // 智力派生系数
    // ============================================================

    [Header("智力派生系数")]
    [Tooltip("每点智力 + 最大魔法")]
    public float int_mpPerPoint = 10f;

    [Tooltip("每点智力 + 魔法恢复/秒")]
    public float int_mpRegenPerP = 0.5f;

    [Tooltip("每10点智力 + 技能增强（0.05 = 5%）")]
    public float int_skillEnhPer10 = 0.05f;

    // ============================================================
    // 敏捷派生系数
    // ============================================================

    [Header("敏捷派生系数")]
    [Tooltip("每点敏捷 + 攻击速度")]
    public float agi_atkSpdPerP = 2f;

    [Tooltip("每点敏捷 + 闪避率（0.01 = 1%）")]
    public float agi_dodgePerP = 0.01f;

    [Tooltip("每10点敏捷 + 冷却缩减（0.05 = 5%）")]
    public float agi_cdReducePer10 = 0.05f;

    // ============================================================
    // 攻击速度基准
    // ============================================================

    [Header("攻击速度基准")]
    [Tooltip("攻击速度基础值（派生公式: atkSpeedBase + AGI × agi_atkSpdPerP）")]
    public float atkSpeedBase = 100f;

    // ============================================================
    // 升级配置
    // ============================================================

    [Header("初始生命/魔法")]
    [Tooltip("初始生命基础值（最终生命 = 此值 + STR派生加成）")]
    public float initialHealth = 5f;

    [Tooltip("初始魔法基础值（最终魔法 = 此值 + INT派生加成）")]
    public float initialMana = 100f;

    [Header("升级配置")]
    [Tooltip("每级获得属性点数")]
    public int attrPointsPerLv = 3;

    [Tooltip("初始可分配点数")]
    public int initialPoints = 0;

    // ============================================================
    // 成长曲线（可选 — 每级自动成长主属性）
    // ============================================================

    [Header("成长曲线（可选）")]
    [Tooltip("每级自动成长力量（null = 不自动成长）")]
    public AnimationCurve strGrowthCurve;

    [Tooltip("每级自动成长智力")]
    public AnimationCurve intGrowthCurve;

    [Tooltip("每级自动成长敏捷")]
    public AnimationCurve agiGrowthCurve;
}
