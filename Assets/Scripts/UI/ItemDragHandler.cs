using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;

/// <summary>
/// 物品拖拽处理器 — 实现 IBeginDragHandler / IDragHandler / IEndDragHandler
/// 挂 ItemCell GameObject（与 ItemCell.cs 同一对象）
/// 
/// 拖拽流程：
///   1. BeginDrag → 记录源容器/索引到 DragSession，创建拖拽幽灵图标
///   2. Drag → 幽灵图标跟随鼠标位置
///   3. EndDrag → 销毁幽灵图标，清除 DragSession（此时 OnDrop 已执行完毕）
/// 
/// 幽灵图标：从对象池或 Instantiate 创建一个半透明 Image，RaycastTarget=false
/// </summary>
[RequireComponent(typeof(ItemCell))]
public class ItemDragHandler : MonoBehaviour, IBeginDragHandler, IDragHandler, IEndDragHandler
{
    // ============================================================
    // 配置
    // ============================================================

    [Header("拖拽视觉")]
    [Tooltip("拖拽幽灵图标的透明度")]
    [SerializeField] [Range(0.1f, 1f)] private float ghostAlpha = 0.7f;

    [Tooltip("幽灵图标相对鼠标的偏移")]
    [SerializeField] private Vector2 ghostOffset = new Vector2(15f, -15f);

    [Tooltip("幽灵图标的缩放")]
    [SerializeField] private float ghostScale = 0.9f;

    [Header("拖拽设置")]
    [Tooltip("空格子是否允许拖拽")]
    [SerializeField] private bool allowDragEmpty = false;

    // ============================================================
    // 组件引用
    // ============================================================

    private ItemCell _itemCell;
    private Canvas _parentCanvas;
    private RectTransform _ghostRect;

    // ============================================================
    // 生命周期
    // ============================================================

    private void Awake()
    {
        _itemCell = GetComponent<ItemCell>();
        _parentCanvas = GetComponentInParent<Canvas>();
    }

    // ============================================================
    // IBeginDragHandler
    // ============================================================

    public void OnBeginDrag(PointerEventData eventData)
    {
        // 获取此格子对应的物品数据
        ItemInstance item = GetItemData();
        if (item == null && !allowDragEmpty) return;

        // 记录拖拽源
        DragSession.BeginDrag(_itemCell.ContainerType, _itemCell.SlotIndex, item);

        // [Phase5] 使用对象池创建幽灵图标
        Sprite icon = item?.template?.icon;
        _ghostRect = DragSession.GetGhost(_parentCanvas, icon, ghostAlpha, ghostScale);

        // 初始位置
        RectTransformUtility.ScreenPointToLocalPointInRectangle(
            _parentCanvas.transform as RectTransform,
            Input.mousePosition,
            _parentCanvas.worldCamera,
            out Vector2 localPoint);
        _ghostRect.localPosition = localPoint + ghostOffset;
    }

    // ============================================================
    // IDragHandler
    // ============================================================

    public void OnDrag(PointerEventData eventData)
    {
        if (!DragSession.IsDragging) return;

        // 移动幽灵图标到鼠标位置
        if (_ghostRect != null)
        {
            RectTransformUtility.ScreenPointToLocalPointInRectangle(
                _parentCanvas.transform as RectTransform,
                eventData.position,
                _parentCanvas.worldCamera,
                out Vector2 localPoint);

            _ghostRect.localPosition = localPoint + ghostOffset;
        }
    }

    // ============================================================
    // IEndDragHandler
    // ============================================================

    public void OnEndDrag(PointerEventData eventData)
    {
        // [Phase5] 归还幽灵图标到对象池
        DragSession.ReturnGhost();
        _ghostRect = null;

        // 清除拖拽状态（此时 IDropHandler.OnDrop 已被 Unity 事件系统调用完毕）
        DragSession.EndDrag();
    }

    // ============================================================
    // 幽灵图标管理
    // ============================================================

    /// <summary>创建拖拽时跟随鼠标的幽灵图标</summary>
    private void CreateDragGhost(ItemInstance item)
    {
        // 创建幽灵 GameObject
        GameObject ghostObj = new GameObject("DragGhost");
        ghostObj.transform.SetParent(_parentCanvas.transform, false);
        ghostObj.transform.SetAsLastSibling(); // 确保在最上层

        // 添加 Image 组件
        Image ghostImage = ghostObj.AddComponent<Image>();
        ghostImage.raycastTarget = false; // 不阻挡射线

        // 设置图标
        if (item != null && item.template.icon != null)
        {
            ghostImage.sprite = item.template.icon;
            ghostImage.SetNativeSize();
        }

        // 透明度
        Color c = ghostImage.color;
        c.a = ghostAlpha;
        ghostImage.color = c;

        // 缩放
        _ghostRect = ghostObj.GetComponent<RectTransform>();
        _ghostRect.localScale = Vector3.one * ghostScale;

        // 初始位置
        RectTransformUtility.ScreenPointToLocalPointInRectangle(
            _parentCanvas.transform as RectTransform,
            Input.mousePosition,
            _parentCanvas.worldCamera,
            out Vector2 localPoint);
        _ghostRect.localPosition = localPoint + ghostOffset;
    }

    /// <summary>销毁幽灵图标</summary>
    private void DestroyDragGhost()
    {
        if (_ghostRect != null)
        {
            Destroy(_ghostRect.gameObject);
            _ghostRect = null;
        }
    }

    // ============================================================
    // 辅助方法
    // ============================================================

    /// <summary>获取此格子的物品数据</summary>
    private ItemInstance GetItemData()
    {
        InventoryManager inv = InventoryManager.Instance;
        if (inv == null) return null;

        switch (_itemCell.ContainerType)
        {
            case DragSourceContainer.Inventory:
                return inv.GetPlayerItem(_itemCell.SlotIndex);
            case DragSourceContainer.Warehouse:
                return inv.GetWarehouseItem(_itemCell.SlotIndex);
            default:
                return null;
        }
    }
}
