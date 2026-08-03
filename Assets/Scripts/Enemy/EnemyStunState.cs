using UnityEngine;

/// <summary>
/// 敌人踩头硬直状态 — 被玩家踩头后短暂眩晕，近战远程共用。
/// 0.5秒后根据能否看到玩家决定追击或回到空闲/巡逻状态。
/// </summary>
public class EnemyStunState : IState
{
    private readonly EnemyControllerBase enemy;
    private readonly StateMachine fsm;
    private float timer;

    public EnemyStunState(EnemyControllerBase enemy, StateMachine fsm)
    {
        this.enemy = enemy;
        this.fsm = fsm;
    }

    public void OnEnter()
    {
        enemy.moveInput = 0f;
        enemy.ApplyStateColor(new Color(1f, 0f, 1f)); // 品红色（硬直）
        timer = 1f;
        enemy.attackCooldownTimer = 0f; // 中断攻击冷却
    }

    public void OnUpdate()
    {
        timer -= Time.deltaTime;

        // 死亡则不做任何状态切换
        if (enemy.IsDead) return;

        if (timer <= 0f)
        {
            if (enemy.CanSeePlayer())
            {
                fsm.ChangeState(enemy.CreateChaseState());
            }
            else
            {
                fsm.ChangeState(enemy.CreateFallbackState());
            }
        }
    }

    public void OnExit()
    {
        // 无需特殊处理
    }
}
