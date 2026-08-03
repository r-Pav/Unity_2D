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
    public static InventoryManager Instance
    {
        get
        {
            if (_instance == null)
                _instance = FindObjectOfType<InventoryManager>();
            return _instance;
        }
    }

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
            if (item != null && item.template.category == category)
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
            if (item != null && item.template.category == category)
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

        if (item.template.category != ItemCategory.Equipment)
        {
            Debug.LogWarning($"[InventoryManager] {item.DisplayName} 不是装备");
            return false;
        }

        if (item.template.slotType != slot)
        {
            Debug.LogWarning($"[InventoryManager] {item.DisplayName} 槽位不匹配: {item.template.slotType} ≠ {slot}");
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
    /// 将背包物品绑定到快捷栏
    /// </summary>
    public bool SetQuickSlot(int slotIndex, int playerInventoryIndex)
    {
        if (slotIndex < 0 || slotIndex >= QUICK_SLOT_COUNT) return false;

        ItemInstance item = GetPlayerItem(playerInventoryIndex);
        if (item == null) return false;
        if (item.template.category != ItemCategory.Consumable)
        {
            Debug.LogWarning($"[InventoryManager] 快捷栏只能绑定消耗品，{item.DisplayName} 分类为 {item.template.category}");
            return false;
        }

        quickSlots[slotIndex] = item;
        OnQuickSlotsChanged?.Invoke();
        return true;
    }

    /// <summary>清空快捷栏指定槽位</summary>
    public void ClearQuickSlot(int slotIndex)
    {
        if (slotIndex < 0 || slotIndex >= QUICK_SLOT_COUNT) return;
        quickSlots[slotIndex] = null;
        OnQuickSlotsChanged?.Invoke();
    }

    /// <summary>使用快捷栏物品（减少 1 个堆叠，0 时自动清空）</summary>
    public bool UseQuickSlot(int slotIndex)
    {
        ItemInstance item = GetQuickSlot(slotIndex);
        if (item == null) return false;

        // TODO: 实际效果由后续 ItemEffectDataSO 驱动，此处仅减少数量
        item.TryRemove(1);

        if (item.stackSize <= 0)
        {
            // 从背包中清除
            for (int i = 0; i < playerItems.Count; i++)
            {
                if (playerItems[i] == item)
                {
                    playerItems[i] = null;
                    break;
                }
            }
            quickSlots[slotIndex] = null;
        }

        CleanupEmptySlotsBackpack();
        OnInventoryChanged?.Invoke();
        OnQuickSlotsChanged?.Invoke();
        return true;
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

    /// <summary>将 ItemInstance 放入背包首个空格（或堆叠）</summary>
    private void AddItemInstance(ItemInstance item)
    {
        if (item == null || !item.IsValid) return;

        // 尝试堆叠
        for (int i = 0; i < playerItems.Count; i++)
        {
            ItemInstance existing = playerItems[i];
            if (existing == null) continue;
            if (existing.template != item.template) continue;
            if (!existing.CanStack) continue;

            int added = existing.TryStack(item.stackSize);
            item.TryRemove(added);
            if (item.stackSize <= 0) return;
        }

        // 放入空格
        for (int i = 0; i < playerItems.Count; i++)
        {
            if (playerItems[i] == null)
            {
                playerItems[i] = item;
                return;
            }
        }

        Debug.LogWarning($"[InventoryManager] 背包已满，{item.DisplayName} 无法放入");
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

        // ── 快捷栏（只保存背包引用索引）──
        data.quickSlotBindings = new int[QUICK_SLOT_COUNT];
        for (int i = 0; i < QUICK_SLOT_COUNT; i++)
        {
            data.quickSlotBindings[i] = -1;
            if (quickSlots[i] != null)
            {
                // 找到该物品在背包中的索引
                for (int j = 0; j < playerItems.Count; j++)
                {
                    if (playerItems[j] == quickSlots[i])
                    {
                        data.quickSlotBindings[i] = j;
                        break;
                    }
                }
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

        // ── 恢复快捷栏绑定 ──
        if (data.quickSlotBindings != null)
        {
            for (int i = 0; i < Mathf.Min(data.quickSlotBindings.Length, QUICK_SLOT_COUNT); i++)
            {
                int backpackIndex = data.quickSlotBindings[i];
                if (backpackIndex >= 0 && backpackIndex < playerItems.Count)
                {
                    var item = playerItems[backpackIndex];
                    if (item != null && item.template.category == ItemCategory.Consumable)
                        quickSlots[i] = item;
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

    /// <summary>快捷栏绑定：每个元素 = 背包索引，-1 = 空</summary>
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
