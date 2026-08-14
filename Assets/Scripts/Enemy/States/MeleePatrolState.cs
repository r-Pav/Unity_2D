using UnityEngine;

/// <summary>
/// 近战敌人巡逻状态 — 在巡逻范围内来回移动，发现玩家转追击。
/// </summary>
public class MeleePatrolState : EntityState
{
    private float originX;
    private int patrolDir;
    private float changeDirTimer;
    private float pauseTimer;   // 转向停顿计时器（>0 时原地 idle，归零后继续巡逻）

    public MeleePatrolState(CharacterBase owner, StateMachine stateMachine, Animator anim = null)
        : base(owner, stateMachine, anim)
    {
    }

    public override void OnEnter()
    {
        var me = (EnemyMeleeController)owner;
        originX = me.transform.position.x;
        patrolDir = Random.value > 0.5f ? 1 : -1;
        changeDirTimer = Random.Range(2f, 4f);
        pauseTimer = 0f;
        me.OnExitCombatState();
        me.ApplyStateColor(new Color(0.2f, 0.4f, 1.0f));
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

        changeDirTimer -= Time.deltaTime;
        if (changeDirTimer <= 0f)
        {
            patrolDir *= -1;
            changeDirTimer = Random.Range(2f, 4f);
            pauseTimer = Random.Range(1f, 2f);   // 停顿 1-2 秒再走
        }

        float dx = me.transform.position.x - originX;
        if (dx > me.PatrolRange) patrolDir = -1;
        else if (dx < -me.PatrolRange) patrolDir = 1;

        // [悬崖检测] 前方脚下无地面（悬崖/空洞）→ 转向防掉落。
        // 转向后必须重置 changeDirTimer，防止悬崖边每帧重复检测反复转向。
        if (!me.HasGroundAhead(patrolDir))
        {
            patrolDir *= -1;
            changeDirTimer = Random.Range(2f, 4f);
            pauseTimer = Random.Range(1f, 2f);   // 停顿 1-2 秒再走
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
