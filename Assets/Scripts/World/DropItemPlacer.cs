using UnityEngine;

/// <summary>
/// 在地上预摆一个可拾取物品 —— 挂在场景里的空物体上,Start 时生成一个 DropItem。
/// 只为「直接放一个在地上捡」这种测试/摆点用;正式掉落走 EnemyLootDrop / EnemyEquipment。
/// 生成出来就是标准 DropItem:图标 + 稀有度边框 + trigger 拾取,玩家走过去自动进背包。
/// </summary>
public class DropItemPlacer : MonoBehaviour
{
    [Tooltip("要生成的物品(ItemSO 资产)")]
    [SerializeField] private ItemSO item;

    [Tooltip("数量(可堆叠物品用)")]
    [SerializeField] private int count = 1;

    [Tooltip("掉落物 Prefab —— 拖 Assets/Prefab/DropItem.prefab")]
    [SerializeField] private DropItem dropItemPrefab;

    [Tooltip("谁能捡:默认勾 Player 层(6)")]
    [SerializeField] private LayerMask ownerMask = 1 << 6;

    [Tooltip("存活时间(秒);<=0 用预制体默认 30 秒")]
    [SerializeField] private float lifetime = -1f;

    private void Start()
    {
        if (item == null || dropItemPrefab == null)
        {
            Debug.LogWarning($"[DropItemPlacer] {name}: 未填 item 或 dropItemPrefab,已跳过");
            return;
        }

        DropItem.Spawn(
            dropItemPrefab,
            new ItemInstance(item, count),
            1,                       // level:装备等级缩放用,这里固定 1
            ownerMask,
            transform.position,
            useAnimation: false,     // 直接落在摆放位置,不弹跳
            lifetime: lifetime);
    }
}
