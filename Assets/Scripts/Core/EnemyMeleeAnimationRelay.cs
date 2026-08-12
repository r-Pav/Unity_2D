using UnityEngine;

/// <summary>
/// 近战敌人动画事件转发 — 挂在 Enemy_Melee 的 Anim 子物体上（与 Animator 同物体）。
/// 收到 AnimationEvent 后转发到根上的 EnemyControllerBase 通用方法：
///   OnAttackHitFrame → 当前攻击状态 IEnemyAttackState.OnHitFrame（命中帧执行攻击）
///   OnAttackAnimationEnd → IEnemyAttackState.OnAnimEnd（攻击结束回 Idle）
///   OnDeathAnimationEnd → 死亡结算（VFX/掉落/事件/销毁）
/// 方法名带 Melee 前缀，与其他角色 relay 隔离，事件下拉只显示本角色事件。
/// </summary>
public class EnemyMeleeAnimationRelay : MonoBehaviour
{
    private EnemyControllerBase _enemy;

    void Awake()
    {
        _enemy = GetComponentInParent<EnemyControllerBase>();
    }

    public void OnMeleeAttackHitFrame() => _enemy?.OnAttackHitFrame();
    public void OnMeleeAttackEnd()      => _enemy?.OnAttackAnimationEnd();
    public void OnMeleeDeathEnd()       => _enemy?.OnDeathAnimationEnd();
}
