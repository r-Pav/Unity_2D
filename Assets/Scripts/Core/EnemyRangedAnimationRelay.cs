using UnityEngine;

/// <summary>
/// 远程敌人动画事件转发 — 挂在 Enemy_Ranged 的 Anim 子物体上（与 Animator 同物体）。
/// 收到 AnimationEvent 后转发到根上的 EnemyControllerBase 通用方法：
///   OnRangedAttack1HitFrame → OnAttackHitFrame → RangedAttackState.OnHitFrame（attack1 近战命中帧）
///   OnRangedAttack2Charge   → OnRangedCharge   → RangedAttackState.OnCharge（attack2 蓄力帧）
///   OnRangedAttack2Fire     → OnRangedFire     → RangedAttackState.OnFire（attack2 发射帧）
///   OnRangedAttackEnd       → OnAttackAnimationEnd → RangedAttackState.OnAnimEnd（攻击结束回巡逻）
///   OnRangedDeathEnd        → OnDeathAnimationEnd → 死亡结算（VFX/掉落/事件/销毁）
/// 方法名带 Ranged 前缀，与其他角色 relay 隔离，事件下拉只显示本角色事件。
/// </summary>
public class EnemyRangedAnimationRelay : MonoBehaviour
{
    private EnemyControllerBase _enemy;

    void Awake()
    {
        _enemy = GetComponentInParent<EnemyControllerBase>();
    }

    public void OnRangedAttack1HitFrame()
    {
        if (_enemy != null) _enemy.OnAttackHitFrame();
    }

    public void OnRangedAttack2Charge()
    {
        if (_enemy != null) _enemy.OnRangedCharge();
    }

    public void OnRangedAttack2Fire()
    {
        if (_enemy != null) _enemy.OnRangedFire();
    }

    public void OnRangedAttackEnd()
    {
        if (_enemy != null) _enemy.OnAttackAnimationEnd();
    }

    public void OnRangedDeathEnd()
    {
        if (_enemy != null) _enemy.OnDeathAnimationEnd();
    }
}
