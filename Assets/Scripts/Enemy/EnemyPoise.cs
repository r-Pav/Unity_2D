using UnityEngine;

/// <summary>
/// 敌人霸体与击退打断组件 — 挂在 Enemy GameObject 上，与 EnemyControllerBase 同级。
/// 负责识别近战攻击、累计命中次数、在达到随机阈值后激活霸体免疫击退。
/// 霸体期间免疫击退但不免疫 FSM 打断（stun 始终生效）。
/// </summary>
public class EnemyPoise : MonoBehaviour
{
    // ============================================================
    // 配置参数
    // ============================================================

    [Header("霸体阈值")]
    [Tooltip("触发霸体所需近战命中次数下限")]
    [SerializeField] private int poiseThresholdMin = 3;
    [Tooltip("触发霸体所需近战命中次数上限（含）")]
    [SerializeField] private int poiseThresholdMax = 5;

    [Header("霸体免疫")]
    [Tooltip("霸体激活后免疫击退的次数")]
    [SerializeField] private int poiseImmuneCount = 4;

    [Header("近战击退")]
    [Tooltip("近战攻击的击退力度（小于远程的 3f），霸体免疫中此值为 0")]
    [SerializeField] private float meleeKnockbackForce = 1.5f;

    [Header("攻击类型识别")]
    [Tooltip("被视为近战的 attackType 标签前缀（如 Sword），调用方传入的 attackType 与此匹配时走霸体系统")]
    [SerializeField] private string meleeAttackType = "Sword";
    [Tooltip("被视为重击的 attackType 标签（如 Sword_Heavy），只有此类型命中才计入霸体计数器")]
    [SerializeField] private string heavyAttackType = "Sword_Heavy";

    // ============================================================
    // 运行时状态
    // ============================================================

    private int meleeHitCount;          // 近战命中累计
    private int poiseThreshold;         // 本次随机阈值（poiseThresholdMin ~ poiseThresholdMax）
    private int remainingPoise;         // 剩余霸体免疫次数
    private bool isPoiseActive;         // 霸体是否激活中

    // ============================================================
    // 公开属性
    // ============================================================

    /// <summary>霸体是否激活中</summary>
    public bool IsPoiseActive => isPoiseActive;

    /// <summary>剩余霸体免疫次数（霸体未激活时返回 0）</summary>
    public int RemainingPoise => isPoiseActive ? remainingPoise : 0;

    // ============================================================
    // 生命周期
    // ============================================================

    void Awake()
    {
        RollPoiseThreshold();
    }

    // ============================================================
    // 公开方法
    // ============================================================

    /// <summary>
    /// 判断给定的 attackType 是否为近战攻击。
    /// 与 meleeAttackTypes 白名单做精确匹配。
    /// </summary>
    public bool IsMeleeAttack(string attackType)
    {
        return !string.IsNullOrEmpty(attackType)
            && (attackType == meleeAttackType || attackType == heavyAttackType);
    }

    /// <summary>
    /// 处理一次近战命中。只有 heavyAttackType 标签的命中才计入霸体计数器，
    /// 普通近战只触发 stun 但不推进霸体。
    /// </summary>
    public bool RegisterMeleeHit(string attackType, out float out_knockbackForce)
    {
        // 只有重击才推进霸体计数器
        if (attackType == heavyAttackType)
            meleeHitCount++;

        // 检查是否触发霸体：未激活霸体 + 累计命中 ≥ 随机阈值
        if (!isPoiseActive && meleeHitCount >= poiseThreshold)
        {
            isPoiseActive = true;
            remainingPoise = poiseImmuneCount;
            // 触发霸体的这一击仍然施加击退
            out_knockbackForce = meleeKnockbackForce;
            return true;
        }

        if (isPoiseActive)
        {
            remainingPoise--;
            if (remainingPoise <= 0)
            {
                // 霸体耗尽：重置计数器，重新随机阈值
                isPoiseActive = false;
                meleeHitCount = 0;
                RollPoiseThreshold();
            }
            // 霸体免疫中：不击退
            out_knockbackForce = 0f;
            return false;
        }

        // 非重击：不击退
        if (attackType != heavyAttackType)
        {
            out_knockbackForce = 0f;
            return false;
        }

        // 重击且未触发霸体：正常击退
        out_knockbackForce = meleeKnockbackForce;
        return true;
    }

    /// <summary>
    /// 退出战斗时重置霸体状态。由 EnemyControllerBase.OnExitCombatState() 调用。
    /// 清零计数器并重新随机阈值，确保每次战斗独立计算。
    /// </summary>
    public void ResetPoise()
    {
        isPoiseActive = false;
        meleeHitCount = 0;
        remainingPoise = 0;
        RollPoiseThreshold();
    }

    // ============================================================
    // 私有方法
    // ============================================================

    /// <summary>随机掷出霸体触发阈值（闭区间 [poiseThresholdMin, poiseThresholdMax]）</summary>
    private void RollPoiseThreshold()
    {
        poiseThreshold = Random.Range(poiseThresholdMin, poiseThresholdMax + 1);
    }
}
