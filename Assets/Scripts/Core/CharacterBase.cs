using UnityEngine;

/// <summary>
/// 角色抽象基类 — 横板角色的通用框架
/// 提供 Rigidbody 初始化、地面检测、面朝方向、基础移动/跳跃
/// 子类通过重写 OnUpdate / OnFixedUpdate 实现具体行为
/// </summary>
[RequireComponent(typeof(Rigidbody2D))]
public abstract class CharacterBase : MonoBehaviour
{
    // ============================================================
    // 配置参数
    // ============================================================

    [Header("移动")]
    [Tooltip("基础移动速度（实际移速 = 基础值 + 修饰器叠加）")]
    [SerializeField] protected float baseMoveSpeed = 10f;

    /// <summary>临时速度覆盖(非 null 时直接作为 MoveSpeed,绕过修饰器管线;用于管道内限速等场景)</summary>
    private float? _moveSpeedOverride;

    /// <summary>设置临时速度覆盖,null 恢复原速(修饰器管线)</summary>
    public void SetMoveSpeedOverride(float? speed) => _moveSpeedOverride = speed;

    [Header("地面检测")]
    [Tooltip("要检测为地面的 Layer")]
    [SerializeField] protected LayerMask groundLayer = 1 << 3;

    [Tooltip("射线长度")]
    [SerializeField] private float groundCheckDist = 1f;

    [Tooltip("Gizmo颜色")]
    [SerializeField] private Color groundCheckColor = Color.green;

    // ============================================================
    // 组件引用
    // ============================================================

    protected Rigidbody2D rb;
    protected Collider2D col;
    protected SpriteRenderer _spriteRenderer;
    protected Animator _animator;

    // 修饰器管理器（可选 — 挂载 StatModifierManager 的 GameObject 自动生效）
    protected StatModifierManager statModManager;

    // ============================================================
    // 运行时状态
    // ============================================================

    protected bool grounded;
    protected int facing = 1;  // 1 = 朝右, -1 = 朝左

    // 击退
    protected bool isKnockedBack;

    // ============================================================
    // 公开访问接口（供状态类使用）
    // ============================================================

    /// <summary>公开的 SetVelocity（供外部状态类调用）</summary>
    public void SetVelocityPublic(float? x = null, float? y = null) => SetVelocity(x, y);

    public bool IsGrounded => grounded;
    public Rigidbody2D Rb => rb;
    public int FacingDir => facing;
    public Collider2D Col => col;
    public Animator Animator => _animator;

    /// <summary>当前移动速度（有临时覆盖直接用；否则走修饰器管线；末尾乘外部速度乘数）</summary>
    protected float MoveSpeed => (_moveSpeedOverride ?? (statModManager != null
        ? statModManager.GetFinalValue(baseMoveSpeed, StatId.MoveSpeed)
        : baseMoveSpeed)) * speedMultiplier;

    /// <summary>外部速度乘数(减速圈等用);1 = 不变</summary>
    [Tooltip("外部速度乘数(减速圈等用);1 = 不变")]
    public float speedMultiplier = 1f;

    // ============================================================
    // 生命周期
    // ============================================================

    protected virtual void Awake()
    {
        rb = GetComponent<Rigidbody2D>();
        col = GetComponent<Collider2D>();
        _spriteRenderer = GetComponent<SpriteRenderer>();
        _animator = null;
        var animators = GetComponentsInChildren<Animator>();
        foreach (var a in animators)
        {
            if (a.runtimeAnimatorController != null) { _animator = a; break; }
        }
        statModManager = GetComponent<StatModifierManager>();

        rb.interpolation = RigidbodyInterpolation2D.Interpolate;
        rb.collisionDetectionMode = CollisionDetectionMode2D.Continuous;
        rb.constraints = RigidbodyConstraints2D.FreezeRotation;
    }

    /// <summary>框架 Update：地面检测 → 动画更新 → 子类 OnUpdate</summary>
    protected virtual void Update()
    {
        HandleGroundCheck();
        UpdateAnimation();
        OnUpdate();
    }

    /// <summary>动画参数更新（子类可重写以驱动各角色动画）</summary>
    protected virtual void UpdateAnimation() { }

    /// <summary>
    /// 框架 FixedUpdate：子类 OnFixedUpdate
    /// </summary>
    protected virtual void FixedUpdate()
    {
        OnFixedUpdate();
    }

#if UNITY_EDITOR
    /// <summary>
    /// 编辑器内自动校验组件
    /// </summary>
    protected virtual void OnValidate()
    {
        if (rb == null) rb = GetComponent<Rigidbody2D>();
        if (col == null) col = GetComponent<Collider2D>();
    }
#endif

    // ============================================================
    // 子类接口（多态）
    // ============================================================

    protected abstract void OnUpdate();
    protected abstract void OnFixedUpdate();

    // ============================================================
    // 通用方法 — 检测
    // ============================================================

    /// <summary>地面检测 — 从碰撞体中心向下</summary>
    protected virtual void HandleGroundCheck()
    {
        grounded = col != null && Physics2D.Raycast(col.bounds.center, Vector2.down, groundCheckDist, groundLayer);
    }

    /// <summary>统一速度写入入口 — 击退时自动跳过</summary>
    protected void SetVelocity(float? x = null, float? y = null)
    {
        if (isKnockedBack) return;
        Vector2 vel = rb.velocity;
        if (x.HasValue) vel.x = x.Value;
        if (y.HasValue) vel.y = y.Value;
        rb.velocity = vel;
    }

    /// <summary>水平移动</summary>
    protected virtual void Move(float direction)
    {
        SetVelocity(x: direction * MoveSpeed);
    }

    /// <summary>跳跃（清除 Y 速度 + Impulse 力）</summary>
    protected virtual void Jump(float force)
    {
        SetVelocity(y: 0f);
        rb.AddForce(Vector2.up * force, ForceMode2D.Impulse);
    }

    /// <summary>根据水平输入更新面朝方向（使用 transform.localScale.x 翻转，兼容 Animator）</summary>
    public virtual void UpdateFacing(float direction)
    {
        if (direction == 0f) return;

        facing = direction > 0f ? 1 : -1;
        // Animator 不认 flipX，改用 transform.localScale.x
        Vector3 scale = transform.localScale;
        scale.x = Mathf.Abs(scale.x) * facing;
        transform.localScale = scale;
    }

    // ============================================================
    // Gizmos
    // ============================================================

    protected virtual void OnDrawGizmosSelected()
    {
        if (col == null) return;

        // 地面检测射线
        Gizmos.color = groundCheckColor;
        Gizmos.DrawRay(col.bounds.center, Vector2.down * groundCheckDist);

        // 面朝方向
        Gizmos.color = Color.blue;
        Gizmos.DrawRay(transform.position, Vector3.right * facing * 1.5f);
    }
}
