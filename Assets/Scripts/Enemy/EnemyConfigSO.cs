using UnityEngine;

/// <summary>
/// 敌人单等级数值块 — 一个敌人类型 SO 内含 Lv1/Lv2/Lv3 三档，运行时按 level 取档。
/// 字段默认值 = 原代码默认值（新建 SO 资产时即"内置默认"，可直接当模板）。
/// 近战/远程共用此结构：近战填近战字段，远程填远程字段（未用字段留默认）。
/// </summary>
[System.Serializable]
public class EnemyLvStats
{
    // ============================================================
    // 基础数值（EnemyControllerBase）
    // ============================================================

    [Tooltip("最大血量基础值（运行时走管线后为管线输入 baseValue）")]
    public float maxHealth = 3f;

    [Tooltip("检测矩形半宽（X 轴）")]
    public float detectionWidth = 8f;

    [Tooltip("检测矩形半高（Y 轴）")]
    public float detectionHeight = 3f;

    [Tooltip("攻击矩形半宽（X 轴；远程资产填 10）")]
    public float attackWidth = 1.5f;

    [Tooltip("攻击矩形半高（Y 轴；远程资产填 6）")]
    public float attackHeight = 1.5f;

    [Tooltip("攻击冷却时间（秒）")]
    public float attackCooldownDuration = 1f;

    [Tooltip("远程攻击击退力度（近战击退由 PoiseComponent 控制）")]
    public float rangedKnockbackForce = 5f;

    // ============================================================
    // 近战专属（EnemyMeleeController / EnemyMeleeAttack / EnemyContactTrigger）
    // ============================================================

    [Tooltip("巡逻范围（左右各多少单位）")]
    public float patrolRange = 3f;

    [Tooltip("近战伤害基础值（运行时走管线后为管线输入 baseValue）")]
    public float meleeDamage = 1f;

    [Tooltip("接触伤害推开力度")]
    public float contactPushForce = 3f;

    [Tooltip("接触伤害冷却（秒）")]
    public float contactCooldown = 0.3f;

    [Tooltip("接触伤害检测半径")]
    public float contactDetectRadius = 0.6f;

    // ============================================================
    // 远程专属（EnemyRangedController / EnemyRangedAttack）
    // ============================================================

    [Tooltip("后退矩形半宽（X 轴，低于此距离后退）")]
    public float retreatWidth = 3f;

    [Tooltip("后退矩形半高（Y 轴，低于此距离后退）")]
    public float retreatHeight = 3f;

    [Tooltip("恢复追击矩形半宽（X 轴，迟滞区间上限）")]
    public float retreatRecoverWidth = 10f;

    [Tooltip("恢复追击矩形半高（Y 轴，迟滞区间上限）")]
    public float retreatRecoverHeight = 6f;

    [Tooltip("远程伤害基础值（运行时走管线后为管线输入 baseValue）")]
    public float rangedDamage = 1f;

    [Tooltip("子弹飞行速度")]
    public float bulletSpeed = 6f;

    [Tooltip("子弹半径")]
    public float bulletRadius = 0.5f;
}

/// <summary>
/// [Lv 收敛版] 敌人数值配置 ScriptableObject — 一个 SO = 一个敌人类型（Melee/Ranged），
/// 内含 Lv1/Lv2/Lv3 三档数值（EnemyLvStats），运行时按 EnemyControllerBase.level 取档。
/// 纯数据类（public 字段），对齐 PlayerAttrConfigSO 模式：CreateAssetMenu Game/ 前缀。
/// 敌人编辑器（Tools → 敌人编辑器）集中管理：新建模板 / 克隆场景敌人时按 Lv 烘焙数值。
///
/// [Boss 单独设计] Boss 专属字段（bossName/hpThresholds/phaseTransitionDuration/knockbackResistance/
/// deathDelay/hpMultiplier/moveSpeedMultiplier/attackRangeMultiplier/p2MoveSpeedMult/p3MoveSpeedMult）
/// 已注释保留于此文件底部，后续剥离到独立 BossConfigSO；Boss 逻辑走 BossControllerBase 自身字段。
/// </summary>
[CreateAssetMenu(fileName = "EnemyConfig_", menuName = "Game/EnemyConfigSO")]
public class EnemyConfigSO : ScriptableObject
{
    [Header("Lv1")]
    public EnemyLvStats lv1 = new EnemyLvStats();

    [Header("Lv2")]
    public EnemyLvStats lv2 = new EnemyLvStats();

    [Header("Lv3")]
    public EnemyLvStats lv3 = new EnemyLvStats();

    /// <summary>按等级取档（1~3，越界回退 Lv1）</summary>
    public EnemyLvStats GetLvStats(int level)
    {
        switch (level)
        {
            case 2: return lv2;
            case 3: return lv3;
            default: return lv1;
        }
    }

    // ============================================================
    // [Boss 专属字段 — 注释保留，后续剥离到独立 BossConfigSO]
    // ============================================================
    // public string bossName = "Boss";
    // public float[] hpThresholds = { 0.6f, 0.25f };
    // public float phaseTransitionDuration = 1.5f;
    // public float knockbackResistance = 0.8f;
    // public float deathDelay = 2f;
    // public float hpMultiplier = 12f;
    // public float moveSpeedMultiplier = 0.5f;
    // public float attackRangeMultiplier = 1.5f;
    // public float p2MoveSpeedMult = 1.2f;
    // public float p3MoveSpeedMult = 1.5f;
}
