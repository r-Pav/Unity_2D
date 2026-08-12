using UnityEngine;

/// <summary>
/// 远程敌人攻击状态 — 0.5s 攻击窗口，0.3s 时执行远程攻击，结束转追击/待机。
/// </summary>
public class RangedAttackState : EntityState
{
    private IEnemyAttack attackModule;
    private float timer;
    private bool attacked;

    public RangedAttackState(CharacterBase owner, StateMachine stateMachine, Animator anim = null)
        : base(owner, stateMachine, anim)
    {
    }

    public override void OnEnter()
    {
        var me = (EnemyRangedController)owner;
        timer = 0.5f;
        attacked = false;
        me.moveInput = 0f;
        me.OnEnterCombatState();
        me.GetComponent<Rigidbody2D>().velocity = new Vector2(0f, me.GetComponent<Rigidbody2D>().velocity.y);
        me.ApplyStateColor(new Color(1.0f, 0.7f, 0.0f));
        attackModule = me.GetComponent<IEnemyAttack>();
    }

    public override void OnUpdate()
    {
        var me = (EnemyRangedController)owner;
        timer -= Time.deltaTime;

        if (!attacked && timer <= 0.3f)
        {
            attacked = true;
            // Debug.Log($"[{me.name}] 执行远程攻击");
            attackModule?.PerformAttack(me);
        }

        if (timer <= 0f)
        {
            if (me.CanSeePlayer())
                me.Fsm.ChangeState(me.CreateChaseState());
            else
                me.Fsm.ChangeState(new RangedIdleState(owner, stateMachine, anim));
        }
    }

    public override void OnExit()
    {
        var me = (EnemyRangedController)owner;
        me.attackCooldownTimer = me.AttackCooldownDuration;
    }
}
