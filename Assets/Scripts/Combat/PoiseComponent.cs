using UnityEngine;

/// <summary>
/// 韧性/霸体组件 — EnemyPoise 的通用化版本（P4a 迁移）。
/// 玩家和敌人都可挂载，不再限定 Enemy 命名空间/目录。
/// 挂在 Player/Enemy GameObject 上，由 ICombatant.Poise 暴露。
/// 负责识别近战攻击、累计命中次数、在达到随机阈值后激活霸体免疫击退。
/// 霸体期间免疫击退但不免疫 FSM 打断（stun 始终生效）。
/// </summary>
public class PoiseComponent : MonoBehaviour
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
    [Tooltip("近战攻击标签白名单（如 Sword/Sword_Heavy），调用方传入的 attackLabel 命中此白名单时走霸体系统。数组最后一个元素为重击标签（如 Sword_Heavy），只有它计入霸体计数器")]
    [SerializeField] private string[] meleeAttackLabels = { "Sword", "Sword_Heavy" };

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
    /// 判断给定的 attackLabel 是否为近战攻击。
    /// 与 meleeAttackLabels 白名单做精确匹配。
    /// </summary>
    public bool IsMeleeAttack(string attackLabel)
    {
        if (string.IsNullOrEmpty(attackLabel)) return false;
        foreach (string label in meleeAttackLabels)
        {
            if (label == attackLabel) return true;
        }
        return false;
    }

    /// <summary>
    /// 处理一次近战命中。只有重击标签（meleeAttackLabels 最后一个元素）的命中才计入霸体计数器，
    /// 普通近战不推进霸体。触发条件满足时激活霸体（后续命中免疫击退）。
    /// 返回值已无调用方（击退决策已移交 CombatResolver：霸体激活期间免疫，非霸体按调用方 info.knockback 击退），
    /// 保留返回/out 参数仅为接口兼容。
    /// </summary>
    public bool RegisterHit(string attackLabel, out float knockbackForce)
    {
        // 只有重击才推进霸体计数器
        bool isHeavy = meleeAttackLabels.Length > 0
            && attackLabel == meleeAttackLabels[meleeAttackLabels.Length - 1];
        if (isHeavy)
            meleeHitCount++;

        // 检查是否触发霸体：未激活霸体 + 累计命中 ≥ 随机阈值
        if (!isPoiseActive && meleeHitCount >= poiseThreshold)
        {
            isPoiseActive = true;
            remainingPoise = poiseImmuneCount;
            // 触发霸体的这一击仍然施加击退
            knockbackForce = meleeKnockbackForce;
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
            knockbackForce = 0f;
            return false;
        }

        // 非重击：不击退
        if (!isHeavy)
        {
            knockbackForce = 0f;
            return false;
        }

        // 重击且未触发霸体：正常击退
        knockbackForce = meleeKnockbackForce;
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
