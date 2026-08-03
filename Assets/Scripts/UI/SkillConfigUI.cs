using TMPro;
using UnityEngine;
using UnityEngine.UI;
using System.Collections.Generic;

/// <summary>
/// [P7] 技能配置页面 — 挂在 SkillConfigPanel 上。
/// 左栏：SkillPool 中所有已拥有技能（滚动列表），支持拖拽到右栏装备。
/// 右栏：4 个 HUD 槽位，支持拖拽装备/交换/卸载。
///
/// 实现 IPanel 接口，由 PanelManager 自动发现并注册。
/// </summary>
public class SkillConfigUI : MonoBehaviour, IPanel
{
    PanelType IPanel.PanelType => PanelType.FullScreen;
    bool IPanel.PauseGame => true;
    bool IPanel.LockInput => true;
    bool IPanel.ShowCursor => true;

    // ============================================================
    // Inspector 绑定 — 左栏
    // ============================================================

    [Header("左栏 — 已拥有技能列表")]
    [SerializeField] private Transform skillListContainer;
    [Tooltip("技能条目模板 Prefab（需挂 SkillListEntry 组件）")]
    [SerializeField] private GameObject skillListItemPrefab;
    [SerializeField] private TMP_Text emptyHint;

    // ============================================================
    // Inspector 绑定 — 右栏 HUD 槽位组件
    // ============================================================

    [Header("HUD 槽位组件")]
    [Tooltip("4 个 HUD 槽位对象，每个需挂 SkillHudSlot 组件。按 Q/E/R/F 顺序拖入")]
    [SerializeField] private SkillHudSlot[] hudSlots = new SkillHudSlot[4];

    [Header("拖拽设置")]
    [Tooltip("拖拽幽灵的父容器（通常是 Canvas 根节点或本面板根节点）")]
    [SerializeField] private RectTransform dragGhostParent;

    [Header("卸载判定")]
    [Tooltip("RightColumn 的 RectTransform。拖拽 HUD 技能超出此区域 = 卸载。")]
    [SerializeField] private RectTransform rightColumnArea;

    // ============================================================
    // Inspector 绑定 — 页面跳转
    // ============================================================

    [Header("页面跳转")]
    [SerializeField] private Button toCraftBtn;
    [SerializeField] private Button toSkillTreeBtn;
    [SerializeField] private PanelManager panelManager;
    [SerializeField] private GameObject craftPanel;
    [SerializeField] private GameObject skillTreePanel;

    // ============================================================
    // 运行时引用
    // ============================================================

    private SkillManager skillManager;
    private SkillPool skillPool;

    // ============================================================
    // 生命周期
    // ============================================================

    private void Awake()
    {
        var player = PlayerController.Instance;
        if (player != null)
        {
            skillManager = player.GetComponent<SkillManager>();
            skillPool = player.GetComponent<SkillPool>();
        }

        // 初始化 HUD 槽位：注入 hudIndex 和 SkillConfigUI 引用
        for (int i = 0; i < hudSlots.Length; i++)
        {
            if (hudSlots[i] != null)
            {
                hudSlots[i].Initialize(i, this);
            }
        }

        // 页面跳转按钮（不变）
        if (panelManager == null) panelManager = PanelManager.Instance;
        toCraftBtn?.onClick.AddListener(() => panelManager?.OpenPanel(craftPanel));
        toSkillTreeBtn?.onClick.AddListener(() => panelManager?.OpenPanel(skillTreePanel));
    }

    private void OnEnable()
    {
        if (skillPool != null)
        {
            skillPool.OnPoolChanged += RefreshAll;
            skillPool.OnHudSlotChanged += RefreshHudSlot;
        }
        RefreshAll();
    }

    private void OnDisable()
    {
        if (skillPool != null)
        {
            skillPool.OnPoolChanged -= RefreshAll;
            skillPool.OnHudSlotChanged -= RefreshHudSlot;
        }
    }

    // ============================================================
    // 刷新逻辑
    // ============================================================

    private void RefreshAll()
    {
        RefreshLeftList();
        RefreshRightSlots();
    }

    /// <summary>左栏：刷新已拥有技能列表</summary>
    private void RefreshLeftList()
    {
        if (skillListContainer == null) return;

        foreach (Transform child in skillListContainer)
            Destroy(child.gameObject);

        var owned = skillPool?.GetOwnedSkills();
        if (owned == null || owned.Count == 0)
        {
            if (emptyHint != null) emptyHint.gameObject.SetActive(true);
            return;
        }

        // 过滤掉已装备到 HUD 槽位的技能
        var hudIds = new HashSet<string>(skillPool.GetHudAssignments());
        var available = new List<OwnedSkillEntry>();
        foreach (var entry in owned)
        {
            if (!hudIds.Contains(entry.id) || string.IsNullOrEmpty(entry.id))
                available.Add(entry);
        }

        if (available.Count == 0)
        {
            if (emptyHint != null) emptyHint.gameObject.SetActive(true);
            return;
        }

        if (emptyHint != null) emptyHint.gameObject.SetActive(false);

        foreach (var entry in available)
        {
            if (skillListItemPrefab == null)
            {
                Debug.LogError("[SkillConfigUI] skillListItemPrefab 未赋值！请在 SkillConfigPanel 下放一个 inactive 的条目并拖入此字段");
                break;
            }
            var item = Instantiate(skillListItemPrefab, skillListContainer);
            item.SetActive(true);  // 模板 inactive，克隆体需手动激活

            var itemScript = item.GetComponent<SkillListEntry>();
            if (itemScript != null)
            {
                itemScript.Setup(entry);
            }
            else
            {
                FillListItemFallback(item, entry);
            }
        }
    }

    /// <summary>
    /// 兜底填充：当 skillListItemPrefab 没有挂 SkillListEntry 组件时，
    /// 通过 Transform.Find 查找 Icon/Name/Level 等子元素填充。
    /// </summary>
    private void FillListItemFallback(GameObject item, OwnedSkillEntry entry)
    {
        var iconImg = item.transform.Find("Icon")?.GetComponent<Image>();
        var nameText = item.transform.Find("Name")?.GetComponent<TMP_Text>();
        var levelText = item.transform.Find("Level")?.GetComponent<TMP_Text>();

        if (iconImg != null && entry.skillData != null)
        {
            var active = entry.skillData as ActiveSkillData;
            iconImg.sprite = active != null ? active.GetIconForLevel(entry.level) : entry.skillData.icon;
            iconImg.enabled = true;
        }
        if (nameText != null) nameText.text = entry.skillData?.skillName ?? entry.id;
        if (levelText != null) levelText.text = $"Lv{entry.level}";
    }

    /// <summary>右栏：刷新 4 个 HUD 槽位显示</summary>
    private void RefreshRightSlots()
    {
        if (hudSlots == null) return;
        for (int i = 0; i < hudSlots.Length; i++)
        {
            if (hudSlots[i] != null)
                hudSlots[i].RefreshFromPool(skillPool);
        }
    }

    private void RefreshHudSlot(int index)
    {
        if (hudSlots != null && index >= 0 && index < hudSlots.Length && hudSlots[index] != null)
            hudSlots[index].RefreshFromPool(skillPool);
    }

    // ============================================================
    // 拖拽回调（由 SkillHudSlot 调用）
    // ============================================================

    /// <summary>
    /// 由 SkillHudSlot 在接收到 drop 时回调。
    /// 左栏技能拖入 → 装备；HUD 槽间拖入 → 交换。
    /// </summary>
    /// <param name="targetSlotIndex">目标 HUD 槽位索引</param>
    /// <param name="skillId">技能 ID（即 skillName）</param>
    /// <param name="sourceSlot">来源 HUD 槽位（null = 来自左栏）</param>
    public void HandleSkillDrop(int targetSlotIndex, string skillId, SkillHudSlot sourceSlot)
    {
        if (skillPool == null) return;

        if (sourceSlot != null)
        {
            int sourceIndex = sourceSlot.HudIndex;
            if (sourceIndex == targetSlotIndex) return;

            string targetSkillId = skillPool.GetHudAssignments()[targetSlotIndex];
            string sourceSkillId = skillPool.GetHudAssignments()[sourceIndex];

            skillPool.ClearHudSlot(sourceIndex);
            skillPool.ClearHudSlot(targetSlotIndex);

            if (!string.IsNullOrEmpty(sourceSkillId))
                skillPool.EquipToHud(targetSlotIndex, sourceSkillId);
            if (!string.IsNullOrEmpty(targetSkillId))
                skillPool.EquipToHud(sourceIndex, targetSkillId);
        }
        else
        {
            skillPool.EquipToHud(targetSlotIndex, skillId);
        }

        // 装备/交换后刷新左栏（已装备技能从池子中移除显示）
        RefreshLeftList();
    }

    /// <summary>
    /// 由 SkillHudSlot 在被拖到空白区时回调。
    /// </summary>
    public void HandleSkillUnequip(int hudSlotIndex)
    {
        skillPool?.ClearHudSlot(hudSlotIndex);
        RefreshLeftList();  // 卸载后技能回到左栏
    }

    /// <summary>
    /// 检查屏幕坐标是否在 RightColumn 之外（= 卸载区域）。
    /// rightColumnArea 为空时返回 false（不允许卸载）。
    /// </summary>
    public bool IsOverUnequipZone(Vector2 screenPoint)
    {
        if (rightColumnArea == null) return false;
        return !RectTransformUtility.RectangleContainsScreenPoint(
            rightColumnArea, screenPoint, null);
    }
}
