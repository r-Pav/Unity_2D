using UnityEngine;

public class PlayerAnimation : MonoBehaviour
{
    [Header("移速动画分档")]
    [Tooltip("速度达到此值切换为 Run(BlendTree: 0=Idle / 0.5=Walk / 1=Run / 1.5=RunJump)")]
    [SerializeField] private float runSpeedThreshold = 5f;

    [Tooltip("速度达到此值切换为 RunJump")]
    [SerializeField] private float runJumpSpeedThreshold = 8f;

    [Tooltip("低于此速度视为静止(Idle),防微小速度抖动")]
    [SerializeField] private float idleDeadZone = 0.1f;

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

        // 移速动画分档(硬分档,无中间混合):0=Idle / <run=Walk / <runJump=Run / 其余=RunJump
        float v = Mathf.Abs(_rb.velocity.x);
        float speedParam;
        if (v <= idleDeadZone)
            speedParam = 0f;                                  // Idle
        else if (v < runSpeedThreshold)
            speedParam = 0.5f;                                // Walk(BlendTree 0.5)
        else if (v < runJumpSpeedThreshold)
            speedParam = 1f;                                  // Run(BlendTree 1)
        else
            speedParam = 1.5f;                                // RunJump(BlendTree 1.5)
        _animator.SetFloat(AnimParams.Speed, speedParam);

        // 聚合 IsMove：所有动作状态为 false 时才是移动
        // 状态源为 C# 字段（Animator 只做输出），不再每帧回读 Animator
        bool isMove = !(_jump != null && (_jump.IsJumping || _jump.IsFalling))
                   && !(_combat != null && (_combat.IsAttacking || _combat.IsBlocking || _combat.IsAirAttacking))
                   && !(_dash != null && _dash.IsDashing)
                   && !(_health != null && (_health.IsHurt || _health.IsAirHurt));

        _animator.SetBool(AnimParams.IsMove, isMove);
    }
}
