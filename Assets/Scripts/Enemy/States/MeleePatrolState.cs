using UnityEngine;

/// <summary>
/// 近战敌人巡逻状态 — 靠边界检测（管道水平射线 / 脚下悬崖）控制移动范围，发现玩家转追击。
/// </summary>
public class MeleePatrolState : EntityState
{
    private int patrolDir;
    private float pauseTimer;   // 转向停顿计时器（>0 时原地 idle，归零后继续巡逻）

    public MeleePatrolState(CharacterBase owner, StateMachine stateMachine, Animator anim = null)
        : base(owner, stateMachine, anim)
    {
    }

    public override void OnEnter()
    {
        var me = (EnemyMeleeController)owner;
        patrolDir = Random.value > 0.5f ? 1 : -1;
        pauseTimer = 0f;
        me.OnExitCombatState();
        // me.ApplyStateColor(new Color(0.2f, 0.4f, 1.0f));  // [状态色已移除]
    }

    public override void OnUpdate()
    {
        var me = (EnemyMeleeController)owner;

        // [转向停顿] 转向后先原地停顿 1-2 秒（moveInput=0 → 每帧 IsIdle=true，动画自然摆 idle）。
        // 停顿期间保留玩家检测分流（CanSeePlayer → 追击），与正常巡逻一致。
        if (pauseTimer > 0f)
        {
            pauseTimer -= Time.deltaTime;
            me.moveInput = 0f;
            if (me.CanSeePlayer())
                me.Fsm.ChangeState(me.CreateChaseState());
            return;
        }

        // [管道边界] 前方有管道（Channel 层）→ 转向 + 停顿，防止走出管道区。
        // 停顿期内不重复检测，归零后沿反方向走。
        if (me.HasChannelAhead(patrolDir))
        {
            patrolDir *= -1;
            pauseTimer = Random.Range(1f, 2f);
        }

        // [悬崖检测] 前方脚下无地面（悬崖/空洞）→ 转向 + 停顿，防止走下悬崖。
        // 停顿期内不重复检测，归零后沿反方向走。
        if (!me.HasGroundAhead(patrolDir))
        {
            patrolDir *= -1;
            pauseTimer = Random.Range(1f, 2f);
        }

        me.moveInput = patrolDir * 0.5f;

        if (me.CanSeePlayer())
            me.Fsm.ChangeState(me.CreateChaseState());
    }

    public override void OnExit()
    {
        var me = (EnemyMeleeController)owner;
        me.moveInput = 0f;
    }
}
