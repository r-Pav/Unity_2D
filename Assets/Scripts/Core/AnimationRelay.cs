using UnityEngine;

/// <summary>
/// 通用动画事件转发 — 挂在 Player 的 Anim 子物体上（与 Animator 同物体）。
/// 收到 AnimationEvent 后向上查找父级组件，逐个尝试转发。
/// Player 专用；敌人事件转发已拆分到 EnemyMeleeAnimationRelay / EnemyRangedAnimationRelay / BossAnimationRelay。
/// </summary>
public class AnimationRelay : MonoBehaviour
{
    private PlayerCombat _combat;
    private PlayerHealth _health;
    private WeaponThrow _weaponThrow;

    void Awake()
    {
        _combat = GetComponentInParent<PlayerCombat>();
        _health = GetComponentInParent<PlayerHealth>();
        // WeaponThrow 挂在 Player 的子物体(武器)上,从 Player 根向下找
        _weaponThrow = GetComponentInParent<PlayerController>()?.GetComponentInChildren<WeaponThrow>();
    }

    public void OnAttackAnimationStart() => _combat?.OnAttackAnimationStart();
    public void OnAttackAnimationEnd()   => _combat?.OnAttackAnimationEnd();
    public void OnMeleeHitFrame()        => _combat?.OnMeleeHitFrame();
    public void OnDeathAnimationEnd()    => _health?.OnDeathAnimationEnd();

    // 武器投掷三连击(动画事件下拉里选这些,转发到 WeaponThrow)
    // 空中攻击复用地面 Attack1/2/3 clip 时会触发这些事件 → 空中屏蔽,不做投掷(2026-08-24)
    public void OnWeaponAttack1()
    {
        if (_combat != null && _combat.IsAirAttacking) return;
        _weaponThrow?.OnAttackStart1();
    }
    public void OnWeaponAttack2()
    {
        if (_combat != null && _combat.IsAirAttacking) return;
        _weaponThrow?.OnAttackStart2();
    }
    public void OnWeaponAttack3()
    {
        if (_combat != null && _combat.IsAirAttacking) return;
        _weaponThrow?.OnAttackStart3();
    }
    public void OnWeaponAttackEnd() => _weaponThrow?.OnAttackEnd();

    // 空中攻击(动画事件下拉里选这些,转发到 PlayerCombat)
    public void OnAirAttackHitFrame() => _combat?.OnAirAttackHitFrame();
    public void OnAirAttackEnd() => _combat?.OnAirAttackEnd();

    // 输入门事件帧(攻击动画命中帧之后挂,转发到 PlayerCombat → 当前攻击状态)
    public void OnAttackInputOpen() => _combat?.OnAttackInputOpen();
}
