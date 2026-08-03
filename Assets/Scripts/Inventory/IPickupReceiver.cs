/// <summary>
/// 拾取接收接口 — 由 Player 的 InventoryManager 和 Enemy 的 EnemyEquipment 实现
/// 当世界中的 DropItem 检测到可拾取对象进入 Trigger 时，通过此接口尝试拾取
/// </summary>
public interface IPickupReceiver
{
    /// <summary>
    /// 尝试拾取掉落物
    /// </summary>
    /// <param name="drop">世界中的 DropItem</param>
    /// <returns>true = 拾取成功（DropItem 应销毁自身）；false = 拾取失败（物品留在地上）</returns>
    bool TryPickup(DropItem drop);
}
