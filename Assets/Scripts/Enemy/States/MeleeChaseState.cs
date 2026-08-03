using UnityEngine;

public class MeleeChaseState : IState
{
    private readonly EnemyMeleeController owner;
    private float losePlayerTimer;

    public MeleeChaseState(EnemyMeleeController owner) { this.owner = owner; }

    public void OnEnter()
    {
        losePlayerTimer = 3f;
        owner.OnEnterCombatState();
        owner.ApplyStateColor(new Color(1.0f, 0.2f, 0.2f));
        owner.moveInput = owner.DirectionToPlayer();
    }

    public void OnUpdate()
    {
        if (owner.IsDead) return;

        if (owner.CanSeePlayer())
        {
            losePlayerTimer = 3f;
            // 玩家已在攻击范围内：停住等待攻击（防重叠后 DirectionToPlayer 抖动导致左右震动）
            if (owner.PlayerInAttackRange())
            {
                owner.moveInput = 0f;
            }
            else
            {
                owner.moveInput = owner.DirectionToPlayer();
            }

            if (owner.CanAttack())
            {
                owner.moveInput = 0f;
                owner.Fsm.ChangeState(new MeleeAttackState(owner));
            }
        }
        else
        {
            losePlayerTimer -= Time.deltaTime;
            if (losePlayerTimer <= 0f)
                owner.Fsm.ChangeState(new MeleePatrolState(owner));
        }
    }

    public void OnExit() { }
}
