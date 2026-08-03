using UnityEngine;

/// <summary>
/// 动画事件转发（兼容旧引用）— 挂在 Anim GameObject 上。
/// 转发到父级的 PlayerCombat / PlayerHealth。
/// </summary>
public class PlayerAnimationRelay : MonoBehaviour
{
    private PlayerCombat _combat;
    private PlayerHealth _health;

    void Awake()
    {
        _combat = GetComponentInParent<PlayerCombat>();
        _health = GetComponentInParent<PlayerHealth>();
    }

    public void OnAttackAnimationStart() => _combat?.OnAttackAnimationStart();
    public void OnAttackAnimationEnd()   => _combat?.OnAttackAnimationEnd();
    public void OnMeleeHitFrame()        => _combat?.OnMeleeHitFrame();
    public void OnDeathAnimationEnd()    => _health?.OnDeathAnimationEnd();
}
