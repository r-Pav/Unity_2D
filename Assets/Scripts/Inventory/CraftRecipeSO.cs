using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 配方材料条目 — 一条「材料物品 + 需求数量」
/// 纯数据，用于 CraftRecipeSO 内序列化，不持有背包/仓库/暂存区状态
/// </summary>
[Serializable]
public struct CraftMaterial
{
    [Tooltip("材料物品 — 约定为 category == Material 的 ItemSO")]
    public ItemSO item;

    [Tooltip("需求数量")]
    [Min(1)]
    public int count;
}

/// <summary>
/// 制作配方数据 ScriptableObject
/// 本类只描述配方数据：产物模板 + 产出数量 + 材料清单，不持有背包/仓库状态，
/// 也不负责材料扣除与产出入库（由后续的制作界面主控脚本按原子流程执行）。
/// 材料与产物都引用 ItemSO（ItemSO 是静态模板，运行时实例是 ItemInstance）。
/// 配方资产存放约定：Assets/Resources/Recipes/，运行时 Resources.LoadAll 全量读
/// （与 CombinationCraftSystem 读 Skills/Combo 同口径）；资产由 saika 在编辑器创建，
/// 本类不含任何资产生成逻辑。
/// </summary>
[CreateAssetMenu(fileName = "Recipe_", menuName = "Game/CraftRecipeSO")]
public class CraftRecipeSO : ScriptableObject
{
    // ============================================================
    // 产物
    // ============================================================

    [Header("产物")]
    [Tooltip("产物物品模板")]
    public ItemSO resultItem;

    [Tooltip("产出数量，最少 1")]
    [Min(1)]
    public int resultCount = 1;

    // ============================================================
    // 材料
    // ============================================================

    [Header("材料")]
    [Tooltip("材料清单 — 固定 2 格（界面只有两个合成槽位），空槽的 item 留空。同种材料要多个时写进 count，不占两格")]
    public CraftMaterial[] materials = new CraftMaterial[2];

    // ============================================================
    // 只读辅助（不做任何写操作）
    // ============================================================

    /// <summary>
    /// 配方是否有效：产物非空，且至少有一条材料 item 非空且 count > 0
    /// 只读校验，不修改任何字段
    /// </summary>
    public bool IsValid()
    {
        if (resultItem == null) return false;
        if (materials == null) return false;

        for (int i = 0; i < materials.Length; i++)
        {
            if (materials[i].item != null && materials[i].count > 0)
                return true;
        }
        return false;
    }

    /// <summary>
    /// 取有效材料的副本列表（item 非空即算一条，不看 count）— 供后续扣除/界面展示使用
    /// 返回的是新列表，调用方可以随意改动；本方法不改动 SO 自身数据
    /// </summary>
    public List<CraftMaterial> GetValidMaterials()
    {
        var list = new List<CraftMaterial>();
        if (materials == null) return list;

        for (int i = 0; i < materials.Length; i++)
        {
            if (materials[i].item != null)
                list.Add(materials[i]);
        }
        return list;
    }
}
