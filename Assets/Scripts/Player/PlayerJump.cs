using UnityEngine;

/// <summary>
/// 跳跃执行器(降级) — P1 起不再持有 IsJumping/IsFalling 状态(迁移到 FSM 状态类)
/// 保留:跳跃次数管理(ResetJumps)、跳跃执行(TryJump→owner.ExecuteJump)、
///      跳跃缓冲(UpdateJumpBuffer)、锁定期间打断/缓冲(OnLockedUpdate)
/// 由 PlayerJumpState/PlayerFallState 等 FSM 状态类调用
/// </summary>
public class PlayerJump : MonoBehaviour
{
    [SerializeField] private int maxJumps = 2;

    [Tooltip("跳跃打断攻击:攻击锁定期间按空格直接打断当前攻击并跳跃(优先级:跳 > 攻击)。false = 缓冲补跳")]
    [SerializeField] private bool jumpBreaksAttack = true;

    [Tooltip("跳跃缓冲窗口(秒):攻击锁定期间按空格记录意图,解锁后窗口内自动补跳")]
    [SerializeField] private float jumpBufferWindow = 0.2f;

    private int jumpsLeft;
    private float jumpBufferTimer;   // >0 = 有待执行的跳跃意图(缓冲方案用)
    private CharacterBase _charBase;
    private Animator Anim => _charBase != null ? _charBase.Animator : null;

    void Awake()
    {
        jumpsLeft = maxJumps;
        _charBase = GetComponent<CharacterBase>();
    }

    public void ResetJumps() => jumpsLeft = maxJumps;

    /// <summary>锁定期间调用(PlayerController.IsActionLocked 分支):处理跳跃打断/缓冲。
    /// P2:攻击锁定由 FSM 状态表达(PlayerAttackState/PlayerAirAttackState 当前状态),
    /// 跳跃打断攻击 = ChangeState(JumpState),状态 OnExit 自动清理(原 CancelAttackForJump 职责)。
    /// 正常移动/落地检测由 FSM 状态接管,此处只响应空格输入。</summary>
    public void OnLockedUpdate(PlayerController owner)
    {
        // 只有攻击类状态可被跳跃打断(原 combat.IsInputLocked 条件)
        bool inBreakableAttack = owner.PlayerFsm != null
            && (owner.PlayerFsm.CurrentState is PlayerAttackState
                || owner.PlayerFsm.CurrentState is PlayerAirAttackState);
        if (!inBreakableAttack) return;
        if (!Input.GetKeyDown(KeyCode.Space)) return;

        if (jumpBreaksAttack)
        {
            // 墙顶优先翻顶:翻顶同样打断攻击,但不进入跳跃状态(TriggerVault 已切换状态)
            if (owner.NearWallTop() && owner.CanVault())
            {
                owner.WallClingState?.TriggerVault();
                return;
            }
            // 跳跃打断攻击(力由 TryJump 施加;攻击状态由 ChangeState 自动退出并清理)
            if (TryJump(owner))
                owner.PlayerFsm.ChangeState(owner.JumpState);
        }
        else
        {
            // 缓冲 — 记录跳跃意图,解锁后各状态 OnUpdate 窗口内补跳
            jumpBufferTimer = jumpBufferWindow;
        }
    }

    /// <summary>跳跃缓冲递减:>0 时尝试补跳。返回 true 表示已跳起(调用方切换 JumpState);
    /// 翻顶时返回 false(TriggerVault 已切换状态,不再需要 JumpState)。</summary>
    public bool UpdateJumpBuffer(PlayerController owner)
    {
        if (jumpBufferTimer <= 0f) return false;
        jumpBufferTimer -= Time.deltaTime;
        if (jumpBufferTimer <= 0f) return false;

        // 墙顶优先翻顶
        if (owner.NearWallTop() && owner.CanVault())
        {
            jumpBufferTimer = 0f;
            owner.WallClingState?.TriggerVault();
            return false;
        }

        if (TryJump(owner))
        {
            jumpBufferTimer = 0f;
            return true;
        }
        return false;
    }

    /// <summary>尝试跳跃:优先空中翻顶,否则消耗跳跃次数并施加跳跃力。成功返回 true。
    /// 由 FSM 状态类(Idle/Move/Jump/Fall)在输入或缓冲命中时调用。</summary>
    public bool TryJump(PlayerController owner)
    {
        // 空中/贴墙接近墙顶:优先翻顶(蹬墙跳飞行中接近对面墙顶可直接翻上去)
        if (owner.NearWallTop() && owner.CanVault())
        {
            owner.WallClingState?.TriggerVault();
            return true;
        }
        if (jumpsLeft > 0)
        {
            jumpsLeft--;
            owner.ExecuteJump(owner.JumpForce);
            return true;
        }
        return false;
    }
}
