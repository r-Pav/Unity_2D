using UnityEngine;

/// <summary>
/// Boss 待机状态 — 激活前静止待机，激活后转追击。
/// </summary>
public class BossIdleState : EntityState
{
    public BossIdleState(CharacterBase owner, StateMachine stateMachine, Animator anim = null)
        : base(owner, stateMachine, anim)
    {
    }

    public override void OnEnter()
    {
        var boss = (FirstBoss)owner;
        boss.moveInput = 0f;
        // boss.ApplyStateColor(new Color(0.5f, 0.5f, 0.5f));  // [状态色已移除]
    }

    public override void OnUpdate()
    {
        var boss = (FirstBoss)owner;
        if (boss.IsActivated)
            boss.Fsm.ChangeState(boss.CreateChaseState());
    }

    public override void OnExit() { }
}
