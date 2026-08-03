using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;

/// <summary>
/// 装备槽位 UI 组件
/// 挂 Slot_Weapon / Slot_Armor / Slot_Accessory_0 / Slot_Accessory_1
/// 
/// 职责：
///   1. IDropHandler — 接受拖入装备（仅匹配 slotType 的 Equipment 类物品）
///   2. IBeginDragHandler — 作为拖拽源，将装备从槽位拖回背包
///   3. 显示装备图标 + 槽位背景
///   4. 右键卸下装备到背包
///   5. 拖入高亮反馈
/// </summary>
public class EquipmentSlot : MonoBehaviour, IDropHandler, IBeginDragHandler, IDragHandler, IEndDragHandler,
    IPointerEnterHandler, IPointerExitHandler, IPointerClickHandler
{
    // ============================================================
    // 配置
    // ============================================================

    [Header("槽位类型")]
    [Tooltip("此槽位接受的装备类型")]
    [SerializeField] private EquipmentSlotType slotType = EquipmentSlotType.Weapon;

    public EquipmentSlotType SlotType => slotType;

    [Header("UI 组件")]
    [Tooltip("装备图标 Image")]
    [SerializeField] private Image iconImage;

    [Tooltip("空槽位背景图（有装备时隐藏，无装备时显示）")]
    [SerializeField] private Image emptyBackground;

    [Tooltip("拖入高亮覆盖层")]
    [SerializeField] private Image highlightOverlay;

    [Tooltip("槽位标签文本（显示 Weapon/Armor 等）")]
    [SerializeField] private Text labelText;

    [Header("拖拽视觉")]
    [Tooltip("拖拽幽灵透明度")]
    [SerializeField] [Range(0.1f, 1f)] private float ghostAlpha = 0.7f;

    // ============================================================
    // 运行时状态
    // ============================================================

    private Canvas _parentCanvas;
    private RectTransform _ghostRect;
    private static readonly Color s_highlightColor = new Color(0f, 1f, 0f, 0.25f); // 淡绿色高亮
    private static readonly Color s_invalidHighlightColor = new Color(1f, 0f, 0f, 0.25f); // 淡红色

    // ============================================================
    // 生命周期
    // ============================================================

    private void Awake()
    {
        _parentCanvas = GetComponentInParent<Canvas>();

        if (iconImage == null)
        {
            Transform iconChild = transform.Find("Icon");
            iconImage = iconChild != null ? iconChild.GetComponent<Image>() : GetComponent<Image>();
        }

        if (highlightOverlay != null)
            highlightOverlay.gameObject.SetActive(false);

        if (labelText != null)
            labelText.text = GetSlotLabel();
    }

    private void OnEnable()
    {
        RefreshDisplay();
    }

    // ============================================================
    // 公开方法
    // ============================================================

    /// <summary>刷新装备槽显示</summary>
    public void RefreshDisplay()
    {
        InventoryManager inv = InventoryManager.Instance;
        if (inv == null) return;

        ItemInstance equipped = inv.GetEquippedItem(slotType);
        bool hasEquip = equipped != null;

        // 图标
        if (iconImage != null)
        {
            if (hasEquip)
            {
                iconImage.sprite = equipped.template.icon;
                iconImage.color = Color.white;
                iconImage.enabled = true;
            }
            else
            {
                iconImage.sprite = null;
                iconImage.enabled = false;
            }
        }

        // 空槽背景
        if (emptyBackground != null)
            emptyBackground.gameObject.SetActive(!hasEquip);
    }

    // ============================================================
    // IDropHandler — 接受拖入装备
    // ============================================================

    public void OnDrop(PointerEventData eventData)
    {
        SetHighlight(false);

        if (!DragSession.IsDragging) return;

        DragSourceContainer sourceContainer = DragSession.SourceContainer;
        int sourceIndex = DragSession.SourceIndex;
        ItemInstance draggedItem = DragSession.DraggedItem;

        InventoryManager inv = InventoryManager.Instance;
        if (inv == null) return;

        // 验证：只接受物品类型为装备且槽位匹配
        if (draggedItem == null || draggedItem.template == null) return;
        if (draggedItem.template.category != ItemCategory.Equipment) return;
        if (draggedItem.template.slotType != slotType) return;

        switch (sourceContainer)
        {
            case DragSourceContainer.Inventory:
                // 背包 → 装备槽：装备
                inv.EquipItem(sourceIndex, slotType);
                RefreshDisplay();
                break;

            case DragSourceContainer.EquipmentSlot:
                // 装备槽 → 装备槽：交换装备
                SwapEquipment(inv, (EquipmentSlotType)sourceIndex);
                RefreshDisplay();
                break;

            case DragSourceContainer.Warehouse:
                // 仓库 → 装备槽：取出仓库物品再装备（两步）
                if (inv.WarehouseCount > sourceIndex)
                {
                    // 先从仓库取出到背包
                    inv.WithdrawFromWarehouse(sourceIndex);
                    // 找到刚才放入的位置并装备
                    int lastIdx = inv.PlayerItems.Count - 1;
                    for (int i = 0; i < inv.PlayerItems.Count; i++)
                    {
                        if (inv.GetPlayerItem(i) != null && inv.GetPlayerItem(i).template == draggedItem.template)
                        {
                            inv.EquipItem(i, slotType);
                            break;
                        }
                    }
                }
                break;
        }
    }

    public void OnPointerEnter(PointerEventData eventData)
    {
        if (!DragSession.IsDragging) return;

        ItemInstance draggedItem = DragSession.DraggedItem;
        bool valid = draggedItem != null
            && draggedItem.template != null
            && draggedItem.template.category == ItemCategory.Equipment
            && draggedItem.template.slotType == slotType;

        SetHighlight(true, valid);
    }

    public void OnPointerExit(PointerEventData eventData)
    {
        SetHighlight(false);
    }

    // ============================================================
    // IBeginDragHandler / IDragHandler / IEndDragHandler — 拖出装备
    // ============================================================

    public void OnBeginDrag(PointerEventData eventData)
    {
        InventoryManager inv = InventoryManager.Instance;
        if (inv == null) return;

        ItemInstance equipped = inv.GetEquippedItem(slotType);
        if (equipped == null) return;

        // 记录拖拽源为装备槽
        DragSession.BeginDrag(DragSourceContainer.EquipmentSlot, (int)slotType, equipped);

        // [Phase5] 使用对象池创建幽灵图标
        _ghostRect = DragSession.GetGhost(_parentCanvas, equipped.template.icon, ghostAlpha, 0.9f);

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
        if (!DragSession.IsDragging) return;

        if (_ghostRect != null && _parentCanvas != null)
        {
            RectTransformUtility.ScreenPointToLocalPointInRectangle(
                _parentCanvas.transform as RectTransform,
                eventData.position,
                _parentCanvas.worldCamera,
                out Vector2 localPoint);

            _ghostRect.localPosition = localPoint + new Vector2(15f, -15f);
        }
    }

    public void OnEndDrag(PointerEventData eventData)
    {
        // [Phase5] 归还幽灵图标到对象池
        DragSession.ReturnGhost();
        _ghostRect = null;
        DragSession.EndDrag();
        RefreshDisplay();
    }

    // ============================================================
    // IPointerClickHandler — 右键卸下装备
    // ============================================================

    public void OnPointerClick(PointerEventData eventData)
    {
        // 右键卸下
        if (eventData.button == PointerEventData.InputButton.Right)
        {
            InventoryManager inv = InventoryManager.Instance;
            if (inv == null) return;

            ItemInstance equipped = inv.GetEquippedItem(slotType);
            if (equipped == null) return;

            inv.UnequipItem(slotType);
            RefreshDisplay();
        }
    }

    // ============================================================
    // 内部方法
    // ============================================================

    /// <summary>交换两个装备槽的物品</summary>
    private void SwapEquipment(InventoryManager inv, EquipmentSlotType sourceSlot)
    {
        ItemInstance sourceItem = inv.GetEquippedItem(sourceSlot);
        ItemInstance targetItem = inv.GetEquippedItem(slotType);

        // 验证双方槽位类型互容
        if (sourceItem != null && sourceItem.template.slotType != slotType) return;
        if (targetItem != null && targetItem.template.slotType != sourceSlot) return;

        // 直接交换：先卸下双方装备（保留引用），再重新装备到对方槽位
        if (sourceItem != null) inv.UnequipItem(sourceSlot);
        if (targetItem != null) inv.UnequipItem(slotType);

        // 重新装备（UnequipItem 把物品放回背包末尾，从背包中找回并装备）
        if (sourceItem != null)
            EquipItemToSlotFromBackpack(inv, sourceItem, slotType);
        if (targetItem != null)
            EquipItemToSlotFromBackpack(inv, targetItem, sourceSlot);
    }

    /// <summary>从背包中找到指定物品并装备到目标槽位</summary>
    private void EquipItemToSlotFromBackpack(InventoryManager inv, ItemInstance targetItem, EquipmentSlotType targetSlot)
    {
        for (int i = 0; i < inv.PlayerItems.Count; i++)
        {
            if (inv.GetPlayerItem(i) == targetItem)
            {
                inv.EquipItem(i, targetSlot);
                return;
            }
        }
    }

    /// <summary>设置高亮状态</summary>
    private void SetHighlight(bool active, bool valid = true)
    {
        if (highlightOverlay == null) return;
        highlightOverlay.gameObject.SetActive(active);
        highlightOverlay.color = valid ? s_highlightColor : s_invalidHighlightColor;
    }

    /// <summary>创建拖拽幽灵</summary>
    private void CreateDragGhost(ItemInstance item)
    {
        if (_parentCanvas == null) return;

        GameObject ghostObj = new GameObject("EquipDragGhost");
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

    /// <summary>获取槽位标签文本</summary>
    private string GetSlotLabel()
    {
        return slotType switch
        {
            EquipmentSlotType.Weapon => "武器",
            EquipmentSlotType.Armor => "护甲",
            EquipmentSlotType.Accessory0 => "饰品1",
            EquipmentSlotType.Accessory1 => "饰品2",
            _ => "装备"
        };
    }
}
