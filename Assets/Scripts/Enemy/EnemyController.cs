/// <summary>
/// 敌人控制器兼容壳 — 继承 EnemyMeleeController，保持现有 Prefab/场景引用不中断。
/// 所有逻辑已迁移至 EnemyControllerBase / EnemyMeleeController / EnemyRangedController。
/// 新开发的近战敌人请直接使用 EnemyMeleeController；远程敌人请使用 EnemyRangedController。
/// </summary>
public class EnemyController : EnemyMeleeController { }
