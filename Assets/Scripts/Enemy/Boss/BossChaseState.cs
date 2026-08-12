using UnityEngine;

/// <summary>
/// Boss 追击状态 — 追击玩家，可攻击时转攻击状态。
/// </summary>
public class BossChaseState : EntityState
{
    public BossChaseState(CharacterBase owner, StateMachine stateMachine, Animator anim = null)
        : base(owner, stateMachine, anim)
    {
    }

    public override void OnEnter()
    {
        var boss = (FirstBoss)owner;
        boss.OnEnterCombatState();
        boss.ApplyStateColor(new Color(0.8f, 0.2f, 0.2f));
        boss.moveInput = boss.DirectionToPlayer();
    }

    public override void OnUpdate()
    {
        var boss = (FirstBoss)owner;
        if (boss.IsDead) return;

        if (boss.CanBossAttack())
        {
            boss.moveInput = 0f;
            boss.Fsm.ChangeState(new BossAttackState(owner, stateMachine, anim));
            return;
        }

        boss.moveInput = boss.DirectionToPlayer();
    }

    public override void OnExit()
    {
        var boss = (FirstBoss)owner;
        boss.moveInput = 0f;
    }
}
