using TMPro;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;

/// <summary>
/// 快捷栏 UI — 挂 QuickSlotBar GameObject
/// 管理 2 个快捷消耗品槽位（QuickSlot_0 / QuickSlot_1）
/// 
/// 职责：
///   1. 显示快捷槽绑定的消耗品图标和数量
///   2. 点击使用消耗品（减少堆叠）
///   3. IDropHandler — 接受从背包 ItemCell 拖入的消耗品（绑定快捷栏）
///   4. IBeginDragHandler — 可从快捷槽拖出（清空绑定）
///   5. 快捷键 1 / 2 使用（按键配在 InventoryManager 的 quickSlotKey1/2）
/// </summary>
public class QuickSlotBar : MonoBehaviour, IDropHandler, IBeginDragHandler, IDragHandler, IEndDragHandler,
    IPointerClickHandler, IPointerEnterHandler, IPointerExitHandler
{
    // ============================================================
    // 内部类型
    // ============================================================

    [System.Serializable]
    private class QuickSlotRef
    {
        public Image iconImage;
        public TMP_Text stackText;   // 2026-09-26 由 UnityEngine.UI.Text 改 TMP_Text：场景里是 Text (TMP)，旧类型永远拖不进来
        public Image highlightOverlay;
        public Image emptyBackground;
    }

    // ============================================================
    // 配置
    // ============================================================

    [Header("槽位引用")]
    [Tooltip("QuickSlot_0 的 UI 组件")]
    [SerializeField] private QuickSlotRef slot0;

    [Tooltip("QuickSlot_1 的 UI 组件")]
    [SerializeField] private QuickSlotRef slot1;

    [Header("幽灵图标")]
    [Tooltip("拖拽幽灵透明度")]
    [SerializeField] [Range(0.1f, 1f)] private float ghostAlpha = 0.7f;

    // ============================================================
    // 运行时状态
    // ============================================================

    private Canvas _parentCanvas;
    private RectTransform _ghostRect;
    private int _hoveredSlotIndex = -1; // 当前鼠标悬停的槽位索引

    private static readonly Color s_highlightColor = new Color(0f, 1f, 0f, 0.25f);
    private static readonly Color s_invalidColor = new Color(1f, 0f, 0f, 0.25f);

    private QuickSlotRef[] _slots;

    // ============================================================
    // 生命周期
    // ============================================================

    private void Awake()
    {
        _parentCanvas = GetComponentInParent<Canvas>();
        _slots = new QuickSlotRef[] { slot0, slot1 };
    }

    private void OnEnable()
    {
        InventoryManager inv = InventoryManager.Instance;
        if (inv != null)
        {
            inv.OnQuickSlotsChanged += RefreshDisplay;
            inv.OnInventoryChanged += RefreshDisplay; // 背包变化影响快捷栏物品数量
        }
        RefreshDisplay();
    }

    private void OnDisable()
    {
        InventoryManager inv = InventoryManager.Instance;
        if (inv != null)
        {
            inv.OnQuickSlotsChanged -= RefreshDisplay;
            inv.OnInventoryChanged -= RefreshDisplay;
        }
    }

    // ============================================================
    // 显示刷新
    // ============================================================

    /// <summary>刷新两个快捷槽的显示</summary>
    public void RefreshDisplay()
    {
        InventoryManager inv = InventoryManager.Instance;
        if (inv == null) return;

        for (int i = 0; i < 2; i++)
        {
            QuickSlotRef slot = _slots[i];
            if (slot == null) continue;

            ItemInstance item = inv.GetQuickSlot(i);
            bool hasItem = item != null && item.IsValid;

            // 图标
            if (slot.iconImage != null)
            {
                if (hasItem)
                {
                    slot.iconImage.sprite = item.template.icon;
                    slot.iconImage.color = Color.white;
                    slot.iconImage.enabled = true;
                }
                else
                {
                    slot.iconImage.sprite = null;
                    slot.iconImage.enabled = false;
                }
            }

            // 数量
            if (slot.stackText != null)
            {
                slot.stackText.text = hasItem && item.stackSize > 1 ? item.stackSize.ToString() : "";
            }

            // 空背景
            if (slot.emptyBackground != null)
                slot.emptyBackground.gameObject.SetActive(!hasItem);
        }
    }

    // ============================================================
    // IDropHandler — 接受拖入消耗品
    // ============================================================

    public void OnDrop(PointerEventData eventData)
    {
        ClearAllHighlights();

        if (!DragSession.IsDragging) return;

        int targetSlot = _hoveredSlotIndex;
        if (targetSlot < 0 || targetSlot > 1) return;

        DragSourceContainer sourceContainer = DragSession.SourceContainer;
        int sourceIndex = DragSession.SourceIndex;
        ItemInstance draggedItem = DragSession.DraggedItem;

        InventoryManager inv = InventoryManager.Instance;
        if (inv == null) return;

        // 验证：只接受消耗品
        if (draggedItem == null || draggedItem.template == null) return;
        if (draggedItem.template.category != ItemCategory.Consumable) return;

        switch (sourceContainer)
        {
            case DragSourceContainer.Inventory:
                // 背包格 → 快捷槽：搬入/堆叠（2026-09-26 由「引用绑定」改为搬运，带快捷槽上限）
                inv.MoveBackpackToQuickSlot(targetSlot, sourceIndex);
                break;

            case DragSourceContainer.QuickSlot:
                // 快捷槽 → 快捷槽：直接交换两边内容
                inv.SwapQuickSlots(sourceIndex, targetSlot);
                break;
        }
    }

    public void OnPointerEnter(PointerEventData eventData)
    {
        // 悬停(非拖拽)显示槽内物品详情(2026-09-22);拖拽中不弹,那是拖放判定
        if (!DragSession.IsDragging)
        {
            ShowQuickSlotTooltip(GetSlotIndexFromEvent(eventData));
            return;
        }

        // 判断鼠标在哪个槽位上（通过 GameObject 名称判断）
        _hoveredSlotIndex = GetSlotIndexFromEvent(eventData);

        ItemInstance draggedItem = DragSession.DraggedItem;
        bool valid = draggedItem != null
            && draggedItem.template != null
            && draggedItem.template.category == ItemCategory.Consumable;

        SetSlotHighlight(_hoveredSlotIndex, true, valid);
    }

    public void OnPointerExit(PointerEventData eventData)
    {
        SetSlotHighlight(_hoveredSlotIndex, false);
        _hoveredSlotIndex = -1;
        UITooltip.Hide();
    }

    /// <summary>悬停时把快捷槽里的物品交给 tooltip(空槽不弹)</summary>
    private void ShowQuickSlotTooltip(int slotIndex)
    {
        if (slotIndex < 0) return;

        InventoryManager inv = InventoryManager.Instance;
        if (inv == null) return;

        ItemInstance item = inv.GetQuickSlot(slotIndex);
        if (item == null || item.template == null) return;

        UITooltip.ShowItem(item.template, (RectTransform)transform);
    }

    // ============================================================
    // IBeginDragHandler / IDragHandler / IEndDragHandler
    // ============================================================

    public void OnBeginDrag(PointerEventData eventData)
    {
        int slotIndex = GetSlotIndexFromEvent(eventData);
        if (slotIndex < 0) return;

        InventoryManager inv = InventoryManager.Instance;
        if (inv == null) return;

        ItemInstance item = inv.GetQuickSlot(slotIndex);
        if (item == null) return;

        DragSession.BeginDrag(DragSourceContainer.QuickSlot, slotIndex, item);

        // [Phase5] 使用对象池创建幽灵图标
        _ghostRect = DragSession.GetGhost(_parentCanvas, item.template.icon, ghostAlpha, 0.9f);

        // 初始位置
        RectTransformUtility.ScreenPointToLocalPointInRectangle(
            _parentCanvas.transform as RectTransform,
            Input.mousePosition,
            _parentCanvas.worldCamera,
            out Vector2 localPoint);
        _ghostRect.localPosition = localPoint + new Vector2(15f, -15f);
    }

    public void OnDrag(PointerEventData eventData)
    {
        if (!DragSession.IsDragging || _ghostRect == null || _parentCanvas == null) return;

        RectTransformUtility.ScreenPointToLocalPointInRectangle(
            _parentCanvas.transform as RectTransform,
            eventData.position,
            _parentCanvas.worldCamera,
            out Vector2 localPoint);
        _ghostRect.localPosition = localPoint + new Vector2(15f, -15f);
    }

    public void OnEndDrag(PointerEventData eventData)
    {
        // [Phase5] 归还幽灵图标到对象池
        DragSession.ReturnGhost();
        _ghostRect = null;
        DragSession.EndDrag();
    }

    // ============================================================
    // IPointerClickHandler — 左键使用 / 右键清空
    // ============================================================

    public void OnPointerClick(PointerEventData eventData)
    {
        // HUD 是战斗界面：只做「左键使用」，不做任何背包管理操作（2026-09-26 去掉右键放回）
        if (eventData.button != PointerEventData.InputButton.Left) return;

        int slotIndex = GetSlotIndexFromEvent(eventData);
        if (slotIndex < 0) return;

        InventoryManager inv = InventoryManager.Instance;
        if (inv == null) return;

        inv.UseQuickSlot(slotIndex);
    }

    // ============================================================
    // 内部方法
    // ============================================================

    /// <summary>根据 PointerEventData 判断悬停在哪个槽位上</summary>
    private int GetSlotIndexFromEvent(PointerEventData eventData)
    {
        if (eventData.pointerEnter != null)
        {
            string name = eventData.pointerEnter.name;
            if (name.Contains("QuickSlot_0") || name == "QuickSlot_0") return 0;
            if (name.Contains("QuickSlot_1") || name == "QuickSlot_1") return 1;
        }
        return -1;
    }

    private void SetSlotHighlight(int index, bool active, bool valid = true)
    {
        if (index < 0 || index > 1) return;
        QuickSlotRef slot = _slots[index];
        if (slot?.highlightOverlay == null) return;

        slot.highlightOverlay.gameObject.SetActive(active);
        slot.highlightOverlay.color = valid ? s_highlightColor : s_invalidColor;
    }

    private void ClearAllHighlights()
    {
        SetSlotHighlight(0, false);
        SetSlotHighlight(1, false);
    }

    private void CreateDragGhost(ItemInstance item)
    {
        if (_parentCanvas == null) return;

        GameObject ghostObj = new GameObject("QuickSlotDragGhost");
        ghostObj.transform.SetParent(_parentCanvas.transform, false);
        ghostObj.transform.SetAsLastSibling();

        Image ghostImage = ghostObj.AddComponent<Image>();
        ghostImage.raycastTarget = false;

        if (item?.template.icon != null)
        {
            ghostImage.sprite = item.template.icon;
            ghostImage.SetNativeSize();
        }

        Color c = ghostImage.color;
        c.a = ghostAlpha;
        ghostImage.color = c;

        _ghostRect = ghostObj.GetComponent<RectTransform>();

        RectTransformUtility.ScreenPointToLocalPointInRectangle(
            _parentCanvas.transform as RectTransform,
            Input.mousePosition,
            _parentCanvas.worldCamera,
            out Vector2 localPoint);
        _ghostRect.localPosition = localPoint + new Vector2(15f, -15f);
    }

    private void DestroyDragGhost()
    {
        if (_ghostRect != null)
        {
            Destroy(_ghostRect.gameObject);
            _ghostRect = null;
        }
    }
}
