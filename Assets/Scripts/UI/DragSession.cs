/// <summary>
/// 拖拽来源容器类型 — 记录拖拽操作的起始位置
/// </summary>
public enum DragSourceContainer
{
    /// <summary>无拖拽</summary>
    None,
    /// <summary>背包面板</summary>
    Inventory,
    /// <summary>仓库面板</summary>
    Warehouse,
    /// <summary>装备槽位</summary>
    EquipmentSlot,
    /// <summary>快捷栏</summary>
    QuickSlot
}

/// <summary>
/// 拖拽会话 — 静态状态持有者，贯穿 BeginDrag → Drag → EndDrag → OnDrop 全流程
/// 任何 IDragHandler / IDropHandler 通过此类共享当前拖拽信息
/// </summary>
public static class DragSession
{
    /// <summary>当前拖拽来源容器类型</summary>
    public static DragSourceContainer SourceContainer { get; private set; } = DragSourceContainer.None;

    /// <summary>来源容器的槽位索引（背包/仓库列表中的索引，或装备槽 EquipmentSlotType 枚举值）</summary>
    public static int SourceIndex { get; private set; } = -1;

    /// <summary>被拖拽的物品实例引用</summary>
    public static ItemInstance DraggedItem { get; private set; }

    /// <summary>是否正在拖拽中</summary>
    public static bool IsDragging => SourceContainer != DragSourceContainer.None;

    // ============================================================
    // [Phase5] 拖拽幽灵对象池 — 避免每次拖拽创建/销毁 GameObject
    // ============================================================

    private static UnityEngine.GameObject _pooledGhost;
    private static UnityEngine.UI.Image _pooledGhostImage;
    private static UnityEngine.RectTransform _pooledGhostRect;

    /// <summary>
    /// 从对象池获取或创建幽灵图标
    /// </summary>
    public static UnityEngine.RectTransform GetGhost(UnityEngine.Canvas parentCanvas, UnityEngine.Sprite icon,
        float alpha = 0.7f, float scale = 0.9f)
    {
        // 检查池中对象是否仍有效（可能在场景切换时被销毁）
        if (_pooledGhost == null)
        {
            _pooledGhost = new UnityEngine.GameObject("DragGhost_Pooled");
            UnityEngine.Object.DontDestroyOnLoad(_pooledGhost);
            _pooledGhostImage = _pooledGhost.AddComponent<UnityEngine.UI.Image>();
            _pooledGhostImage.raycastTarget = false;
            _pooledGhostRect = _pooledGhost.GetComponent<UnityEngine.RectTransform>();
        }

        _pooledGhost.transform.SetParent(parentCanvas.transform, false);
        _pooledGhost.transform.SetAsLastSibling();

        if (icon != null)
        {
            _pooledGhostImage.sprite = icon;
            _pooledGhostImage.SetNativeSize();
        }
        else
        {
            _pooledGhostImage.sprite = null;
        }

        UnityEngine.Color c = _pooledGhostImage.color;
        c.a = alpha;
        _pooledGhostImage.color = c;

        _pooledGhostRect.localScale = UnityEngine.Vector3.one * scale;
        _pooledGhost.SetActive(true);

        return _pooledGhostRect;
    }

    /// <summary>
    /// 归还幽灵图标到对象池（隐藏但不销毁）
    /// </summary>
    public static void ReturnGhost()
    {
        if (_pooledGhost != null)
        {
            _pooledGhost.SetActive(false);
            _pooledGhost.transform.SetParent(null);
        }
    }

    // ============================================================
    // 拖拽生命周期
    // ============================================================

    /// <summary>
    /// 开始拖拽 — 在 ItemDragHandler.OnBeginDrag 中调用
    /// </summary>
    public static void BeginDrag(DragSourceContainer container, int index, ItemInstance item)
    {
        SourceContainer = container;
        SourceIndex = index;
        DraggedItem = item;
    }

    /// <summary>
    /// 结束拖拽 — 在 ItemDragHandler.OnEndDrag 中调用
    /// 注意：应先由 OnDrop 处理完逻辑，再调用此方法清除状态
    /// </summary>
    public static void EndDrag()
    {
        SourceContainer = DragSourceContainer.None;
        SourceIndex = -1;
        DraggedItem = null;
    }
}
