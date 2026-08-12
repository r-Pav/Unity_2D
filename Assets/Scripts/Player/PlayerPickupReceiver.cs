using UnityEngine;

/// <summary>
/// 挂 Player 上：接收 DropItem 触发 → 转发给 InventoryManager。
/// </summary>
public class PlayerPickupReceiver : MonoBehaviour, IPickupReceiver
{
    private InventoryManager _inventory;

    private void Awake()
    {
        _inventory = InventoryManager.Instance;
    }

    public bool TryPickup(DropItem drop)
    {
        // [2026-08-10] 玩家死亡后不拾取——尸体不捡自己掉落的装备（否则掉落物生成瞬间被捡回销毁，看不到掉落）。
        // 复活后（IsDead=false）正常拾取。
        var health = GetComponent<PlayerHealth>();
        if (health != null && health.IsDead) return false;

        if (_inventory == null)
            return false;

        return _inventory.TryPickup(drop);
    }
}
