using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using TMPro;

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

    /// <summary>
    /// 堆叠数量文本（2026-09-25 由 UnityEngine.UI.Text 改为 TMPro.TMP_Text，与项目 TMP 口径统一）。
    /// 旧类型与项目 UI 的 TextMeshPro 不匹配 ⇒ 场景里这个字段一直拖不进对象、恒为空；
    /// 改类型时该字段在 26 个格子里本来就都是空引用，因此无引用断裂风险。
    /// </summary>
    [Tooltip("堆叠数量文本（TMP）；留空则按子物体名 \"Count\"（兼容旧名 \"StackCount\"）查找")]
    [SerializeField] private TMP_Text stackText;

    /// <summary>名称文本（可选，默认不显示；hideName=false 时才写入。留空则按子物体名 "Name" 查找）</summary>
    [Tooltip("名称文本（TMP）；默认不显示，留空则按子物体名 \"Name\" 查找")]
    [SerializeField] private TMP_Text nameText;

    [Tooltip("稀有度边框 Image（可选）")]
    [SerializeField] private Image rarityFrame;

    [Tooltip("空槽位默认图标（可选，有物品时隐藏）")]
    [SerializeField] private Image emptySlotIcon;

    [Tooltip("拖入高亮覆盖层（可选，留空则按子物体名 Highlight 找）")]
    [SerializeField] private Image highlightOverlay;

    [Tooltip("选中高亮覆盖层（可选，与拖入高亮分开用；留空则按子物体名 SelectedHighlight 找，找不到就复用 Highlight）")]
    [SerializeField] private Image selectedHighlightOverlay;

    [Header("外观设置")]
    [Tooltip("空格子时的图标透明度")]
    [SerializeField] [Range(0f, 1f)] private float emptyAlpha = 0.3f;

    [Tooltip("堆叠数量 < 2 时隐藏数量文字")]
    [SerializeField] private bool hideSingleStack = true;

    [Tooltip("隐藏名称文字（默认 true：名称只出现在悬停 tooltip 里；置 false 时才写入 nameText）")]
    [SerializeField] private bool hideName = true;

    // ============================================================
    // 运行时状态
    // ============================================================

    // 2026-09-25 saika 定:高亮的颜色与贴图一律由素材决定(Highlight / SelectedHighlight 元素自己的 Image),
    // 代码只做显隐开关,不再写死颜色 —— 原先这里的两行硬编码色(s_highlightColor / s_selectedColor)已删。

    // ── 子物体名字约定（模板化显示:字段拖了就用字段,没拖就按这些名字找）──

    private const string k_IconChildName = "Icon";
    private const string k_CountChildName = "Count";
    private const string k_LegacyCountChildName = "StackCount"; // 旧格子的数量文本名字,兼容用
    private const string k_NameChildName = "Name";
    private const string k_FrameChildName = "Frame";
    private const string k_HighlightChildName = "Highlight";
    private const string k_SelectedHighlightChildName = "SelectedHighlight";

    /// <summary>显示元素是否已解析过（懒解析只做一次；未命中也记下,之后不再 transform.Find）</summary>
    private bool _refsResolved;

    // ============================================================
    // 生命周期
    // ============================================================

    private void Awake()
    {
        // 组件自动查找已统一到 ResolveRefsOnce（首次 RefreshDisplay 时按名字解析并缓存），
        // 见「辅助方法 — 显示元素解析」一节；此处只保留原本就有的高亮层初始隐藏。
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
        ResolveRefsOnce();   // 懒解析:字段没拖就按子物体名字找(只做一次并缓存)

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

        // 名称文字（默认不显示：hideName=true 时名称只出现在悬停 tooltip 里）
        if (nameText != null)
        {
            if (isEmpty || hideName || string.IsNullOrEmpty(item.template.itemName))
            {
                nameText.text = string.Empty;
            }
            else
            {
                nameText.text = item.template.itemName;
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

        // 悬停换格子:tip 要切到当前这一格,同时取消别处的选中
        // (选中只在"点完鼠标不动"的前提下成立;一移动就失效,免得点第二次时装备错目标)
        var rect = (RectTransform)transform;
        if (!UITooltip.IsPinnedTo(rect))
            UITooltip.SetPinned(null, false);

        ShowItemTooltip();
    }

    public void OnPointerExit(PointerEventData eventData)
    {
        SetHighlight(false);
        UITooltip.Hide();
    }

    /// <summary>
    /// 悬停时把这一格的内容交给 tooltip(2026-09-22 新增)。
    /// 空格子不弹;拖拽中不弹(拖拽时格子会被高亮盖住,弹窗反而挡视线)。
    /// </summary>
    private void ShowItemTooltip()
    {
        if (DragSession.IsDragging) return;

        ItemInstance item = GetItemData();
        if (item == null || item.template == null) return;

        UITooltip.ShowItem(item.template, (RectTransform)transform);
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
        if (ContainerType == DragSourceContainer.Inventory)
        {
            // 快捷槽 → 背包格：优先堆叠进那一格的同种，其次放进那一格，再退回首空格
            inv.ReturnQuickSlotToBackpack(sourceIdx, SlotIndex);
        }
        else if (ContainerType == DragSourceContainer.Warehouse)
        {
            // 快捷槽 → 仓库：先放回背包，再从背包存入仓库
            int bagIndex = inv.ReturnQuickSlotToBackpack(sourceIdx);
            if (bagIndex >= 0)
                inv.DepositToWarehouse(bagIndex);
        }
    }

    // ============================================================
    // IPointerClickHandler — 左键点击装备
    // ============================================================

    /// <summary>
    /// 点击逻辑(2026-09-26 改):
    ///   左键第 1 次 = 选中这一格(固定 tip + 高亮),不做别的;
    ///   左键同格再点 = 装备类自动装备到空槽(饰品两个槽自动选,两个都满则不动),消耗品与材料不响应;
    ///   右键 = 使用消耗品(只在背包格里生效),装备/材料不响应。
    /// 鼠标移开时若仍选中则 tip 不移除(见 UITooltip.Hide)。
    /// </summary>
    public void OnPointerClick(PointerEventData eventData)
    {
        if (DragSession.IsDragging) return;

        ItemInstance item = GetItemData();
        if (item == null || !item.IsValid) return;

        // ── 右键：使用消耗品（背包格限定，仓库格不响应）──
        if (eventData.button == PointerEventData.InputButton.Right)
        {
            if (item.template.category != ItemCategory.Consumable) return;
            if (ContainerType != DragSourceContainer.Inventory) return;

            InventoryManager.Instance?.UsePlayerItem(SlotIndex);
            return;
        }

        if (eventData.button != PointerEventData.InputButton.Left) return;

        var rect = (RectTransform)transform;

        // 第一段：选中
        if (!UITooltip.IsPinnedTo(rect))
        {
            UITooltip.ShowItem(item.template, rect);
            UITooltip.SetPinned(rect, true);
            SetSelected(true);
            return;
        }

        // 第二段：装备类 = 自动装备到空槽；消耗品/材料不做事（消耗品改用右键使用）
        if (item.template.category != ItemCategory.Equipment) return;
        if (ContainerType != DragSourceContainer.Inventory) return;

        InventoryManager inv = InventoryManager.Instance;
        if (inv == null) return;

        UITooltip.Close();   // 物品会移出格子，选中与 tip 一起收
        inv.EquipItemAuto(SlotIndex);
    }

    /// <summary>
    /// 选中态高亮（2026-09-25 改）：只做显隐开关，颜色与贴图由 SelectedHighlight 元素自己定；
    /// 没建 SelectedHighlight 就复用 Highlight。两个都没有就什么都不做。
    /// </summary>
    public void SetSelected(bool on)
    {
        Image target = selectedHighlightOverlay != null ? selectedHighlightOverlay : highlightOverlay;
        if (target == null) return;

        target.gameObject.SetActive(on);
    }

    // ============================================================
    // 辅助方法
    // ============================================================

    /// <summary>
    /// 显示元素解析（模板化显示，2026-09-25）—— 规则统一为
    /// 「**字段拖了就用字段，没拖就按子物体名字找**」，名字约定：
    ///   Icon（找不到再退回自身 Image）/ Count（兼容旧名 StackCount）/ Name / Frame / Highlight。
    /// 解析只做一次并缓存（懒解析：首次 RefreshDisplay 时做）；
    /// 未命中的元素同样记为已解析，之后不再 transform.Find ——
    /// 因此 prefab 里后补的子物体要重新进 Play（或重载场景）才生效。
    /// </summary>
    private void ResolveRefsOnce()
    {
        if (_refsResolved) return;
        _refsResolved = true;

        // 图标:"Icon" 子物体 → 自身 Image（保留原兜底）
        if (iconImage == null)
        {
            Transform iconChild = transform.Find(k_IconChildName);
            iconImage = iconChild != null ? iconChild.GetComponent<Image>() : GetComponent<Image>();
        }

        // 堆叠数量:"Count" → 旧名 "StackCount"
        if (stackText == null)
        {
            Transform countChild = transform.Find(k_CountChildName);
            if (countChild == null) countChild = transform.Find(k_LegacyCountChildName);
            if (countChild != null) stackText = countChild.GetComponent<TMP_Text>();
        }

        // 名称:"Name"
        if (nameText == null)
        {
            Transform nameChild = transform.Find(k_NameChildName);
            if (nameChild != null) nameText = nameChild.GetComponent<TMP_Text>();
        }

        // 稀有度边框:"Frame"
        if (rarityFrame == null)
        {
            Transform frameChild = transform.Find(k_FrameChildName);
            if (frameChild != null) rarityFrame = frameChild.GetComponent<Image>();
        }

        // 高亮覆盖层:"Highlight"（拖入用）/ "SelectedHighlight"（选中用;找不到就复用 Highlight）
        // 两个元素解析完都先隐掉;颜色与贴图由素材自己定,代码不写颜色(2026-09-25)
        if (highlightOverlay == null)
        {
            Transform highlightChild = transform.Find(k_HighlightChildName);
            if (highlightChild != null) highlightOverlay = highlightChild.GetComponent<Image>();
        }

        if (selectedHighlightOverlay == null)
        {
            Transform selectedChild = transform.Find(k_SelectedHighlightChildName);
            selectedHighlightOverlay = selectedChild != null
                ? selectedChild.GetComponent<Image>()
                : highlightOverlay;
        }

        if (highlightOverlay != null) highlightOverlay.gameObject.SetActive(false);
        if (selectedHighlightOverlay != null) selectedHighlightOverlay.gameObject.SetActive(false);
    }

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

    /// <summary>拖入可放置高亮：只做显隐开关,颜色与贴图由 Highlight 元素自己定(2026-09-25)</summary>
    private void SetHighlight(bool active)
    {
        if (highlightOverlay == null) return;

        highlightOverlay.gameObject.SetActive(active);
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
