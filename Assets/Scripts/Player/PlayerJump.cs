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

    /// <summary>本次滞空是否已用过空中攻击(落地 ResetJumps 时清;空中攻击一滞空只能一次)</summary>
    public bool AirAttackUsed { get; private set; }

    void Awake()
    {
        jumpsLeft = maxJumps;
        _charBase = GetComponent<CharacterBase>();
    }

    public void ResetJumps()
    {
        jumpsLeft = maxJumps;
        AirAttackUsed = false;   // 落地重置:新滞空周期,空中攻击次数恢复
    }

    /// <summary>标记本次滞空已用过空中攻击(进入 AirAttackState 时调用)</summary>
    public void MarkAirAttackUsed() => AirAttackUsed = true;

    /// <summary>仅刷新空中攻击计数(空中背刺命中时调用):跳跃次数不动,本次滞空可重新空中攻击。
    /// 与 ResetJumps 区别:不清 jumpsLeft(二段跳次数保持),只恢复空中攻击资格。</summary>
    public void ResetAirAttackOnly() => AirAttackUsed = false;

    /// <summary>锁定期间调用(PlayerController.IsActionLocked 分支):处理跳跃打断/缓冲。
    /// [2026-08-21 扩展] 所有锁定状态统一响应空格,不再只认攻击:
    ///   - 攻击类(PlayerAttackState/PlayerAirAttackState):jumpBreaksAttack=true 打断直接跳(跳>攻优先级),false 走缓冲
    ///   - 其他锁定状态(冲刺/下坠/施法/受击):记录缓冲意图,状态结束后 Idle/Move/Jump/Fall 的 UpdateJumpBuffer 窗口内补跳
    ///   - 排除:死亡 / 瞄准(技能流程) / 贴墙(蹬墙跳自己的空格,WallClingState 处理)
    /// P2:攻击锁定由 FSM 状态表达,跳跃打断攻击 = ChangeState(JumpState),状态 OnExit 自动清理(原 CancelAttackForJump 职责)。</summary>
    public void OnLockedUpdate(PlayerController owner)
    {
        if (!Input.GetKeyDown(KeyCode.Space)) return;

        // 不可缓冲状态:死亡 / 瞄准(技能流程) / 贴墙(蹬墙跳自己的空格)
        var cur = owner.PlayerFsm != null ? owner.PlayerFsm.CurrentState : null;
        if (cur is PlayerDeadState || cur is PlayerAimingState || cur is WallClingState) return;

        // 攻击类状态:输入门(事件帧前 = 只记录意图,事件帧后 = 打断/缓冲)
        if (cur is PlayerAttackState atk)
        {
            if (!atk.InputOpen) { atk.QueueJump(); return; }   // 门前:记意图,事件帧后自动跳
            if (jumpBreaksAttack)
            {
                // 墙顶优先翻顶:TryVault(框+射线)成功 → 翻顶同样打断攻击(传送完成,状态由攻击自然收尾);
                // 不进入跳跃状态(去重标记已置位,状态切换交给调用方)
                if (owner.TryVault())
                    return;
                // 跳跃打断攻击(力由 TryJump 施加;攻击状态由 ChangeState 自动退出并清理)
                if (TryJump(owner))
                    owner.PlayerFsm.ChangeState(owner.JumpState);
                return;
            }
        }
        else if (cur is PlayerAirAttackState air)
        {
            if (!air.InputOpen) { air.QueueJump(); return; }   // 门前:记意图,事件帧后自动跳
            if (jumpBreaksAttack)
            {
                if (owner.TryVault())
                    return;
                if (TryJump(owner))
                    owner.PlayerFsm.ChangeState(owner.JumpState);
                return;
            }
        }

        // 其他锁定状态(冲刺/下坠/施法/受击)及攻击缓冲态:记录跳跃意图,解锁后各状态 OnUpdate 窗口内补跳
        jumpBufferTimer = jumpBufferWindow;
    }

    /// <summary>跳跃缓冲递减:>0 时尝试补跳。返回 true 表示已跳起(调用方切换 JumpState);
    /// 翻顶时返回 false(TryVault 已传送完成,不再需要 JumpState)。</summary>
    public bool UpdateJumpBuffer(PlayerController owner)
    {
        if (jumpBufferTimer <= 0f) return false;
        jumpBufferTimer -= Time.deltaTime;
        if (jumpBufferTimer <= 0f) return false;

        // 墙顶优先翻顶
        if (owner.TryVault())
        {
            jumpBufferTimer = 0f;
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
        // 空中/贴墙接近墙顶:优先翻顶(框+射线统一判定;翻顶后 return true,调用方不再进跳跃)
        if (owner.TryVault())
            return true;
        if (jumpsLeft > 0)
        {
            jumpsLeft--;
            owner.ExecuteJump(owner.JumpForce);
            return true;
        }
        return false;
    }
}
