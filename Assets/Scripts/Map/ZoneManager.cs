using UnityEngine;

/// <summary>
/// 地区管理器 — 管道地区切换系统。
/// 设计:玩家进管道时显示目标地区(前后场景同时加载),玩家自由走过管道,
/// 出管道时关闭来源地区。无自动移动、无锁输入、无过场动画;
/// 镜头缩放仅作过渡提示,不影响玩家控制。
/// 
/// [2026-08-13] 背景统一:随地区开关直接显示/隐藏(无淡变)。
/// 背景 = BackgroundScroller 无限平铺(Far/Mid) + 地区下 BG/Bg_Near(静态近景,随地区显隐)。
/// 背景移动边界(管道出口 clamp)仍由 ParallaxLayer 执行,防止背景被视差顶出场景地盘。
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
    // 地区显隐(背景随地区直接开关,无淡变)
    // ============================================================

    /// <summary>
    /// 显示地区(进管道时):地区 SetActive(true)。
    /// 背景统一处理——地区内所有 ParallaxLayer 重置回摆放原位置并停用(固定原位置,零位置计算),
    /// 背景随场景开关直接显示,不过管道位置不变。
    /// </summary>
    public void ShowArea(GameObject area)
    {
        if (area == null) return;
        area.SetActive(true);
        // 背景固定:重置回摆放原位置并停用视差计算(过管道位置不再漂移)
        var parallaxLayers = area.GetComponentsInChildren<ParallaxLayer>(true);
        for (int i = 0; i < parallaxLayers.Length; i++)
        {
            if (parallaxLayers[i] != null)
                parallaxLayers[i].ResetToOriginalAndDisable();
        }
    }

    /// <summary>关闭地区(出管道时):直接 SetActive(false),背景随场景关闭消失</summary>
    public void HideArea(GameObject area)
    {
        if (area == null) return;
        area.SetActive(false);
    }
}
