using Cinemachine;
using System.Collections;
using UnityEngine;

/// <summary>
/// 相机区域管理器 — 挂 Virtual Camera 物体上（与 CinemachineVirtualCamera + CinemachineConfiner2D 同物体）。
/// 玩家进入某 CameraZone → 切换 Confiner Bounding Shape + 平滑缩放 VCam Lens。
/// Start 时一次性绑定出生区域（玩家出生在区域内不触发 OnTriggerEnter，需初始化）。
/// 事件驱动，无每帧检测。
/// </summary>
public class CameraZoneManager : MonoBehaviour
{
    public static CameraZoneManager Instance { get; private set; }

    [Header("切换")]
    [Tooltip("Confiner 边界切换延迟（秒）：切边界前的缓冲（边界覆盖管道口时相机已在边界内，此值只需防极端滞后）")]
    [SerializeField] private float shapeSwitchDelay = 0.15f;

    private CinemachineVirtualCamera _vcam;
    private CinemachineConfiner2D _confiner;
    private Coroutine _zoomRoutine;
    private Coroutine _shapeRoutine;

    private void Awake()
    {
        Instance = this;
        _vcam = GetComponent<CinemachineVirtualCamera>();
        if (_vcam != null)
            _confiner = GetComponent<CinemachineConfiner2D>();
    }

    private IEnumerator Start()
    {
        // 等一帧（玩家/物理初始化完成）再检测出生区域
        yield return null;

        var player = GameObject.FindGameObjectWithTag("Player");
        if (player != null)
            RefreshZoneAt(player.transform.position);
    }

    /// <summary>
    /// 按世界坐标查询玩家所在区域并切换（空间查询 OverlapPoint，不依赖 collider 间接触——
    /// 规避团结引擎 PolygonCollider2D 触发不工作的问题）。由 AreaChannelTrigger（Box trigger，
    /// 已验证正常）在玩家进出管道时驱动；Start 时用于出生区域绑定。
    /// </summary>
    public void RefreshZoneAt(Vector2 worldPos)
    {
        Collider2D[] hits = Physics2D.OverlapPointAll(worldPos);
        // 命中多个区域（collider 扩大后重叠常见）时，选 orthoSize 最大的（地区 4 > 管道 3）——
        // 出生/初始化时优先绑定地区，避免绑到管道
        CameraZone best = null;
        for (int i = 0; i < hits.Length; i++)
        {
            CameraZone zone = hits[i] != null ? hits[i].GetComponent<CameraZone>() : null;
            if (zone == null) continue;
            if (best == null || zone.TargetOrtho > best.TargetOrtho)
                best = zone;
        }
        if (best != null)
            EnterZone(best);
    }

    /// <summary>玩家进入区域：平滑缩放到该区域 orthoSize + 延迟切换 Confiner 边界</summary>
    public void EnterZone(CameraZone zone)
    {
        if (zone == null) return;

        // 缩放立即平滑过渡（orthoSize 插值，不影响相机位置）
        if (_vcam != null && zone.TargetOrtho > 0f)
        {
            if (_zoomRoutine != null) StopCoroutine(_zoomRoutine);
            _zoomRoutine = StartCoroutine(ZoomRoutine(zone.TargetOrtho, zone.ZoomSpeed));
        }

        // Confiner 边界延迟切换：等相机跟随到位再切，避免相机（阻尼滞后）在新边界外被 clamp 拉回造成跳变
        if (_confiner != null && zone.Bounds != null && zone.Bounds != _confiner.m_BoundingShape2D)
        {
            if (_shapeRoutine != null) StopCoroutine(_shapeRoutine);
            _shapeRoutine = StartCoroutine(SwitchShapeDelayed(zone.Bounds));
        }
    }

    private IEnumerator SwitchShapeDelayed(Collider2D bounds)
    {
        // 固定短延迟缓冲（边界 collider 应覆盖管道口——玩家出管道时相机已在边界内，切边界不跳变）
        if (shapeSwitchDelay > 0f)
        {
            float elapsed = 0f;
            while (elapsed < shapeSwitchDelay)
            {
                elapsed += Time.unscaledDeltaTime;
                yield return null;
            }
        }
        _confiner.m_BoundingShape2D = bounds;
        _confiner.InvalidateCache(); // 重算缓存，立即生效
    }

    private IEnumerator ZoomRoutine(float target, float speed)
    {
        float from = _vcam.m_Lens.OrthographicSize;
        float duration = Mathf.Abs(target - from) / Mathf.Max(0.05f, speed);
        float elapsed = 0f;

        while (elapsed < duration)
        {
            elapsed += Time.unscaledDeltaTime; // 暂停（timeScale=0）时也能播完
            _vcam.m_Lens.OrthographicSize = Mathf.Lerp(from, target, Mathf.Clamp01(elapsed / duration));
            yield return null;
        }
        _vcam.m_Lens.OrthographicSize = target;
    }
}
