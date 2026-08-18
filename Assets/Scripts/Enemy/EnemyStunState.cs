using UnityEngine;

/// <summary>
/// 敌人踩头硬直状态 — 被玩家踩头后短暂眩晕，近战远程共用。
/// 硬直结束后按仇恨逻辑分流：
///   - 能看到玩家 → 追击
///   - 看不到玩家但被打前处于战斗（有仇恨）→ 继续追击（ChaseState 内部有 losePlayerTimer 兜底）
///   - 看不到玩家且无仇恨（巡逻中被偷袭/踩头）→ 回空闲/巡逻
/// </summary>
public class EnemyStunState : EntityState
{
    private readonly EnemyControllerBase enemy;
    private float timer;

    public EnemyStunState(EnemyControllerBase enemy, StateMachine stateMachine)
        : base(enemy, stateMachine)
    {
        this.enemy = enemy;
    }

    public override void OnEnter()
    {
        enemy.moveInput = 0f;
        // enemy.ApplyStateColor(new Color(1f, 0f, 1f)); // 品红色（硬直）[状态色已移除]
        timer = 1f;
        enemy.attackCooldownTimer = 0f; // 中断攻击冷却
    }

    public override void OnUpdate()
    {
        timer -= Time.deltaTime;

        // 死亡则不做任何状态切换
        if (enemy.IsDead) return;

        if (timer <= 0f)
        {
            if (enemy.CanSeePlayer())
            {
                stateMachine.ChangeState(enemy.CreateChaseState());
            }
            else if (enemy.IsInCombatState)
            {
                // 被击退/被打前正处于战斗中，有仇恨 → 继续追击（ChaseState 内有 losePlayerTimer 3s 兜底）
                stateMachine.ChangeState(enemy.CreateChaseState());
            }
            else
            {
                stateMachine.ChangeState(enemy.CreateFallbackState());
            }
        }
    }

    public override void OnExit()
    {
        // 无需特殊处理
    }
}
