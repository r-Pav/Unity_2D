using UnityEngine;
using UnityEngine.UI;
using System.Collections.Generic;

/// <summary>
/// 仓库面板管理 — 挂 WarehousePanel GameObject
/// 实现 IPanel 以接入 PanelManager 的 ESC 关闭/暂停等栈管理
/// 
/// 职责：
///   1. 管理 WarehouseGrid 中的 ItemCell 子对象（自动发现并 Setup）
///   2. 监听 InventoryManager.OnWarehouseChanged / OnCategoryChanged 刷新显示
///   3. 处理 CategoryTabs 按钮点击 → 同步分类状态
///   4. 与背包面板互为拖拽目标（通过 ItemCell.IDropHandler 处理）
/// </summary>
public class WarehousePanel : MonoBehaviour, IPanel
{
    // ============================================================
    // IPanel 实现
    // ============================================================

    // Dialog：与背包面板可同时显示（物品互拖需要两面板同时在场景中）
    public PanelType PanelType => PanelType.Dialog;
    public bool PauseGame => true;
    public bool LockInput => true;
    public bool ShowCursor => true;

    // ============================================================
    // 配置
    // ============================================================

    [Header("网格容器")]
    [Tooltip("WarehouseGrid Transform（ScrollRect 的 Content）")]
    [SerializeField] private Transform warehouseGridContent;

    [Header("分类标签按钮")]
    [Tooltip("CategoryTabs 下的 4 个 Button，按顺序：全部/消耗品/装备/材料")]
    [SerializeField] private Button[] categoryButtons;

    [Header("缓存")]
    [Tooltip("启动时自动从 WarehouseGrid 发现 ItemCell 子对象")]
    [SerializeField] private bool autoDiscoverCells = true;

    // ============================================================
    // 运行时状态
    // ============================================================

    /// <summary>WarehouseGrid 中的 ItemCell 列表</summary>
    private readonly List<ItemCell> _itemCells = new List<ItemCell>();

    /// <summary>上次刷新的分类</summary>
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
        SetupCategoryButtons();
    }

    private void OnEnable()
    {
        InventoryManager inv = InventoryManager.Instance;
        if (inv != null)
        {
            inv.OnWarehouseChanged += OnWarehouseChanged;
            inv.OnCategoryChanged += OnCategoryChanged;

            // 初始刷新
            RefreshAll();
            UpdateCategoryButtonHighlight(inv.ActiveCategory);
        }
    }

    private void OnDisable()
    {
        InventoryManager inv = InventoryManager.Instance;
        if (inv != null)
        {
            inv.OnWarehouseChanged -= OnWarehouseChanged;
            inv.OnCategoryChanged -= OnCategoryChanged;
        }
    }

    // ============================================================
    // 自动发现
    // ============================================================

    [ContextMenu("Discover Cells")]
    private void DiscoverCells()
    {
        _itemCells.Clear();

        if (warehouseGridContent != null)
        {
            ItemCell[] cells = warehouseGridContent.GetComponentsInChildren<ItemCell>(true);
            for (int i = 0; i < cells.Length; i++)
            {
                cells[i].Setup(DragSourceContainer.Warehouse, i);
                _itemCells.Add(cells[i]);
            }

            // Debug.Log($"[WarehousePanel] 发现 {_itemCells.Count} 个仓库 ItemCell");
        }
        else
        {
            Debug.LogWarning("[WarehousePanel] warehouseGridContent 未配置，请在 Inspector 拖入 WarehouseGrid");
        }
    }

    // ============================================================
    // 分类按钮
    // ============================================================

    private void SetupCategoryButtons()
    {
        if (categoryButtons == null || categoryButtons.Length < 4)
        {
            Debug.LogWarning("[WarehousePanel] categoryButtons 数量不足 4，分类按钮事件未绑定");
            return;
        }

        for (int i = 0; i < categoryButtons.Length && i < 4; i++)
        {
            if (categoryButtons[i] == null) continue;
            int capturedIndex = i;
            categoryButtons[i].onClick.AddListener(() => OnCategoryButtonClicked(capturedIndex));
        }
    }

    /// <summary>
    /// 分类按钮点击 → 通知 InventoryManager 切换分类
    /// InventoryManager 触发 OnCategoryChanged → 双面板同步刷新
    /// </summary>
    private void OnCategoryButtonClicked(int buttonIndex)
    {
        ItemCategory category = (ItemCategory)buttonIndex;
        InventoryManager.Instance?.SetActiveCategory(category);
    }

    // ============================================================
    // 事件回调 — 刷新显示
    // ============================================================

    private void OnWarehouseChanged()
    {
        RefreshWarehouseGrid();
    }

    private void OnCategoryChanged(ItemCategory category)
    {
        _lastCategory = category;
        UpdateCategoryButtonHighlight(category);
        RefreshWarehouseGrid();
    }

    // ============================================================
    // 刷新方法
    // ============================================================

    /// <summary>刷新全部</summary>
    public void RefreshAll()
    {
        RefreshWarehouseGrid();
    }

    /// <summary>刷新 WarehouseGrid 中所有 ItemCell</summary>
    private void RefreshWarehouseGrid()
    {
        InventoryManager inv = InventoryManager.Instance;
        if (inv == null) return;

        ItemCategory cat = inv.ActiveCategory;
        IReadOnlyList<ItemInstance> warehouseItems = inv.WarehouseItems;

        // [Phase5] 性能优化：预计算分类过滤索引映射，避免 O(n²) 嵌套循环
        if (cat == ItemCategory.All)
        {
            // 全部显示：直接按索引对应
            for (int i = 0; i < _itemCells.Count; i++)
            {
                ItemCell cell = _itemCells[i];
                if (cell == null) continue;

                if (i < warehouseItems.Count)
                {
                    cell.SlotIndex = i;
                    cell.RefreshDisplay();
                }
                else
                {
                    cell.SlotIndex = i;
                    cell.RefreshDisplay(); // 空显示
                }
            }
        }
        else
        {
            // 分类过滤：先收集所有匹配物品的仓库索引
            var matching = new System.Collections.Generic.List<int>();
            for (int j = 0; j < warehouseItems.Count; j++)
            {
                ItemInstance item = warehouseItems[j];
                if (item != null && item.template.category == cat)
                    matching.Add(j);
            }

            // 填充 ItemCell
            for (int i = 0; i < _itemCells.Count; i++)
            {
                ItemCell cell = _itemCells[i];
                if (cell == null) continue;

                if (i < matching.Count)
                {
                    cell.SlotIndex = matching[i];
                    cell.RefreshDisplay();
                }
                else
                {
                    // 超出匹配数量，显示为空
                    cell.SlotIndex = i;
                    cell.RefreshDisplay();
                }
            }
        }
    }

    /// <summary>更新分类按钮高亮</summary>
    private void UpdateCategoryButtonHighlight(ItemCategory category)
    {
        if (categoryButtons == null) return;

        int activeIndex = (int)category;

        for (int i = 0; i < categoryButtons.Length; i++)
        {
            if (categoryButtons[i] == null) continue;

            ColorBlock colors = categoryButtons[i].colors;
            colors.normalColor = (i == activeIndex)
                ? new Color(0.3f, 0.6f, 1f)
                : Color.white;
            categoryButtons[i].colors = colors;
        }
    }

    // ============================================================
    // 公开 API
    // ============================================================

    /// <summary>手动注册 ItemCell</summary>
    public void RegisterItemCell(ItemCell cell, int index)
    {
        cell.Setup(DragSourceContainer.Warehouse, index);
        while (_itemCells.Count <= index)
            _itemCells.Add(null);
        _itemCells[index] = cell;
    }
}
