using UnityEngine;

public class PlayerDash : MonoBehaviour
{
    [Header("冲刺")]
    [SerializeField] private float dashSpeed = 10f;
    [SerializeField] private float dashDuration = 0.15f;
    [SerializeField] private float dashCooldown = 0.6f;

    private bool isDashing;
    private float dashTimer;
    private float dashCooldownTimer;
    private CharacterBase _charBase;
    private Animator Anim => _charBase != null ? _charBase.Animator : null;
    private PlayerCombat _combat;

    public bool IsDashing => isDashing;
    public bool CooldownReady => dashCooldownTimer <= 0f;

    void Awake()
    {
        _charBase = GetComponent<CharacterBase>();
        _combat = GetComponent<PlayerCombat>();
    }

    public bool OnPlayerUpdate(PlayerController owner)
    {
        if (dashCooldownTimer > 0f)
            dashCooldownTimer -= Time.deltaTime;

        if (isDashing)
        {
            dashTimer -= Time.deltaTime;
            if (dashTimer <= 0f)
            {
                isDashing = false;
                if (owner.IsGrounded())
                {
                    Anim?.SetBool(AnimParams.IsFalling, false);
                    Anim?.SetBool(AnimParams.IsJumping, false);
                }
                return false;
            }
            return true;
        }

        HandleDashInput(owner);
        return false;
    }

    private void HandleDashInput(PlayerController owner)
    {
        if (_combat != null && _combat.IsInputLocked) return;

        if (Input.GetKeyDown(KeyCode.LeftShift) && dashCooldownTimer <= 0f)
        {
            isDashing = true;
            dashTimer = dashDuration;
            dashCooldownTimer = dashCooldown;

            Rigidbody2D rb = owner.GetRigidbody();
            rb.velocity = Vector2.zero;
            rb.velocity = new Vector2(owner.GetFacing() * dashSpeed, 0);
        }
    }
}
