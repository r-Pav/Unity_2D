using UnityEngine;

/// <summary>
/// 近战敌人追击状态 — 发现玩家后追击，进入攻击范围且冷却好则转攻击；丢玩家 3s 回巡逻。
/// </summary>
public class MeleeChaseState : EntityState
{
    private float losePlayerTimer;

    public MeleeChaseState(CharacterBase owner, StateMachine stateMachine, Animator anim = null)
        : base(owner, stateMachine, anim)
    {
    }

    public override void OnEnter()
    {
        var me = (EnemyMeleeController)owner;
        losePlayerTimer = 5f;
        me.OnEnterCombatState();
        // me.ApplyStateColor(new Color(1.0f, 0.2f, 0.2f));  // [状态色已移除]
        me.moveInput = me.DirectionToPlayer();
    }

    public override void OnUpdate()
    {
        var me = (EnemyMeleeController)owner;
        if (me.IsDead) return;

        if (me.CanSeePlayer())
        {
            losePlayerTimer = 5f;
            // 玩家已在攻击范围内：停住等待攻击（防重叠后 DirectionToPlayer 抖动导致左右震动）
            if (me.PlayerInAttackRange())
            {
                me.moveInput = 0f;
            }
            else
            {
                me.moveInput = me.DirectionToPlayer();
            }

            if (me.CanAttack())
            {
                me.moveInput = 0f;
                me.Fsm.ChangeState(new MeleeAttackState(owner, stateMachine, anim));
            }
        }
        else
        {
            // 玩家不可见时:若前方检测到管道(玩家可能进管道/被管道遮挡)→ 立即移除仇恨回巡逻
            int chaseDir = (int)Mathf.Sign(me.DirectionToPlayer());
            if (chaseDir != 0 && me.HasChannelAhead(chaseDir))
            {
                me.moveInput = 0f;
                me.Fsm.ChangeState(new MeleePatrolState(owner, stateMachine, anim));
                return;
            }
            losePlayerTimer -= Time.deltaTime;
            if (losePlayerTimer <= 0f)
                me.Fsm.ChangeState(new MeleePatrolState(owner, stateMachine, anim));
        }
    }

    public override void OnExit() { }
}
