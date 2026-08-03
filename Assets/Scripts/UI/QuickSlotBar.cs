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
///   5. 右键清空快捷槽
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
        public Text stackText;
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
                // 背包 → 快捷栏：绑定
                inv.SetQuickSlot(targetSlot, sourceIndex);
                break;

            case DragSourceContainer.QuickSlot:
                // 快捷栏 → 快捷栏：交换绑定
                if (targetSlot != sourceIndex)
                {
                    ItemInstance itemA = inv.GetQuickSlot(sourceIndex);
                    ItemInstance itemB = inv.GetQuickSlot(targetSlot);

                    // 清空双方
                    inv.ClearQuickSlot(sourceIndex);
                    inv.ClearQuickSlot(targetSlot);

                    // 重新绑定（通过查找物品在背包中的索引）
                    if (itemA != null)
                    {
                        for (int j = 0; j < inv.PlayerItems.Count; j++)
                        {
                            if (inv.GetPlayerItem(j) == itemA)
                            {
                                inv.SetQuickSlot(targetSlot, j);
                                break;
                            }
                        }
                    }
                    if (itemB != null)
                    {
                        for (int j = 0; j < inv.PlayerItems.Count; j++)
                        {
                            if (inv.GetPlayerItem(j) == itemB)
                            {
                                inv.SetQuickSlot(sourceIndex, j);
                                break;
                            }
                        }
                    }
                }
                break;
        }
    }

    public void OnPointerEnter(PointerEventData eventData)
    {
        if (!DragSession.IsDragging) return;

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
        int slotIndex = GetSlotIndexFromEvent(eventData);
        if (slotIndex < 0) return;

        InventoryManager inv = InventoryManager.Instance;
        if (inv == null) return;

        if (eventData.button == PointerEventData.InputButton.Left)
        {
            // 左键使用
            inv.UseQuickSlot(slotIndex);
        }
        else if (eventData.button == PointerEventData.InputButton.Right)
        {
            // 右键清空绑定
            inv.ClearQuickSlot(slotIndex);
        }
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
