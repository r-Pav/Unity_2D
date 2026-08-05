using UnityEngine;
using System.Collections;

/// <summary>
/// 地区管理器 — 管道地区切换系统。
/// 设计:玩家进管道时显示目标地区(前后场景同时加载),玩家自由走过管道,
/// 出管道时关闭来源地区。无自动移动、无锁输入、无过场动画;
/// 镜头缩放仅作过渡提示,不影响玩家控制。
/// </summary>
public class ZoneManager : MonoBehaviour
{
    // ============================================================
    // Singleton
    // ============================================================

    private static ZoneManager _instance;
    public static ZoneManager Instance
    {
        get
        {
            if (_instance == null)
                _instance = FindObjectOfType<ZoneManager>();
            return _instance;
        }
    }

    // ============================================================
    // 配置
    // ============================================================

    [Header("地区引用")]
    [Tooltip("初始地区(仅记录用,可留空)")]
    [SerializeField] private GameObject currentArea;

    // ============================================================
    // 运行时状态
    // ============================================================

    private float _originalOrtho;   // 镜头原始缩放(第一次拉近时记录)
    private bool _orthoSaved;

    // ============================================================
    // 地区显隐
    // ============================================================

    /// <summary>显示地区(进管道时显示目标地区,前后场景同时加载)</summary>
    public void ShowArea(GameObject area)
    {
        if (area != null)
            area.SetActive(true);
    }

    /// <summary>关闭地区(出管道时关闭来源地区)</summary>
    public void HideArea(GameObject area)
    {
        if (area != null)
            area.SetActive(false);
    }

    // ============================================================
    // 镜头缩放(过渡提示,不锁玩家控制)
    // ============================================================

    /// <summary>进管道:镜头拉近</summary>
    public void ZoomIn(float targetOrtho, float speed = 3f)
    {
        var cam = Camera.main;
        if (cam == null) return;
        if (!_orthoSaved)
        {
            _originalOrtho = cam.orthographicSize;
            _orthoSaved = true;
        }
        StopAllCoroutines();
        StartCoroutine(ZoomRoutine(targetOrtho, speed));
    }

    /// <summary>出管道:镜头恢复原始缩放</summary>
    public void ZoomOut(float speed = 3f)
    {
        var cam = Camera.main;
        if (cam == null) return;
        float target = _orthoSaved ? _originalOrtho : cam.orthographicSize;
        StopAllCoroutines();
        StartCoroutine(ZoomRoutine(target, speed));
    }

    private IEnumerator ZoomRoutine(float target, float speed)
    {
        var cam = Camera.main;
        if (cam == null) yield break;
        while (Mathf.Abs(cam.orthographicSize - target) > 0.05f)
        {
            cam.orthographicSize = Mathf.Lerp(cam.orthographicSize, target, speed * Time.deltaTime);
            yield return null;
        }
        cam.orthographicSize = target;
    }
}
