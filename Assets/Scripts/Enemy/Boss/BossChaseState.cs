using UnityEngine;

/// <summary>
/// Boss 追击状态 — 无 AI 检测矩形/视野判断(detectionWidth/Height 不参与)。
/// 激活后一直朝玩家移动;玩家进入攻击范围子物体(BossAttackRange) → 停住 + 请求攻击。
/// 攻击编排(BossAttackDirector)决定放技能还是普攻:技能执行中站桩等结束;普攻走攻击状态(动画播完回追击);
/// 临近技能点(剩余时间不足一次完整普攻动画)时由 BossAttackDirector.TryHandleBackoff 接管这一帧的移动输入(后退拉距)。
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

        // 重击期间:站桩(位置/朝向由 BossHeavyAttack 冻结),不请求攻击
        if (boss.IsHeavyActive)
        {
            boss.moveInput = 0f;
            return;
        }

        // 技能执行中:站桩等技能结束(技能动画独立播放,不切 FSM 状态)
        if (boss.IsAttacking)
        {
            boss.moveInput = 0f;
            return;
        }

        // 临近技能点(剩余时间不足一次完整普攻动画)且玩家在范围内:停手 + 背离玩家后退一小段
        // (到点由 BossAttackDirector.Update 释放技能;这里只负责这一帧的移动输入)
        if (boss.AttackDirector != null && boss.AttackDirector.TryHandleBackoff())
        {
            boss.moveInput = boss.AttackDirector.BackoffMoveInput;
            return;
        }

        // 玩家在攻击范围(子 obj)内:停住 + 请求攻击(CanAttack 检查激活/存活/技能/范围)
        if (boss.IsPlayerInBossAttackRange())
        {
            boss.moveInput = 0f;
            if (boss.CanAttack() && boss.AttackDirector != null)
            {
                // 触发技能(返回 true)或普攻(切攻击状态);普攻间隔中 TryAttack 返回 false → 等待
                boss.AttackDirector.TryAttack();
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
