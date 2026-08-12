using UnityEngine;

/// <summary>
/// 相机区域 — 挂每个地区/管道的范围 collider 上（Polygon/Box 均可，须勾 Is Trigger）。
/// 玩家进入本区域 → 通知 CameraZoneManager：
///   1. 切换 CinemachineConfiner2D 的 Bounding Shape 到本区域 collider（相机被锁在本范围内）
///   2. 平滑过渡 VCam 镜头 orthoSize 到本区域配置（管道 3 拉近，地区 4 正常）
/// collider 自动 GetComponent 获取（每区域一个 collider），无需拖拽。
/// </summary>
public class CameraZone : MonoBehaviour
{
    [Header("相机参数")]
    [Tooltip("本区域的相机 orthoSize（地区 4 正常；管道 3 拉近；0 不缩放保持当前）")]
    [SerializeField] private float orthoSize = 4f;

    [Tooltip("缩放过渡速度（越大越快；越小越平滑）")]
    [SerializeField] private float zoomSpeed = 3f;

    private Collider2D _col;

    private void Awake()
    {
        _col = GetComponent<Collider2D>();
    }

    private void OnTriggerEnter2D(Collider2D other)
    {
        if (!other.CompareTag("Player")) return;
        CameraZoneManager.Instance?.EnterZone(this);
    }

    public Collider2D Bounds => _col;
    public float TargetOrtho => orthoSize;
    public float ZoomSpeed => zoomSpeed;
}
