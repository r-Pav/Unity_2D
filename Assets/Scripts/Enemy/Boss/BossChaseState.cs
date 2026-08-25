using UnityEngine;

/// <summary>
/// Boss 追击状态 — 无 AI 检测矩形/视野判断(detectionWidth/Height 不参与)。
/// 激活后一直朝玩家移动;玩家进入攻击范围子物体(BossAttackRange) → 停住 + 攻击。
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

        // 玩家在攻击范围(子 obj)内:停住 + 触发攻击(CanAttack 检查激活/存活/技能/冷却)
        if (boss.IsPlayerInBossAttackRange())
        {
            boss.moveInput = 0f;
            if (boss.CanAttack())
            {
                boss.Fsm.ChangeState(new BossAttackState(owner, stateMachine, anim));
            }
            return;
        }

        // 玩家不在攻击范围:持续追击
        boss.moveInput = boss.DirectionToPlayer();
    }

    public override void OnExit()
    {
        var boss = (FirstBoss)owner;
        boss.moveInput = 0f;
    }
}
