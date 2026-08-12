using UnityEngine;

/// <summary>
/// 管道触发器 — 双向自动识别进出方向,操作的都是"对侧地区"。
/// 玩家从本侧地区进入管道 → 显示对侧地区(前后场景同时加载)
/// 玩家从管道返回本侧地区 → 关闭对侧地区(对侧即来源地区)
/// 镜头缩放已由 CameraZone/CameraZoneManager 接管（玩家进入管道区域自动拉近）。
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

    [Header("管道内限速")]
    [Tooltip("管道内玩家移动速度(进管道时强制,出管道恢复原速;0=不限速)")]
    [SerializeField] private float channelMoveSpeed = 5f;

    [Header("相机区域")]
    [Tooltip("管道的 CameraZone（玩家进入管道时切到此区域；拖管道 collider 上的 CameraZone）")]
    [SerializeField] private CameraZone channelZone;
    [Tooltip("本侧地区的 CameraZone（玩家从管道返回本侧时切回此区域；拖本侧地区 collider 上的 CameraZone）")]
    [SerializeField] private CameraZone sideZone;

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
            // 从本侧地区进入管道:显示对侧地区(前后场景同时加载)+ 限速
            // （镜头缩放由 CameraZone 进入管道区域时处理）
            zm.ShowArea(otherSideArea);
            if (character != null && channelMoveSpeed > 0f)
                character.SetMoveSpeedOverride(channelMoveSpeed);

            // 进入新地区 → 自动存档（SaveSystem 订阅 AreaEnterEvent 触发 AutoSave）
            EventBus.Trigger(new AreaEnterEvent());

            // 进管道 → 相机切到管道区域（显式切 channelZone；管道口与地区 Polygon 重叠，
            // 位置查询会命中多个区域取错，不依赖查询）
            if (channelZone != null)
                CameraZoneManager.Instance?.EnterZone(channelZone);
            else
                CameraZoneManager.Instance?.RefreshZoneAt(other.transform.position);
        }
        else
        {
            // 从管道返回本侧地区:关闭对侧地区(来源)+ 恢复原速
            zm.HideArea(otherSideArea);
            if (character != null)
                character.SetMoveSpeedOverride(null);

            // 返回本侧 → 直接切本侧地区（玩家此刻还在管道口/管道范围内，位置查询会命中管道，必须显式切回）
            if (sideZone != null)
                CameraZoneManager.Instance?.EnterZone(sideZone);
            else
                CameraZoneManager.Instance?.RefreshZoneAt(other.transform.position);
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
