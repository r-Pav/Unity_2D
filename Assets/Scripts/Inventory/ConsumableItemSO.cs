using UnityEngine;

/// <summary>
/// 消耗品类物品模板（2026-09-26 从 ItemSO 拆出）。
/// 当前只有回血（healAmount）；以后要加 buff / 回蓝 / 增益等，在这个类里加效果字段或效果数组，
/// 效果执行统一在 InventoryManager.TryApplyConsumableEffect 里分发。
/// 创建菜单：Game/物品/消耗品
/// </summary>
[CreateAssetMenu(fileName = "Consume_", menuName = "Game/物品/消耗品")]
public class ConsumableItemSO : ItemSO
{
    [Header("消耗品效果")]
    [Tooltip("使用后恢复的生命值（正数；0 = 无回血效果）")]
    public float healAmount;

    public override ItemCategory Category => ItemCategory.Consumable;
    public override float HealAmount => healAmount;

#if UNITY_EDITOR
    private void OnValidate()
    {
        category = ItemCategory.Consumable;   // 分类由类型决定，基类字段只是给旧代码/存档读
        id = name;                             // 物品 ID 自动取资产文件名（改文件名即改 ID）
    }
#endif
}
