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

    /// <summary>墙检测单线丢失容忍时间(秒):贴墙中单条检测线短暂丢失不退出,防墙面起伏震动</summary>
    [Tooltip("贴墙中单条检测线丢失的容忍时间(秒),防墙面起伏导致反复进出贴墙震动")]
    [SerializeField] private float wallLostTolerance = 0.12f;

    private float _wallLostTimer;

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

        // 进入严格:两条检测线都命中才算贴墙(防墙顶/墙沿边缘只碰一条线就误贴)
        // 退出宽容:已贴墙时单条线短暂丢失不立即退出(防墙面起伏导致反复进出贴墙→清速度→震动),
        //           持续丢失超过 wallLostTolerance 才退出;两条都丢则立即退出
        if (footHit && headHit)
        {
            isTouchingWall = true;
            wallDirection = facing;
            _wallLostTimer = 0f;
        }
        else if (footHit || headHit)
        {
            if (isTouchingWall)
            {
                _wallLostTimer += Time.deltaTime;
                if (_wallLostTimer > wallLostTolerance)
                {
                    isTouchingWall = false;
                    wallDirection = 0;
                }
            }
            // 未贴墙 + 只命中一条:不进入
        }
        else
        {
            isTouchingWall = false;
            wallDirection = 0;
            _wallLostTimer = 0f;
        }
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

    /// <summary>
    /// 墙顶提前量判断:墙顶距玩家头顶小于 leadDistance 即视为"接近墙顶,可翻"。
    /// 比 CheckWallTop(头顶完全越过墙顶才翻)窗口大得多,攀爬中按跳跃不易错过。
    /// 贴墙用 wallDirection,空中用 facing(玩家朝向)检测。
    /// </summary>
    public virtual bool NearWallTop(float leadDistance = 1f)
    {
        if (col == null) return false;
        int dir = wallDirection != 0 ? wallDirection : facing;
        if (dir == 0) return false;

        float checkUp = 2f;   // 从头顶上方 2m 开始向下找墙顶
        float maxDist = checkUp + 2f;
        // origin 贴着检测方向的墙面(墙外 0.05m):向下射线只命中这面墙的墙顶,
        // 不会被头顶上方横着的其他墙(高墙)或脚下地面干扰
        Vector2 origin = (Vector2)transform.position
                       + Vector2.right * dir * (col.bounds.extents.x + 0.05f)
                       + Vector2.up * (col.bounds.extents.y + checkUp);
        LayerMask layer = detect != null ? detect.WallLayer : ~0;

        RaycastHit2D hit = Physics2D.Raycast(origin, Vector2.down, maxDist, layer);
        if (!hit) return false;

        float wallTopY = hit.point.y;                          // 墙顶高度
        float playerHeadY = transform.position.y + col.bounds.extents.y;   // 玩家头顶高度
        // 命中点必须在头顶上方(排除脚下地面/下方平台);墙顶距头顶 < leadDistance 即可翻
        if (wallTopY <= playerHeadY) return false;
        return wallTopY - playerHeadY < leadDistance;
    }

    public virtual bool CanVault()
    {
        int dir = wallDirection != 0 ? wallDirection : facing;
        if (dir == 0) return false;

        float up = detect != null ? detect.VaultUpOffset : 2f;
        float fwd = detect != null ? detect.VaultForwardOffset : 0.5f;
        LayerMask layer = detect != null ? detect.WallLayer : ~0;

        Vector2 vaultTarget = (Vector2)transform.position
                            + Vector2.up * up
                            + Vector2.right * dir * fwd;

        int selfLayer = 1 << gameObject.layer;
        LayerMask checkMask = (layer.value == ~0)
            ? ~selfLayer
            : (layer & ~selfLayer);

        float ceilingCheckDist = 1.0f;
        if (Physics2D.Raycast(vaultTarget, Vector2.up, ceilingCheckDist, checkMask))
            return false;

        float forwardCheckDist = fwd + 0.3f;
        if (Physics2D.Raycast(vaultTarget, Vector2.right * dir, forwardCheckDist, checkMask))
            return false;

        return true;
    }
}
