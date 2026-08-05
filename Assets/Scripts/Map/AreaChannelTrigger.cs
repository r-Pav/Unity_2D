using UnityEngine;

/// <summary>
/// 管道触发器 — 双向自动识别进出方向,操作的都是"对侧地区"。
/// 玩家从本侧地区进入管道 → 显示对侧地区 + 镜头拉近(前后场景同时加载)
/// 玩家从管道返回本侧地区 → 关闭对侧地区 + 镜头恢复(对侧即来源地区)
/// 设计前提:管道水平,本侧地区在触发器的 outsideDirection 方向。
/// </summary>
public class AreaChannelTrigger : MonoBehaviour
{
    [Header("地区")]
    [Tooltip("对侧地区:从本侧进入管道时显示,从管道返回时关闭")]
    [SerializeField] private GameObject otherSideArea;

    [Header("方向")]
    [Tooltip("本侧地区相对触发器的方向:+1=右,-1=左(水平通道;用于判断玩家从哪边进入)")]
    [SerializeField] private int outsideDirection = -1;

    [Header("镜头")]
    [Tooltip("管道内镜头缩放(orthoSize,越小越近;0=不缩放)")]
    [SerializeField] private float zoomAmount = 3f;

    [Tooltip("镜头过渡速度")]
    [SerializeField] private float zoomSpeed = 3f;

    [Header("管道内限速")]
    [Tooltip("管道内玩家移动速度(进管道时强制,出管道恢复原速;0=不限速)")]
    [SerializeField] private float channelMoveSpeed = 5f;

    private void OnTriggerEnter2D(Collider2D other)
    {
        if (!other.CompareTag("Player")) return;

        var col = GetComponent<Collider2D>();
        if (col == null) return;

        // 判断玩家从哪边进入:玩家 x 相对触发器中心的方向
        float dirFromPlayer = Mathf.Sign(other.transform.position.x - col.bounds.center.x);

        var zm = ZoneManager.Instance;
        if (zm == null)
        {
            Debug.LogError("[AreaChannelTrigger] 场景中未找到 ZoneManager");
            return;
        }

        // 管道内限速:进管道设速度,出管道恢复
        var character = other.GetComponentInParent<CharacterBase>();

        if (Mathf.Sign(dirFromPlayer) == Mathf.Sign(outsideDirection))
        {
            // 从本侧地区进入管道:显示对侧地区(前后场景同时加载)+ 镜头拉近 + 限速
            zm.ShowArea(otherSideArea);
            if (zoomAmount > 0f)
                zm.ZoomIn(zoomAmount, zoomSpeed);
            if (character != null && channelMoveSpeed > 0f)
                character.SetMoveSpeedOverride(channelMoveSpeed);
        }
        else
        {
            // 从管道返回本侧地区:关闭对侧地区(来源)+ 镜头恢复 + 恢复原速
            zm.HideArea(otherSideArea);
            if (zoomAmount > 0f)
                zm.ZoomOut(zoomSpeed);
            if (character != null)
                character.SetMoveSpeedOverride(null);
        }
    }

    // ============================================================
    // Gizmos(编辑器可视化)
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

        // 本侧方向指示(黄色箭头指向本侧地区)
        Vector3 center = col.bounds.center;
        Gizmos.color = Color.yellow;
        Gizmos.DrawRay(center, new Vector3(outsideDirection, 0f, 0f) * 1.5f);
    }
#endif
}
