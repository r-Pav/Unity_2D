using UnityEngine;

/// <summary>
/// 掉落表 ScriptableObject — 不同 enemy 引用不同表，改一张表全局生效。
/// 菜单：Create → Inventory → Loot Table
/// </summary>
[CreateAssetMenu(fileName = "LootTable", menuName = "Inventory/Loot Table", order = 1)]
public class LootTableSO : ScriptableObject
{
    [Tooltip("掉落条目列表，按权重加权随机")]
    [SerializeField] private DropEntry[] entries;

    [Tooltip("至少掉落几件")]
    [SerializeField] [Range(0, 5)] private int guaranteedDrops;

    [Tooltip("掉落物品等级")]
    [SerializeField] [Range(1, 99)] private int dropLevel = 1;

    public int DropLevel => dropLevel;

    public ItemSO[] RollDrops()
    {
        if (entries == null || entries.Length == 0)
            return new ItemSO[0];

        int count = guaranteedDrops;
        if (count <= 0)
            return new ItemSO[0];

        ItemSO[] results = new ItemSO[count];
        for (int i = 0; i < count; i++)
            results[i] = PickWeighted();

        return results;
    }

    private ItemSO PickWeighted()
    {
        float totalWeight = 0f;
        foreach (var e in entries)
        {
            if (e.item != null)
                totalWeight += e.weight;
        }

        if (totalWeight <= 0f) return null;

        float roll = Random.Range(0f, totalWeight);
        float cursor = 0f;
        foreach (var e in entries)
        {
            if (e.item == null) continue;
            cursor += e.weight;
            if (roll <= cursor)
                return e.item;
        }

        return entries[entries.Length - 1].item;
    }
}
