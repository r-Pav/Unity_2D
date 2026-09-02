using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 死亡面板 — 玩家死亡时弹出。
/// [2026-08-10] PauseGame 改 false：死亡后游戏世界继续运行（敌人照常行动），仅玩家输入被锁（PlayerDeadState.LocksInput）。
/// [2026-08-10] 复活按钮：绑定后点击 → PlayerHealth.Revive()（原地复活，不清位置；背包装备保留，身上 4 槽已掉落）。
/// [2026-09-02 石碑系统 T6] 复活页新增第三区「按 Area 传送」：
///   - OnEnable 刷新 WaypointSystem.ActivatedAreas() 去重列表，每激活 Area 一行按钮（抄 TeleportPanel 模式）；
///   - 行按钮文本 = displayName（AreaIdentity.DisplayName，取不到回退 areaId）；当前区（CurrentAreaId）行禁用并标注「当前」；
///   - 无激活 Area → 显示 emptyHint；新字段全部可空：areaTeleportContent 为空 = 整个传送区不显示（旧版零回归）；
///   - 点 Area 行 → WaypointSystem.RequestTeleport(areaId, ignoreCombat:true)（死亡入口，跳过战斗门 R4）。
///     先 Revive 再传送由 TeleportFlow.onFullyBlack 黑场全黑内执行（黑场盖住回出生点闪移，方案 §3.6/§5）；
///     面板关闭由流程内 CloseTopPanel 负责；被拒（IsTeleporting/无锚点）面板保持，玩家可改选或点原地复活兜底。
/// 挂 Canvas 下的 DeathPanel GameObject。页面内容你自己在 Unity 里搭。
/// 注意：PanelManager 负责订阅 PlayerDeathEvent 并打开此面板，本类只声明 IPanel 接口 + 按钮绑定。
/// </summary>
public class DeathPanel : MonoBehaviour, IPanel
{
    public PanelType PanelType => PanelType.FullScreen;
    public bool PauseGame => false;   // 死亡不暂停游戏，世界继续
    public bool LockInput => true;
    public bool ShowCursor => true;

    [Header("复活")]
    [Tooltip("复活按钮（你在页面里加的）— 点击原地复活；拖到此处")]
    [SerializeField] private Button reviveButton;

    [Header("读档")]
    [Tooltip("读档按钮（页面里新增 Btn_Load）— 点击打开读档面板")]
    [SerializeField] private Button loadButton;
    [Tooltip("读档面板（Canvas > Panels > LoadPanel）")]
    [SerializeField] private GameObject loadPanel;

    [Header("按区传送（石碑系统 T6；字段可空 = 不显示传送区）")]
    [Tooltip("Area 按钮容器（ScrollView > Content；为空 = 不显示传送区，复活页维持旧版）")]
    [SerializeField] private RectTransform areaTeleportContent;

    [Tooltip("Area 行按钮 prefab（Button > TMP 文本；运行时克隆；为空则只显示空态提示）")]
    [SerializeField] private GameObject areaTeleportButtonPrefab;

    [Tooltip("无激活 Area 提示（文案如「尚未激活任何石碑」；为空 = 不显示提示）")]
    [SerializeField] private GameObject areaTeleportEmptyHint;

    [Tooltip("当前 Area 标签（区标题旁，显示所在区显示名；可空/可不接）")]
    [SerializeField] private TMP_Text currentAreaLabel;

    /// <summary>动态创建的 Area 行按钮缓存（OnEnable 重建前 / OnDisable 销毁用，防残留）</summary>
    private readonly List<GameObject> _generatedAreaButtons = new List<GameObject>();

    private void OnEnable()
    {
        if (reviveButton != null)
            reviveButton.onClick.AddListener(OnReviveClicked);
        if (loadButton != null)
            loadButton.onClick.AddListener(OnLoadClicked);
        RefreshAreaTeleport();
    }

    private void OnDisable()
    {
        if (reviveButton != null)
            reviveButton.onClick.RemoveListener(OnReviveClicked);
        if (loadButton != null)
            loadButton.onClick.RemoveListener(OnLoadClicked);
        DestroyGeneratedAreaButtons();
    }

    private void OnReviveClicked()
    {
        // 原地复活：Revive() 不动位置；背包装备保留，身上 4 槽装备已在死亡动画末帧掉落
        PlayerHealth health = PlayerController.Instance != null
            ? PlayerController.Instance.GetComponent<PlayerHealth>()
            : null;
        health?.Revive();

        // 关闭死亡面板
        PanelManager.Instance?.CloseTopPanel();
    }

    private void OnLoadClicked()
    {
        // 打开读档面板（LoadPanel，SaveLoadPanel mode=Load）
        if (loadPanel != null)
            PanelManager.Instance?.OpenPanel(loadPanel);
    }

    // ============================================================
    // 按区传送列表（石碑系统 T6；逻辑抄 TeleportPanel.RefreshList，加当前区禁用）
    // ============================================================

    /// <summary>
    /// 每次打开刷新：先销毁上一轮动态按钮 → 按 ActivatedAreas() 重建。
    /// 容器未接线（areaTeleportContent 为空）→ 整个传送区不显示，复活页行为与 T5 前完全一致（零回归）。
    /// 无激活 Area / WaypointSystem 未挂 / prefab 未接线 → 只显示 emptyHint，不生成按钮。
    /// </summary>
    private void RefreshAreaTeleport()
    {
        DestroyGeneratedAreaButtons();

        // 容器未拖引用（T7 编辑器接线前）：不显示传送区，复活页维持旧版（零回归）
        if (areaTeleportContent == null)
            return;

        // 标题旁当前区标签（可空）：显示 ZoneManager.CurrentAreaId 对应显示名；取不到则空文本
        if (currentAreaLabel != null)
        {
            string current = ZoneManager.Instance != null ? ZoneManager.Instance.CurrentAreaId : null;
            currentAreaLabel.text = string.IsNullOrEmpty(current) ? string.Empty : ResolveAreaDisplayName(current);
        }

        var system = WaypointSystem.Instance;
        var areas = system != null ? system.ActivatedAreas() : null;
        string currentAreaId = ZoneManager.Instance != null ? ZoneManager.Instance.CurrentAreaId : null;

        if (areas == null || areas.Count == 0)
        {
            // 无激活 Area（含 WaypointSystem 未挂）：空态
            if (areaTeleportEmptyHint != null) areaTeleportEmptyHint.SetActive(true);
            return;
        }

        if (areaTeleportButtonPrefab == null)
        {
            // 有激活 Area 但 prefab 未接线（T7 前）：空态提示 + 告警一次，不崩溃
            if (areaTeleportEmptyHint != null) areaTeleportEmptyHint.SetActive(true);
            Debug.LogWarning("[DeathPanel] 有已激活石碑但 areaTeleportButtonPrefab 未拖引用(检查 Inspector 接线),无法生成按钮", this);
            return;
        }

        if (areaTeleportEmptyHint != null) areaTeleportEmptyHint.SetActive(false);

        for (int i = 0; i < areas.Count; i++)
        {
            string areaId = areas[i];
            if (string.IsNullOrEmpty(areaId)) continue;
            bool isCurrent = !string.IsNullOrEmpty(currentAreaId) && areaId == currentAreaId;
            SpawnAreaButton(areaId, isCurrent);
        }
    }

    /// <summary>克隆一行 Area 按钮：文本 = displayName（无 AreaIdentity 回退 areaId）；当前区行禁用并标注「当前」</summary>
    private void SpawnAreaButton(string areaId, bool isCurrent)
    {
        GameObject row = Instantiate(areaTeleportButtonPrefab, areaTeleportContent);
        _generatedAreaButtons.Add(row);

        // 行文本：找按钮子物体上的 TMP 文本
        TMP_Text label = row.GetComponentInChildren<TMP_Text>(true);
        if (label != null)
            label.text = isCurrent ? ResolveAreaDisplayName(areaId) + "(当前)" : ResolveAreaDisplayName(areaId);

        Button button = row.GetComponent<Button>();
        if (button == null)
        {
            Debug.LogWarning($"[DeathPanel] AreaButtonPrefab({row.name}) 缺 Button 组件,该行不可点", this);
            return;
        }

        // 当前区禁用：原地复活已覆盖该语义（回出生点=当前区），避免无意义传送（方案 §5）
        if (isCurrent)
            button.interactable = false;

        // 闭包捕获 areaId（本方法参数为每次迭代独立局部，捕获安全）
        string captured = areaId;
        button.onClick.AddListener(() => OnAreaClicked(captured));
    }

    // ============================================================
    // 交互（T6：接真实传送；复活页入口 ignoreCombat=true）
    // ============================================================

    /// <summary>
    /// 点击 Area 行按钮 → WaypointSystem.RequestTeleport(areaId, ignoreCombat:true)：
    /// 死亡入口跳过战斗门（尸体无仇恨，R4）；先 Revive 再瞬移由 TeleportFlow.onFullyBlack 黑场全黑内完成
    /// （黑场盖住 Revive 回出生点的闪移，方案 §3.6/§5），故本处不先调 Revive。
    /// 面板关闭由传送流程（onFullyBlack 内 CloseTopPanel）处理；
    /// 若 RequestTeleport 因 IsTeleporting / 无锚点被拒 → 面板保持开启，玩家可改选其他 Area 或点原地复活兜底。
    /// </summary>
    private void OnAreaClicked(string areaId)
    {
        var system = WaypointSystem.Instance;
        if (system == null)
        {
            Debug.LogWarning("[DeathPanel] WaypointSystem.Instance 为空(场景未挂 WaypointSystem?),无法传送", this);
            return;
        }
        system.RequestTeleport(areaId, ignoreCombat: true);
    }

    /// <summary>
    /// areaId → 显示名：经 ZoneManager 注册表拿 Area 根 → 读 AreaIdentity.DisplayName；
    /// 根未注册 / 未挂 AreaIdentity / ZoneManager 未挂时回退 areaId 本身（不抛不崩）。
    /// </summary>
    private static string ResolveAreaDisplayName(string areaId)
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

    /// <summary>销毁全部动态生成的按钮（OnEnable 刷新前调用防残留;OnDisable 兜底）</summary>
    private void DestroyGeneratedAreaButtons()
    {
        for (int i = 0; i < _generatedAreaButtons.Count; i++)
        {
            if (_generatedAreaButtons[i] != null)
                Destroy(_generatedAreaButtons[i]);
        }
        _generatedAreaButtons.Clear();
    }
}
