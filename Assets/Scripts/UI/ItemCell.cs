using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;

/// <summary>
/// 物品格子 UI 组件 — 背包/仓库网格中的单个格子
/// 挂所有 ItemCell GameObject
/// 
/// 职责：
///   1. 显示物品图标、堆叠数量、稀有度边框
///   2. 实现 IDropHandler 接受从其他 ItemCell / EquipmentSlot / QuickSlot 的拖放
///   3. 处理拖入高亮反馈
///   4. 右键菜单（后续扩展）
/// 
/// 数据流：ItemCell 不直接持有数据，而是通过 ContainerType + SlotIndex
/// 从 InventoryManager 读取 ItemInstance
/// </summary>
[RequireComponent(typeof(Image))]
public class ItemCell : MonoBehaviour, IDropHandler, IPointerEnterHandler, IPointerExitHandler, IPointerClickHandler
{
    // ============================================================
    // 容器标识
    // ============================================================

    /// <summary>此格子所属的容器类型</summary>
    public DragSourceContainer ContainerType { get; set; } = DragSourceContainer.Inventory;

    /// <summary>在容器中的槽位索引</summary>
    public int SlotIndex { get; set; } = -1;

    // ============================================================
    // UI 组件引用
    // ============================================================

    [Header("UI 组件（Inspector 拖入或自动查找）")]
    [Tooltip("物品图标 Image")]
    [SerializeField] private Image iconImage;

    [Tooltip("堆叠数量 Text（TMP_Text 或普通 Text）")]
    [SerializeField] private Text stackText; // 兼容旧版 Text；后续可换 TMP_Text

    [Tooltip("稀有度边框 Image（可选）")]
    [SerializeField] private Image rarityFrame;

    [Tooltip("空槽位默认图标（可选，有物品时隐藏）")]
    [SerializeField] private Image emptySlotIcon;

    [Tooltip("拖入高亮覆盖层（可选，接受拖入时短亮）")]
    [SerializeField] private Image highlightOverlay;

    [Header("外观设置")]
    [Tooltip("空格子时的图标透明度")]
    [SerializeField] [Range(0f, 1f)] private float emptyAlpha = 0.3f;

    [Tooltip("堆叠数量 < 2 时隐藏数量文字")]
    [SerializeField] private bool hideSingleStack = true;

    // ============================================================
    // 运行时状态
    // ============================================================

    private static readonly Color s_highlightColor = new Color(1f, 1f, 0f, 0.3f); // 淡黄色高亮

    // ============================================================
    // 生命周期
    // ============================================================

    private void Awake()
    {
        // 自动查找组件
        if (iconImage == null)
        {
            // 尝试从自身或子节点查找 Image（排除自身主 Image 以外的子节点图标）
            Transform iconChild = transform.Find("Icon");
            iconImage = iconChild != null ? iconChild.GetComponent<Image>() : GetComponent<Image>();
        }

        if (stackText == null)
        {
            Transform countChild = transform.Find("StackCount");
            if (countChild != null) stackText = countChild.GetComponent<Text>();
        }

        if (highlightOverlay != null)
            highlightOverlay.gameObject.SetActive(false);
    }

    private void OnEnable()
    {
        RefreshDisplay();
    }

    // ============================================================
    // 公开方法 — 显示刷新
    // ============================================================

    /// <summary>
    /// 刷新格子显示（从 InventoryManager 读取当前数据）
    /// </summary>
    public void RefreshDisplay()
    {
        ItemInstance item = GetItemData();
        bool isEmpty = item == null || !item.IsValid;

        // 图标
        if (iconImage != null)
        {
            if (isEmpty)
            {
                iconImage.sprite = null;
                Color c = iconImage.color;
                c.a = emptyAlpha;
                iconImage.color = c;
            }
            else
            {
                iconImage.sprite = item.template.icon;
                Color c = iconImage.color;
                c.a = 1f;
                iconImage.color = c;
            }
        }

        // 堆叠数量
        if (stackText != null)
        {
            if (isEmpty || (hideSingleStack && item.stackSize <= 1))
            {
                stackText.text = "";
            }
            else
            {
                stackText.text = item.stackSize.ToString();
            }
        }

        // 稀有度边框
        if (rarityFrame != null)
        {
            if (isEmpty)
            {
                rarityFrame.color = new Color(0.5f, 0.5f, 0.5f, 0.2f); // 空槽低调边框
            }
            else
            {
                rarityFrame.color = RarityColor.GetColor(item.template.rarity);
            }
        }

        // 空格子默认图
        if (emptySlotIcon != null)
            emptySlotIcon.gameObject.SetActive(isEmpty);
    }

    /// <summary>
    /// 设置容器标识并刷新显示
    /// </summary>
    public void Setup(DragSourceContainer containerType, int slotIndex)
    {
        ContainerType = containerType;
        SlotIndex = slotIndex;
        RefreshDisplay();
    }

    // ============================================================
    // IDropHandler — 接受拖放
    // ============================================================

    public void OnDrop(PointerEventData eventData)
    {
        if (!DragSession.IsDragging) return;

        // 清除高亮
        SetHighlight(false);

        DragSourceContainer sourceContainer = DragSession.SourceContainer;
        int sourceIndex = DragSession.SourceIndex;

        InventoryManager inv = InventoryManager.Instance;
        if (inv == null) return;

        // ── 根据源容器 × 目标容器组合执行操作 ──

        switch (sourceContainer)
        {
            case DragSourceContainer.Inventory:
                HandleDropFromInventory(inv, sourceIndex);
                break;

            case DragSourceContainer.Warehouse:
                HandleDropFromWarehouse(inv, sourceIndex);
                break;

            case DragSourceContainer.EquipmentSlot:
                HandleDropFromEquipmentSlot(inv, (EquipmentSlotType)sourceIndex);
                break;

            case DragSourceContainer.QuickSlot:
                HandleDropFromQuickSlot(inv, sourceIndex);
                break;
        }
    }

    public void OnPointerEnter(PointerEventData eventData)
    {
        if (DragSession.IsDragging && IsValidDropTarget())
            SetHighlight(true);
    }

    public void OnPointerExit(PointerEventData eventData)
    {
        SetHighlight(false);
    }

    // ============================================================
    // 拖放处理 — 按来源分类
    // ============================================================

    private void HandleDropFromInventory(InventoryManager inv, int sourceIdx)
    {
        if (ContainerType == DragSourceContainer.Inventory)
        {
            // 背包 → 背包：交换/堆叠
            inv.SwapPlayerItems(sourceIdx, SlotIndex);
        }
        else if (ContainerType == DragSourceContainer.Warehouse)
        {
            // 背包 → 仓库：存入
            inv.DepositToWarehouse(sourceIdx);
        }
        // 背包 → 自身：无操作
    }

    private void HandleDropFromWarehouse(InventoryManager inv, int sourceIdx)
    {
        if (ContainerType == DragSourceContainer.Inventory)
        {
            // 仓库 → 背包：取出到当前格子
            inv.WithdrawFromWarehouse(sourceIdx, targetPlayerSlot: SlotIndex);
        }
        else if (ContainerType == DragSourceContainer.Warehouse)
        {
            // 仓库 → 仓库：交换
            inv.SwapWarehouseItems(sourceIdx, SlotIndex);
        }
    }

    private void HandleDropFromEquipmentSlot(InventoryManager inv, EquipmentSlotType slot)
    {
        if (ContainerType == DragSourceContainer.Inventory)
        {
            // 装备槽 → 背包：卸下到当前格子
            ItemInstance equipItem = inv.GetEquippedItem(slot);
            if (equipItem == null) return;

            // 记录目标槽位（在卸下前确认有空位或目标已被占用）
            ItemInstance existingTarget = inv.GetPlayerItem(SlotIndex);

            inv.UnequipItem(slot);

            // UnequipItem 会把物品放到背包首个空格
            // 如果目标格原本有物品，需要把卸下装备与目标格物品交换
            if (existingTarget != null)
            {
                // 通过引用找到刚卸下的物品，与目标格交换
                ItemInstance justUnequipped = null;
                int unequippedIdx = -1;
                for (int i = 0; i < inv.PlayerItems.Count; i++)
                {
                    var item = inv.GetPlayerItem(i);
                    if (item != null && item == equipItem)
                    {
                        justUnequipped = item;
                        unequippedIdx = i;
                        break;
                    }
                }
                if (justUnequipped != null && unequippedIdx >= 0)
                {
                    inv.SwapPlayerItems(unequippedIdx, SlotIndex);
                }
            }
        }
        else if (ContainerType == DragSourceContainer.Warehouse)
        {
            // 装备槽 → 仓库：先卸下到背包，再存入仓库
            ItemInstance equipItem = inv.GetEquippedItem(slot);
            if (equipItem == null) return;

            inv.UnequipItem(slot);

            // 找到刚卸下的物品在背包中的索引
            for (int i = 0; i < inv.PlayerItems.Count; i++)
            {
                if (inv.GetPlayerItem(i) == equipItem)
                {
                    inv.DepositToWarehouse(i);
                    break;
                }
            }
        }
    }

    private void HandleDropFromQuickSlot(InventoryManager inv, int sourceIdx)
    {
        if (ContainerType == DragSourceContainer.Inventory || ContainerType == DragSourceContainer.Warehouse)
        {
            // 快捷栏 → 背包格子：仅清空快捷栏（物品本身就在背包中）
            inv.ClearQuickSlot(sourceIdx);
        }
    }

    // ============================================================
    // IPointerClickHandler — 左键点击装备
    // ============================================================

    public void OnPointerClick(PointerEventData eventData)
    {
        if (eventData.button != PointerEventData.InputButton.Left) return;
        if (DragSession.IsDragging) return;

        ItemInstance item = GetItemData();
        if (item == null || !item.IsValid) return;
        if (item.template.category != ItemCategory.Equipment) return;

        InventoryManager inv = InventoryManager.Instance;
        if (inv == null) return;

        inv.EquipItem(SlotIndex, item.template.slotType);
    }

    // ============================================================
    // 辅助方法
    // ============================================================

    /// <summary>获取此格子对应的 ItemInstance</summary>
    private ItemInstance GetItemData()
    {
        InventoryManager inv = InventoryManager.Instance;
        if (inv == null) return null;

        switch (ContainerType)
        {
            case DragSourceContainer.Inventory:
                return inv.GetPlayerItem(SlotIndex);
            case DragSourceContainer.Warehouse:
                return inv.GetWarehouseItem(SlotIndex);
            default:
                return null;
        }
    }

    /// <summary>设置高亮覆盖层</summary>
    private void SetHighlight(bool active)
    {
        if (highlightOverlay == null) return;
        highlightOverlay.gameObject.SetActive(active);
        if (active)
            highlightOverlay.color = s_highlightColor;
    }

    /// <summary>判断当前拖拽物品是否可以放入此格子</summary>
    private bool IsValidDropTarget()
    {
        if (!DragSession.IsDragging) return false;

        DragSourceContainer srcContainer = DragSession.SourceContainer;

        // 装备槽 → 只能放入背包格子
        if (srcContainer == DragSourceContainer.EquipmentSlot)
            return ContainerType == DragSourceContainer.Inventory;

        // 快捷栏 → 只能放入背包格子
        if (srcContainer == DragSourceContainer.QuickSlot)
            return ContainerType == DragSourceContainer.Inventory;

        // 背包/仓库格子 → 可互为拖放目标
        return ContainerType == DragSourceContainer.Inventory || ContainerType == DragSourceContainer.Warehouse;
    }
}
