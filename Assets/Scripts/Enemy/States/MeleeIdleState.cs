using UnityEngine;

public class MeleeIdleState : IState
{
    private readonly EnemyMeleeController owner;
    private float timer;

    public MeleeIdleState(EnemyMeleeController owner) { this.owner = owner; }

    public void OnEnter()
    {
        timer = Random.Range(1f, 2.5f);
        owner.moveInput = 0f;
        owner.OnExitCombatState();
        owner.ApplyStateColor(new Color(0.6f, 0.6f, 0.6f));
    }

    public void OnUpdate()
    {
        timer -= Time.deltaTime;
        if (timer <= 0f)
            owner.Fsm.ChangeState(new MeleePatrolState(owner));
        else if (owner.CanSeePlayer())
            owner.Fsm.ChangeState(owner.CreateChaseState());
    }

    public void OnExit() { }
}
