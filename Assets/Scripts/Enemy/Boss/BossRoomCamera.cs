using UnityEngine;

/// <summary>
/// Boss 房相机接管 — 挂场景空物体(或 BossRoomTrigger 同物体)。
/// 进 Boss 房:主相机锁定到 enterAnchor(相机固定);出房(Boss 死亡):恢复原位置。
/// roomBounds 可选:相机范围边界(collider),进房期间相机位置 clamp 在边界内(相机要微调时用)。
/// CameraFollow 保持禁用勿动(项目约定)。
/// </summary>
public class BossRoomCamera : MonoBehaviour
{
    [Header("引用")]
    [Tooltip("主相机(留空 = Camera.main)")]
    [SerializeField] private Camera targetCamera;
    [Tooltip("进房相机锚点(拖场景空物体,相机停靠位置)")]
    [SerializeField] private Transform enterAnchor;
    [Tooltip("出房恢复的相机位置(拖场景空物体;留空 = 进房前位置)")]
    [SerializeField] private Transform exitAnchor;
    [Tooltip("可选:相机范围边界(拖 collider),进房期间 clamp 相机在此范围内")]
    [SerializeField] private Collider2D roomBounds;

    private Vector3 _savedPos;
    private bool _inBossRoom;

    private Camera Cam => targetCamera != null ? targetCamera : Camera.main;

    /// <summary>进房:保存当前相机位置并锁定到锚点(由 BossRoomTrigger 调用)</summary>
    public void EnterBossRoom()
    {
        var cam = Cam;
        if (cam == null || enterAnchor == null) return;
        _savedPos = cam.transform.position;
        _inBossRoom = true;
        cam.transform.position = enterAnchor.position;
    }

    /// <summary>出房:恢复相机位置(由 Boss 死亡事件调用)</summary>
    public void ExitBossRoom()
    {
        var cam = Cam;
        if (cam == null) return;
        _inBossRoom = false;
        cam.transform.position = exitAnchor != null ? exitAnchor.position : _savedPos;
    }

    private void LateUpdate()
    {
        if (!_inBossRoom || roomBounds == null) return;
        var cam = Cam;
        if (cam == null) return;

        Bounds b = roomBounds.bounds;
        float halfH = cam.orthographicSize;
        float halfW = halfH * cam.aspect;
        Vector3 p = cam.transform.position;
        p.x = Mathf.Clamp(p.x, b.min.x + halfW, b.max.x - halfW);
        p.y = Mathf.Clamp(p.y, b.min.y + halfH, b.max.y - halfH);
        cam.transform.position = p;
    }
}
