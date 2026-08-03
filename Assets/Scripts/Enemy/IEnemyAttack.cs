/// <summary>
/// 敌人攻击接口 — 所有敌人攻击组件实现此接口
/// EnemyController 在 AttackState 中通过 GetComponent<IEnemyAttack>() 获取并调用
/// </summary>
public interface IEnemyAttack
{
    void PerformAttack(EnemyControllerBase owner);
}
