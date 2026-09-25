using UnityEngine;
using UnityEngine.EventSystems;

/// <summary>
/// 合成槽 — 界面 Slot_Left / Slot_Right 上的交互组件（[装备制作 S4]）
///
/// 只做两件事（规格第三节第 1 条「暂存区模式」）：
///   1. 拖入填入：从背包 / 仓库格拖进来 → 调 <see cref="CraftStagingArea.TryStage"/>
///      把材料从来源容器真扣掉放进合成暂存区；
///   2. 右键取消填入：调 <see cref="CraftStagingArea.TryUnstage"/> 把该格材料归还原容器。
///
/// 本组件**不碰任何 UI 显示**：槽内容变化只通过 <see cref="OnSlotChanged"/> 通知订阅方，
/// 图标 / 名称 / 数量文本由界面主控（S7）读暂存区自行刷新。
/// 组件本体不由本步骤挂到场景 —— 由 saika 挂到 Canvas/Create/Hc/Slot_Left 与 Slot_Right 并填 slotIndex。
///
/// 数据层 <see cref="Staging"/> 由界面主控 new 出来注入（普通 C# 类，不能在 Inspector 里拖引用）；
/// 未注入（null）时两个入口都安全 return：不报错、不改任何数据。
/// </summary>
public class CraftMakeSlot : MonoBehaviour, IDropHandler, IPointerClickHandler
{
    // ============================================================
    // 配置 / 注入
    // ============================================================

    [Tooltip("槽位号：0 = 左槽（Slot_Left），1 = 右槽（Slot_Right）")]
    [SerializeField] private int slotIndex = 0;

    /// <summary>本槽的槽位号（只读；界面主控按下标对齐槽与配方材料时用）</summary>
    public int SlotIndex => slotIndex;

    /// <summary>
    /// 合成暂存区 —— 界面主控 new 出来并注入的共享实例。
    /// 非序列化属性（普通 C# 类无法在 Inspector 拖引用）。
    /// </summary>
    public CraftStagingArea Staging { get; set; }

    /// <summary>
    /// 本次填入希望扣的数量：&gt; 0 = 最多扣这么多（再与源格存量取小）；
    /// &lt;= 0（默认 -1）= 按源格现有数量整格填入。
    /// 界面主控载入配方后按配方需求设置。
    /// </summary>
    public int PreferredCount { get; set; } = -1;

    /// <summary>
    /// 槽内容变化回调（填入成功 / 取消成功各触发一次），参数 = 槽位号。
    /// UI 刷新交给订阅方，本组件不碰显示。
    /// </summary>
    public System.Action<int> OnSlotChanged;

    // ============================================================
    // 行为 1：拖入填入（IDropHandler）
    // ============================================================

    /// <summary>
    /// 背包 / 仓库格拖到本槽 → 填入暂存区。
    /// 其它来源（装备槽 / 快捷栏 / 没有拖拽）直接忽略；
    /// 填入失败（槽已有料、源格空、暂存区缺失等）静默返回：不弹提示、不改任何数据。
    ///
    /// Unity 事件顺序保证 OnDrop 先于 OnEndDrag，所以这里读到的 DragSession 仍然有效。
    /// </summary>
    public void OnDrop(PointerEventData eventData)
    {
        if (Staging == null) return;          // 主控还没注入 → 安全返回
        if (!DragSession.IsDragging) return;  // 没有拖拽（正常不会走到，保险）

        DragSourceContainer source = DragSession.SourceContainer;
        bool fromWarehouse;
        if (source == DragSourceContainer.Inventory) fromWarehouse = false;
        else if (source == DragSourceContainer.Warehouse) fromWarehouse = true;
        else return;                          // 装备槽 / 快捷栏：不接收

        int count = ResolveFillCount(fromWarehouse, DragSession.SourceIndex);
        if (count <= 0) return;               // 源格空 / 数量非法

        if (Staging.TryStage(slotIndex, fromWarehouse, DragSession.SourceIndex, count))
            OnSlotChanged?.Invoke(slotIndex);
    }

    // ============================================================
    // 行为 2：右键取消填入（IPointerClickHandler）
    // ============================================================

    /// <summary>
    /// 右键 = 取消填入（材料按来源归还原容器）。
    /// 左键不处理、也不拦截 —— 留给后续步骤的「材料列表入口」用。
    /// 归还失败（背包 / 仓库放不下）静默返回，材料仍留在暂存区。
    /// </summary>
    public void OnPointerClick(PointerEventData eventData)
    {
        if (eventData == null || eventData.button != PointerEventData.InputButton.Right) return;
        if (DragSession.IsDragging) return;   // 拖拽过程中的点击不算
        if (Staging == null) return;

        if (Staging.TryUnstage(slotIndex))
            OnSlotChanged?.Invoke(slotIndex);
    }

    // ============================================================
    // 辅助
    // ============================================================

    /// <summary>
    /// 算出本次实际填入数量：PreferredCount &gt; 0 时取它与源格现有数量的较小值，
    /// 否则按源格现有数量整格填入。源格取不到料（越界 / 空格 / InventoryManager 缺失）返回 0。
    /// </summary>
    private int ResolveFillCount(bool fromWarehouse, int sourceIndex)
    {
        int available = GetSourceStackSize(fromWarehouse, sourceIndex);
        if (available <= 0) return 0;

        return PreferredCount > 0 ? Mathf.Min(PreferredCount, available) : available;
    }

    /// <summary>读源格当前堆叠数量（只读，不改任何数据）</summary>
    private static int GetSourceStackSize(bool fromWarehouse, int sourceIndex)
    {
        InventoryManager inventory = InventoryManager.Instance;
        if (inventory == null) return 0;

        ItemInstance item = fromWarehouse
            ? inventory.GetWarehouseItem(sourceIndex)
            : inventory.GetPlayerItem(sourceIndex);
        if (item == null || item.template == null) return 0;

        return item.stackSize;
    }
}
