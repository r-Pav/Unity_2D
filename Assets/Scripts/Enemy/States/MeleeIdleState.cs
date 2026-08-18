using UnityEngine;

/// <summary>
/// 近战敌人待机状态 — 短时静止后转巡逻，发现玩家直接追击。
/// </summary>
public class MeleeIdleState : EntityState
{
    private float timer;

    public MeleeIdleState(CharacterBase owner, StateMachine stateMachine, Animator anim = null)
        : base(owner, stateMachine, anim)
    {
    }

    public override void OnEnter()
    {
        var me = (EnemyMeleeController)owner;
        timer = Random.Range(1f, 2.5f);
        me.moveInput = 0f;
        me.OnExitCombatState();
        // me.ApplyStateColor(new Color(0.6f, 0.6f, 0.6f));  // [状态色已移除]
    }

    public override void OnUpdate()
    {
        var me = (EnemyMeleeController)owner;
        timer -= Time.deltaTime;
        if (timer <= 0f)
            me.Fsm.ChangeState(new MeleePatrolState(owner, stateMachine, anim));
        else if (me.CanSeePlayer())
            me.Fsm.ChangeState(me.CreateChaseState());
    }

    public override void OnExit() { }
}
