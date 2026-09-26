using UnityEngine;
using System.Collections.Generic;
using System.Linq;

/// <summary>
/// [Phase4] 背包/仓库/装备数据总管
/// 挂 InventorySystem GameObject
/// 
/// 职责：
///   1. 管理背包物品列表（playerItems，最大 11 格）
///   2. 管理仓库物品列表（warehouseItems，最大 15 格）
///   3. 管理快捷栏引用（quickSlots，2 格，引用背包中的 ItemInstance）
///   4. 委托装备操作给 EquipmentManager
///   5. 分类状态同步（双面板共享 activeCategory）
///   6. 面板开/关委托给 PanelManager
///   7. 实现 IPickupReceiver，处理世界掉落物拾取
///   8. [Phase5] 存档接口：SaveToData / LoadFromData
/// 
/// 分类过滤：不触发全量刷新 — 由面板各自处理过滤显示
/// </summary>
[DefaultExecutionOrder(-10000)]   // 早于默认顺序业务脚本:PlayerPickupReceiver.Awake / InventoryPanel.OnEnable 等都要读 Instance
public class InventoryManager : MonoBehaviour, IPickupReceiver
{
    // ============================================================
    // 常量
    // ============================================================

    /// <summary>背包最大容量（与 Hierarchy 中 ItemGrid 下的 ItemCell 数量一致）</summary>
    public const int INVENTORY_MAX_SLOTS = 11;

    /// <summary>仓库最大容量（与 Hierarchy 中 WarehouseGrid 下的 ItemCell 数量一致）</summary>
    public const int WAREHOUSE_MAX_SLOTS = 15;

    /// <summary>快捷栏槽位数量</summary>
    public const int QUICK_SLOT_COUNT = 2;

    // ============================================================
    // Singleton
    // ============================================================

    private static InventoryManager _instance;

    /// <summary>
    /// 当前实例。无 Find 兜底：靠 Awake 接管 + OnDestroy 自清维护，
    /// 避免兜底把"还没 Awake 的自己"提前写进静态字段导致自身被当重复实例销毁。
    /// </summary>
    public static InventoryManager Instance => _instance;

    // ============================================================
    // 配置
    // ============================================================

    [Header("面板引用（Inspector 拖入 InventorySystem 下的子面板）")]
    [Tooltip("背包面板 GameObject")]
    [SerializeField] private GameObject inventoryPanel;

    [Tooltip("仓库面板 GameObject")]
    [SerializeField] private GameObject warehousePanel;

    [Header("物品注册表")]
    [Tooltip("拖入所有 ItemSO 资产，供存档系统通过 ID 查找模板")]
    [SerializeField] private ItemSO[] itemTemplates;

    [Header("快捷槽")]
    [Tooltip("快捷槽单格堆叠上限（可变）。<= 0 = 不限制，沿用物品自身的 maxStack")]
    [SerializeField] private int quickSlotMaxStack = 5;

    [Header("快捷槽快捷键")]
    [Tooltip("使用第 1 个快捷槽的按键")]
    [SerializeField] private KeyCode quickSlotKey1 = KeyCode.Alpha1;

    [Tooltip("使用第 2 个快捷槽的按键")]
    [SerializeField] private KeyCode quickSlotKey2 = KeyCode.Alpha2;

    [Header("快捷切换键")]
    [Tooltip("打开/关闭背包面板的快捷键")]
    [SerializeField] private KeyCode toggleInventoryKey = KeyCode.B;

    [Tooltip("打开/关闭仓库面板的快捷键")]
    [SerializeField] private KeyCode toggleWarehouseKey = KeyCode.N;

    // ============================================================
    // 运行时数据 — 背包
    // ============================================================

    /// <summary>背包物品列表（索引对应 ItemGrid 中的 ItemCell 位置）</summary>
    private readonly List<ItemInstance> playerItems = new List<ItemInstance>();

    // ============================================================
    // 运行时数据 — 仓库
    // ============================================================

    /// <summary>仓库物品列表（索引对应 WarehouseGrid 中的 ItemCell 位置）</summary>
    private readonly List<ItemInstance> warehouseItems = new List<ItemInstance>();

    // ============================================================
    // 运行时数据 — 快捷栏
    // ============================================================

    /// <summary>
    /// 快捷栏槽位（大小固定为 2）
    /// 非 null 值表示该槽位绑定了背包中的某个物品（共享同一 ItemInstance 引用）
    /// QuickSlotBar 会显示该物品的图标和数量
    /// </summary>
    private readonly ItemInstance[] quickSlots = new ItemInstance[QUICK_SLOT_COUNT];

    // ============================================================
    // 运行时数据 — 分类
    // ============================================================

    /// <summary>当前活跃分类（背包和仓库面板共享）</summary>
    private ItemCategory _activeCategory = ItemCategory.All;

    public ItemCategory ActiveCategory => _activeCategory;

    // ============================================================
    // 缓存引用
    // ============================================================

    private EquipmentManager _equipmentManager;
    private PlayerAttributeSystem _attrSystem;

    // ============================================================
    // 事件（UI 订阅刷新）
    // ============================================================

    /// <summary>背包内容变化（增/删/移/排序）</summary>
    public System.Action OnInventoryChanged;

    /// <summary>仓库内容变化（存入/取出/排序）</summary>
    public System.Action OnWarehouseChanged;

    /// <summary>分类切换（双面板各自刷新 Tab 高亮 + 过滤列表）</summary>
    public System.Action<ItemCategory> OnCategoryChanged;

    /// <summary>快捷栏内容变化（绑定/使用/移除）</summary>
    public System.Action OnQuickSlotsChanged;

    /// <summary>装备槽位变化（穿戴/卸下/死亡掉落）</summary>
    public System.Action OnEquipmentChanged;

    // ============================================================
    // 生命周期
    // ============================================================

    private void Awake()
    {
        if (_instance != null && _instance != this)
        {
            Destroy(gameObject);
            return;
        }
        _instance = this;

        // [Phase5] 注册物品模板到全局查找表
        ItemSO.ClearRegistry();
        if (itemTemplates != null)
        {
            foreach (var template in itemTemplates)
            {
                if (template != null)
                    ItemSO.Register(template);
            }
        }

        // 初始化空列表：用 null 填充到最大容量
        InitializeSlots();
    }

    private void Start()
    {
        // 查找 Player 上的 EquipmentManager
        GameObject player = GameObject.FindGameObjectWithTag("Player");
        if (player != null)
        {
            _equipmentManager = player.GetComponent<EquipmentManager>();
            _attrSystem = player.GetComponent<PlayerAttributeSystem>();
        }
        else
        {
            Debug.LogWarning("[InventoryManager] 未找到 Player GameObject，请确保玩家 Tag 为 'Player'");
        }

        // 注册 EquipmentManager 回调
        if (_equipmentManager != null)
        {
            _equipmentManager.RegisterCallbacks(OnEquipCallback, OnUnequipCallback);
        }

        // [Phase5] 检查是否有 SaveSystem 暂存的待加载背包数据
        if (SaveSystem.TryConsumePendingInventoryData(out InventorySaveData pendingData))
        {
            LoadFromData(pendingData);
            // Debug.Log("[InventoryManager] 已加载暂存的背包存档数据");
        }

        // 初始隐藏面板
        if (inventoryPanel != null) inventoryPanel.SetActive(false);
        if (warehousePanel != null) warehousePanel.SetActive(false);
    }

    private void Update()
    {
        // 快捷槽快捷键（2026-09-26 加）：面板打开时 PanelManager 会把 Time.timeScale 置 0，
        // 暂停态下不响应，避免和背包里的右键使用/拖拽打架。
        if (Time.timeScale > 0f)
        {
            if (quickSlotKey1 != KeyCode.None && Input.GetKeyDown(quickSlotKey1)) UseQuickSlot(0);
            if (quickSlotKey2 != KeyCode.None && Input.GetKeyDown(quickSlotKey2)) UseQuickSlot(1);
        }

        // 快捷键切换面板
        if (Input.GetKeyDown(toggleInventoryKey))
            ToggleInventoryPanel();
        if (Input.GetKeyDown(toggleWarehouseKey))
            ToggleWarehousePanel();
    }

    private void OnDestroy()
    {
        if (_equipmentManager != null)
        {
            _equipmentManager.UnregisterCallbacks(OnEquipCallback, OnUnequipCallback);
        }

        if (_instance == this)
            _instance = null;
    }

    // ============================================================
    // 内部初始化
    // ============================================================

    private void InitializeSlots()
    {
        // 背包初始化为全空（用 null 占位）
        for (int i = 0; i < INVENTORY_MAX_SLOTS; i++)
            playerItems.Add(null);
    }

    // ============================================================
    // 背包物品操作
    // ============================================================

    /// <summary>获取背包物品（指定索引，null = 空格子）</summary>
    public ItemInstance GetPlayerItem(int index)
    {
        if (index < 0 || index >= playerItems.Count) return null;
        return playerItems[index];
    }

    /// <summary>获取背包物品列表（只读）</summary>
    public IReadOnlyList<ItemInstance> PlayerItems => playerItems;

    /// <summary>
    /// 向背包添加物品（自动堆叠，否则填充到首个空格子）
    /// </summary>
    /// <param name="template">物品模板</param>
    /// <param name="count">数量</param>
    /// <returns>true = 至少部分添加成功</returns>
    public bool AddItem(ItemSO template, int count = 1)
    {
        if (template == null || count <= 0) return false;

        int remaining = count;

        // 优先堆叠到已有同类物品
        for (int i = 0; i < playerItems.Count && remaining > 0; i++)
        {
            ItemInstance existing = playerItems[i];
            if (existing == null) continue;
            if (existing.template != template) continue;
            if (!existing.CanStack) continue;

            int added = existing.TryStack(remaining);
            remaining -= added;
        }

        // 剩余放入空格子
        for (int i = 0; i < playerItems.Count && remaining > 0; i++)
        {
            if (playerItems[i] != null) continue;

            int stackAmount = Mathf.Min(remaining, template.maxStack);
            playerItems[i] = new ItemInstance(template, stackAmount);
            remaining -= stackAmount;
        }

        if (remaining < count)
        {
            CleanupEmptySlotsBackpack();
            OnInventoryChanged?.Invoke();
            return true;
        }

        Debug.LogWarning($"[InventoryManager] 背包已满，无法添加 {template.itemName}");
        return false;
    }

    /// <summary>
    /// [装备制作 S2] 只读试算：背包能否「全量」装下 count 个 template。
    ///
    /// 可容纳空间 = 已有同类可堆叠物品的剩余堆叠余量之和
    ///            + 空槽位可用容量（空槽数 × template.maxStack）
    ///
    /// 与 AddItem 的区别：AddItem 是「至少部分添加成功即返回 true」，本方法要求全量装得下才返回 true
    /// （合成是原子操作，需要先问「能不能全放下」）。
    /// 纯只读：不修改任何数据、不触发任何事件。
    /// </summary>
    /// <param name="template">物品模板</param>
    /// <param name="count">需要全量装下的数量</param>
    /// <returns>true = 全量装得下；template == null 或 count &lt;= 0 返回 false</returns>
    public bool CanFitFully(ItemSO template, int count)
    {
        if (template == null || count <= 0) return false;

        int capacity = 0;     // 可容纳总数
        int emptySlots = 0;   // 空槽位数量

        for (int i = 0; i < playerItems.Count; i++)
        {
            ItemInstance item = playerItems[i];

            if (item == null)
            {
                emptySlots++;
                continue;
            }

            // 只有同类、且还能继续堆叠的物品才贡献余量
            if (item.template != template) continue;
            if (!item.CanStack) continue;

            capacity += item.RemainingStackSpace;
        }

        // 每个空槽位最多放 template.maxStack 个
        capacity += emptySlots * template.maxStack;

        return capacity >= count;
    }

    /// <summary>
    /// 从背包移除物品
    /// </summary>
    /// <param name="index">物品索引</param>
    /// <param name="count">移除数量（默认全部）</param>
    /// <returns>true = 移除成功</returns>
    public bool RemoveItem(int index, int count = -1)
    {
        ItemInstance item = GetPlayerItem(index);
        if (item == null) return false;

        int toRemove = count < 0 ? item.stackSize : count;
        int removed = item.TryRemove(toRemove);

        if (item.stackSize <= 0)
        {
            playerItems[index] = null;
            // 如果该物品在快捷栏中，清除快捷栏引用
            ClearQuickSlotRef(item);
        }

        CleanupEmptySlotsBackpack();
        OnInventoryChanged?.Invoke();
        return removed > 0;
    }

    /// <summary>
    /// 交换背包中两个位置的物品（或叠放到同一格）
    /// </summary>
    public void SwapPlayerItems(int indexA, int indexB)
    {
        if (indexA == indexB) return;
        ItemInstance itemA = GetPlayerItem(indexA);
        ItemInstance itemB = GetPlayerItem(indexB);

        // 同物品堆叠
        if (itemA != null && itemB != null && itemA.template == itemB.template)
        {
            int moved = itemB.TryStack(itemA.stackSize);
            itemA.TryRemove(moved);
            if (itemA.stackSize <= 0)
            {
                playerItems[indexA] = null;
                ClearQuickSlotRef(itemA);
            }
        }
        else
        {
            // 纯交换
            playerItems[indexA] = itemB;
            playerItems[indexB] = itemA;
        }

        CleanupEmptySlotsBackpack();
        OnInventoryChanged?.Invoke();
    }

    /// <summary>
    /// 按分类过滤并排序背包物品
    /// 返回索引列表（按稀有度降序 + 名称排序），空槽位在末尾
    /// </summary>
    public List<ItemInstance> GetFilteredPlayerItems(ItemCategory category)
    {
        if (category == ItemCategory.All)
            return new List<ItemInstance>(playerItems); // 保持原位顺序

        var filtered = new List<ItemInstance>(playerItems.Count);
        for (int i = 0; i < playerItems.Count; i++)
        {
            ItemInstance item = playerItems[i];
            if (item != null && item.template.Category == category)
                filtered.Add(item);
            else
                filtered.Add(null); // 保持槽位对齐（分类过滤时隐藏不匹配项，由 UI 处理）
        }
        return filtered;
    }

    /// <summary>获取背包中第一个空格子的索引，-1 = 满了</summary>
    public int GetFirstEmptyPlayerSlot()
    {
        for (int i = 0; i < playerItems.Count; i++)
            if (playerItems[i] == null) return i;
        return -1;
    }

    // ============================================================
    // 仓库操作
    // ============================================================

    /// <summary>获取仓库物品（指定索引）</summary>
    public ItemInstance GetWarehouseItem(int index)
    {
        if (index < 0 || index >= warehouseItems.Count) return null;
        if (index >= WAREHOUSE_MAX_SLOTS) return null;
        return warehouseItems.Count > index ? warehouseItems[index] : null;
    }

    /// <summary>获取仓库物品列表（只读）</summary>
    public IReadOnlyList<ItemInstance> WarehouseItems => warehouseItems;

    /// <summary>仓库当前物品数量</summary>
    public int WarehouseCount => warehouseItems.Count;

    /// <summary>
    /// 存入仓库（从背包指定索引移动到仓库）
    /// </summary>
    public bool DepositToWarehouse(int playerIndex, int count = -1)
    {
        ItemInstance playerItem = GetPlayerItem(playerIndex);
        if (playerItem == null) return false;

        int toMove = count < 0 ? playerItem.stackSize : Mathf.Min(count, playerItem.stackSize);

        // 尝试在仓库中堆叠到同类物品
        int remaining = toMove;
        for (int i = 0; i < warehouseItems.Count && remaining > 0; i++)
        {
            ItemInstance wItem = warehouseItems[i];
            if (wItem == null) continue;
            if (wItem.template != playerItem.template) continue;
            if (!wItem.CanStack) continue;
            int added = wItem.TryStack(remaining);
            remaining -= added;
        }

        // 剩余创建新条目
        while (remaining > 0 && warehouseItems.Count < WAREHOUSE_MAX_SLOTS)
        {
            int stackAmount = Mathf.Min(remaining, playerItem.template.maxStack);
            warehouseItems.Add(new ItemInstance(playerItem.template, stackAmount));
            remaining -= stackAmount;
        }

        // 从背包移除
        if (remaining < toMove)
        {
            playerItem.TryRemove(toMove - remaining);
            if (playerItem.stackSize <= 0)
            {
                playerItems[playerIndex] = null;
                ClearQuickSlotRef(playerItem);
            }

            CleanupEmptySlotsBackpack();
            CleanupZeroStackWarehouse();
            OnInventoryChanged?.Invoke();
            OnWarehouseChanged?.Invoke();
            return true;
        }

        Debug.LogWarning("[InventoryManager] 仓库已满，无法存入");
        return false;
    }

    /// <summary>
    /// [装备制作 S2] 直接往仓库添加 count 个 template（原子写入：全成功才写，不够则一个都不写）。
    ///
    /// 流程：先只读试算仓库容量（同类剩余堆叠余量 + 剩余槽位数 × template.maxStack），
    ///       不够 → 直接 return false，**不改动任何数据、不触发事件**（合成需要这种原子性）；
    ///       够 → 按 DepositToWarehouse 的仓库堆叠口径写入（先堆叠同类、再补新条目），写完整笔成功，触发 OnWarehouseChanged。
    ///
    /// 与 DepositToWarehouse 的区别：入口是「ItemSO + 数量」，不涉及背包索引；且要求全量成功。
    /// </summary>
    /// <param name="template">物品模板</param>
    /// <param name="count">需要全量加入仓库的数量</param>
    /// <returns>true = 全量加入成功；template == null、count &lt;= 0 或仓库装不下返回 false</returns>
    public bool TryAddToWarehouse(ItemSO template, int count)
    {
        if (template == null || count <= 0) return false;

        // ── 第一步：只读试算仓库可容纳空间 ──
        int capacity = 0;
        for (int i = 0; i < warehouseItems.Count; i++)
        {
            ItemInstance wItem = warehouseItems[i];
            if (wItem == null) continue;
            if (wItem.template != template) continue;
            if (!wItem.CanStack) continue;

            capacity += wItem.RemainingStackSpace;
        }

        // 剩余槽位数（与 DepositToWarehouse 的扩容条件 warehouseItems.Count < WAREHOUSE_MAX_SLOTS 同口径）
        capacity += (WAREHOUSE_MAX_SLOTS - warehouseItems.Count) * template.maxStack;

        if (capacity < count) return false;   // 装不下 → 原子性：一个都不写

        // ── 第二步：写入（复用 DepositToWarehouse 的仓库堆叠口径：先堆叠同类）──
        int remaining = count;
        for (int i = 0; i < warehouseItems.Count && remaining > 0; i++)
        {
            ItemInstance wItem = warehouseItems[i];
            if (wItem == null) continue;
            if (wItem.template != template) continue;
            if (!wItem.CanStack) continue;

            remaining -= wItem.TryStack(remaining);
        }

        // ── 剩余创建新条目（同上口径：每个新条目最多 template.maxStack 个）──
        while (remaining > 0 && warehouseItems.Count < WAREHOUSE_MAX_SLOTS)
        {
            int stackAmount = Mathf.Min(remaining, template.maxStack);
            warehouseItems.Add(new ItemInstance(template, stackAmount));
            remaining -= stackAmount;
        }

        OnWarehouseChanged?.Invoke();
        return true;
    }

    /// <summary>
    /// 从仓库取出（到背包指定位置或首个空格）
    /// </summary>
    public bool WithdrawFromWarehouse(int warehouseIndex, int count = -1, int targetPlayerSlot = -1)
    {
        if (warehouseIndex < 0 || warehouseIndex >= warehouseItems.Count) return false;
        ItemInstance wItem = warehouseItems[warehouseIndex];
        if (wItem == null) return false;

        int toMove = count < 0 ? wItem.stackSize : Mathf.Min(count, wItem.stackSize);

        // 找目标槽位
        if (targetPlayerSlot < 0)
            targetPlayerSlot = GetFirstEmptyPlayerSlot();

        if (targetPlayerSlot < 0 || targetPlayerSlot >= playerItems.Count)
        {
            Debug.LogWarning("[InventoryManager] 背包无空位");
            return false;
        }

        ItemInstance targetItem = playerItems[targetPlayerSlot];

        // 同物品堆叠（目标槽非空且同类型）
        if (targetItem != null && targetItem.template == wItem.template && targetItem.CanStack)
        {
            int added = targetItem.TryStack(toMove);
            wItem.TryRemove(added);

            if (wItem.stackSize <= 0)
            {
                warehouseItems.RemoveAt(warehouseIndex);
            }
        }
        else if (targetItem == null)
        {
            // 空格子，直接放入
            int stackAmount = Mathf.Min(toMove, wItem.template.maxStack);
            playerItems[targetPlayerSlot] = new ItemInstance(wItem.template, stackAmount);
            wItem.TryRemove(stackAmount);

            if (wItem.stackSize <= 0)
            {
                warehouseItems.RemoveAt(warehouseIndex);
            }
        }
        else
        {
            // 目标槽有不同物品，交换
            ItemInstance temp = playerItems[targetPlayerSlot];
            playerItems[targetPlayerSlot] = wItem;
            warehouseItems[warehouseIndex] = temp;
        }

        CleanupZeroStackWarehouse();
        OnInventoryChanged?.Invoke();
        OnWarehouseChanged?.Invoke();
        return true;
    }

    /// <summary>
    /// [装备制作 S3] 从仓库指定格位直接移除物品（对称于 RemoveItem 的仓库版）。
    ///
    /// 与 WithdrawFromWarehouse 的区别：本方法**不经过背包**、不占背包格 ——
    /// 合成暂存区「填入时真从仓库扣掉材料」需要这个口径（取到背包会占背包格，口径不对）。
    /// 与 RemoveItem 的差别只有清理/事件那一对：仓库走 CleanupZeroStackWarehouse() / OnWarehouseChanged
    /// （快捷栏只会绑定背包物品实例，故本方法不需要 ClearQuickSlotRef）。
    ///
    /// 纯数据层：扣除成功才走现有 CleanupZeroStackWarehouse() 压缩列表并触发 OnWarehouseChanged；
    /// 格位不合法 / 该格为空 → 直接返回 false，不改动任何数据、不触发事件。
    /// </summary>
    /// <param name="index">仓库格位下标</param>
    /// <param name="count">移除数量（&lt; 0 = 全部取走）</param>
    /// <returns>true = 至少移除了 1 个（与 RemoveItem 同口径：不足时扣掉现有的并返回 true）</returns>
    public bool RemoveWarehouseItem(int index, int count = -1)
    {
        ItemInstance item = GetWarehouseItem(index);
        if (item == null) return false;

        int toRemove = count < 0 ? item.stackSize : count;
        int removed = item.TryRemove(toRemove);

        if (item.stackSize <= 0)
            warehouseItems[index] = null;

        CleanupZeroStackWarehouse();
        OnWarehouseChanged?.Invoke();
        return removed > 0;
    }

    /// <summary>仓库物品交换</summary>
    public void SwapWarehouseItems(int indexA, int indexB)
    {
        if (indexA == indexB) return;
        if (indexA < 0 || indexB < 0) return;
        if (indexA >= warehouseItems.Count || indexB >= warehouseItems.Count) return;

        ItemInstance itemA = warehouseItems[indexA];
        ItemInstance itemB = warehouseItems[indexB];

        // 同物品堆叠
        if (itemA != null && itemB != null && itemA.template == itemB.template)
        {
            int moved = itemB.TryStack(itemA.stackSize);
            itemA.TryRemove(moved);
            if (itemA.stackSize <= 0)
                warehouseItems.RemoveAt(indexA);
        }
        else
        {
            warehouseItems[indexA] = itemB;
            warehouseItems[indexB] = itemA;
        }

        CleanupZeroStackWarehouse();
        OnWarehouseChanged?.Invoke();
    }

    /// <summary>按分类过滤仓库物品</summary>
    public List<ItemInstance> GetFilteredWarehouseItems(ItemCategory category)
    {
        if (category == ItemCategory.All)
            return new List<ItemInstance>(warehouseItems);

        var filtered = new List<ItemInstance>();
        for (int i = 0; i < warehouseItems.Count; i++)
        {
            ItemInstance item = warehouseItems[i];
            if (item != null && item.template.Category == category)
                filtered.Add(item);
        }
        return filtered;
    }

    // ============================================================
    // 装备操作（委托 EquipmentManager）
    // ============================================================

    /// <summary>
    /// 从背包装备到指定槽位（移除背包物品 → 委托 EquipmentManager.Equip）
    /// 若槽位已满，旧装备自动换回背包
    /// </summary>
    public bool EquipItem(int playerIndex, EquipmentSlotType slot)
    {
        ItemInstance item = GetPlayerItem(playerIndex);
        if (item == null) return false;
        if (_equipmentManager == null)
        {
            Debug.LogWarning("[InventoryManager] EquipmentManager 未找到");
            return false;
        }

        if (item.template.Category != ItemCategory.Equipment)
        {
            Debug.LogWarning($"[InventoryManager] {item.DisplayName} 不是装备");
            return false;
        }

        if (!EquipmentSlotUtility.CanEquipTo(item.template.EquipCategory, slot))
        {
            Debug.LogWarning($"[InventoryManager] {item.DisplayName} 槽位不匹配: {item.template.EquipCategory} ≠ {slot}");
            return false;
        }

        // 检查目标槽是否已有装备（会被 EquipmentManager.Equip 自动卸下）
        ItemInstance oldEquip = _equipmentManager.GetEquipped(slot);

        // 从背包移除
        playerItems[playerIndex] = null;
        ClearQuickSlotRef(item);

        // 委托装备
        bool success = _equipmentManager.Equip(slot, item);
        if (!success)
        {
            // 回滚：放回背包
            playerItems[playerIndex] = item;
            CleanupEmptySlotsBackpack();
            OnInventoryChanged?.Invoke();
            return false;
        }

        // 旧装备放回背包（如果存在）
        if (oldEquip != null)
        {
            // 尝试放回原位
            if (playerItems[playerIndex] == null)
                playerItems[playerIndex] = oldEquip;
            else
                AddItemInstance(oldEquip);
        }

        CleanupEmptySlotsBackpack();
        OnInventoryChanged?.Invoke();
        OnEquipmentChanged?.Invoke();
        return true;
    }

    /// <summary>
    /// 卸下指定槽位的装备到背包首个空格
    /// </summary>
    public bool UnequipItem(EquipmentSlotType slot)
    {
        if (_equipmentManager == null) return false;

        ItemInstance unequipped = _equipmentManager.Unequip(slot);
        if (unequipped == null) return false;

        // 放回背包
        AddItemInstance(unequipped);

        OnInventoryChanged?.Invoke();
        OnEquipmentChanged?.Invoke();
        return true;
    }

    /// <summary>
    /// 自动装备到匹配的空槽（2026-09-26 加，供背包左键第二段用）。
    /// 候选槽由装备类别决定（饰品两个槽，顺序即优先顺序）；候选槽全满 → 不操作，返回 false。
    /// </summary>
    public bool EquipItemAuto(int playerIndex)
    {
        ItemInstance item = GetPlayerItem(playerIndex);
        if (item == null || item.template == null) return false;
        if (item.template.Category != ItemCategory.Equipment) return false;
        if (_equipmentManager == null) return false;

        foreach (EquipmentSlotType slot in EquipmentSlotUtility.GetSlots(item.template.EquipCategory))
        {
            if (!_equipmentManager.HasEquipped(slot))
                return EquipItem(playerIndex, slot);
        }

        return false;
    }

    /// <summary>查询指定槽位装备</summary>
    public ItemInstance GetEquippedItem(EquipmentSlotType slot)
    {
        return _equipmentManager != null ? _equipmentManager.GetEquipped(slot) : null;
    }

    // ============================================================
    // 快捷栏操作
    // ============================================================

    /// <summary>获取快捷栏物品（索引 0 或 1）</summary>
    public ItemInstance GetQuickSlot(int index)
    {
        if (index < 0 || index >= QUICK_SLOT_COUNT) return null;
        return quickSlots[index];
    }

    /// <summary>
    /// [搬运模型 2026-09-26] 背包格 → 快捷槽。
    /// 规则：
    ///   1. 槽空          → 搬入（数量超上限就拆，上限那部分进槽，余量留在背包格）
    ///   2. 两边同种      → 往槽里堆到上限，超出的留背包格
    ///   3. 两边异种      → 背包那件不超上限时直接互换；超上限则拆上限个进槽，槽里那件收回背包
    /// 反方向（快捷槽 → 背包）用 ReturnQuickSlotToBackpack，它优先堆叠进背包里的同种。
    /// </summary>
    /// <returns>是否执行成功</returns>
    public bool MoveBackpackToQuickSlot(int quickSlotIndex, int playerInventoryIndex)
    {
        if (quickSlotIndex < 0 || quickSlotIndex >= QUICK_SLOT_COUNT) return false;
        if (playerInventoryIndex < 0 || playerInventoryIndex >= playerItems.Count) return false;

        ItemInstance fromBag = playerItems[playerInventoryIndex];
        ItemInstance fromQuick = quickSlots[quickSlotIndex];

        if (fromBag == null) return false;   // 这个方向必须有来源物品

        // 快捷槽只收消耗品
        if (fromBag.template == null || fromBag.template.Category != ItemCategory.Consumable)
        {
            Debug.LogWarning($"[InventoryManager] 快捷槽只能放消耗品，{fromBag.DisplayName} 分类为 {fromBag.template?.Category}");
            return false;
        }

        int limit = GetQuickSlotStackLimit(fromBag);

        // ① 槽空 → 搬入（可拆分）
        if (fromQuick == null)
        {
            quickSlots[quickSlotIndex] = TakeFromBag(playerInventoryIndex, limit);
            NotifyQuickSlotAndInventoryChanged();
            return true;
        }

        // ② 同种 → 往槽里堆叠到上限，超出的留背包格
        if (fromBag.template == fromQuick.template)
        {
            int space = Mathf.Max(0, limit - fromQuick.stackSize);
            if (space > 0)
            {
                int moved = fromBag.TryRemove(Mathf.Min(space, fromBag.stackSize));
                fromQuick.TryStack(moved);
            }

            if (fromBag.stackSize <= 0)
                playerItems[playerInventoryIndex] = null;

            NotifyQuickSlotAndInventoryChanged();
            return true;
        }

        // ③ 异种 → 不超上限直接互换；超上限则拆上限个进槽，槽里那件收回背包
        if (fromBag.stackSize <= limit)
        {
            quickSlots[quickSlotIndex] = fromBag;
            playerItems[playerInventoryIndex] = fromQuick;
        }
        else
        {
            quickSlots[quickSlotIndex] = TakeFromBag(playerInventoryIndex, limit);
            AddItemInstance(fromQuick);   // 旧物回背包（先堆叠，再找空格）
        }

        NotifyQuickSlotAndInventoryChanged();
        return true;
    }

    /// <summary>
    /// 从背包指定格取出最多 count 个，返回独立实例（刚好取完时整件取走，并把背包格清空）。
    /// 快捷槽搬运拆分专用。
    /// </summary>
    private ItemInstance TakeFromBag(int bagIndex, int count)
    {
        ItemInstance bagItem = playerItems[bagIndex];
        if (bagItem == null) return null;

        count = Mathf.Clamp(count, 1, bagItem.stackSize);

        if (count >= bagItem.stackSize)
        {
            playerItems[bagIndex] = null;
            return bagItem;
        }

        var split = new ItemInstance(bagItem.template, count);
        bagItem.TryRemove(count);
        return split;
    }

    /// <summary>快捷槽单格堆叠上限：配置 > 0 用它，否则回退到物品自身的 maxStack</summary>
    private int GetQuickSlotStackLimit(ItemInstance item)
    {
        if (quickSlotMaxStack > 0) return quickSlotMaxStack;

        int fallback = item?.template != null ? item.template.maxStack : 1;
        return Mathf.Max(1, fallback);
    }

    private void NotifyQuickSlotAndInventoryChanged()
    {
        OnInventoryChanged?.Invoke();
        OnQuickSlotsChanged?.Invoke();
    }

    /// <summary>
    /// [搬运模型 2026-09-26] 把快捷槽里的物品放回背包。
    /// preferredIndex >= 0 且那一格为空时优先进那一格，否则进首个空格。
    /// </summary>
    /// <returns>放回后的背包索引；-1 = 失败（背包满）</returns>
    public int ReturnQuickSlotToBackpack(int quickSlotIndex, int preferredIndex = -1)
    {
        if (quickSlotIndex < 0 || quickSlotIndex >= QUICK_SLOT_COUNT) return -1;

        ItemInstance item = quickSlots[quickSlotIndex];
        if (item == null) return -1;

        // 指定格优先：空格直接放；同种且有空间则先堆进去（这就是「拖回背包要自己找堆叠」）
        if (preferredIndex >= 0 && preferredIndex < playerItems.Count)
        {
            ItemInstance target = playerItems[preferredIndex];
            if (target == null)
            {
                playerItems[preferredIndex] = item;
                quickSlots[quickSlotIndex] = null;
                NotifyQuickSlotAndInventoryChanged();
                return preferredIndex;
            }
            if (target.template == item.template && target.CanStack)
            {
                int moved = target.TryStack(item.stackSize);
                item.TryRemove(moved);
                if (item.stackSize <= 0)
                {
                    quickSlots[quickSlotIndex] = null;
                    NotifyQuickSlotAndInventoryChanged();
                    return preferredIndex;
                }
                // 还有剩 → 落到下面的统一收纳
            }
        }

        // 统一收纳：先堆叠到背包里的同种，再放首个空格
        int landed = AddItemInstance(item);

        if (item.stackSize <= 0 || landed >= 0)
            quickSlots[quickSlotIndex] = null;   // 全部放完
        else
            Debug.LogWarning("[InventoryManager] 背包已满，快捷槽物品放不回去（剩余仍留在快捷槽）");

        NotifyQuickSlotAndInventoryChanged();
        return landed;
    }

    /// <summary>交换两个快捷槽的内容（2026-09-26 加，供背包页锚点槽位互拖）</summary>
    public void SwapQuickSlots(int slotA, int slotB)
    {
        if (slotA == slotB) return;
        if (slotA < 0 || slotA >= QUICK_SLOT_COUNT) return;
        if (slotB < 0 || slotB >= QUICK_SLOT_COUNT) return;

        (quickSlots[slotA], quickSlots[slotB]) = (quickSlots[slotB], quickSlots[slotA]);
        OnQuickSlotsChanged?.Invoke();
    }

    /// <summary>使用快捷栏物品（执行效果 → 减少 1 个堆叠；用光后槽位空出来，物品消失不回收进背包）</summary>
    public bool UseQuickSlot(int slotIndex)
    {
        ItemInstance item = GetQuickSlot(slotIndex);
        if (item == null) return false;

        // 执行消耗品效果；返回 false = 本次未生效（如满血/无效果），此时不扣数量
        if (!TryApplyConsumableEffect(item)) return false;

        item.TryRemove(1);

        // 搬运模型：物品住在快捷槽里，用完直接从槽位消失（背包里没有它的份）
        if (item.stackSize <= 0)
            quickSlots[slotIndex] = null;

        OnQuickSlotsChanged?.Invoke();
        return true;
    }

    /// <summary>
    /// 使用背包指定格的消耗品（2026-09-26 加：背包格左键第二段点击入口）。
    /// 只接受 ItemCategory.Consumable；效果与快捷栏共用 TryApplyConsumableEffect。
    /// </summary>
    /// <param name="playerInventoryIndex">背包格索引（与 playerItems 同步）</param>
    public bool UsePlayerItem(int playerInventoryIndex)
    {
        ItemInstance item = GetPlayerItem(playerInventoryIndex);
        if (item == null || item.template == null) return false;
        if (item.template.Category != ItemCategory.Consumable) return false;

        if (!TryApplyConsumableEffect(item)) return false;

        item.TryRemove(1);
        CleanupEmptySlotsBackpack();
        OnInventoryChanged?.Invoke();
        OnQuickSlotsChanged?.Invoke();
        return true;
    }

    /// <summary>
    /// 执行消耗品效果（2026-09-26 加，先只做回血）。
    /// 返回 false = 本次不消耗：满血不浪费、或该物品没配任何效果。
    /// 后续要多种效果（回蓝/增益等）时再抽 ItemEffectDataSO，此处只做分支。
    /// </summary>
    private bool TryApplyConsumableEffect(ItemInstance item)
    {
        if (item?.template == null) return false;

        float heal = item.template.HealAmount;
        if (heal > 0f)
        {
            PlayerHealth health = PlayerHealth.Instance;
            if (health == null) return false;
            if (health.CurrentHealth >= health.MaxHealth) return false;   // 满血：不消耗
            health.Heal(heal);
            return true;
        }

        return false;   // 未配置任何效果 = 不可用（不扣数量）
    }

    // ============================================================
    // 分类状态管理
    // ============================================================

    /// <summary>
    /// 设置当前分类（背包/仓库任一 Tab 点击时调用）
    /// 触发 OnCategoryChanged → 双面板同步刷新 Tab 高亮和过滤
    /// </summary>
    public void SetActiveCategory(ItemCategory category)
    {
        if (_activeCategory == category) return;
        _activeCategory = category;
        OnCategoryChanged?.Invoke(category);
    }

    // ============================================================
    // 面板开关（委托 PanelManager）
    // ============================================================

    /// <summary>切换背包面板显隐</summary>
    public void ToggleInventoryPanel()
    {
        if (inventoryPanel != null)
            PanelManager.Instance?.TogglePanel(inventoryPanel);
    }

    /// <summary>切换仓库面板显隐</summary>
    public void ToggleWarehousePanel()
    {
        if (warehousePanel != null)
            PanelManager.Instance?.TogglePanel(warehousePanel);
    }

    /// <summary>打开背包面板</summary>
    /// <summary>
    /// 背包面板是否已经打开（2026-09-25 加：合成面板打开时要自动把背包带出来，已开就不动）
    /// 直接用面板物体的激活状态判定，不改任何现有开关逻辑。
    /// </summary>
    public bool IsInventoryOpen => inventoryPanel != null && inventoryPanel.activeSelf;

    public void OpenInventoryPanel()
    {
        if (inventoryPanel != null && !inventoryPanel.activeSelf)
            PanelManager.Instance?.OpenPanel(inventoryPanel);
    }

    /// <summary>关闭背包面板</summary>
    public void CloseInventoryPanel()
    {
        if (inventoryPanel != null && inventoryPanel.activeSelf)
            PanelManager.Instance?.ClosePanel(inventoryPanel);
    }

    // ============================================================
    // IPickupReceiver — 世界掉落物拾取
    // ============================================================

    public bool TryPickup(DropItem drop)
    {
        if (drop == null || drop.ItemData == null) return false;
        return AddItem(drop.ItemData.template, drop.ItemData.stackSize);
    }

    // ============================================================
    // EquipmentManager 回调
    // ============================================================

    private void OnEquipCallback(EquipmentSlotType slot, ItemInstance item)
    {
        // 装备成功回调，由 EquipItem 中已经处理了数据
        // 这里仅用于日志或额外逻辑
    }

    private void OnUnequipCallback(EquipmentSlotType slot, ItemInstance item)
    {
        // 卸下成功回调，由 UnequipItem 中已经处理了数据
    }

    // ============================================================
    // 内部辅助方法
    // ============================================================

    /// <summary>将 ItemInstance 放入背包（优先堆叠到同种，再放首个空格）</summary>
    /// <returns>落位索引（堆叠时返回被堆进的那一格）；-1 = 放不下（可能已部分堆叠，剩余仍留在 item 里）</returns>
    private int AddItemInstance(ItemInstance item)
    {
        if (item == null || !item.IsValid) return -1;

        // 尝试堆叠
        for (int i = 0; i < playerItems.Count; i++)
        {
            ItemInstance existing = playerItems[i];
            if (existing == null) continue;
            if (existing.template != item.template) continue;
            if (!existing.CanStack) continue;

            int added = existing.TryStack(item.stackSize);
            item.TryRemove(added);
            if (item.stackSize <= 0) return i;   // 全堆进这一格
        }

        // 放入空格
        for (int i = 0; i < playerItems.Count; i++)
        {
            if (playerItems[i] == null)
            {
                playerItems[i] = item;
                return i;
            }
        }

        Debug.LogWarning($"[InventoryManager] 背包已满，{item.DisplayName} 无法放入");
        return -1;
    }

    /// <summary>清理背包末端的 null 项，压缩列表</summary>
    private void CleanupEmptySlotsBackpack()
    {
        // 从末尾移除 null（保持定长到最大容量）
        // 背包使用固定 11 格，不移除 null 项
        // 仅清理引用
    }

    /// <summary>清理仓库中 stackSize=0 的条目</summary>
    private void CleanupZeroStackWarehouse()
    {
        for (int i = warehouseItems.Count - 1; i >= 0; i--)
        {
            if (warehouseItems[i] == null || warehouseItems[i].stackSize <= 0)
                warehouseItems.RemoveAt(i);
        }
    }

    /// <summary>清理快捷栏中对已删除物品的引用</summary>
    private void ClearQuickSlotRef(ItemInstance item)
    {
        for (int i = 0; i < quickSlots.Length; i++)
        {
            if (quickSlots[i] == item)
                quickSlots[i] = null;
        }
    }

    // ============================================================
    // [Phase5] 存档接口
    // ============================================================

    /// <summary>
    /// 通过物品 ID 查找 ItemSO 模板
    /// </summary>
    public ItemSO FindItemById(string id)
    {
        return ItemSO.FindById(id);
    }

    /// <summary>
    /// 将当前背包/仓库/装备/快捷栏状态序列化为可 JSON 保存的数据
    /// </summary>
    public InventorySaveData SaveToData()
    {
        var data = new InventorySaveData();

        // ── 背包 ──
        data.playerItems = new ItemSaveEntry[playerItems.Count];
        for (int i = 0; i < playerItems.Count; i++)
        {
            var item = playerItems[i];
            if (item != null && item.IsValid && item.template != null)
            {
                data.playerItems[i] = new ItemSaveEntry
                {
                    itemId = item.template.id,
                    stackSize = item.stackSize,
                    durability = item.currentDurability
                };
            }
        }

        // ── 仓库 ──
        data.warehouseItems = new ItemSaveEntry[warehouseItems.Count];
        for (int i = 0; i < warehouseItems.Count; i++)
        {
            var item = warehouseItems[i];
            if (item != null && item.IsValid && item.template != null)
            {
                data.warehouseItems[i] = new ItemSaveEntry
                {
                    itemId = item.template.id,
                    stackSize = item.stackSize,
                    durability = item.currentDurability
                };
            }
        }

        // ── 装备槽 ──
        data.equipSlots = new EquipmentSlotSave[4];
        if (_equipmentManager != null)
        {
            for (int i = 0; i < 4; i++)
            {
                var slot = (EquipmentSlotType)i;
                var equipped = _equipmentManager.GetEquipped(slot);
                if (equipped != null && equipped.IsValid && equipped.template != null)
                {
                    data.equipSlots[i] = new EquipmentSlotSave
                    {
                        slotType = (int)slot,
                        itemId = equipped.template.id,
                        stackSize = equipped.stackSize,
                        durability = equipped.currentDurability
                    };
                }
            }
        }

        // ── 快捷槽（搬运模型 2026-09-26：物品住在槽里，直接存它自己）──
        data.quickSlotItems = new ItemSaveEntry[QUICK_SLOT_COUNT];
        for (int i = 0; i < QUICK_SLOT_COUNT; i++)
        {
            var quickItem = quickSlots[i];
            if (quickItem != null && quickItem.IsValid && quickItem.template != null)
            {
                data.quickSlotItems[i] = new ItemSaveEntry
                {
                    itemId = quickItem.template.id,
                    stackSize = quickItem.stackSize,
                    durability = quickItem.currentDurability
                };
            }
        }

        // ── 分类状态 ──
        data.activeCategory = (int)_activeCategory;

        return data;
    }

    /// <summary>
    /// 从存档数据恢复背包/仓库/装备/快捷栏状态
    /// </summary>
    public void LoadFromData(InventorySaveData data)
    {
        if (data == null) return;

        // ── 清空当前状态 ──
        playerItems.Clear();
        warehouseItems.Clear();
        for (int i = 0; i < QUICK_SLOT_COUNT; i++)
            quickSlots[i] = null;

        // ── 恢复背包 ──
        if (data.playerItems != null)
        {
            for (int i = 0; i < data.playerItems.Length && i < INVENTORY_MAX_SLOTS; i++)
            {
                var entry = data.playerItems[i];
                if (entry != null && !string.IsNullOrEmpty(entry.itemId))
                {
                    ItemSO template = ItemSO.FindById(entry.itemId);
                    if (template != null)
                    {
                        var item = new ItemInstance(template, entry.stackSize);
                        if (entry.hasDurability)
                            item.currentDurability = entry.durability;
                        playerItems.Add(item);
                    }
                    else
                    {
                        Debug.LogWarning($"[InventoryManager] 存档中物品 ID '{entry.itemId}' 未找到对应模板，跳过");
                        playerItems.Add(null);
                    }
                }
                else
                {
                    playerItems.Add(null);
                }
            }
            // 补充空槽到最大容量
            while (playerItems.Count < INVENTORY_MAX_SLOTS)
                playerItems.Add(null);
        }

        // ── 恢复仓库 ──
        if (data.warehouseItems != null)
        {
            foreach (var entry in data.warehouseItems)
            {
                if (entry != null && !string.IsNullOrEmpty(entry.itemId))
                {
                    ItemSO template = ItemSO.FindById(entry.itemId);
                    if (template != null)
                    {
                        var item = new ItemInstance(template, entry.stackSize);
                        if (entry.hasDurability)
                            item.currentDurability = entry.durability;
                        warehouseItems.Add(item);
                    }
                }
            }
        }

        // ── 恢复装备槽 ──
        if (_equipmentManager != null && data.equipSlots != null)
        {
            for (int i = 0; i < data.equipSlots.Length && i < 4; i++)
            {
                var slotData = data.equipSlots[i];
                if (slotData != null && !string.IsNullOrEmpty(slotData.itemId))
                {
                    ItemSO template = ItemSO.FindById(slotData.itemId);
                    if (template != null)
                    {
                        var item = new ItemInstance(template, 1); // 装备不堆叠
                        if (slotData.hasDurability)
                            item.currentDurability = slotData.durability;
                        _equipmentManager.Equip((EquipmentSlotType)slotData.slotType, item);
                    }
                }
            }
        }

        // ── 恢复快捷槽（搬运模型：槽里直接放实例）──
        if (data.quickSlotItems != null)
        {
            for (int i = 0; i < Mathf.Min(data.quickSlotItems.Length, QUICK_SLOT_COUNT); i++)
            {
                var entry = data.quickSlotItems[i];
                if (entry == null || string.IsNullOrEmpty(entry.itemId)) continue;

                ItemSO template = ItemSO.FindById(entry.itemId);
                if (template == null)
                {
                    Debug.LogWarning($"[InventoryManager] 快捷槽存档物品 ID '{entry.itemId}' 未找到对应模板，跳过");
                    continue;
                }

                var item = new ItemInstance(template, entry.stackSize);
                if (entry.hasDurability)
                    item.currentDurability = entry.durability;
                quickSlots[i] = item;
            }
        }
        // ── 旧档兼容（2026-09-26 之前的 quickSlotBindings = 背包索引）：把背包里那件搬进快捷槽 ──
        else if (data.quickSlotBindings != null)
        {
            for (int i = 0; i < Mathf.Min(data.quickSlotBindings.Length, QUICK_SLOT_COUNT); i++)
            {
                int backpackIndex = data.quickSlotBindings[i];
                if (backpackIndex < 0 || backpackIndex >= playerItems.Count) continue;

                var item = playerItems[backpackIndex];
                if (item != null && item.template != null && item.template.Category == ItemCategory.Consumable)
                {
                    quickSlots[i] = item;
                    playerItems[backpackIndex] = null;   // 搬运模型：搬走，背包那格留空
                }
            }
        }

        // ── 恢复分类状态 ──
        _activeCategory = (ItemCategory)Mathf.Clamp(data.activeCategory, 0, 3);

        // ── 触发各面板刷新 ──
        OnInventoryChanged?.Invoke();
        OnWarehouseChanged?.Invoke();
        OnEquipmentChanged?.Invoke();
        OnQuickSlotsChanged?.Invoke();
        OnCategoryChanged?.Invoke(_activeCategory);
    }

    // ============================================================
    // 调试
    // ============================================================

    #if UNITY_EDITOR
    [ContextMenu("Debug/Print Inventory")]
    private void DebugPrintInventory()
    {
        Debug.Log($"=== 背包 (分类: {_activeCategory}) ===");
        for (int i = 0; i < playerItems.Count; i++)
        {
            var item = playerItems[i];
            Debug.Log($"  [{i}] {(item != null ? item.ToString() : "(空)")}");
        }

        Debug.Log("=== 仓库 ===");
        for (int i = 0; i < warehouseItems.Count; i++)
        {
            var item = warehouseItems[i];
            Debug.Log($"  [{i}] {(item != null ? item.ToString() : "(空)")}");
        }

        Debug.Log("=== 装备槽 ===");
        if (_equipmentManager != null)
        {
            Debug.Log($"  Weapon:     {_equipmentManager.GetEquipped(EquipmentSlotType.Weapon)?.DisplayName ?? "(空)"}");
            Debug.Log($"  Armor:      {_equipmentManager.GetEquipped(EquipmentSlotType.Armor)?.DisplayName ?? "(空)"}");
            Debug.Log($"  Accessory0: {_equipmentManager.GetEquipped(EquipmentSlotType.Accessory0)?.DisplayName ?? "(空)"}");
            Debug.Log($"  Accessory1: {_equipmentManager.GetEquipped(EquipmentSlotType.Accessory1)?.DisplayName ?? "(空)"}");
        }

        Debug.Log("=== 快捷栏 ===");
        for (int i = 0; i < quickSlots.Length; i++)
        {
            var item = quickSlots[i];
            Debug.Log($"  [{i}] {(item != null ? item.ToString() : "(空)")}");
        }
    }
    #endif
}

// ============================================================
// [Phase5] 存档数据类 — JSON 可序列化，使用字符串 ID 而非 SO 引用
// ============================================================

/// <summary>存档数据总容器</summary>
[System.Serializable]
public class InventorySaveData
{
    /// <summary>背包物品列表（定长 11，null = 空格）</summary>
    public ItemSaveEntry[] playerItems;

    /// <summary>仓库物品列表（变长，动态列表）</summary>
    public ItemSaveEntry[] warehouseItems;

    /// <summary>装备槽数据（4 个定长）</summary>
    public EquipmentSlotSave[] equipSlots;

    /// <summary>[搬运模型] 快捷槽内容（2 格，null = 空；2026-09-26 起物品直接住在槽里）</summary>
    public ItemSaveEntry[] quickSlotItems;

    /// <summary>[已废弃] 旧档的快捷栏绑定：每个元素 = 背包索引，-1 = 空。仅读档时做向后兼容搬运。</summary>
    public int[] quickSlotBindings;

    /// <summary>当前分类状态：0=All, 1=Consumable, 2=Equipment, 3=Material</summary>
    public int activeCategory;
}

/// <summary>单个物品的存档条目</summary>
[System.Serializable]
public class ItemSaveEntry
{
    /// <summary>物品模板 ID（对应 ItemSO.id）</summary>
    public string itemId;

    /// <summary>堆叠数量</summary>
    public int stackSize = 1;

    /// <summary>当前耐久度（仅装备类有效，-1=不适用）</summary>
    public int durability = -1;

    /// <summary>是否有耐久度数据</summary>
    public bool hasDurability => durability >= 0;
}

/// <summary>装备槽存档条目</summary>
[System.Serializable]
public class EquipmentSlotSave
{
    /// <summary>槽位类型：0=Weapon, 1=Armor, 2=Accessory0, 3=Accessory1</summary>
    public int slotType;

    /// <summary>装备物品模板 ID</summary>
    public string itemId;

    /// <summary>堆叠数量（装备通常为 1）</summary>
    public int stackSize = 1;

    /// <summary>当前耐久度</summary>
    public int durability = -1;

    /// <summary>是否有耐久度数据</summary>
    public bool hasDurability => durability >= 0;
}
