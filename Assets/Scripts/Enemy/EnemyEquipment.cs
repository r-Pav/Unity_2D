using UnityEngine;

/// <summary>
/// [Phase3] 敌人装备管理器 — 管理单个装备槽位
/// 挂 Enemy Prefab，负责：
///   1. 拾取掉落物（实现 IPickupReceiver，按等级判断是否替换旧装备）
///   2. 死亡掉落（生成 DropItem，ownerMask=Player）
///   3. 等级缩放属性加成（注入到 StatModifierManager）
///   4. 视觉控制（无装备隐藏图标，有装备 50% 透明度）
///
/// 等级缩放：f(level) = 1.0 + (level-1) × 0.25，clamp(2.0)
///   Lv1=1.00x  Lv2=1.25x  Lv3=1.50x  Lv4=1.75x  Lv5+=2.00x
/// </summary>
public class EnemyEquipment : MonoBehaviour, IPickupReceiver
{
    // ============================================================
    // 配置
    // ============================================================

    [Header("掉落物")]
    [Tooltip("DropItem Prefab 引用（用于死亡掉落和旧装备替换掉落）")]
    [SerializeField] private DropItem dropItemPrefab;

    [Header("视觉")]
    [Tooltip("装备图标渲染器 — 显示当前装备的图标")]
    [SerializeField] private SpriteRenderer iconRenderer;

    [Tooltip("图标父节点（整体 SetActive 控制显隐）")]
    [SerializeField] private GameObject iconRoot;

    [Header("组件引用（自动查找）")]
    [Tooltip("StatModifierManager 引用（可选，自动从同一 GameObject 获取）")]
    [SerializeField] private StatModifierManager statModManager;

    // ============================================================
    // 运行时状态
    // ============================================================

    /// <summary>当前装备的物品实例（null = 空）</summary>
    private ItemInstance _equippedItem;

    /// <summary>当前装备的等级（用于死亡时掉落还原等级）</summary>
    private int _equippedLevel;

    /// <summary>Enemy 实例 ID（用于生成唯一的 modifier source）</summary>
    private int _instanceId;

    // ============================================================
    // 公开属性
    // ============================================================

    /// <summary>是否有装备</summary>
    public bool HasEquipment => _equippedItem != null;

    /// <summary>当前装备的等级（用于掉落还原）</summary>
    public int EquippedLevel => _equippedLevel;

    // ============================================================
    // 生命周期
    // ============================================================

    private void Awake()
    {
        _instanceId = GetInstanceID();

        if (statModManager == null)
            statModManager = GetComponent<StatModifierManager>();

        if (iconRenderer == null && iconRoot != null)
            iconRenderer = iconRoot.GetComponent<SpriteRenderer>();

        // 初始隐藏装备图标
        HideIcon();
    }

    // ============================================================
    // IPickupReceiver 实现 — 拾取检测
    // ============================================================

    /// <summary>
    /// 尝试拾取掉落物
    /// 拾取策略：优先捡等级更高的装备；已有装备时旧装备掉落替换
    /// </summary>
    public bool TryPickup(DropItem drop)
    {
        if (drop == null || drop.ItemData == null) return false;

        ItemInstance newItem = drop.ItemData;
        int newLevel = drop.DropLevel;

        // 只拾取装备类物品
        if (newItem.template == null || newItem.template.category != ItemCategory.Equipment)
            return false;

        // 空槽位 → 直接拾取
        if (_equippedItem == null)
        {
            EquipItem(newItem, newLevel);
            return true;
        }

        // 新装备等级严格高于当前装备 → 替换
        if (newLevel > _equippedLevel)
        {
            // 旧装备掉回地面（ownerMask=Player，仅玩家可捡）
            DropCurrentItem(LayerMask.GetMask("Player"));

            EquipItem(newItem, newLevel);
            return true;
        }

        // 新装备等级 ≤ 当前 → 不拾取，物品留在地上
        return false;
    }

    // ============================================================
    // 公开接口 — 死亡掉落
    // ============================================================

    /// <summary>
    /// 死亡时生成掉落物并清空装备
    /// 在 EnemyControllerBase.Die() 中 EnemyDeathEvent 之前调用
    /// ownerMask = Player（仅玩家可捡回）
    /// </summary>
    public void DropOnDeath()
    {
        if (_equippedItem == null) return;

        // 移除属性加成
        RemoveEquipmentModifiers();

        // 生成世界掉落物
        SpawnDropItem(_equippedItem, _equippedLevel, LayerMask.GetMask("Player"));

        // 清空并隐藏图标
        _equippedItem = null;
        _equippedLevel = 0;
        HideIcon();

        Debug.Log($"[EnemyEquipment] 死亡掉落完成 (instanceId={_instanceId})");
    }

    // ============================================================
    // 内部方法 — 装备/卸下
    // ============================================================

    /// <summary>将物品填入槽位并注入属性加成</summary>
    private void EquipItem(ItemInstance item, int level)
    {
        _equippedItem = item;
        _equippedLevel = level;

        // 注入等级缩放的属性加成
        AddEquipmentModifiers(item, level);

        // 显示图标（50% 透明度）
        ShowIcon(item.template.icon, alpha: 0.5f);

        // 发送事件（供 AI/音效等模块订阅）
        EventBus.Trigger(new EnemyEquipmentPickupEvent(
            GetComponent<EnemyControllerBase>(), item, level));

        Debug.Log($"[EnemyEquipment] 拾取装备：{item.DisplayName} Lv.{level} (instanceId={_instanceId})");
    }

    /// <summary>将当前装备生成掉落物到世界</summary>
    private void DropCurrentItem(LayerMask ownerMask)
    {
        if (_equippedItem == null) return;

        RemoveEquipmentModifiers();

        SpawnDropItem(_equippedItem, _equippedLevel, ownerMask);

        Debug.Log($"[EnemyEquipment] 旧装备掉落替换：{_equippedItem.DisplayName} (instanceId={_instanceId})");
    }

    // ============================================================
    // 内部方法 — 属性加成注入/移除（等级缩放）
    // ============================================================

    /// <summary>
    /// 注入装备属性加成到 StatModifierManager
    /// 加成值 = 原值 × 等级缩放倍率 f(level)
    /// source = "EnemyEquip_{instanceId}_{statId}"
    /// </summary>
    private void AddEquipmentModifiers(ItemInstance item, int level)
    {
        if (statModManager == null) return;

        var stats = item.template.equipmentStats;
        if (stats == null || stats.Value.bonuses == null) return;

        float scale = GetLevelScale(level);

        foreach (var bonus in stats.Value.bonuses)
        {
            float scaledValue = bonus.value * scale;
            string source = GetEnemyEquipSource(bonus.statId);
            var mod = new Modifier(bonus.statId, scaledValue, bonus.type, source, priority: 0);
            statModManager.AddModifier(mod);
        }

        Debug.Log($"[EnemyEquipment] 注入属性加成：{item.DisplayName} scale={scale:F2}x (instanceId={_instanceId})");
    }

    /// <summary>移除由当前装备注入的所有修饰器</summary>
    private void RemoveEquipmentModifiers()
    {
        if (statModManager == null || _equippedItem == null) return;

        var stats = _equippedItem.template.equipmentStats;
        if (stats == null || stats.Value.bonuses == null) return;

        foreach (var bonus in stats.Value.bonuses)
        {
            string source = GetEnemyEquipSource(bonus.statId);
            statModManager.RemoveModifier(source);
        }
    }

    // ============================================================
    // 内部方法 — 掉落生成
    // ============================================================

    /// <summary>生成世界掉落物（位置 = 当前位置 + 小偏移）</summary>
    private void SpawnDropItem(ItemInstance item, int level, LayerMask ownerMask)
    {
        if (dropItemPrefab == null)
        {
            Debug.LogWarning($"[EnemyEquipment] dropItemPrefab 未配置，跳过掉落：{item.DisplayName}");
            return;
        }

        Vector2 pos = (Vector2)transform.position + new Vector2(
            Random.Range(-0.3f, 0.3f),
            Random.Range(0f, 0.2f));

        DropItem.Spawn(dropItemPrefab, item, level, ownerMask, pos);

        Debug.Log($"[EnemyEquipment] 生成掉落物：{item.DisplayName} Lv.{level} at {pos}");
    }

    // ============================================================
    // 内部方法 — 视觉控制
    // ============================================================

    /// <summary>显示装备图标（alpha=0.5，半透明以区分 Enemy 装备）</summary>
    private void ShowIcon(Sprite icon, float alpha)
    {
        if (iconRenderer != null)
        {
            iconRenderer.sprite = icon;
            Color c = iconRenderer.color;
            c.a = Mathf.Clamp01(alpha);
            iconRenderer.color = c;
        }

        if (iconRoot != null)
            iconRoot.SetActive(true);
    }

    /// <summary>隐藏装备图标</summary>
    private void HideIcon()
    {
        if (iconRoot != null)
            iconRoot.SetActive(false);
        else if (iconRenderer != null)
            iconRenderer.sprite = null;
    }

    // ============================================================
    // 公开接口 — 查询
    // ============================================================

    /// <summary>获取当前装备的物品实例</summary>
    public ItemInstance GetEquippedItem() => _equippedItem;

    // ============================================================
    // 辅助方法
    // ============================================================

    /// <summary>
    /// 等级缩放公式：f(level) = min(2.0, 1.0 + (level-1) × 0.25)
    /// </summary>
    private static float GetLevelScale(int level)
    {
        if (level <= 1) return 1.0f;
        return Mathf.Min(2.0f, 1.0f + (level - 1) * 0.25f);
    }

    /// <summary>生成敌人装备修饰器的唯一 source 标识符</summary>
    private string GetEnemyEquipSource(string statId)
    {
        return $"EnemyEquip_{_instanceId}_{statId}";
    }
}
