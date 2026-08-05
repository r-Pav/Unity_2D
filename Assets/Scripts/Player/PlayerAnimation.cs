using UnityEngine;

public class PlayerAnimation : MonoBehaviour
{
    private Animator _animator;
    private Rigidbody2D _rb;
    private PlayerJump _jump;
    private PlayerCombat _combat;
    private PlayerDash _dash;
    private PlayerHealth _health;

    void Awake()
    {
        _rb = GetComponentInParent<Rigidbody2D>();

        var all = GetComponentsInChildren<Animator>();
        foreach (var a in all)
            if (a.runtimeAnimatorController != null) { _animator = a; break; }

        // C# 状态源组件（在 Player 根上，用 InParent 向上找）
        _jump = GetComponentInParent<PlayerJump>();
        _combat = GetComponentInParent<PlayerCombat>();
        _dash = GetComponentInParent<PlayerDash>();
        _health = GetComponentInParent<PlayerHealth>();
    }

    void Update()
    {
        if (_animator == null || _rb == null) return;

        _animator.SetFloat(AnimParams.Speed, Mathf.Abs(_rb.velocity.x));

        // 聚合 IsMove：所有动作状态为 false 时才是移动
        // 状态源为 C# 字段（Animator 只做输出），不再每帧回读 Animator
        bool isMove = !(_jump != null && (_jump.IsJumping || _jump.IsFalling))
                   && !(_combat != null && (_combat.IsAttacking || _combat.IsBlocking || _combat.IsAirAttacking))
                   && !(_dash != null && _dash.IsDashing)
                   && !(_health != null && (_health.IsHurt || _health.IsAirHurt));

        _animator.SetBool(AnimParams.IsMove, isMove);
    }
}
