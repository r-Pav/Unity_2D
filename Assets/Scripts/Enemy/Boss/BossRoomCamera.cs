using UnityEngine;

/// <summary>
/// Boss 房相机接管 — 双相机方案。
/// 进 Boss 房:玩家相机 enabled=false,切换到 bossCamera(新建相机,无 AudioListener),
/// 无缝衔接(从玩家相机当前位置开始)→ 平滑跟随玩家(死区,参数独立)→ 限制在 roomBounds 范围内移动。
/// Boss 死亡:bossCamera enabled=false,恢复玩家相机。
/// 参考 CameraFollow 的死区/平滑逻辑,但参数独立可配;CameraFollow 本体保持禁用勿动。
/// </summary>
public class BossRoomCamera : MonoBehaviour
{
    [Header("相机")]
    [Tooltip("玩家相机(留空 = Camera.main)")]
    [SerializeField] private Camera playerCamera;
    [Tooltip("Boss 房专用相机(新建 Camera,不要加 AudioListener)")]
    [SerializeField] private Camera bossCamera;
    [Tooltip("跟随目标(玩家,留空自动按 Tag 找)")]
    [SerializeField] private Transform followTarget;

    [Header("Boss 相机参数(独立,仿玩家相机)")]
    [Tooltip("正交大小(缩放)")]
    [SerializeField] private float orthoSize = 5f;
    [Tooltip("偏左偏移:越大画面越靠左")]
    [SerializeField] private float biasLeft = 3f;
    [Tooltip("死区半宽:玩家在此范围内相机不动")]
    [SerializeField] private float deadZoneHalf = 1.5f;
    [Tooltip("垂直偏移")]
    [SerializeField] private float verticalOffset = 1f;
    [Tooltip("跟随平滑:玩家满速时紧贴的速度基准")]
    [SerializeField] private float maxFollowSpeed = 10f;

    [Header("范围")]
    [Tooltip("Boss 房范围 collider(相机限制在此范围内移动)")]
    [SerializeField] private Collider2D roomBounds;

    private bool _active;
    private bool _playerCamWasEnabled;
    private float _targetX;
    private Vector3 _posVelocity;

    private void Awake()
    {
        if (playerCamera == null)
            playerCamera = Camera.main;
        if (bossCamera != null)
            bossCamera.enabled = false;
    }

    private void LateUpdate()
    {
        if (!_active || bossCamera == null) return;
        if (followTarget == null)
        {
            var p = GameObject.FindGameObjectWithTag("Player");
            if (p == null) return;
            followTarget = p.transform;
        }

        // 死区跟随(同 CameraFollow:玩家在 targetX ± deadZoneHalf 内相机不动)
        float px = followTarget.position.x;
        float dx = px - _targetX;
        if (dx > deadZoneHalf)
            _targetX = px - deadZoneHalf;
        else if (dx < -deadZoneHalf)
            _targetX = px + deadZoneHalf;

        float desiredX = _targetX;
        float desiredY = followTarget.position.y + verticalOffset;

        // 范围限制(Boss 房内)
        if (roomBounds != null)
        {
            Bounds b = roomBounds.bounds;
            float halfH = bossCamera.orthographicSize;
            float halfW = halfH * bossCamera.aspect;
            desiredX = Mathf.Clamp(desiredX, b.min.x + halfW, b.max.x - halfW);
            desiredY = Mathf.Clamp(desiredY, b.min.y + halfH, b.max.y - halfH);
        }

        // 平滑跟随(玩家越快跟得越紧)
        float playerSpeed = 0f;
        var rb = followTarget.GetComponent<Rigidbody2D>();
        if (rb != null) playerSpeed = Mathf.Abs(rb.velocity.x);
        float smoothTime = Mathf.Lerp(0.3f, 0.08f, Mathf.Clamp01(playerSpeed / maxFollowSpeed));
        Vector3 desired = new Vector3(desiredX, desiredY, bossCamera.transform.position.z);
        bossCamera.transform.position = Vector3.SmoothDamp(bossCamera.transform.position, desired, ref _posVelocity, smoothTime);
    }

    /// <summary>进房:切到 Boss 相机(无缝衔接当前位置,再平滑跟随)</summary>
    public void EnterBossRoom()
    {
        if (bossCamera == null) return;

        _playerCamWasEnabled = playerCamera != null && playerCamera.enabled;

        if (playerCamera != null)
        {
            // 无缝:boss 相机从玩家相机当前位置/大小开始
            bossCamera.transform.position = playerCamera.transform.position;
            bossCamera.orthographicSize = playerCamera.orthographicSize;
        }
        bossCamera.orthographicSize = orthoSize;
        _targetX = followTarget != null ? followTarget.position.x + biasLeft
            : (playerCamera != null ? playerCamera.transform.position.x + biasLeft : bossCamera.transform.position.x + biasLeft);
        _posVelocity = Vector3.zero;

        if (playerCamera != null)
            playerCamera.enabled = false;
        bossCamera.enabled = true;
        _active = true;
    }

    /// <summary>出房(Boss 死亡):关 Boss 相机,恢复玩家相机</summary>
    public void ExitBossRoom()
    {
        _active = false;
        if (bossCamera != null)
            bossCamera.enabled = false;
        if (playerCamera != null)
            playerCamera.enabled = _playerCamWasEnabled;
    }
}
