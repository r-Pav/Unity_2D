using UnityEngine;

public class MeleePatrolState : IState
{
    private readonly EnemyMeleeController owner;
    private float originX;
    private int patrolDir;
    private float changeDirTimer;

    public MeleePatrolState(EnemyMeleeController owner) { this.owner = owner; }

    public void OnEnter()
    {
        originX = owner.transform.position.x;
        patrolDir = Random.value > 0.5f ? 1 : -1;
        changeDirTimer = Random.Range(2f, 4f);
        owner.OnExitCombatState();
        owner.ApplyStateColor(new Color(0.2f, 0.4f, 1.0f));
    }

    public void OnUpdate()
    {
        changeDirTimer -= Time.deltaTime;
        if (changeDirTimer <= 0f)
        {
            patrolDir *= -1;
            changeDirTimer = Random.Range(2f, 4f);
        }

        float dx = owner.transform.position.x - originX;
        if (dx > owner.PatrolRange) patrolDir = -1;
        else if (dx < -owner.PatrolRange) patrolDir = 1;

        owner.moveInput = patrolDir * 0.5f;

        if (owner.CanSeePlayer())
            owner.Fsm.ChangeState(owner.CreateChaseState());
    }

    public void OnExit()
    {
        owner.moveInput = 0f;
    }
}
