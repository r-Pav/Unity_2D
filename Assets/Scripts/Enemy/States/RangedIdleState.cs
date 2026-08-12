using UnityEngine;

/// <summary>
/// 远程敌人待机状态 — 短时静止循环，发现玩家转追击（远程无巡逻）。
/// </summary>
public class RangedIdleState : EntityState
{
    private float timer;

    public RangedIdleState(CharacterBase owner, StateMachine stateMachine, Animator anim = null)
        : base(owner, stateMachine, anim)
    {
    }

    public override void OnEnter()
    {
        var me = (EnemyRangedController)owner;
        timer = Random.Range(1f, 2.5f);
        me.moveInput = 0f;
        me.OnExitCombatState();
        me.ApplyStateColor(new Color(0.6f, 0.6f, 0.6f)); // 灰白
    }

    public override void OnUpdate()
    {
        var me = (EnemyRangedController)owner;
        timer -= Time.deltaTime;
        if (timer <= 0f)
            me.Fsm.ChangeState(new RangedIdleState(owner, stateMachine, anim));  // 原地循环，无 Patrol
        else if (me.CanSeePlayer())
            me.Fsm.ChangeState(me.CreateChaseState());
    }

    public override void OnExit() { }
}
