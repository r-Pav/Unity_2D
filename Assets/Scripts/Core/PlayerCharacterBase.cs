using UnityEngine;

/// <summary>
/// 玩家角色抽象基类 — 继承 CharacterBase，承载墙/跳/爬墙逻辑
/// 子类 PlayerController 实现具体输入和状态机调度
/// 检测参数统一从 PlayerDetectionConfig 读取
/// </summary>
public abstract class PlayerCharacterBase : CharacterBase
{
    // ============================================================
    // 配置参数 — 跳跃
    // ============================================================

    [Header("跳跃")]
    [Tooltip("跳跃力度")]
    [SerializeField] protected float jumpForce = 7f;

    // ============================================================
    // 配置参数 — 贴墙移动（非检测参数）
    // ============================================================

    [Header("贴墙移动")]
    [Tooltip("贴墙下滑速度")]
    [SerializeField] protected float wallSlideSpeed = 2f;

    [Tooltip("加速下滑倍率")]
    [SerializeField] protected float wallFastSlideMultiplier = 2.0f;

    [Tooltip("上爬速度")]
    [SerializeField] protected float wallClimbSpeed = 1.0f;

    [Tooltip("按住W多久后开始上爬（秒，旧版保留）")]
    [SerializeField] protected float wallClimbHoldTime = 1.0f;

    // ============================================================
    // 检测配置引用
    // ============================================================

    protected PlayerDetectionConfig detect;

    // ============================================================
    // 运行时状态 — 墙
    // ============================================================

    protected bool isTouchingWall;
    protected int wallDirection;

    // ============================================================
    // 公开访问接口（供状态类使用）
    // ============================================================

    public float JumpForce => jumpForce;

    public bool IsTouchingWall => isTouchingWall;
    public int WallDirection => wallDirection;

    public void ClearWallContact() { isTouchingWall = false; wallDirection = 0; }

    public float WallSlideSpeed => wallSlideSpeed;
    public float WallFastSlideMultiplier => wallFastSlideMultiplier;
    public float WallClimbSpeed => wallClimbSpeed;
    public float WallClimbHoldTime => wallClimbHoldTime;

    // ── 检测参数转发 ──
    public float WallCheckFootHeight => detect != null ? detect.WallCheckFootHeight : 0.1f;
    public float WallCheckDistance => detect != null ? detect.WallCheckDistance : 0.5f;
    public float WallGapRayDistance => detect != null ? detect.WallGapRayDistance : 0.5f;
    public float WallClimbCheckOffset => detect != null ? detect.WallClimbCheckOffset : 0.3f;
    public float VaultUpOffset => detect != null ? detect.VaultUpOffset : 2f;
    public float VaultForwardOffset => detect != null ? detect.VaultForwardOffset : 0.5f;
    public LayerMask WallLayer => detect != null ? detect.WallLayer : ~0;

    // ============================================================
    // 生命周期
    // ============================================================

    protected override void Awake()
    {
        base.Awake();
        detect = GetComponent<PlayerDetectionConfig>();
    }

    protected override void Update()
    {
        HandleGroundCheck();
        if (detect != null && detect.EnableWallDetection) CheckWall();
        OnUpdate();
    }

    // ============================================================
    // 墙检测
    // ============================================================

    protected virtual void CheckWall()
    {
        float footH = detect != null ? detect.WallCheckFootHeight : 0.1f;
        float headH = detect != null ? detect.WallCheckHeadHeight : 1.5f;
        float dist = detect != null ? detect.WallCheckDistance : 0.5f;
        LayerMask layer = detect != null ? detect.WallLayer : ~0;

        Vector2 footOrigin = (Vector2)transform.position + Vector2.up * footH;
        Vector2 headOrigin = (Vector2)transform.position + Vector2.up * headH;
        Vector2 dir = Vector2.right * facing;

        bool footHit = Physics2D.Raycast(footOrigin, dir, dist, layer);
        bool headHit = Physics2D.Raycast(headOrigin, dir, dist, layer);

        if (footHit && headHit)      { isTouchingWall = true;  wallDirection = facing; }
        else if (isTouchingWall && (footHit || headHit)) { }
        else                         { isTouchingWall = false; wallDirection = 0; }
    }

    // ============================================================
    // 增强爬墙 — 翻顶检测
    // ============================================================

    public virtual bool CheckWallTop()
    {
        if (col == null || wallDirection == 0) return false;

        float offset = detect != null ? detect.WallClimbCheckOffset : 0.3f;
        float dist = detect != null ? detect.WallCheckDistance : 0.5f;
        LayerMask layer = detect != null ? detect.WallLayer : ~0;

        Vector2 origin = (Vector2)transform.position
                       + Vector2.up * (col.bounds.extents.y + offset);
        Vector2 dir = Vector2.right * wallDirection;

        return Physics2D.Raycast(origin, dir, dist, layer);
    }

    public virtual bool CanVault()
    {
        if (wallDirection == 0) return false;

        float up = detect != null ? detect.VaultUpOffset : 2f;
        float fwd = detect != null ? detect.VaultForwardOffset : 0.5f;
        LayerMask layer = detect != null ? detect.WallLayer : ~0;

        Vector2 vaultTarget = (Vector2)transform.position
                            + Vector2.up * up
                            + Vector2.right * wallDirection * fwd;

        int selfLayer = 1 << gameObject.layer;
        LayerMask checkMask = (layer.value == ~0)
            ? ~selfLayer
            : (layer & ~selfLayer);

        float ceilingCheckDist = 1.0f;
        if (Physics2D.Raycast(vaultTarget, Vector2.up, ceilingCheckDist, checkMask))
            return false;

        float forwardCheckDist = fwd + 0.3f;
        if (Physics2D.Raycast(vaultTarget, Vector2.right * wallDirection, forwardCheckDist, checkMask))
            return false;

        return true;
    }
}
