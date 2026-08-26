using UnityEngine;

/// <summary>
/// 地面攻击状态 — 三连击单状态(方案 7.2 + 7.4),继承 PlayerComboState。
/// 差异仅:连击段推进续段(基类默认) + 武器投掷重生(OnComboExit)。
/// 动画事件经 PlayerCombat 薄转发:OnAnimStart/OnAnimEnd/OnHitFrame/OnInputOpen(基类实现)。
/// </summary>
public class PlayerAttackState : PlayerComboState
{
    private readonly WeaponThrow weaponThrow;

    public PlayerAttackState(CharacterBase owner, StateMachine stateMachine, Animator anim,
        PlayerCombat combat, WeaponThrow weaponThrow, float comboResetTimer, float comboExitWindow)
        : base(owner, stateMachine, anim, new[] { AnimParams.IsAttacking }, combat, comboResetTimer, comboExitWindow)
    {
        this.weaponThrow = weaponThrow;
    }

    protected override bool IsAirAttack => false;

    protected override void OnComboExit()
    {
        // 武器投掷重生判定:攻击链结束(原 ExitComboChain/OnAttackAnimationEnd/CancelAttackForJump)
        weaponThrow?.OnAttackEnd();
    }
}
