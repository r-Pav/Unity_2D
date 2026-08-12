using UnityEngine;

/// <summary>
/// Boss 动画事件转发（预留）— Boss 接入动画时挂到 Boss 的 Anim 子物体上（与 Animator 同物体）。
/// 结构已就位，转发方法注释保留：接入时取消注释、按实际动画事件调整方法名（Boss 前缀），
/// 转发目标统一是 EnemyControllerBase 的通用方法（OnAttackHitFrame / OnAttackAnimationEnd / OnDeathAnimationEnd）。
///
/// 示例（接入时用）：
///   public void OnBossAttackHitFrame() => _enemy?.OnAttackHitFrame();
///   public void OnBossMagicFrame()     => _enemy?.OnAttackHitFrame();   // 魔法/技能命中帧
///   public void OnBossAttackEnd()      => _enemy?.OnAttackAnimationEnd();
///   public void OnBossDeathEnd()       => _enemy?.OnDeathAnimationEnd();
/// </summary>
public class BossAnimationRelay : MonoBehaviour
{
    private EnemyControllerBase _enemy;

    void Awake()
    {
        _enemy = GetComponentInParent<EnemyControllerBase>();
    }

    // 预留：Boss 接入动画时在这里加转发方法（见类注释示例）
}
