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
        // [2026-09-24] 不在这里清仇恨:Idle 是战斗内待机(本状态 OnUpdate 看到玩家就转 Chase)。
        // 若在此 OnExitCombatState,攻击结束→Idle→立刻脱战→下一帧又 Chase,一刀一循环(战斗相机/管道实心跟着闪)。
        // 仇恨统一由 Patrol.OnEnter(真正回巡逻)清。
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
