using UnityEngine;

/// <summary>
/// 远程敌人待机状态 — 短时静止后转巡逻；发现玩家分流：
///   攻击框内（近战/远程）→ 攻击入口（RangedAttackState 判框选动画）
///   框外 → 加速移动（RangedRushState）
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

        if (me.CanSeePlayer())
        {
            // 攻击框内（且冷却就绪）→ 攻击入口；框外 → 加速移动
            if (me.PlayerInAnyAttackRect())
            {
                if (me.attackCooldownTimer <= 0f)
                    me.Fsm.ChangeState(me.CreateChaseState());
            }
            else
            {
                me.Fsm.ChangeState(new RangedRushState(owner, stateMachine, anim));
            }
        }
        else if (timer <= 0f)
        {
            // 待机超时 → 巡逻（不再原地循环）
            me.Fsm.ChangeState(new RangedPatrolState(owner, stateMachine, anim));
        }
        else
        {
            timer -= Time.deltaTime;
        }
    }

    public override void OnExit() { }
}
