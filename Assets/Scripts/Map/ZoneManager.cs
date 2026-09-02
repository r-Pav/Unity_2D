using System.Collections.Generic;
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
/// 
/// [2026-09-02 石碑系统] areaId ↔ Area 根 映射 + 当前地区状态(T1+T3):
/// T1:RegisterArea/UnregisterArea/GetAreaRoot——AreaIdentity.Awake 自注册,传送执行时手里只有 areaId,
///     靠映射表拿 Area 根做显隐(免每次扫描场景)。
/// T3:CurrentAreaId 运行时状态源(唯一写入口本类):管道到达/传送完成 → NotifyAreaEntered(写+广播
///     AreaEnterEvent → SaveSystem.AutoSave);读档恢复 → SetCurrentAreaSilent(只写不广播,防反向覆盖)。
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
    // areaId ↔ Area 根 映射(石碑系统 T1 加;AreaIdentity.Awake/OnDestroy 自注册)
    // ============================================================

    /// <summary>areaId → Area 根 GameObject:传送执行按 areaId 拿根做显隐,免去每次扫描场景</summary>
    private readonly Dictionary<string, GameObject> _areaRoots = new Dictionary<string, GameObject>();

    /// <summary>注册 Area 根(AreaIdentity.Awake 调);空 id / 空根忽略,重复注册覆盖(新值生效)</summary>
    public void RegisterArea(string areaId, GameObject root)
    {
        if (string.IsNullOrEmpty(areaId) || root == null) return;
        _areaRoots[areaId] = root;
    }

    /// <summary>反注册 Area 根(AreaIdentity.OnDestroy 调)</summary>
    public void UnregisterArea(string areaId)
    {
        if (string.IsNullOrEmpty(areaId)) return;
        _areaRoots.Remove(areaId);
    }

    /// <summary>按 areaId 取 Area 根;未注册(Area 根没挂 AreaIdentity 或 id 不匹配)LogWarning 并返回 null</summary>
    public GameObject GetAreaRoot(string areaId)
    {
        if (string.IsNullOrEmpty(areaId))
        {
            Debug.LogWarning("[ZoneManager] GetAreaRoot 收到空 areaId,返回 null");
            return null;
        }

        if (_areaRoots.TryGetValue(areaId, out var root))
            return root;

        Debug.LogWarning($"[ZoneManager] GetAreaRoot 未找到 areaId={areaId}(Area 根需挂 AreaIdentity 且 areaId 一致),返回 null");
        return null;
    }

    // ============================================================
    // 当前地区运行时状态(石碑系统 T3;唯一写入口 = 本类)
    // 区分两个写版本:广播版(NotifyAreaEntered)与静默版(SetCurrentAreaSilent)。
    // 读档恢复必须走静默版——广播版会触发 AreaEnterEvent → SaveSystem.AutoSave,
    // 反向覆盖刚读的档(方案风险 R5)。
    // ============================================================

    /// <summary>玩家当前所在地区 areaId(运行时状态源;存档 areaName 写入/传送页显示用)</summary>
    public string CurrentAreaId { get; private set; }

    /// <summary>新游戏/未知时兜底的初始地区 id(对应场景 Area_default 根;编辑器可改)</summary>
    [SerializeField] private string initialAreaId = "Area_default";

    /// <summary>未序列化/场景未配置时兜底的默认首区(防止 CurrentAreaId 空串)</summary>
    private const string DefaultAreaId = "Area_default";

    private void Awake()
    {
        // CurrentAreaId 兜底:任何 Read/写入口之前,运行时状态源先落到初始地区。
        // 之后由管道到达(NotifyAreaEntered)或读档(SetCurrentAreaSilent)覆盖。
        CurrentAreaId = string.IsNullOrEmpty(initialAreaId) ? DefaultAreaId : initialAreaId;
    }

    /// <summary>
    /// 静默写状态:只更新 CurrentAreaId,不广播任何事件。
    /// 用途:读档恢复(LoadGame)——若走广播版会触发 AutoSave 反向覆盖刚读的档(风险 R5)。
    /// 空 id 忽略,保持现值(与 initialAreaId 兜底配合,旧档 areaName=\"\" 场景不破坏状态)。
    /// </summary>
    public void SetCurrentAreaSilent(string areaId)
    {
        if (string.IsNullOrEmpty(areaId)) return;
        CurrentAreaId = areaId;
    }

    /// <summary>
    /// 广播版写入口:写状态 + 触发 AreaEnterEvent(SaveSystem 已订阅 → AutoSave 到自动槽)。
    /// 调用方:管道到达(AreaChannelTrigger 收尾)、传送完成(TeleportFlow T5)。
    /// 空 id 忽略(与 SetCurrentAreaSilent 一致,不广播)。
    /// </summary>
    public void NotifyAreaEntered(string areaId)
    {
        if (string.IsNullOrEmpty(areaId)) return;
        SetCurrentAreaSilent(areaId);
        EventBus.Trigger(new AreaEnterEvent());
    }

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
