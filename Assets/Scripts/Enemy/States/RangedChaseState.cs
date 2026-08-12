using UnityEngine;

/// <summary>
/// 远程敌人追击状态 — 三级矩形距离策略保持射击距离，丢玩家 3s 回待机。
/// </summary>
public class RangedChaseState : EntityState
{
    private float losePlayerTimer;
    private float debugCooldown;

    public RangedChaseState(CharacterBase owner, StateMachine stateMachine, Animator anim = null)
        : base(owner, stateMachine, anim)
    {
    }

    public override void OnEnter()
    {
        var me = (EnemyRangedController)owner;
        losePlayerTimer = 3f;
        debugCooldown = 0f;
        me.OnEnterCombatState();
        me.ApplyStateColor(new Color(1.0f, 0.2f, 0.2f)); // 红色
        me.moveInput = me.DirectionToPlayer();
    }

    public override void OnUpdate()
    {
        var me = (EnemyRangedController)owner;
        if (me.IsDead) return;

        if (me.CanSeePlayer())
        {
            losePlayerTimer = 3f;
            TryTransitionToAttack(me);
        }
        else
        {
            losePlayerTimer -= Time.deltaTime;
            if (losePlayerTimer <= 0f)
                me.Fsm.ChangeState(new RangedIdleState(owner, stateMachine, anim));
        }
    }

    private void TryTransitionToAttack(EnemyRangedController me)
    {
        // 三级矩形距离策略（带迟滞区间，防止 single-threshold 震荡）
        if (me.InRect(me.RetreatWidth, me.RetreatHeight))
            me.moveInput = -me.DirectionToPlayer() * 0.5f;       // 太近，后退
        else if (me.InRect(me.RetreatRecoverWidth, me.RetreatRecoverHeight))
            me.moveInput = 0f;                                   // 迟滞区间，静止
        else
            me.moveInput = me.DirectionToPlayer();                // 足够远，追击

        if (me.CanAttack())
        {
            // Debug.Log($"[{me.name}] 进入攻击！");
            me.Fsm.ChangeState(new RangedAttackState(owner, stateMachine, anim));
        }
        else
        {
            debugCooldown -= Time.deltaTime;
            if (debugCooldown <= 0f)
            {
                debugCooldown = 0.5f;
                float dx = me.PlayerTarget.position.x - me.transform.position.x;
                float dy = me.PlayerTarget.position.y - me.transform.position.y;
                // Debug.Log($"[{me.name}] CanAttack=false | CanSeePlayer={me.CanSeePlayer()} | cooldown={me.attackCooldownTimer:F2} | " +
                //     $"deltaX={dx:F2} deltaY={dy:F2} | atkW={me.AttackWidth} atkH={me.AttackHeight} | " +
                //     $"retW={me.RetreatWidth} retH={me.RetreatHeight}");
            }
        }
    }

    public override void OnExit()
    {
    }
}
