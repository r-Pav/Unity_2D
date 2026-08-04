using UnityEngine;

/// <summary>
/// 动画事件转发（兼容旧引用）— 挂在 Anim GameObject 上。
/// 转发到父级的 PlayerCombat / PlayerHealth / WeaponThrow。
/// </summary>
public class PlayerAnimationRelay : MonoBehaviour
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
    public void OnWeaponAttack1() => _weaponThrow?.OnAttackStart1();
    public void OnWeaponAttack2() => _weaponThrow?.OnAttackStart2();
    public void OnWeaponAttack3() => _weaponThrow?.OnAttackStart3();
    public void OnWeaponAttackEnd() => _weaponThrow?.OnAttackEnd();

    // 空中攻击(动画事件下拉里选这些,转发到 PlayerCombat)
    public void OnAirAttackHitFrame() => _combat?.OnAirAttackHitFrame();
    public void OnAirAttackEnd() => _combat?.OnAirAttackEnd();
}
