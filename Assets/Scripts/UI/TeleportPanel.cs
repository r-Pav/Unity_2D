using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 传送页(石碑系统 T4 骨架 + T5 接真实传送)— 挂 Panels 下 TeleportPanel GameObject(默认 inactive,PanelManager 自动注册)。
/// IPanel:FullScreen + PauseGame + LockInput + ShowCursor(与 SavePanel 一致)。
///
/// 职责:列出所有已激活 Area(WaypointSystem.ActivatedAreas 去重结果),每 Area 一行按钮;
/// 按钮文本 = displayName(AreaIdentity,未挂/找不到回退 areaId);
/// 点击行按钮 = WaypointSystem.RequestTeleport(areaId)(T5 起:黑场 TeleportFlow 真实传送,
/// 面板在传送流程内自动关闭;战斗中被 RequestTeleport 内部拒绝,面板保持开启)。
///
/// 动态按钮生命周期(抄 SaveLoadPanel 缓存模式):OnEnable 每次打开先销毁旧按钮再按当前激活列表重建;
/// OnDisable 兜底销毁,防止残留/重复。Prefab/引用由 T7 编辑器接线,未接线时只显示空态提示,不报错。
/// </summary>
public class TeleportPanel : MonoBehaviour, IPanel
{
    public PanelType PanelType => PanelType.FullScreen;
    public bool PauseGame => true;
    public bool LockInput => true;
    public bool ShowCursor => true;

    [Header("列表")]
    [Tooltip("Area 按钮容器(纵向 LayoutGroup 的 Content)")]
    [SerializeField] private RectTransform areaListContent;

    [Tooltip("Area 行按钮 prefab(Button > TMP 文本;运行时克隆)")]
    [SerializeField] private GameObject areaButtonPrefab;

    [Tooltip("空态提示(无激活 Area 时显示;骨架期未接线 prefab 时也显示)")]
    [SerializeField] private GameObject emptyHint;

    [Tooltip("当前 Area 标签(标题旁,显示所在区显示名;可空/可不接)")]
    [SerializeField] private TMP_Text currentAreaLabel;

    /// <summary>动态创建的 Area 行按钮缓存(OnEnable 重建前 / OnDisable 销毁用,防残留)</summary>
    private readonly List<GameObject> _generatedButtons = new List<GameObject>();

    private void OnEnable()
    {
        RefreshList();
    }

    private void OnDisable()
    {
        DestroyGeneratedButtons();
    }

    // ============================================================
    // 列表刷新
    // ============================================================

    /// <summary>
    /// 每次打开刷新:先销毁上一轮动态按钮 → 按 ActivatedAreas() 重建。
    /// 无激活 Area / WaypointSystem 未挂 / prefab 未接线 → 只显示 emptyHint,不生成按钮。
    /// </summary>
    private void RefreshList()
    {
        DestroyGeneratedButtons();

        // 标题旁当前区标签(可空):显示 ZoneManager.CurrentAreaId 对应显示名;取不到则空文本
        if (currentAreaLabel != null)
        {
            string current = ZoneManager.Instance != null ? ZoneManager.Instance.CurrentAreaId : null;
            currentAreaLabel.text = string.IsNullOrEmpty(current) ? string.Empty : ResolveDisplayName(current);
        }

        var system = WaypointSystem.Instance;
        var areas = system != null ? system.ActivatedAreas() : null;

        if (areas == null || areas.Count == 0)
        {
            // 无激活 Area(含 WaypointSystem 未挂):空态
            if (emptyHint != null) emptyHint.SetActive(true);
            return;
        }

        if (areaButtonPrefab == null || areaListContent == null)
        {
            // 有激活 Area 但未接线 prefab/容器(T7 前):空态提示 + 告警一次,不崩溃
            if (emptyHint != null) emptyHint.SetActive(true);
            Debug.LogWarning("[TeleportPanel] 有已激活石碑但 areaButtonPrefab/areaListContent 未拖引用(检查 Inspector 接线),无法生成按钮", this);
            return;
        }

        if (emptyHint != null) emptyHint.SetActive(false);

        for (int i = 0; i < areas.Count; i++)
        {
            string areaId = areas[i];
            if (string.IsNullOrEmpty(areaId)) continue;
            SpawnAreaButton(areaId);
        }
    }

    /// <summary>克隆一行 Area 按钮:文本 = displayName(无 AreaIdentity 回退 areaId);点击 = 骨架行为(关面板+日志)</summary>
    private void SpawnAreaButton(string areaId)
    {
        GameObject row = Instantiate(areaButtonPrefab, areaListContent);
        _generatedButtons.Add(row);

        // 行文本:找按钮子物体上的 TMP 文本
        TMP_Text label = row.GetComponentInChildren<TMP_Text>(true);
        if (label != null)
            label.text = ResolveDisplayName(areaId);

        Button button = row.GetComponent<Button>();
        if (button == null)
        {
            Debug.LogWarning($"[TeleportPanel] AreaButtonPrefab({row.name}) 缺 Button 组件,该行不可点", this);
            return;
        }

        // 闭包捕获 areaId(本方法参数为每次迭代独立局部,捕获安全)
        string captured = areaId;
        button.onClick.AddListener(() => OnAreaClicked(captured));
    }

    // ============================================================
    // 交互(T5 起接真实传送)
    // ============================================================

    /// <summary>
    /// 点击 Area 行按钮 → WaypointSystem.RequestTeleport(areaId):
    /// 活着入口 ignoreCombat=false → 内部再验 !InCombat(战斗中被拒,面板保持开启可点别的/ESC 关)。
    /// 传送成功后面板由 TeleportFlow 黑场流程内 CloseTopPanel 关闭,本处不再手动关。
    /// </summary>
    private void OnAreaClicked(string areaId)
    {
        var system = WaypointSystem.Instance;
        if (system == null)
        {
            Debug.LogWarning("[TeleportPanel] WaypointSystem.Instance 为空(场景未挂 WaypointSystem?),无法传送", this);
            return;
        }
        system.RequestTeleport(areaId);
    }

    /// <summary>
    /// areaId → 显示名:经 ZoneManager 注册表拿 Area 根 → 读 AreaIdentity.DisplayName;
    /// 根未注册 / 未挂 AreaIdentity / ZoneManager 未挂时回退 areaId 本身(不抛不崩)。
    /// </summary>
    private static string ResolveDisplayName(string areaId)
    {
        if (string.IsNullOrEmpty(areaId)) return areaId ?? string.Empty;

        var zm = ZoneManager.Instance;
        if (zm == null) return areaId;

        GameObject root = zm.GetAreaRoot(areaId);
        if (root != null)
        {
            var identity = root.GetComponent<AreaIdentity>();
            if (identity != null)
                return identity.DisplayName;
        }
        return areaId;
    }

    /// <summary>销毁全部动态生成的按钮(OnEnable 刷新前调用防残留;OnDisable 兜底)</summary>
    private void DestroyGeneratedButtons()
    {
        for (int i = 0; i < _generatedButtons.Count; i++)
        {
            if (_generatedButtons[i] != null)
                Destroy(_generatedButtons[i]);
        }
        _generatedButtons.Clear();
    }
}
