using UnityEngine;

/// <summary>
/// 玩家角色抽象基类 — 继承 CharacterBase，承载墙/跳/爬墙逻辑
/// 子类 PlayerController 实现具体输入和状态机调度
/// 检测参数统一从 PlayerDetectionConfig 读取
/// 2026-08-14:翻顶检测统一入口 TryVault()(框+射线);旧 NearWallTop/CanVault 已废弃(方法体注释保留)
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

    // ── 翻顶检测(框+射线)参数转发 ──
    public Vector2 VaultBoxSize => detect != null ? detect.VaultBoxSize : new Vector2(0.6f, 0.8f);
    public float VaultBoxForwardOffset => detect != null ? detect.VaultBoxForwardOffset : 0.4f;
    public float VaultRayDistance => detect != null ? detect.VaultRayDistance : 1.5f;
    public float VaultMaxTopDistance => detect != null ? detect.VaultMaxTopDistance : 0.8f;
    public float VaultFreezeTime => detect != null ? detect.VaultFreezeTime : 0.15f;

    /// <summary>翻顶执行后的钩子 — 由子类实现冻结输入等副作用(基类不定义 FreezeTimer,避免与 PlayerController 序列化冲突)</summary>
    protected virtual void OnVaultExecuted() { }

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
    // 2026-08-14 改造:翻顶判定统一为"框 + 射线"(TryVault),
    // 旧 NearWallTop / CanVault 已废弃,方法体注释保留(防外部引用断)。
    // ============================================================

    /// <summary>翻顶去重标记:触发翻顶后置 true,落地(HandleGroundCheck)或进入贴墙(WallClingState.OnEnter)时复位</summary>
    private bool _vaultTriggered;

    /// <summary>复位翻顶去重标记(落地/进入贴墙时调用)</summary>
    public void ResetVaultFlag() => _vaultTriggered = false;

    /// <summary>
    /// 统一翻顶入口:去重检查 → 框+射线判定 → 传送。
    /// 只做"判定+传送+置位",状态切换由调用方判断(贴墙调用方切 FallState,跳跃调用方不切)。
    /// 触发条件(全满足):
    ///   1. 框(OverlapBox,尺寸 vaultBoxSize)内无 WallLayer 碰撞 → 落点区域空;
    ///   2. 从框底向下射线命中墙,且墙顶距框底 ≤ vaultMaxTopDistance(防瞬移回去)。
    /// </summary>
    public bool TryVault()
    {
        // 去重:已触发翻顶且未落地复位 → 不再重复判定
        if (_vaultTriggered) return false;
        if (col == null || rb == null) return false;

        int dir = facing;   // 面朝方向(贴墙时 WallClingState.OnEnter/OnUpdate 已把 facing 同步为墙方向)
        if (dir == 0) return false;

        Vector2 boxSize = VaultBoxSize;
        float halfH = col.bounds.extents.y;

        // 框中心 = 玩家位置 + 面朝dir × vaultBoxForwardOffset + 上 × (半高 + boxSize.y/2);框底 ≈ 玩家头顶
        Vector2 boxCenter = GetVaultBoxCenter(dir, boxSize, halfH);

        LayerMask layer = WallLayer;
        int selfLayer = 1 << gameObject.layer;
        LayerMask checkMask = (layer.value == ~0) ? ~selfLayer : (layer & ~selfLayer);

        // 条件1:框内无 WallLayer 碰撞 → 落点区域空
        if (Physics2D.OverlapBox(boxCenter, boxSize, 0f, checkMask))
            return false;

        // 条件2:从框底向下射线命中墙;命中点(墙顶)距框底 ≤ vaultMaxTopDistance
        Vector2 boxBottom = GetVaultBoxBottom(boxCenter, boxSize);
        RaycastHit2D hit = Physics2D.Raycast(boxBottom, Vector2.down, VaultRayDistance, checkMask);
        if (!hit) return false;
        float wallTopDist = boxBottom.y - hit.point.y;   // 墙顶到框底的距离(射线向下,恒 ≥ 0)
        if (wallTopDist > VaultMaxTopDistance) return false;

        // 传送:玩家中心 → 框中心(物理体置位,不用 transform);落地冻结防抖动
        rb.position = boxCenter;
        OnVaultExecuted();

        _vaultTriggered = true;
        // TEMP 诊断日志(2026-08-14,saika 验证后删除):确认翻顶触发时机与落点
        Debug.Log($"[Vault] triggered dir={dir} boxCenter={boxCenter} hitDist={wallTopDist:F2}");
        return true;
    }

    /// <summary>
    /// 翻顶检测框中心 = 玩家位置 + 面朝dir×VaultBoxForwardOffset + 上×(半高 + boxSize.y/2)。
    /// TryVault 与 Gizmos 共用,保证可视化与实际判定完全一致。
    /// </summary>
    private Vector2 GetVaultBoxCenter(int dir, Vector2 boxSize, float halfH)
    {
        return (Vector2)transform.position
             + Vector2.right * dir * VaultBoxForwardOffset
             + Vector2.up * (halfH + boxSize.y * 0.5f);
    }

    /// <summary>翻顶检测框底 = 框中心 - 上×(boxSize.y/2)(向下射线起点,≈ 玩家头顶)</summary>
    private Vector2 GetVaultBoxBottom(Vector2 boxCenter, Vector2 boxSize)
    {
        return boxCenter - Vector2.up * (boxSize.y * 0.5f);
    }

#if UNITY_EDITOR
    /// <summary>
    /// 翻顶检测 Gizmos(OnDrawGizmos:编辑态 Scene 视图总是绘制,不依赖选中;团结引擎 OnDrawGizmosSelected 编辑态不刷新):
    /// 青色线框 = 落点空框检查(OverlapBox);黄色射线 = 向下找墙顶(Raycast);
    /// 洋红短线/小球 = 墙顶可接受深度上限(vaultMaxTopDistance);红点 = 真实射线命中点。
    /// 计算与 TryVault 共用 GetVaultBoxCenter/GetVaultBoxBottom,调参即所见。
    /// </summary>
    protected void OnDrawGizmos()
    {
        DrawVaultGizmos();
    }

    protected override void OnDrawGizmosSelected()
    {
        base.OnDrawGizmosSelected();
        DrawVaultGizmos();
    }

    private void DrawVaultGizmos()
    {
        // 编辑态 col 由 Awake 缓存(Awake 未跑) → GetComponent 兜底,Scene 视图非 Play 也能实时画框
        Collider2D col2d = col;
        if (col2d == null && !TryGetComponent(out col2d)) return;

        Vector2 boxSize = VaultBoxSize;
        float halfH = col2d.bounds.extents.y;
        int dir = facing != 0 ? facing : 1;   // 编辑态 facing 默认 1;为 0 时兜底朝右,与 TryVault 语义一致

        Vector2 boxCenter = GetVaultBoxCenter(dir, boxSize, halfH);
        Vector2 boxBottom = GetVaultBoxBottom(boxCenter, boxSize);

        // 1. 检测框:半透明青色线框(与 OverlapBox 同尺寸同中心)
        Gizmos.color = new Color(0f, 0.8f, 1f, 0.6f);
        Gizmos.DrawWireCube(boxCenter, boxSize);

        // 2. 向下射线:黄色,从框底到 框底 - up×VaultRayDistance(与 Raycast 同起点同长度)
        Gizmos.color = Color.yellow;
        Gizmos.DrawLine(boxBottom, boxBottom + Vector2.down * VaultRayDistance);

        // 3. 墙顶可接受深度上限标记:vaultMaxTopDistance 处短横线 + 小球(墙顶在此之上才算可翻)
        Vector2 topLimit = boxBottom + Vector2.down * VaultMaxTopDistance;
        Gizmos.color = Color.magenta;
        Gizmos.DrawLine(topLimit + Vector2.left * 0.15f, topLimit + Vector2.right * 0.15f);
        Gizmos.DrawSphere(topLimit, 0.05f);

        // 4. 命中点:沿射线真实 Raycast 一次,命中时在 hit.point 画红点(确认墙顶实际位置)
        LayerMask layer = WallLayer;
        int selfLayer = 1 << gameObject.layer;
        LayerMask checkMask = (layer.value == ~0) ? ~selfLayer : (layer & ~selfLayer);
        RaycastHit2D hit = Physics2D.Raycast(boxBottom, Vector2.down, VaultRayDistance, checkMask);
        if (hit)
        {
            Gizmos.color = Color.red;
            Gizmos.DrawSphere(hit.point, 0.08f);
        }
    }
#endif

    /// <summary>翻顶去重复位 — 统一挂在落地检测处:落地(grounded)即复位,覆盖所有落地路径
    /// (FallState/JumpState 落地分支、贴墙退出等),无需在各状态里散落调用。
    /// 注意:只在地面落地复位,不按时间(防误复位)。</summary>
    protected override void HandleGroundCheck()
    {
        base.HandleGroundCheck();
        if (grounded) _vaultTriggered = false;
    }

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
    /// [已废弃] 墙顶提前量判断:翻顶检测已改为 TryVault()(框+射线)。
    /// 方法体注释保留(防外部引用断),始终返回 false。
    /// </summary>
    public virtual bool NearWallTop(float leadDistance = 1f)
    {
        // ===== 已废弃(2026-08-14):翻顶统一走 TryVault(),原方法体注释保留 =====
        // if (col == null) return false;
        // int dir = wallDirection != 0 ? wallDirection : facing;
        // if (dir == 0) return false;
        //
        // float checkUp = 2f;   // 从头顶上方 2m 开始向下找墙顶
        // float maxDist = checkUp + 2f;
        // // origin 贴着检测方向的墙面(墙外 0.05m):向下射线只命中这面墙的墙顶,
        // // 不会被头顶上方横着的其他墙(高墙)或脚下地面干扰
        // Vector2 origin = (Vector2)transform.position
        //                + Vector2.right * dir * (col.bounds.extents.x + 0.05f)
        //                + Vector2.up * (col.bounds.extents.y + checkUp);
        // LayerMask layer = detect != null ? detect.WallLayer : ~0;
        //
        // RaycastHit2D hit = Physics2D.Raycast(origin, Vector2.down, maxDist, layer);
        // if (!hit) return false;
        //
        // float wallTopY = hit.point.y;                          // 墙顶高度
        // float playerHeadY = transform.position.y + col.bounds.extents.y;   // 玩家头顶高度
        // // 命中点必须在头顶上方(排除脚下地面/下方平台);墙顶距头顶 < leadDistance 即可翻
        // if (wallTopY <= playerHeadY) return false;
        // return wallTopY - playerHeadY < leadDistance;
        return false;
    }

    /// <summary>
    /// [已废弃] 翻顶落点可行性判断:翻顶检测已改为 TryVault()(框+射线)。
    /// 方法体注释保留(防外部引用断),始终返回 false。
    /// </summary>
    public virtual bool CanVault()
    {
        // ===== 已废弃(2026-08-14):翻顶统一走 TryVault(),原方法体注释保留 =====
        // int dir = wallDirection != 0 ? wallDirection : facing;
        // if (dir == 0) return false;
        //
        // float up = detect != null ? detect.VaultUpOffset : 2f;
        // float fwd = detect != null ? detect.VaultForwardOffset : 0.5f;
        // LayerMask layer = detect != null ? detect.WallLayer : ~0;
        //
        // Vector2 vaultTarget = (Vector2)transform.position
        //                    + Vector2.up * up
        //                    + Vector2.right * dir * fwd;
        //
        // int selfLayer = 1 << gameObject.layer;
        // LayerMask checkMask = (layer.value == ~0)
        //     ? ~selfLayer
        //     : (layer & ~selfLayer);
        //
        // float ceilingCheckDist = 1.0f;
        // if (Physics2D.Raycast(vaultTarget, Vector2.up, ceilingCheckDist, checkMask))
        //     return false;
        //
        // float forwardCheckDist = fwd + 0.3f;
        // if (Physics2D.Raycast(vaultTarget, Vector2.right * dir, forwardCheckDist, checkMask))
        //     return false;
        //
        // return true;
        return false;
    }
}
