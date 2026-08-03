using UnityEngine;

// ============================================================
// BossAttackSO — ScriptableObject，定义 Boss 特殊技能的完整参数
// ============================================================

/// <summary>
/// Boss 特殊技能 ScriptableObject。
/// 挂载到 BossSkillSlots.allSkills 数组中，由 BossSkillSlots 按类型分发执行。
/// </summary>
[CreateAssetMenu(fileName = "BossAttack_", menuName = "Game/BossAttackSO", order = 100)]
public class BossAttackSO : ScriptableObject
{
    // ============================================================
    // 通用字段（所有类型共用）
    // ============================================================

    [Header("── 通用 ──")]
    [Tooltip("显示名（如\"石卫冲撞\"）")]
    public string skillName = "New Skill";

    [Tooltip("技能类型，决定执行逻辑")]
    public BossSkillType skillType;

    [Tooltip("伤害值")]
    public float damage = 20f;

    [Tooltip("前摇时长（秒）")]
    public float windupTime = 0.5f;

    [Tooltip("判定窗口时长（秒），Charge 类型为冲撞持续时间")]
    public float activeTime = 0.15f;

    [Tooltip("后摇时长（秒）")]
    public float recoveryTime = 0.5f;

    [Tooltip("基础冷却（秒）")]
    public float cooldown = 4f;

    [Tooltip("解锁所需阶段（0=P1, 1=P2, 2=P3），-1=始终可用")]
    public int phaseUnlock;

    [Tooltip("每阶段冷却倍率，如 [1.0, 0.8, 0.6]。长度等于阶段数")]
    public float[] phaseCooldownMultipliers = { 1f, 0.8f, 0.6f };

    [Header("── 格挡/弹反 ──")]
    [Tooltip("是否可格挡")]
    public bool canBeBlocked = true;

    [Tooltip("是否可弹反")]
    public bool canBeParried = true;

    [Tooltip("弹反窗口起始（相对 activeTime 起始的偏移，秒）")]
    public float parryWindowStart = -0.05f;

    [Tooltip("弹反窗口结束（同上）")]
    public float parryWindowEnd = 0.05f;

    [Tooltip("击退力度")]
    public float knockbackForce = 5f;

    [Header("── 表现 ──")]
    [Tooltip("Animator trigger 名称")]
    public string animTrigger;

    [Tooltip("音效 key（可选）")]
    public string sfxKey;

    [Tooltip("命中特效 prefab（可选）")]
    public GameObject hitVFXPrefab;

    [Tooltip("技能图标（可选，供 UI 展示）")]
    public Sprite icon;

    // ============================================================
    // Charge（冲撞）专属字段
    // ============================================================

    [Header("── Charge 冲撞专属 ──")]
    [Tooltip("冲撞速度倍率（× owner baseSpeed）")]
    public float chargeSpeedMultiplier = 3f;

    [Tooltip("最大冲撞距离")]
    public float chargeMaxDistance = 8f;

    [Tooltip("冲撞前方持续判定的矩形宽")]
    public float chargeHitboxWidth = 2f;

    [Tooltip("冲撞前方持续判定的矩形高")]
    public float chargeHitboxHeight = 2f;

    [Tooltip("撞墙是否停止")]
    public bool chargeStopOnWall = true;

    // ============================================================
    // Slam（砸地 AOE）专属字段
    // ============================================================

    [Header("── Slam 砸地专属 ──")]
    [Tooltip("AOE 圆形判定半径")]
    public float slamRadius = 3f;

    [Tooltip("AOE 中心相对 Boss 位置的偏移")]
    public Vector2 slamOffset = new Vector2(0f, -1f);

    [Tooltip("命中击退力度（覆写通用 knockbackForce），0=使用通用值")]
    public float slamKnockbackOverride;

    [Tooltip("屏幕震动强度（0~1）")]
    [Range(0f, 1f)]
    public float slamScreenShakeIntensity;

    [Tooltip("屏幕震动持续时间（秒）")]
    public float slamScreenShakeDuration = 0.15f;

    [Tooltip("地面裂痕/冲击波扩散特效（可选）")]
    public GameObject slamGroundVFXPrefab;

    // ============================================================
    // Shockwave（地面波）专属字段
    // ============================================================

    [Header("── Shockwave 地面波专属 ──")]
    [Tooltip("冲击波投射物 prefab")]
    public GameObject wavePrefab;

    [Tooltip("传播速度（单位/秒）")]
    public float waveSpeed = 4f;

    [Tooltip("最大传播距离（超出销毁）")]
    public float waveMaxDistance = 10f;

    [Tooltip("判定高度（鼓励跳跃躲避时设低值，如 0.5）")]
    public float waveHeight = 0.5f;

    [Tooltip("生成位置偏移（相对 Boss 脚底）")]
    public Vector2 waveSpawnOffset = new Vector2(0f, -0.5f);

    [Tooltip("波数（默认 1，扇形多波时 >1）")]
    public int waveCount = 1;

    [Tooltip("多波时的扇形散布角（度），0=同方向")]
    public float waveSpreadAngle;

    // ============================================================
    // MeleeWrap（近战包装）专属字段
    // ============================================================

    [Header("── MeleeWrap 近战包装专属 ──")]
    [Tooltip("引用的近战组件（挂在同一 GameObject 上）")]
    public EnemyMeleeAttack wrappedAttack;

    [Tooltip("是否覆写判定框大小")]
    public bool overrideHitboxSize;

    [Tooltip("覆写宽度")]
    public float overrideWidth = 1.5f;

    [Tooltip("覆写高度")]
    public float overrideHeight = 1.5f;

    // ============================================================
    // RangedWrap（远程包装）专属字段
    // ============================================================

    [Header("── RangedWrap 远程包装专属 ──")]
    [Tooltip("引用的远程组件")]
    public EnemyRangedAttack wrappedRangedAttack;

    [Tooltip("是否覆写子弹速度")]
    public bool overrideProjectileSpeed;

    [Tooltip("覆写速度")]
    public float overrideSpeed = 6f;

    // ============================================================
    // Combo（连击）专属字段
    // ============================================================

    [Header("── Combo 连击专属 ──")]
    [Tooltip("子技能 SO 数组")]
    public BossAttackSO[] comboAttacks;

    [Tooltip("每击间隔（秒）")]
    public float comboInterval = 0.25f;

    [Tooltip("最后一击是否不可格挡")]
    public bool finalHitUnblockable;

    [Tooltip("最后一击是否不可弹反")]
    public bool finalHitUnparriable;

    [Tooltip("最后一击额外伤害（叠加 damage），0=不叠加")]
    public float finalHitExtraDamage;

    // ============================================================
    // 便捷方法
    // ============================================================

    /// <summary>获取指定阶段的冷却时间</summary>
    public float GetCooldownForPhase(int phase)
    {
        if (phaseCooldownMultipliers == null || phaseCooldownMultipliers.Length == 0)
            return cooldown;
        int idx = Mathf.Clamp(phase, 0, phaseCooldownMultipliers.Length - 1);
        return cooldown * phaseCooldownMultipliers[idx];
    }

    /// <summary>当前阶段是否已解锁</summary>
    public bool IsUnlockedInPhase(int phase)
    {
        if (phaseUnlock < 0) return true;
        return phase >= phaseUnlock;
    }
}
