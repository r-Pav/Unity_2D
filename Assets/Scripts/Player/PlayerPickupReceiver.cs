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
        if (_inventory == null)
            return false;

        return _inventory.TryPickup(drop);
    }
}
