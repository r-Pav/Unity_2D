using UnityEngine;

/// <summary>
/// 通用动画事件转发 — 挂在 Animator GameObject 上。
/// 收到 AnimationEvent 后向上查找父级组件，逐个尝试转发。
/// Player / Enemy / Boss 共用同一份脚本。
/// </summary>
public class AnimationRelay : MonoBehaviour
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
