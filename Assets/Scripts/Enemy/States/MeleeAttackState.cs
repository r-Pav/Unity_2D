using UnityEngine;

public class MeleeAttackState : IState
{
    private readonly EnemyMeleeController owner;
    private IEnemyAttack attackModule;
    private float timer;
    private bool attacked;

    public MeleeAttackState(EnemyMeleeController owner) { this.owner = owner; }

    public void OnEnter()
    {
        timer = 0.5f;
        attacked = false;
        owner.moveInput = 0f;
        owner.OnEnterCombatState();
        owner.GetComponent<Rigidbody2D>().velocity = new Vector2(0f, owner.GetComponent<Rigidbody2D>().velocity.y);
        owner.ApplyStateColor(new Color(1.0f, 0.7f, 0.0f));
        attackModule = owner.GetComponent<IEnemyAttack>();
    }

    public void OnUpdate()
    {
        timer -= Time.deltaTime;

        if (!attacked && timer <= 0.3f)
        {
            attacked = true;
            owner.UpdateFacing(owner.DirectionToPlayer());
            attackModule?.PerformAttack(owner);
        }

        if (timer <= 0f)
        {
            if (owner.CanSeePlayer())
                owner.Fsm.ChangeState(owner.CreateChaseState());
            else
                owner.Fsm.ChangeState(new MeleePatrolState(owner));
        }
    }

    public void OnExit()
    {
        owner.attackCooldownTimer = owner.AttackCooldownDuration;
    }
}
