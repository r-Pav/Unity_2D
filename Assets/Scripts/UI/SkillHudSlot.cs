using TMPro;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;

/// <summary>
/// [P7.1] HUD 槽位组件 — 挂在 SkillConfigPanel 右栏每个 Slot 根节点上。
/// 负责：显示当前装备技能 + 拖拽源（拖出） + 拖拽目标（接受拖入）。
/// </summary>
public class SkillHudSlot : MonoBehaviour, IBeginDragHandler, IDragHandler, IEndDragHandler, IDropHandler
{
    // ============================================================
    // Inspector 绑定
    // ============================================================

    [Header("显示元素")]
    [SerializeField] private TMP_Text keyLabel;
    [SerializeField] private Image icon;
    [SerializeField] private TMP_Text nameText;
    [SerializeField] private TMP_Text levelText;

    [Header("空槽视觉")]
    [SerializeField] private Image emptySlotBackground;
    [SerializeField] private Color emptyColor = new Color(0.3f, 0.3f, 0.3f, 0.5f);
    [SerializeField] private Color filledColor = new Color(0.15f, 0.15f, 0.25f, 0.8f);
    [SerializeField] private Color dropHighlightColor = new Color(0.3f, 0.5f, 0.3f, 0.6f);

    [Header("拖拽")]
    [SerializeField] private CanvasGroup canvasGroup;
    [SerializeField] private float dragAlpha = 0.6f;

    [Header("快捷键标签")]
    [SerializeField] private string keyLabelString = "Q";

    // ============================================================
    // 运行时状态
    // ============================================================

    public int HudIndex { get; private set; } = -1;
    public OwnedSkillEntry CurrentEntry { get; private set; }

    private SkillConfigUI _configUI;
    private GameObject _dragGhost;
    private RectTransform _dragGhostRect;

    /// <summary>
    /// 跨槽位拖拽的源（静态共享）。
    /// 用于 Update() 中检测其他槽位是否正在拖拽并高亮本槽。
    /// </summary>
    private static SkillHudSlot _currentDragSource;

    private void OnDestroy()
    {
        CleanupDragGhost();
    }

    private void OnDisable()
    {
        CleanupDragGhost();
        if (_currentDragSource == this)
            _currentDragSource = null;
    }

    private void CleanupDragGhost()
    {
        if (_dragGhost != null)
        {
            Destroy(_dragGhost);
            _dragGhost = null;
            _dragGhostRect = null;
        }
    }

    /// <summary>由 SkillConfigUI.Awake 调用，绑定索引和父面板引用</summary>
    public void Initialize(int hudIndex, SkillConfigUI configUI)
    {
        HudIndex = hudIndex;
        _configUI = configUI;
        if (keyLabel != null) keyLabel.text = keyLabelString;
    }

    // ============================================================
    // 显示刷新（由 SkillConfigUI 驱动）
    // ============================================================

    /// <summary>从 SkillPool 拉取当前槽位数据并刷新显示</summary>
    public void RefreshFromPool(SkillPool pool)
    {
        if (pool == null) return;
        CurrentEntry = pool.GetHudSkill(HudIndex);
        UpdateDisplay();
    }

    private void UpdateDisplay()
    {
        bool hasSkill = CurrentEntry != null && CurrentEntry.skillData != null;

        if (icon != null)
        {
            if (hasSkill)
            {
                var active = CurrentEntry.skillData as ActiveSkillData;
                icon.sprite = active != null
                    ? active.GetIconForLevel(CurrentEntry.level)
                    : CurrentEntry.skillData.icon;
                icon.enabled = true;
            }
            else
            {
                icon.sprite = null;
                icon.enabled = false;
            }
        }

        if (nameText != null)
            nameText.text = hasSkill ? CurrentEntry.skillData.skillName : "空";

        if (levelText != null)
            levelText.text = hasSkill ? $"Lv{CurrentEntry.level}" : "";

        if (emptySlotBackground != null)
            emptySlotBackground.color = hasSkill ? filledColor : emptyColor;
    }

    /// <summary>拖入悬停时的高亮效果</summary>
    public void SetDropHighlight(bool active)
    {
        if (emptySlotBackground != null)
            emptySlotBackground.color = active
                ? dropHighlightColor
                : (CurrentEntry != null && CurrentEntry.skillData != null ? filledColor : emptyColor);
    }

    // ============================================================
    // 拖拽源（拖出）— IBeginDragHandler / IDragHandler / IEndDragHandler
    // ============================================================

    public void OnBeginDrag(PointerEventData eventData)
    {
        // 空槽不可拖出
        if (CurrentEntry == null || CurrentEntry.skillData == null) return;

        _currentDragSource = this;

        // 禁用自身射线，确保能 hit 到下方的 drop 目标
        if (canvasGroup != null)
        {
            canvasGroup.alpha = dragAlpha;
            canvasGroup.blocksRaycasts = false;
        }

        // 创建拖拽幽灵
        _dragGhost = CreateDragGhost();
        if (_dragGhost != null)
        {
            _dragGhost.transform.SetParent(GetCanvasRoot(), false);
            _dragGhost.transform.SetAsLastSibling();
        }
    }

    public void OnDrag(PointerEventData eventData)
    {
        if (_dragGhostRect != null)
            _dragGhostRect.position = eventData.position;
    }

    public void OnEndDrag(PointerEventData eventData)
    {
        // 恢复射线
        if (canvasGroup != null)
        {
            canvasGroup.alpha = 1f;
            canvasGroup.blocksRaycasts = true;
        }

        // 销毁幽灵
        if (_dragGhost != null)
        {
            Destroy(_dragGhost);
            _dragGhost = null;
            _dragGhostRect = null;
        }

        // 检查是否拖到了有效目标。
        // OnDrop 在 OnEndDrag 之前触发，如果已有目标处理则无事发生。
        // 若没有命中任何 HUD 槽：
        //   - 命中卸载区 → 卸载技能
        //   - 否则 → 恢复原位（防止误触）
        if (_currentDragSource == this)
        {
            var targetSlot = GetDropTarget(eventData);
            if (targetSlot == null)
            {
                // 只有明确拖到卸载区才卸载
                if (_configUI != null && _configUI.IsOverUnequipZone(eventData.position))
                    _configUI.HandleSkillUnequip(HudIndex);
            }
        }

        _currentDragSource = null;
    }

    // ============================================================
    // 拖拽目标（接受拖入）— IDropHandler
    // ============================================================

    public void OnDrop(PointerEventData eventData)
    {
        SetDropHighlight(false);

        // 检查拖入来源：左栏技能列表条目
        var listEntry = eventData.pointerDrag?.GetComponent<SkillListEntry>();
        if (listEntry != null && listEntry.CurrentEntry != null)
        {
            _configUI?.HandleSkillDrop(HudIndex, listEntry.CurrentEntry.id, null);
            return;
        }

        // 检查拖入来源：另一个 HUD 槽位 → 交换
        var sourceSlot = eventData.pointerDrag?.GetComponent<SkillHudSlot>();
        if (sourceSlot != null && sourceSlot != this && sourceSlot.CurrentEntry != null)
        {
            _configUI?.HandleSkillDrop(HudIndex, sourceSlot.CurrentEntry.id, sourceSlot);
            return;
        }
    }

    // ============================================================
    // 高亮反馈（悬停检测）
    // ============================================================

    private void Update()
    {
        // 如果当前有其他 SkillHudSlot 正在拖拽，检测鼠标是否在本槽上
        if (_currentDragSource != null && _currentDragSource != this)
        {
            bool hovering = RectTransformUtility.RectangleContainsScreenPoint(
                (RectTransform)transform, Input.mousePosition, null);
            SetDropHighlight(hovering);
        }
    }

    // ============================================================
    // 辅助方法
    // ============================================================

    private GameObject CreateDragGhost()
    {
        var ghost = new GameObject("DragGhost", typeof(RectTransform), typeof(CanvasGroup), typeof(Image));
        _dragGhostRect = ghost.GetComponent<RectTransform>();
        _dragGhostRect.sizeDelta = new Vector2(64, 64);

        var img = ghost.GetComponent<Image>();
        if (CurrentEntry?.skillData != null)
        {
            var active = CurrentEntry.skillData as ActiveSkillData;
            img.sprite = active != null
                ? active.GetIconForLevel(CurrentEntry.level)
                : CurrentEntry.skillData.icon;
        }
        img.raycastTarget = false;

        var cg = ghost.GetComponent<CanvasGroup>();
        cg.alpha = 0.7f;
        cg.blocksRaycasts = false;

        return ghost;
    }

    private Transform GetCanvasRoot()
    {
        var canvas = GetComponentInParent<Canvas>();
        return canvas != null ? canvas.transform : transform.root;
    }

    /// <summary>检查拖拽结束时鼠标下方是否有有效的 HUD 槽位</summary>
    private SkillHudSlot GetDropTarget(PointerEventData eventData)
    {
        if (eventData.pointerEnter == null) return null;
        return eventData.pointerEnter.GetComponentInParent<SkillHudSlot>();
    }
}
