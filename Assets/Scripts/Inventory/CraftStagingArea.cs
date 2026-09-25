using UnityEngine;

/// <summary>
/// 合成暂存区 — 界面「两个材料槽」的数据层（[装备制作 S3]）
///
/// 定位：纯数据层。普通 C# 类（非 MonoBehaviour，不挂物体、不碰 UI、不发事件）。
/// 界面填材料时把物品从背包/仓库**真扣掉**放进这里（暂存区模式，规格第三节第 1 条），
/// 取消填入时按**来源容器**归还（规格第七节第 1 条），界面刷新由调用方读 <see cref="GetEntry"/> 自己做。
///
/// 固定 2 格（界面只有两个材料槽），每格记录一条：模板 / 数量 / 来源容器。
/// 两格允许放同一种物品（不做去重），也不做堆叠合并 —— 每格独立一条。
///
/// 生命周期：由界面主控（S4 合成槽 / S7 主控）自行 new 出来持有；
/// 背包与仓库一律通过 <see cref="InventoryManager.Instance"/> 访问，
/// Instance 为空时所有会改动数据的入口安全返回 false（不动任何数据）。
/// </summary>
public class CraftStagingArea
{
    /// <summary>暂存格数量 —— 界面只有两个材料槽，固定 2</summary>
    public const int SLOT_COUNT = 2;

    /// <summary>
    /// 一格暂存记录：材料模板 + 暂存数量 + 来源容器。
    /// template == null 表示该格为空（<see cref="GetEntry"/> 返回的记录以此判空）。
    /// </summary>
    public class Entry
    {
        /// <summary>材料模板（null = 空格）</summary>
        public ItemSO template;

        /// <summary>暂存数量（&gt; 0 才有意义）</summary>
        public int count;

        /// <summary>来源容器：true = 仓库，false = 背包（取消填入时按此归还到原容器）</summary>
        public bool fromWarehouse;

        /// <summary>是否为空（只读辅助）</summary>
        public bool IsEmpty => template == null;
    }

    /// <summary>两格暂存记录，构造时一次性建好（之后只改字段，不换引用）</summary>
    private readonly Entry[] slots = new Entry[SLOT_COUNT];

    public CraftStagingArea()
    {
        for (int i = 0; i < slots.Length; i++)
            slots[i] = new Entry();
    }

    // ============================================================
    // 查询
    // ============================================================

    /// <summary>槽位下标是否合法（0 ~ SLOT_COUNT-1）</summary>
    private static bool IsValidSlot(int slot)
    {
        return slot >= 0 && slot < SLOT_COUNT;
    }

    /// <summary>
    /// 该格是否有材料。越界槽位一律视作「没有材料」→ false（调用方不会因此踩空）。
    /// </summary>
    public bool HasStaged(int slot)
    {
        return IsValidSlot(slot) && slots[slot].template != null;
    }

    /// <summary>
    /// 该格是否为空。越界槽位一律视作「空」→ true。
    /// </summary>
    public bool IsEmpty(int slot)
    {
        return !HasStaged(slot);
    }

    /// <summary>
    /// 取该格的暂存记录（只读使用，请勿改写字段）。
    /// 记录里的 template == null 表示该格为空；槽位越界返回 null。
    /// </summary>
    public Entry GetEntry(int slot)
    {
        return IsValidSlot(slot) ? slots[slot] : null;
    }

    // ============================================================
    // 填入 / 取消填入
    // ============================================================

    /// <summary>
    /// 填入：把背包 / 仓库的 sourceIndex 格里的 count 个物品，真扣掉放进暂存区 slot 格。
    ///
    /// 顺序固定为「先扣来源、扣成功再写暂存格」——扣失败则整体不动（不留半笔）。
    /// 失败条件（均不改动任何数据）：slot 越界、该格已有材料、count &lt;= 0、
    /// 源格无物品（含 sourceIndex 越界）、InventoryManager.Instance 缺失、扣除返回失败。
    ///
    /// 记录的数量 = **实际从源格扣掉的量**：count 大于源格存量时按存量暂存
    /// （不会记下比实际扣掉更多的数量，否则暂存区会凭空多出材料）。
    ///
    /// 背包来源走 <see cref="InventoryManager.RemoveItem"/>，仓库来源走
    /// <see cref="InventoryManager.RemoveWarehouseItem"/>，两者都会各自触发对应的变更事件。
    /// </summary>
    /// <param name="slot">暂存格下标（0 ~ SLOT_COUNT-1）</param>
    /// <param name="fromWarehouse">来源容器：true = 仓库，false = 背包</param>
    /// <param name="sourceIndex">来源容器里的格位下标</param>
    /// <param name="count">要填入的数量</param>
    /// <returns>true = 已从来源扣除并写入暂存格</returns>
    public bool TryStage(int slot, bool fromWarehouse, int sourceIndex, int count)
    {
        // ── 入口校验：任一不通过都「整体不动」──
        if (!IsValidSlot(slot)) return false;            // 槽越界
        if (count <= 0) return false;                    // 数量非法

        InventoryManager inventory = InventoryManager.Instance;
        if (inventory == null) return false;             // 单例缺失：安全失败

        // 源格必须有物品（sourceIndex 越界由 GetPlayerItem / GetWarehouseItem 返回 null 兜住）
        ItemInstance sourceItem = fromWarehouse
            ? inventory.GetWarehouseItem(sourceIndex)
            : inventory.GetPlayerItem(sourceIndex);
        if (sourceItem == null || sourceItem.template == null) return false;

        // 该格已有材料时的规则（2026-09-25 改）：同种模板继续累加（一键填入要能多次搬同种材料），
        // 不同种材料一律拒绝。判定放在读源格之后，因为要先知道搬进来的是哪种材料。
        Entry entry = slots[slot];
        if (entry.template != null && entry.template != sourceItem.template) return false;

        // 实际能填入的数量（先读存量，随后扣除会改小 stackSize）
        int stagedCount = Mathf.Min(count, sourceItem.stackSize);
        if (stagedCount <= 0) return false;

        // ── 先从来源扣除 ──
        bool removed = fromWarehouse
            ? inventory.RemoveWarehouseItem(sourceIndex, stagedCount)
            : inventory.RemoveItem(sourceIndex, stagedCount);
        if (!removed) return false;                      // 扣除失败 → 暂存格保持原样

        // ── 扣除成功 → 才写暂存格：同种材料在原有数量上累加 ──
        // 来源标记保留第一次写入的那一个：同种材料同时来自背包与仓库时，归还按首次来源整笔回去
        // （两格各自记录单一来源容器，归还口径保持简单；不精确到「多少个回哪个容器」）。
        if (entry.template == null)
        {
            entry.template = sourceItem.template;
            entry.count = stagedCount;
            entry.fromWarehouse = fromWarehouse;
        }
        else
        {
            entry.count += stagedCount;
        }
        return true;
    }

    /// <summary>
    /// 取消填入：把该格材料归还原容器（背包来源回背包、仓库来源回仓库）。
    ///
    /// 归还前先判容量：背包来源用 <see cref="InventoryManager.CanFitFully"/>（只读试算），
    /// 仓库来源用 <see cref="InventoryManager.TryAddToWarehouse"/>（本身原子）。
    /// 装不下 → 返回 false 且**材料仍留在暂存区**（不半途写出、不丢物品）。
    ///
    /// 空槽或越界槽返回 false（无事可做，不改动任何数据）。
    /// </summary>
    /// <returns>true = 材料已整笔归还到来源容器，该格已清空</returns>
    public bool TryUnstage(int slot)
    {
        if (!IsValidSlot(slot)) return false;

        Entry entry = slots[slot];
        if (entry.template == null) return false;        // 空格：无事可做

        InventoryManager inventory = InventoryManager.Instance;
        if (inventory == null) return false;             // 单例缺失：材料留在暂存区

        bool returned;
        if (entry.fromWarehouse)
        {
            // 仓库来源 → 回仓库（超容量返回 false，内部整笔不写）
            returned = inventory.TryAddToWarehouse(entry.template, entry.count);
        }
        else
        {
            // 背包来源 → 回背包：先只读判容量，装得下才真写
            // （CanFitFully 与 AddItem 同口径，true 即 AddItem 能全量装下 → 不会只写一部分）
            returned = inventory.CanFitFully(entry.template, entry.count);
            if (returned)
                inventory.AddItem(entry.template, entry.count);
        }

        if (!returned) return false;                     // 容器满 → 取消失败，材料留在暂存区

        // 归还成功 → 清空该格
        entry.template = null;
        entry.count = 0;
        entry.fromWarehouse = false;
        return true;
    }

    /// <summary>
    /// 逐格归还（界面关闭时调用）：按 <see cref="TryUnstage"/> 的规则逐格尝试。
    /// 某格归还失败（容器满）就留在暂存区，不抛异常、不静默丢物品。
    /// </summary>
    public void ReturnAll()
    {
        for (int i = 0; i < SLOT_COUNT; i++)
        {
            // 返回值有意忽略：单格失败只意味着那一格留在暂存区，调用方按需再读 IsEmpty / GetEntry
            TryUnstage(i);
        }
    }
}
