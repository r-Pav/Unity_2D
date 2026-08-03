using UnityEngine;

public class PlayerJump : MonoBehaviour
{
    [SerializeField] private int maxJumps = 2;

    private int jumpsLeft;
    private bool wasGrounded;
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
            if (owner.WallClingState != null)
                owner.WallClingState.ConsumeExtraJump();
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

        if (owner.Combat != null && owner.Combat.IsInputLocked)
        {
            wasGrounded = grounded;
            return;
        }

        if (Input.GetKeyDown(KeyCode.Space))
        {
            if (jumpsLeft > 0)
            {
                jumpsLeft--;
                owner.ExecuteJump(owner.JumpForce);
                IsJumping = true;
                IsFalling = false;
                Anim?.SetBool(AnimParams.IsJumping, true);
            }
            else if (owner.WallClingState != null && owner.WallClingState.HasExtraJump)
            {
                owner.WallClingState.ConsumeExtraJump();
                owner.ExecuteJump(owner.JumpForce);
                IsJumping = true;
                IsFalling = false;
                Anim?.SetBool(AnimParams.IsJumping, true);
            }
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
}
