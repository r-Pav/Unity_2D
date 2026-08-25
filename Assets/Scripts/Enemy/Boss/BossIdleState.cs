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
        if (BossDebugFlow.Enabled)
            Debug.Log($"[BossFSM] IdleState.OnEnter activated={boss.IsActivated}");
        // boss.ApplyStateColor(new Color(0.5f, 0.5f, 0.5f));  // [状态色已移除]
    }

    public override void OnUpdate()
    {
        var boss = (FirstBoss)owner;
        if (boss.IsActivated)
        {
            if (BossDebugFlow.Enabled)
                Debug.Log($"[BossFSM] IdleState.OnUpdate activated=true → 切 Chase");
            boss.Fsm.ChangeState(boss.CreateChaseState());
        }
    }

    public override void OnExit() { }
}
