using UnityEngine;
using UnityEngine.UI;
using System.Collections.Generic;

/// <summary>
/// 背包面板管理 — 挂 InventoryPanel GameObject
/// 实现 IPanel 以接入 PanelManager 的 ESC 关闭/暂停等栈管理
/// 
/// 职责：
///   1. 管理 ItemGrid 中的 ItemCell 子对象（自动发现并 Setup）
///   2. 管理 EquipmentSlots 中的装备槽子对象（自动发现）
///   3. 监听 InventoryManager.OnInventoryChanged / OnCategoryChanged 刷新显示
///   4. 处理 CategoryTabs 按钮点击 → 同步分类状态
/// </summary>
public class InventoryPanel : MonoBehaviour, IPanel
{
    // ============================================================
    // IPanel 实现
    // ============================================================

    // Dialog：与仓库面板可同时显示（物品互拖需要两面板同时在场景中）
    public PanelType PanelType => PanelType.Dialog;
    public bool PauseGame => true;
    public bool LockInput => true;
    public bool ShowCursor => true;

    // ============================================================
    // 配置
    // ============================================================

    [Header("网格容器")]
    [Tooltip("ItemGrid Transform（ScrollRect 的 Content）")]
    [SerializeField] private Transform itemGridContent;

    [Header("装备槽位")]
    [Tooltip("EquipmentSlots 父节点 Transform")]
    [SerializeField] private Transform equipmentSlotsParent;

    [Header("分类标签按钮")]
    [Tooltip("CategoryTabs 下的 4 个 Button，按顺序：全部/消耗品/装备/材料")]
    [SerializeField] private Button[] categoryButtons;

    [Header("缓存")]
    [Tooltip("启动时自动从 ItemGrid 发现 ItemCell 子对象")]
    [SerializeField] private bool autoDiscoverCells = true;

    // ============================================================
    // 运行时状态
    // ============================================================

    /// <summary>ItemGrid 中的 ItemCell 列表（按 Hierarchy 顺序）</summary>
    private readonly List<ItemCell> _itemCells = new List<ItemCell>();

    /// <summary>EquipmentSlots 中的 EquipmentSlot 列表</summary>
    private readonly List<EquipmentSlot> _equipSlots = new List<EquipmentSlot>();

    /// <summary>上次刷新的分类（避免重复刷新）</summary>
    private ItemCategory _lastCategory = ItemCategory.All;

    // ============================================================
    // 生命周期
    // ============================================================

    private void Awake()
    {
        if (autoDiscoverCells)
            DiscoverCells();
    }

    private void Start()
    {
        // 设置分类按钮事件
        SetupCategoryButtons();

        // 初始隐藏（由 PanelManager 控制显隐）
        // gameObject.SetActive(false);
    }

    private void OnEnable()
    {
        // 订阅事件
        InventoryManager inv = InventoryManager.Instance;
        if (inv != null)
        {
            inv.OnInventoryChanged += OnInventoryChanged;
            inv.OnCategoryChanged += OnCategoryChanged;
            inv.OnEquipmentChanged += OnEquipmentChanged;

            // 初始刷新
            RefreshAll();
            // 同步当前分类高亮
            OnCategoryChanged(inv.ActiveCategory);
        }
    }

    private void OnDisable()
    {
        InventoryManager inv = InventoryManager.Instance;
        if (inv != null)
        {
            inv.OnInventoryChanged -= OnInventoryChanged;
            inv.OnCategoryChanged -= OnCategoryChanged;
            inv.OnEquipmentChanged -= OnEquipmentChanged;
        }
    }

    // ============================================================
    // 自动发现
    // ============================================================

    [ContextMenu("Discover Cells")]
    private void DiscoverCells()
    {
        _itemCells.Clear();
        _equipSlots.Clear();

        // 发现 ItemCell（在 ItemGrid 下）
        if (itemGridContent != null)
        {
            ItemCell[] cells = itemGridContent.GetComponentsInChildren<ItemCell>(true);
            for (int i = 0; i < cells.Length; i++)
            {
                cells[i].Setup(DragSourceContainer.Inventory, i);
                _itemCells.Add(cells[i]);
            }

            // Debug.Log($"[InventoryPanel] 发现 {_itemCells.Count} 个背包 ItemCell");
        }
        else
        {
            Debug.LogWarning("[InventoryPanel] itemGridContent 未配置，请在 Inspector 拖入 ItemGrid");
        }

        // 发现 EquipmentSlot
        if (equipmentSlotsParent != null)
        {
            EquipmentSlot[] slots = equipmentSlotsParent.GetComponentsInChildren<EquipmentSlot>(true);
            _equipSlots.AddRange(slots);
            // Debug.Log($"[InventoryPanel] 发现 {_equipSlots.Count} 个装备槽");
        }
    }

    // ============================================================
    // 分类按钮
    // ============================================================

    private void SetupCategoryButtons()
    {
        if (categoryButtons == null || categoryButtons.Length < 4)
        {
            Debug.LogWarning("[InventoryPanel] categoryButtons 数量不足 4，分类按钮事件未绑定");
            return;
        }

        // 映射：Button 0=全部, 1=消耗品, 2=装备, 3=材料
        for (int i = 0; i < categoryButtons.Length && i < 4; i++)
        {
            if (categoryButtons[i] == null) continue;

            int capturedIndex = i;
            categoryButtons[i].onClick.AddListener(() => OnCategoryButtonClicked(capturedIndex));
        }
    }

    /// <summary>
    /// 分类按钮点击 → 通知 InventoryManager 切换分类
    /// InventoryManager 触发 OnCategoryChanged → 双面板同步
    /// </summary>
    private void OnCategoryButtonClicked(int buttonIndex)
    {
        // buttonIndex: 0=全部, 1=消耗品, 2=装备, 3=材料
        ItemCategory category = (ItemCategory)buttonIndex;
        InventoryManager.Instance?.SetActiveCategory(category);
    }

    // ============================================================
    // 事件回调 — 刷新显示
    // ============================================================

    private void OnInventoryChanged()
    {
        RefreshItemGrid();
    }

    private void OnCategoryChanged(ItemCategory category)
    {
        _lastCategory = category;

        // 刷新分类按钮高亮
        UpdateCategoryButtonHighlight(category);

        // 按分类刷新物品列表
        RefreshItemGrid();
    }

    private void OnEquipmentChanged()
    {
        RefreshEquipmentSlots();
    }

    // ============================================================
    // 刷新方法
    // ============================================================

    /// <summary>刷新所有显示</summary>
    public void RefreshAll()
    {
        RefreshItemGrid();
        RefreshEquipmentSlots();
    }

    /// <summary>刷新 ItemGrid 中所有 ItemCell</summary>
    private void RefreshItemGrid()
    {
        InventoryManager inv = InventoryManager.Instance;
        if (inv == null) return;

        ItemCategory cat = inv.ActiveCategory;

        for (int i = 0; i < _itemCells.Count; i++)
        {
            ItemCell cell = _itemCells[i];
            if (cell == null) continue;

            ItemInstance item = inv.GetPlayerItem(i);

            // [Phase5] 性能优化：分类过滤时隐藏不匹配的物品格子（不调用全量 RefreshDisplay）
            if (cat != ItemCategory.All && item != null && item.template.category != cat)
            {
                // 不匹配的格子在分类过滤下显示为空 — 但格子仍需刷新以显示为空状态
                cell.RefreshDisplay();
            }
            else
            {
                cell.RefreshDisplay();
            }
        }
    }

    /// <summary>刷新所有装备槽位显示</summary>
    private void RefreshEquipmentSlots()
    {
        foreach (var slot in _equipSlots)
        {
            if (slot != null)
                slot.RefreshDisplay();
        }
    }

    /// <summary>更新分类按钮高亮状态</summary>
    private void UpdateCategoryButtonHighlight(ItemCategory category)
    {
        if (categoryButtons == null) return;

        int activeIndex = (int)category;

        for (int i = 0; i < categoryButtons.Length; i++)
        {
            if (categoryButtons[i] == null) continue;

            // 设置按钮交互状态表示当前选中
            // 使用 colors 或 interactable 来区分选中/未选中
            ColorBlock colors = categoryButtons[i].colors;
            colors.normalColor = (i == activeIndex)
                ? new Color(0.3f, 0.6f, 1f)   // 选中：蓝色
                : Color.white;                  // 未选中：白色
            categoryButtons[i].colors = colors;
        }
    }

    // ============================================================
    // 公开 API
    // ============================================================

    /// <summary>手动设置 ItemCell（如面板通过代码创建格子时使用）</summary>
    public void RegisterItemCell(ItemCell cell, int index)
    {
        cell.Setup(DragSourceContainer.Inventory, index);
        // 确保列表足够大
        while (_itemCells.Count <= index)
            _itemCells.Add(null);
        _itemCells[index] = cell;
    }

    /// <summary>手动注册装备槽（代码创建时使用）</summary>
    public void RegisterEquipmentSlot(EquipmentSlot slot)
    {
        if (!_equipSlots.Contains(slot))
            _equipSlots.Add(slot);
    }
}
