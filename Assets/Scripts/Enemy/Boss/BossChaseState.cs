using UnityEngine;

/// <summary>
/// Boss 追击状态 — 完全对齐普通 enemy MeleeChaseState:
/// CanSeePlayer(检测矩形) → PlayerInAttackRange(attackWidth×0.5,自身为中心)停住防左右闪 → CanAttack → 攻击。
/// 区别:Boss 无巡逻,视野外继续追击(玩家在 Boss 房内总会再进视野)。
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
        boss.moveInput = boss.DirectionToPlayer();
    }

    public override void OnUpdate()
    {
        var boss = (FirstBoss)owner;
        if (boss.IsDead) return;

        if (boss.CanSeePlayer())
        {
            // 玩家已在攻击范围内:停住等待攻击(防重叠后 DirectionToPlayer 抖动导致左右闪)
            if (boss.PlayerInAttackRange())
            {
                boss.moveInput = 0f;
            }
            else
            {
                boss.moveInput = boss.DirectionToPlayer();
            }

            if (boss.CanAttack())
            {
                boss.moveInput = 0f;
                boss.Fsm.ChangeState(new BossAttackState(owner, stateMachine, anim));
                return;
            }
        }
        else
        {
            // 视野外:Boss 无巡逻,继续追击
            boss.moveInput = boss.DirectionToPlayer();
        }
    }

    public override void OnExit()
    {
        var boss = (FirstBoss)owner;
        boss.moveInput = 0f;
    }
}
