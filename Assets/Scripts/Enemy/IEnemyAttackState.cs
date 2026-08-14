/// <summary>
/// 敌人攻击状态接口 — 攻击状态实现此接口，由 EnemyControllerBase 通用动画事件方法转发：
///   OnHitFrame = 命中帧执行攻击（原 0.3s 处逻辑：朝向 + PerformAttack）
///   OnCharge    = 远程攻击蓄力帧（attack2 蓄力粒子）
///   OnFire      = 远程攻击发射帧（attack2 发射子弹）
///   OnAnimEnd   = 攻击动画结束退出攻击状态
/// melee 先行接入；ranged/boss 后续攻击状态实现同一接口即可复用动画事件驱动。
/// </summary>
public interface IEnemyAttackState
{
    /// <summary>命中帧：执行攻击（UpdateFacing + attackModule.PerformAttack）</summary>
    void OnHitFrame();

    /// <summary>蓄力帧：远程攻击蓄力（attack1 近战不实现，空）</summary>
    void OnCharge();

    /// <summary>发射帧：远程攻击发射（attack1 近战不实现，空）</summary>
    void OnFire();

    /// <summary>攻击动画结束：退出攻击状态</summary>
    void OnAnimEnd();
}
