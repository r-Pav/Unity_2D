using UnityEngine;

/// <summary>
/// 近战敌人巡逻状态 — 在巡逻范围内来回移动，发现玩家转追击。
/// </summary>
public class MeleePatrolState : EntityState
{
    private float originX;
    private int patrolDir;
    private float changeDirTimer;

    public MeleePatrolState(CharacterBase owner, StateMachine stateMachine, Animator anim = null)
        : base(owner, stateMachine, anim)
    {
    }

    public override void OnEnter()
    {
        var me = (EnemyMeleeController)owner;
        originX = me.transform.position.x;
        patrolDir = Random.value > 0.5f ? 1 : -1;
        changeDirTimer = Random.Range(2f, 4f);
        me.OnExitCombatState();
        me.ApplyStateColor(new Color(0.2f, 0.4f, 1.0f));
    }

    public override void OnUpdate()
    {
        var me = (EnemyMeleeController)owner;
        changeDirTimer -= Time.deltaTime;
        if (changeDirTimer <= 0f)
        {
            patrolDir *= -1;
            changeDirTimer = Random.Range(2f, 4f);
        }

        float dx = me.transform.position.x - originX;
        if (dx > me.PatrolRange) patrolDir = -1;
        else if (dx < -me.PatrolRange) patrolDir = 1;

        me.moveInput = patrolDir * 0.5f;

        if (me.CanSeePlayer())
            me.Fsm.ChangeState(me.CreateChaseState());
    }

    public override void OnExit()
    {
        var me = (EnemyMeleeController)owner;
        me.moveInput = 0f;
    }
}
