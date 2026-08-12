using UnityEngine;

/// <summary>
/// [Phase3] 敌人装备管理器 — 管理单个装备槽位
/// 挂 Enemy Prefab，负责：
///   1. 拾取掉落物（实现 IPickupReceiver，按等级判断是否替换旧装备）
///   2. 死亡掉落（生成 DropItem，ownerMask=Player）
///   3. 等级缩放属性加成（注入到 StatModifierManager）
///   4. 视觉控制（无装备隐藏图标，有装备 50% 透明度）
///
/// 等级缩放：f(level) = level（线性，saika 要求：1 级 +10 血、2 级 +20 血，若 bonus.value=10）
///   Lv1=1x  Lv2=2x  Lv3=3x ...
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
    /// 注入装备属性加成到 StatModifierManager。
    /// [2026-08-10 用户裁决] 按装备槽类型映射属性：
    ///   武器 → 攻击力(EnemyDamage)、防具 → 血量(MaxHealth)、饰品 → 移速(MoveSpeed)
    /// 加成量 = 自身基础值的 10% × 装备等级（Lv1=10%、Lv2=20%、Lv3=30%）。
    /// Percent 修饰器基于基础值计算（result = base × (1 + ΣPercent)），天然实现"自身值的百分比"。
    /// source = "EnemyEquip_{instanceId}_{statId}"
    /// </summary>
    private void AddEquipmentModifiers(ItemInstance item, int level)
    {
        if (statModManager == null || item?.template == null) return;

        string statId = GetEquipStatId(item.template.slotType);
        if (statId == null) return;

        // 10% × 等级（Lv1=0.1、Lv2=0.2、Lv3=0.3）
        float percent = 0.1f * level;
        string source = GetEnemyEquipSource(statId);
        statModManager.AddModifier(new Modifier(statId, percent, ModifierType.Percent, source, priority: 0));

        Debug.Log($"[EnemyEquipment] 注入属性：{item.DisplayName} → {statId} +{percent * 100:F0}%（Lv.{level}） (instanceId={_instanceId})");
    }

    /// <summary>移除由当前装备注入的所有修饰器</summary>
    private void RemoveEquipmentModifiers()
    {
        if (statModManager == null || _equippedItem?.template == null) return;

        string statId = GetEquipStatId(_equippedItem.template.slotType);
        if (statId == null) return;

        statModManager.RemoveModifier(GetEnemyEquipSource(statId));
    }

    /// <summary>按装备槽类型映射属性 ID：武器→攻击力、防具→血量、饰品→移速；其他返回 null</summary>
    private static string GetEquipStatId(EquipmentSlotType slotType)
    {
        switch (slotType)
        {
            case EquipmentSlotType.Weapon:    return StatId.EnemyDamage;
            case EquipmentSlotType.Armor:     return StatId.MaxHealth;
            case EquipmentSlotType.Accessory0:
            case EquipmentSlotType.Accessory1: return StatId.MoveSpeed;
            default: return null;
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

    /// <summary>生成敌人装备修饰器的唯一 source 标识符</summary>
    private string GetEnemyEquipSource(string statId)
    {
        return $"EnemyEquip_{_instanceId}_{statId}";
    }
}
