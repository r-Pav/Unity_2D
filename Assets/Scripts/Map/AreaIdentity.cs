using UnityEngine;

/// <summary>
/// 地区身份组件 — 挂在 Area 根 GameObject 上,给地区一个稳定的 areaId 标识与显示名。
/// 
/// 石碑系统用途:
/// - areaId 是传送/存档的稳定 key(与 Area 根 GameObject 名字一致,如 "Area_default"),
///   之后 WaypointTrigger 在 Awake 从父级读它,避免每块石碑手动拖引用;
/// - displayName 是 UI(传送页/复活页)显示用的名字,如「初始之地」,存档不存显示名。
/// 
/// 注册表:Awake 自注册到 ZoneManager(areaId → Area 根 GameObject 映射),
/// OnDestroy 反注册——传送执行时手里只有 areaId,靠映射表拿 Area 根做显隐,免去每次扫描。
/// </summary>
public class AreaIdentity : MonoBehaviour
{
    [Header("地区身份")]
    [Tooltip("地区唯一 id(建议与 GameObject 名一致,如 Area_default);传送/存档用此 key")]
    [SerializeField] private string areaId;

    [Tooltip("地区显示名(传送页/复活页列表显示,如「初始之地」;存档不存显示名)")]
    [SerializeField] private string displayName;

    /// <summary>地区唯一 id(只读,注册/查询用)</summary>
    public string AreaId => areaId;

    /// <summary>地区显示名(只读,UI 用;未填则回退 GameObject 名)</summary>
    public string DisplayName => string.IsNullOrEmpty(displayName) ? gameObject.name : displayName;

    private void Awake()
    {
        // 自注册:空 id 的 Area 不注册(编辑器未配置时静默跳过,避免空 key 污染映射表)
        if (string.IsNullOrEmpty(areaId)) return;
        if (ZoneManager.Instance != null)
            ZoneManager.Instance.RegisterArea(areaId, gameObject);
    }

    private void OnDestroy()
    {
        if (string.IsNullOrEmpty(areaId)) return;
        if (ZoneManager.Instance != null)
            ZoneManager.Instance.UnregisterArea(areaId);
    }
}
