using UnityEngine;

/// <summary>
/// 材料类物品模板（2026-09-26 从 ItemSO 拆出）。
/// 没有额外字段，只作类型标记（分类 = 材料），字段全部来自 ItemSO 通用部分。
/// 创建菜单：Game/物品/材料
/// </summary>
[CreateAssetMenu(fileName = "Material_", menuName = "Game/物品/材料")]
public class MaterialItemSO : ItemSO
{
    public override ItemCategory Category => ItemCategory.Material;

#if UNITY_EDITOR
    private void OnValidate()
    {
        category = ItemCategory.Material;   // 分类由类型决定，基类字段只是给旧代码/存档读
        id = name;                           // 物品 ID 自动取资产文件名（改文件名即改 ID）
    }
#endif
}
