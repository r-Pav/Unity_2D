using UnityEngine;

/// <summary>
/// 横板侧视角相机 — 带死区，人物默认偏左 1/3 屏幕位置
/// 
/// 原理：
///   camera 追踪一个 targetX，人物在 targetX ± deadZoneHalf 范围内自由移动
///   超出范围后 targetX 才跟着滑动，实现"死区"效果
///   初始 targetX = player.x + biasLeft，让人物出现在屏幕左侧
/// </summary>
[RequireComponent(typeof(Camera))]
public class CameraFollow : MonoBehaviour
{
    // ============================================================
    // Singleton 注册表（场景内唯一相机；调用方统一走 Instance）
    // ============================================================

    private static CameraFollow _instance;

    public static CameraFollow Instance
    {
        get
        {
            if (_instance == null)
                _instance = FindObjectOfType<CameraFollow>();
            return _instance;
        }
    }

    [Header("目标")]
    [SerializeField] private Transform target;

    [Header("相机")]
    [SerializeField] private float orthoSize = 5f;

    [Header("偏左偏移")]      // 越大，人物越靠左
    [SerializeField] private float biasLeft = 3f;

    [Header("死区")]
    [SerializeField] private float deadZoneHalf = 1.5f;

    [Header("追踪平滑")]
    [SerializeField] [Range(0.1f, 20f)] private float smoothSpeed = 4f;

    [Header("垂直偏移")]
    [SerializeField] private float verticalOffset = 1f;

    private Camera cam;
    private float targetX;   // 相机追踪的 X 位置（死区中心）

    // ── 震动 ──
    private bool isShaking;
    private float shakeTimer;
    private Vector3 shakeOffset;

    // ── 过场动画 ──
    private bool isOverriding;
    private Vector2 overridePosition;
    private float _savedOrthoSize;
    private float _targetZoom;
    private Vector3 _posVelocity;

    private void Start()
    {
        cam = GetComponent<Camera>();

        if (target == null)
        {
            GameObject p = GameObject.FindGameObjectWithTag("Player");
            if (p != null) target = p.transform;
        }

        cam.orthographic = true;
        cam.orthographicSize = orthoSize;

        // 初始：人物在屏幕左 1/3 处
        // camera 在 player.x + biasLeft 的位置 → 人物显示在屏幕左侧
        if (target != null)
            targetX = target.position.x + biasLeft;
    }

    private void LateUpdate()
    {
        if (target == null && !isOverriding) return;

        float desiredX;
        float desiredY;

        if (isOverriding)
        {
            desiredX = overridePosition.x;
            desiredY = overridePosition.y;
        }
        else
        {
            float px = target.position.x;

            // 人物相对死区中心的位置
            float dx = px - targetX;

            // 超出死区 → 推动 targetX
            if (dx > deadZoneHalf)
                targetX = px - deadZoneHalf;
            else if (dx < -deadZoneHalf)
                targetX = px + deadZoneHalf;

            desiredX = targetX;
            desiredY = target.position.y + verticalOffset;
        }

        // 相机平滑移动到目标位置
        Vector3 desired = new Vector3(desiredX, desiredY, -10f);
        Vector3 pos = Vector3.SmoothDamp(transform.position - shakeOffset, desired, ref _posVelocity, 0.3f);

        // ── 震动 ──
        if (isShaking)
        {
            shakeTimer -= Time.unscaledDeltaTime;
            shakeOffset = new Vector3(
                Random.Range(-1f, 1f) * 0.3f,
                Random.Range(-1f, 1f) * 0.3f,
                0f);
            if (shakeTimer <= 0f)
            {
                isShaking = false;
                shakeOffset = Vector3.zero;
            }
        }
        else
        {
            shakeOffset = Vector3.zero;
        }

        transform.position = pos + shakeOffset;

        // 过场时平滑缩放
        if (isOverriding)
            cam.orthographicSize = Mathf.Lerp(cam.orthographicSize, _targetZoom, 3f * Time.deltaTime);
    }

    /// <summary>触发相机震动</summary>
    public void Shake(float duration, float magnitude)
    {
        isShaking = true;
        shakeTimer = duration;
        shakeOffset = Vector3.zero;
    }

    /// <summary>相机聚焦到指定世界坐标（Boss 过场用）</summary>
    public void FocusOnPoint(Vector2 worldPos, float zoom)
    {
        _savedOrthoSize = cam.orthographicSize;
        isOverriding = true;
        overridePosition = worldPos;
        _targetZoom = zoom;
        _posVelocity = Vector3.zero;
    }

    /// <summary>恢复玩家跟随</summary>
    public void RestoreFollow()
    {
        if (target != null)
            targetX = target.position.x; // 同步死区中心，避免回弹
        isOverriding = false;
        cam.orthographicSize = _savedOrthoSize;
    }

    private void OnDrawGizmosSelected()
    {
        if (target == null) return;

        // 死区范围可视化
        Vector3 center = new Vector3(targetX, target.position.y, 0f);
        Gizmos.color = new Color(0, 1, 0, 0.12f);
        Gizmos.DrawCube(center, new Vector3(deadZoneHalf * 2, orthoSize * 2, 0f));

        // 边界线
        Gizmos.color = Color.green;
        float top = target.position.y + orthoSize;
        float bot = target.position.y - orthoSize;
        Vector3 left = Vector3.left * deadZoneHalf + Vector3.forward * 0.1f;
        Vector3 right = Vector3.right * deadZoneHalf + Vector3.forward * 0.1f;
        Gizmos.DrawLine(center + left + Vector3.up * orthoSize,
                        center + left + Vector3.down * orthoSize);
        Gizmos.DrawLine(center + right + Vector3.up * orthoSize,
                        center + right + Vector3.down * orthoSize);
    }
}
