using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// 消耗品快捷槽锚点（2026-09-26 加）— 挂在背包面板里的 2 个快捷消耗槽 GameObject 上。
///
/// 与技能系统同构：
///   SkillPool.hudSlotAssignments  ↔  SkillConfigPanel 的 SkillHudSlot[]  ↔  HUD SkillBarPanel
///   InventoryManager.quickSlots   ↔  本组件（背包页锚点）                ↔  HUD QuickSlotBar
///
/// 数据唯一真相源是 InventoryManager.quickSlots，本组件只负责「拖拽改绑定 + 显示」；
/// HUD 上的 QuickSlotBar 订阅同一个 OnQuickSlotsChanged 事件，两边天然同步。
///
/// 交互（搬运模型，物品真正住进快捷槽，与装备槽同口径）：
///   背包 ItemCell 拖入本槽 → 搬入并堆叠（受快捷槽上限约束，只接受 ItemCategory.Consumable）
///   本槽拖到另一个锚点槽   → 两槽交换内容
///   本槽拖到背包格         → 堆叠进那一格的同种，其次放进那一格，再退回首空格
///   本槽拖到空白处         → 放回背包首个空格
///   右键点击本槽           → 使用（走 InventoryManager.UseQuickSlot；左键留给拖拽）
///   悬停                   → tooltip 显示物品详情
///
/// 显示元素取法沿用项目约定「字段优先 + 子物体名兜底」：
///   Icon / Count / Empty / Highlight，以及自身或子物体上的 CanvasGroup。
/// </summary>
public class ConsumableQuickSlot : MonoBehaviour,
    IBeginDragHandler, IDragHandler, IEndDragHandler, IDropHandler,
    IPointerEnterHandler, IPointerExitHandler, IPointerClickHandler
{
    // ============================================================
    // Inspector 绑定
    // ============================================================

    [Header("槽位索引（0 = 第一个快捷消耗槽，1 = 第二个；由 InventoryPanel 自动注入）")]
    [SerializeField] private int slotIndex;

    [Header("显示元素（留空则按子物体名查找：Icon / Count / Empty / Highlight）")]
    [Tooltip("物品图标 Image")]
    [SerializeField] private Image icon;

    [Tooltip("堆叠数量文本（TMP）")]
    [SerializeField] private TMP_Text countText;

    [Tooltip("空槽底图（有物品时隐藏）")]
    [SerializeField] private Image emptyBackground;

    [Tooltip("拖入高亮层（可选）")]
    [SerializeField] private Image highlightOverlay;

    [Tooltip("拖拽时用于变半透明的 CanvasGroup（可选，默认取自身）")]
    [SerializeField] private CanvasGroup canvasGroup;

    [Header("拖拽视觉")]
    [SerializeField] private float dragAlpha = 0.6f;
    [SerializeField] private float ghostAlpha = 0.7f;
    [SerializeField] private float ghostScale = 0.9f;

    // ============================================================
    // 子物体名字约定（兜底查找用）
    // ============================================================

    private const string k_IconChildName = "Icon";
    private const string k_CountChildName = "Count";
    private const string k_LegacyCountChildName = "StackCount";
    private const string k_EmptyChildName = "Empty";
    private const string k_LegacyEmptyChildName = "Background";
    private const string k_HighlightChildName = "Highlight";

    private bool _refsResolved;

    // ============================================================
    // 运行时状态
    // ============================================================

    private RectTransform _ghostRect;

    /// <summary>拖动结束后判断本次是否被某个槽接住（接住则不卸载）。静态因为拖拽是跨对象的过程。</summary>
    private static bool s_dropConsumed;

    // ============================================================
    // 生命周期
    // ============================================================

    /// <summary>由 InventoryPanel.Awake 注入槽位索引</summary>
    public void Initialize(int index)
    {
        slotIndex = index;
        ResolveRefsOnce();
        Refresh();
    }

    public int SlotIndex => slotIndex;

    private void OnEnable()
    {
        InventoryManager inv = InventoryManager.Instance;
        if (inv != null) inv.OnQuickSlotsChanged += Refresh;

        ResolveRefsOnce();
        Refresh();
    }

    private void OnDisable()
    {
        InventoryManager inv = InventoryManager.Instance;
        if (inv != null) inv.OnQuickSlotsChanged -= Refresh;
    }

    // ============================================================
    // 显示刷新
    // ============================================================

    /// <summary>从 InventoryManager.quickSlots 拉取本槽绑定并刷新图标/数量/空底图</summary>
    public void Refresh()
    {
        if (!_refsResolved) ResolveRefsOnce();

        InventoryManager inv = InventoryManager.Instance;
        ItemInstance item = inv != null ? inv.GetQuickSlot(slotIndex) : null;
        bool hasItem = item != null && item.IsValid;

        if (icon != null)
        {
            icon.sprite = hasItem && item.template != null ? item.template.icon : null;
            icon.enabled = hasItem;
        }

        if (countText != null)
            countText.text = hasItem && item.stackSize > 1 ? item.stackSize.ToString() : string.Empty;

        if (emptyBackground != null)
            emptyBackground.gameObject.SetActive(!hasItem);
    }

    /// <summary>字段优先 + 子物体名兜底的一次性解析（与 ItemCell.ResolveRefsOnce 同口径）</summary>
    private void ResolveRefsOnce()
    {
        if (_refsResolved) return;
        _refsResolved = true;

        if (icon == null)
        {
            Transform t = transform.Find(k_IconChildName);
            if (t != null) icon = t.GetComponent<Image>();
            if (icon == null) icon = GetComponent<Image>();   // 兜底：自身 Image
        }

        if (countText == null)
        {
            Transform t = transform.Find(k_CountChildName);
            if (t == null) t = transform.Find(k_LegacyCountChildName);
            if (t != null) countText = t.GetComponent<TMP_Text>();
        }

        if (emptyBackground == null)
        {
            Transform t = transform.Find(k_EmptyChildName);
            if (t == null) t = transform.Find(k_LegacyEmptyChildName);
            if (t != null) emptyBackground = t.GetComponent<Image>();
        }

        if (highlightOverlay == null)
        {
            Transform t = transform.Find(k_HighlightChildName);
            if (t != null) highlightOverlay = t.GetComponent<Image>();
        }

        if (canvasGroup == null)
            canvasGroup = GetComponent<CanvasGroup>();

        if (highlightOverlay != null)
            highlightOverlay.gameObject.SetActive(false);
    }

    private void SetHighlight(bool active)
    {
        if (highlightOverlay == null) return;
        highlightOverlay.gameObject.SetActive(active);
    }

    // ============================================================
    // 拖拽源（拖出）— IBeginDrag / IDrag / IEndDrag
    // ============================================================

    public void OnBeginDrag(PointerEventData eventData)
    {
        s_dropConsumed = false;   // 先重置，避免空槽拖拽时残留上一轮状态

        InventoryManager inv = InventoryManager.Instance;
        ItemInstance item = inv != null ? inv.GetQuickSlot(slotIndex) : null;
        if (item == null) return;   // 空槽不可拖

        DragSession.BeginDrag(DragSourceContainer.QuickSlot, slotIndex, item);

        if (canvasGroup != null)
        {
            canvasGroup.alpha = dragAlpha;
            canvasGroup.blocksRaycasts = false;   // 不挡射线，保证下层背包格能收到 drop
        }

        Canvas canvas = GetComponentInParent<Canvas>();
        _ghostRect = DragSession.GetGhost(
            canvas,
            item.template != null ? item.template.icon : null,
            ghostAlpha,
            ghostScale);
        MoveGhost(eventData.position);
    }

    public void OnDrag(PointerEventData eventData)
    {
        MoveGhost(eventData.position);
    }

    public void OnEndDrag(PointerEventData eventData)
    {
        if (canvasGroup != null)
        {
            canvasGroup.alpha = 1f;
            canvasGroup.blocksRaycasts = true;
        }

        DragSession.ReturnGhost();
        _ghostRect = null;

        // 没被任何槽接住 = 拖到空白处 → 把物品放回背包首个空格。
        // （拖到背包格时由 ItemCell.HandleDropFromQuickSlot 负责搬运，那时本槽已空，这里调用是空操作）
        if (!s_dropConsumed && DragSession.SourceContainer == DragSourceContainer.QuickSlot)
            InventoryManager.Instance?.ReturnQuickSlotToBackpack(slotIndex);

        s_dropConsumed = false;
        DragSession.EndDrag();
        SetHighlight(false);
    }

    private void MoveGhost(Vector2 screenPos)
    {
        if (_ghostRect == null) return;

        Canvas canvas = GetComponentInParent<Canvas>();
        if (canvas == null) return;

        RectTransform canvasRect = canvas.transform as RectTransform;
        if (canvasRect == null) return;

        RectTransformUtility.ScreenPointToLocalPointInRectangle(
            canvasRect, screenPos, canvas.worldCamera, out Vector2 localPoint);
        _ghostRect.localPosition = localPoint;
    }

    // ============================================================
    // 拖拽目标（接受拖入）— IDropHandler
    // ============================================================

    public void OnDrop(PointerEventData eventData)
    {
        s_dropConsumed = true;
        SetHighlight(false);

        if (!DragSession.IsDragging) return;

        InventoryManager inv = InventoryManager.Instance;
        if (inv == null) return;

        int sourceIndex = DragSession.SourceIndex;

        switch (DragSession.SourceContainer)
        {
            case DragSourceContainer.Inventory:
                // 背包格 → 本槽：搬入/堆叠（受快捷槽上限约束；两边同种会自动叠起来）
                inv.MoveBackpackToQuickSlot(slotIndex, sourceIndex);
                break;

            case DragSourceContainer.QuickSlot:
                // 另一个锚点槽 → 本槽：交换绑定
                inv.SwapQuickSlots(sourceIndex, slotIndex);
                break;
        }
    }

    // ============================================================
    // 悬停 / 点击
    // ============================================================

    public void OnPointerEnter(PointerEventData eventData)
    {
        if (DragSession.IsDragging)
        {
            // 拖动中：只做高亮提示，不弹 tooltip
            if (DragSession.SourceContainer == DragSourceContainer.Inventory ||
                DragSession.SourceContainer == DragSourceContainer.QuickSlot)
                SetHighlight(true);
            return;
        }

        InventoryManager inv = InventoryManager.Instance;
        ItemInstance item = inv != null ? inv.GetQuickSlot(slotIndex) : null;
        if (item == null || item.template == null) return;

        UITooltip.ShowItem(item.template, (RectTransform)transform);
    }

    public void OnPointerExit(PointerEventData eventData)
    {
        SetHighlight(false);
        UITooltip.Hide();
    }

    public void OnPointerClick(PointerEventData eventData)
    {
        // 2026-09-26 改：与背包格统一，右键才是使用；左键留给拖拽
        if (eventData.button != PointerEventData.InputButton.Right) return;
        if (DragSession.IsDragging) return;

        // 右键使用本槽绑定的消耗品
        InventoryManager.Instance?.UseQuickSlot(slotIndex);
    }
}
