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

        Vector3 vaultTarget = (Vector3)((Vector2)transform.position
                            + Vector2.up * vaultUpOffset
                            + Vector2.right * facing * vaultForwardOffset);
        Gizmos.color = Color.cyan;
        Gizmos.DrawWireCube(vaultTarget, Vector3.one * 0.3f);
    }
#endif
}
