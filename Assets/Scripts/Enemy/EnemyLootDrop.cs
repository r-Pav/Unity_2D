using UnityEngine;

/// <summary>
/// 挂 Enemy Prefab 上：引用 LootTableSO，死亡时按掉落表生成 DropItem。
/// 多 enemy 共享同一张表时改表全局生效。
/// </summary>
public class EnemyLootDrop : MonoBehaviour
{
    [Tooltip("掉落表资产")]
    [SerializeField] private LootTableSO lootTable;

    [Tooltip("DropItem Prefab（与世界掉落共用一个）")]
    [SerializeField] private DropItem dropItemPrefab;

    [Tooltip("掉落物 OwnerMask：谁能拾取。Player=仅玩家, Player|Enemy=双方都可")]
    [SerializeField] private LayerMask ownerMask;

    private void OnEnable()
    {
        EventBus.Subscribe<EnemyDeathEvent>(OnEnemyDeath);
    }

    private void OnDisable()
    {
        EventBus.Unsubscribe<EnemyDeathEvent>(OnEnemyDeath);
    }

    private void OnEnemyDeath(EnemyDeathEvent e)
    {
        // 只处理自己
        if (e.enemy == null || e.enemy.gameObject != gameObject)
            return;

        if (lootTable == null || dropItemPrefab == null)
            return;

        ItemSO[] items = lootTable.RollDrops();
        int level = lootTable.DropLevel;

        foreach (ItemSO itemSO in items)
        {
            if (itemSO == null) continue;

            ItemInstance instance = new ItemInstance(itemSO, 1);
            DropItem.Spawn(
                dropItemPrefab,
                instance,
                level,
                ownerMask,
                e.position,
                useAnimation: true
            );
        }
    }
}
