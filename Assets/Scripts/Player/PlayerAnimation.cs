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
    private PlayerController _pc;

    void Awake()
    {
        _rb = GetComponentInParent<Rigidbody2D>();

        var all = GetComponentsInChildren<Animator>();
        foreach (var a in all)
            if (a.runtimeAnimatorController != null) { _animator = a; break; }

        // 状态源统一查 PlayerController 公开状态属性(FSM 状态类驱动,不再是子组件 bool)
        _pc = GetComponentInParent<PlayerController>();
    }

    void Update()
    {
        if (_animator == null || _rb == null || _pc == null) return;

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

        // 聚合 IsMove：FSM 当前状态非动作状态 且 非冲刺/受击 时才是移动
        // P2:战斗状态(Attack/AirAttack/Block/GroundPound)直接查 FSM 状态类型,不再查子组件 bool
        // P3b:冲刺排除走 _pc.IsDashing()(已改为查 FSM CurrentState is PlayerDashState),无需额外处理
        var cur = _pc.PlayerFsm != null ? _pc.PlayerFsm.CurrentState : null;
        bool inCombatState = cur is PlayerAttackState
                          || cur is PlayerAirAttackState
                          || cur is PlayerBlockState
                          || cur is PlayerGroundPoundState;

        bool isMove = !(_pc.IsJumping || _pc.IsFalling)
                   && !inCombatState
                   && !(_pc.IsDashing())
                   && !(_pc.IsHurt || _pc.IsAirHurt);

        _animator.SetBool(AnimParams.IsMove, isMove);
    }
}
