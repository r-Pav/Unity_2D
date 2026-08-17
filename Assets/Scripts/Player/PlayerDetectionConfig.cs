using UnityEngine;

/// <summary>
/// 玩家检测参数集中配置 — 所有射线/范围检测的可调参数
/// 挂在 Player 上，供 PlayerCharacterBase / WallClingState 读取
/// </summary>
public class PlayerDetectionConfig : MonoBehaviour
{
    // ============================================================
    // 墙检测
    // ============================================================

    [Header("墙检测")]
    [Tooltip("脚部射线高度")]
    [SerializeField] private float wallCheckFootHeight = 0.1f;

    [Tooltip("头部射线高度")]
    [SerializeField] private float wallCheckHeadHeight = 1.5f;

    [Tooltip("墙检测射线距离")]
    [SerializeField] private float wallCheckDistance = 0.5f;

    [Tooltip("脚部射线颜色")]
    [SerializeField] private Color wallCheckFootColor = Color.yellow;

    [Tooltip("头部射线颜色")]
    [SerializeField] private Color wallCheckHeadColor = Color.yellow;

    [Tooltip("是否启用墙检测")]
    [SerializeField] private bool enableWallDetection = true;

    [Tooltip("检测为墙面的 Layer")]
    [SerializeField] private LayerMask wallLayer = ~0;

    // ============================================================
    // 贴墙间隙
    // ============================================================

    [Header("贴墙间隙")]
    [Tooltip("间隙修正射线距离")]
    [SerializeField] private float wallGapRayDistance = 0.5f;

    [Tooltip("间隙修正射线颜色")]
    [SerializeField] private Color wallGapRayColor = Color.magenta;

    // ============================================================
    // 翻顶 & 爬墙
    // ============================================================

    [Header("翻顶 & 爬墙")]
    [Tooltip("翻顶检测头顶上方偏移")]
    [SerializeField] private float wallClimbCheckOffset = 0.3f;

    [Tooltip("翻顶垂直上升偏移")]
    [SerializeField] private float vaultUpOffset = 2f;

    [Tooltip("翻顶水平偏移")]
    [SerializeField] private float vaultForwardOffset = 0.5f;

    [Tooltip("垂直兜底射线水平偏移")]
    [SerializeField] private float wallCheckVerticalOffsetX = 0.3f;

    [Tooltip("垂直兜底射线高度")]
    [SerializeField] private float wallCheckVerticalHeight = 1.5f;

    // ============================================================
    // 翻顶检测(框 + 射线) — 2026-08-14 新增,替代旧 NearWallTop/CanVault 逻辑
    // ============================================================

    [Header("翻顶检测(框+射线)")]
    [Tooltip("检测框尺寸(宽 = 落点宽度,高 = 触发窗口)")]
    [SerializeField] private Vector2 vaultBoxSize = new Vector2(0.6f, 0.8f);

    [Tooltip("框中心相对玩家的前方偏移(调到墙顶中间附近)")]
    [SerializeField] private float vaultBoxForwardOffset = 0.4f;

    [Tooltip("从框底向下找墙的射线长度")]
    [SerializeField] private float vaultRayDistance = 1.5f;

    [Tooltip("墙顶距框底的最大允许距离(超过不触发,防倒吸)")]
    [SerializeField] private float vaultMaxTopDistance = 0.8f;

    [Tooltip("传送后落地冻结时长")]
    [SerializeField] private float vaultFreezeTime = 0.15f;

    // ============================================================
    // 公开访问器
    // ============================================================

    public float WallCheckFootHeight => wallCheckFootHeight;
    public float WallCheckHeadHeight => wallCheckHeadHeight;
    public float WallCheckDistance => wallCheckDistance;
    public bool EnableWallDetection => enableWallDetection;
    public LayerMask WallLayer => wallLayer;
    public float WallGapRayDistance => wallGapRayDistance;
    public float WallClimbCheckOffset => wallClimbCheckOffset;
    public float VaultUpOffset => vaultUpOffset;
    public float VaultForwardOffset => vaultForwardOffset;
    public float WallCheckVerticalOffsetX => wallCheckVerticalOffsetX;
    public float WallCheckVerticalHeight => wallCheckVerticalHeight;

    // ---- 翻顶检测(框+射线) ----
    public Vector2 VaultBoxSize => vaultBoxSize;
    public float VaultBoxForwardOffset => vaultBoxForwardOffset;
    public float VaultRayDistance => vaultRayDistance;
    public float VaultMaxTopDistance => vaultMaxTopDistance;
    public float VaultFreezeTime => vaultFreezeTime;

    // ============================================================
    // Gizmos
    // ============================================================

#if UNITY_EDITOR
    private void OnDrawGizmosSelected()
    {
        if (!enableWallDetection) return;

        Collider2D col = GetComponent<Collider2D>();
        if (col == null) return;

        int facing = 1;
        Vector3 scale = transform.localScale;
        if (scale.x < 0) facing = -1;

        // 脚部墙检测射线
        Vector2 footOrigin = (Vector2)transform.position + Vector2.up * wallCheckFootHeight;
        Gizmos.color = wallCheckFootColor;
        Gizmos.DrawRay(footOrigin, Vector2.right * facing * wallCheckDistance);

        // 头部墙检测射线
        Vector2 headOrigin = (Vector2)transform.position + Vector2.up * wallCheckHeadHeight;
        Gizmos.color = wallCheckHeadColor;
        Gizmos.DrawRay(headOrigin, Vector2.right * facing * wallCheckDistance);

        // 间隙修正射线（从腰部高度打，和脚部黄色区分开）
        float gapHeight = (wallCheckFootHeight + wallCheckHeadHeight) * 0.5f;
        Vector2 gapOrigin = (Vector2)transform.position + Vector2.up * gapHeight;
        Gizmos.color = wallGapRayColor;
        Gizmos.DrawRay(gapOrigin, Vector2.right * facing * wallGapRayDistance);

        // 垂直兜底射线
        Vector2 vertOrigin = (Vector2)transform.position
                           + Vector2.up * wallCheckVerticalHeight
                           + Vector2.right * facing * wallCheckVerticalOffsetX;
        Gizmos.color = Color.cyan;
        Gizmos.DrawRay(vertOrigin, Vector2.down * wallCheckVerticalHeight);

        // 翻顶检测射线 + 目标位置
        Vector2 topOrigin = (Vector2)transform.position
                          + Vector2.up * (col.bounds.extents.y + wallClimbCheckOffset);
        Gizmos.color = Color.magenta;
        Gizmos.DrawRay(topOrigin, Vector2.right * facing * wallCheckDistance);
    }
#endif
}
