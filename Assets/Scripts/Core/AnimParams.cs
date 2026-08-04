/// <summary>
/// Animator 参数名常量 — 集中管理，避免字符串硬编码散落各处。
/// 用法：_animator.SetBool(AnimParams.IsHurt, true);
/// </summary>
public static class AnimParams
{
    // Float
    public const string Speed = "Speed";

    // Bool
    public const string IsGrounded = "IsGrounded";
    public const string IsJumping = "IsJumping";
    public const string IsFalling = "IsFalling";
    public const string IsDashing = "IsDashing";
    public const string IsDead = "IsDead";
    public const string IsBlocking = "IsBlocking";
    public const string IsMove = "IsMove";
    public const string IsAttacking = "IsAttacking";
    public const string IsAirAttacking = "IsAirAttacking";
    public const string IsHurt = "IsHurt";
    public const string IsAirHurt = "IsAirHurt";

    // Int
    public const string AttackIndex = "AttackIndex";

    // Trigger
    public const string Attack = "Attack";
    public const string EndAttack = "EndAttack";
    public const string Death = "Death";
    public const string ParrySuccess = "ParrySuccess";
    public const string Hurt = "Hurt"; // 旧参数，待控制器同步后移除
}
