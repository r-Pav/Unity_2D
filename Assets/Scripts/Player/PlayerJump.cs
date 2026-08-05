using UnityEngine;

public class PlayerJump : MonoBehaviour
{
    [SerializeField] private int maxJumps = 2;

    [Tooltip("跳跃打断攻击:攻击锁定期间按空格直接打断当前攻击并跳跃(优先级:跳 > 攻击)。false = 缓冲补跳")]
    [SerializeField] private bool jumpBreaksAttack = true;

    [Tooltip("跳跃缓冲窗口(秒):方案1用,攻击锁定期间按空格记录意图,解锁后窗口内自动补跳")]
    [SerializeField] private float jumpBufferWindow = 0.2f;

    private int jumpsLeft;
    private bool wasGrounded;
    private float jumpBufferTimer;   // >0 = 有待执行的跳跃意图(缓冲方案用)
    private CharacterBase _charBase;
    private Animator Anim => _charBase != null ? _charBase.Animator : null;

    // ============================================================
    // 跳跃状态（C# 字段为状态源，Animator 只做单向输出）
    // ============================================================

    /// <summary>是否处于跳跃上升（Jump 动画）</summary>
    public bool IsJumping { get; private set; }

    /// <summary>是否处于下落（Fall 动画）</summary>
    public bool IsFalling { get; private set; }

    void Awake()
    {
        jumpsLeft = maxJumps;
        _charBase = GetComponent<CharacterBase>();
    }

    public void ResetJumps() => jumpsLeft = maxJumps;

    /// <summary>锁定期间调用(PlayerController.IsActionLocked 分支):处理跳跃打断/缓冲。
    /// 正常移动/落地检测不在此处理,只响应空格输入。</summary>
    public void OnLockedUpdate(PlayerController owner)
    {
        if (owner.Combat != null && owner.Combat.IsInputLocked && Input.GetKeyDown(KeyCode.Space))
        {
            if (jumpBreaksAttack)
            {
                // 方案2:跳跃打断攻击
                if (TryJump(owner))
                    owner.Combat.CancelAttackForJump();
            }
            else
            {
                // 方案1:缓冲 — 记录跳跃意图,解锁后 OnPlayerUpdate 窗口内补跳
                jumpBufferTimer = jumpBufferWindow;
            }
        }
    }

    public void OnPlayerUpdate(PlayerController owner)
    {
        bool grounded = owner.IsGrounded();

        if (grounded && !wasGrounded)
        {
            jumpsLeft = maxJumps;
            IsFalling = false;
            IsJumping = false;
            Anim?.SetBool(AnimParams.IsFalling, false);
            Anim?.SetBool(AnimParams.IsJumping, false);
        }

        if (owner.IsDashing())
        {
            wasGrounded = grounded;
            return;
        }

        if (owner.FreezeTimer > 0f)
        {
            wasGrounded = grounded;
            return;
        }

        // 缓冲窗口递减(方案1:锁定期间记录意图,解锁后窗口内补跳)
        if (jumpBufferTimer > 0f)
        {
            jumpBufferTimer -= Time.deltaTime;
            if (jumpBufferTimer > 0f && TryJump(owner))
                jumpBufferTimer = 0f;
        }

        // 正常即时跳跃
        if (Input.GetKeyDown(KeyCode.Space))
        {
            TryJump(owner);
        }

        // 跳跃转下落
        if (!grounded)
        {
            Rigidbody2D rb = owner.GetRigidbody();
            if (rb != null && rb.velocity.y < -0.1f)
            {
                IsJumping = false;
                IsFalling = true;
                Anim?.SetBool(AnimParams.IsJumping, false);
                Anim?.SetBool(AnimParams.IsFalling, true);
            }
        }

        wasGrounded = grounded;
    }

    /// <summary>尝试跳跃:优先空中翻顶,否则消耗跳跃次数。成功返回 true</summary>
    private bool TryJump(PlayerController owner)
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
            IsJumping = true;
            IsFalling = false;
            Anim?.SetBool(AnimParams.IsJumping, true);
            return true;
        }
        return false;
    }
}
