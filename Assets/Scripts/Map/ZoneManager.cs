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
}
