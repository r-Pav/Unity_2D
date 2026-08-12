using UnityEngine;

/// <summary>
/// 远程敌人动画事件转发（预留）— 远程敌人接入动画时挂到 Enemy_Ranged 的 Anim 子物体上（与 Animator 同物体）。
/// 结构已就位，转发方法注释保留：接入时取消注释、按实际动画事件调整方法名（Ranged 前缀），
/// 转发目标统一是 EnemyControllerBase 的通用方法（OnAttackHitFrame / OnAttackAnimationEnd / OnDeathAnimationEnd）。
///
/// 示例（接入时用）：
///   public void OnRangedAttack1Hit() => _enemy?.OnAttackHitFrame();
///   public void OnRangedAttack2Hit() => _enemy?.OnAttackHitFrame();
///   public void OnRangedAttackEnd()  => _enemy?.OnAttackAnimationEnd();
///   public void OnRangedHurtEnd()    => _enemy?.OnHurtAnimationEnd();   // 若实现受击动画退出
///   public void OnRangedDeathEnd()   => _enemy?.OnDeathAnimationEnd();
/// </summary>
public class EnemyRangedAnimationRelay : MonoBehaviour
{
    private EnemyControllerBase _enemy;

    void Awake()
    {
        _enemy = GetComponentInParent<EnemyControllerBase>();
    }

    // 预留：远程敌人接入动画时在这里加转发方法（见类注释示例）
}
