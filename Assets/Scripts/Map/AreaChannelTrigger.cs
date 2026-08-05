using UnityEngine;

/// <summary>
/// 地区通道触发器 — 挂通道两端各一个实例。
/// 玩家进入时调用 ZoneManager 执行地区切换协程。
/// </summary>
public class AreaChannelTrigger : MonoBehaviour
{
    // ============================================================
    // 配置字段（编辑器设置）
    // ============================================================

    [Header("自动移动")]
    [Tooltip("自动移动速度")]
    [SerializeField] private float moveSpeed = 4f;

    [Header("地区")]
    [Tooltip("目标地区根物体（进通道后到达出口时显示）")]
    [SerializeField] private GameObject targetArea;

    [Tooltip("来源地区根物体（到达出口时隐藏；初始场景地区可留空）")]
    [SerializeField] private GameObject sourceArea;

    [Header("落点")]
    [Tooltip("出口位置：玩家自动移动的终点（落地位置）")]
    [SerializeField] private Vector3 targetSpawnPoint;

    [Header("镜头")]
    [Tooltip("通道内镜头缩放量（orthoSize 缩小到此值，值越小镜头越近）")]
    [SerializeField] private float zoomAmount = 3f;

    // ============================================================
    // 公开属性（供 ZoneManager 读取）
    // ============================================================

    public float MoveSpeed => moveSpeed;
    public GameObject TargetArea => targetArea;
    public GameObject SourceArea => sourceArea;
    public Vector3 TargetSpawnPoint => targetSpawnPoint;
    public float ZoomAmount => zoomAmount;

    // ============================================================
    // 触发逻辑
    // ============================================================

    private void OnTriggerEnter2D(Collider2D other)
    {
        if (!other.CompareTag("Player")) return;

        var zm = ZoneManager.Instance;
        if (zm == null)
        {
            Debug.LogError("[AreaChannelTrigger] 场景中未找到 ZoneManager，请确保主场景有 ZoneManager 节点");
            return;
        }

        if (!zm.CanTrigger) return;

        if (targetArea == null)
        {
            Debug.LogWarning("[AreaChannelTrigger] targetArea 为空，跳过切换");
            return;
        }

        zm.StartTransition(this, other.transform);
    }

    // ============================================================
    // Gizmos（编辑器可视化）
    // ============================================================

#if UNITY_EDITOR
    private void OnDrawGizmos()
    {
        var col = GetComponent<Collider2D>();
        if (col == null) return;

        // 触发器范围
        Gizmos.color = new Color(0.2f, 0.8f, 0.2f, 0.25f);
        Gizmos.DrawCube(col.bounds.center, col.bounds.size);
        Gizmos.color = new Color(0.2f, 0.8f, 0.2f, 0.6f);
        Gizmos.DrawWireCube(col.bounds.center, col.bounds.size);

        // 移动方向箭头:从触发器中心指向落点
        Vector3 arrowOrigin = col.bounds.center;
        Gizmos.color = Color.yellow;
        Gizmos.DrawRay(arrowOrigin, targetSpawnPoint - arrowOrigin);

        // 箭头尖
        Vector3 tip = targetSpawnPoint;
        Gizmos.DrawRay(tip, Quaternion.Euler(0, 0, 150) * (tip - arrowOrigin).normalized * 0.5f);
        Gizmos.DrawRay(tip, Quaternion.Euler(0, 0, -150) * (tip - arrowOrigin).normalized * 0.5f);

        // 目标落点
        Gizmos.color = Color.cyan;
        Gizmos.DrawWireSphere(targetSpawnPoint, 0.3f);
        Gizmos.DrawRay(targetSpawnPoint, Vector3.up * 1f);
        Gizmos.DrawRay(targetSpawnPoint, Vector3.down * 1f);
    }
#endif
}
