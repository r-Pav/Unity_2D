using UnityEngine;

/// <summary>
/// 远程敌人巡逻状态 — 照搬 MeleePatrolState：在 patrolRange 内来回移动，发现玩家分流：
///   攻击框内（近战/远程）→ 攻击入口（RangedAttackState 判框选动画）
///   框外 → 加速移动（RangedRushState）
/// </summary>
public class RangedPatrolState : EntityState
{
    private float originX;
    private int patrolDir;
    private float changeDirTimer;
    private float pauseTimer;   // 转向停顿计时器（>0 时原地 idle，归零后继续巡逻）

    public RangedPatrolState(CharacterBase owner, StateMachine stateMachine, Animator anim = null)
        : base(owner, stateMachine, anim)
    {
    }

    public override void OnEnter()
    {
        var me = (EnemyRangedController)owner;
        originX = me.transform.position.x;
        patrolDir = Random.value > 0.5f ? 1 : -1;
        changeDirTimer = Random.Range(2f, 4f);
        pauseTimer = 0f;
        me.OnExitCombatState();
        // me.ApplyStateColor(new Color(0.2f, 0.4f, 1.0f)); // 蓝色 [状态色已移除]
    }

    public override void OnUpdate()
    {
        var me = (EnemyRangedController)owner;

        // [转向停顿] 转向后先原地停顿 1-2 秒（moveInput=0 → 每帧 IsIdle=true，动画自然摆 idle）。
        // 停顿期间保留玩家检测分流（CanSeePlayer → 攻击框内攻击/框外 Rush），与正常巡逻一致。
        if (pauseTimer > 0f)
        {
            pauseTimer -= Time.deltaTime;
            me.moveInput = 0f;
            if (me.CanSeePlayer())
            {
                if (me.PlayerInAnyAttackRect() && me.attackCooldownTimer <= 0f)
                    me.Fsm.ChangeState(me.CreateChaseState());
                else if (!me.PlayerInAnyAttackRect())
                    me.Fsm.ChangeState(new RangedRushState(owner, stateMachine, anim));
            }
            return;
        }

        changeDirTimer -= Time.deltaTime;
        if (changeDirTimer <= 0f)
        {
            patrolDir *= -1;
            changeDirTimer = Random.Range(2f, 4f);
            pauseTimer = Random.Range(1f, 2f);   // 停顿 1-2 秒再走
        }

        // 巡逻范围边界回弹
        float dx = me.transform.position.x - originX;
        if (dx > me.PatrolRange) patrolDir = -1;
        else if (dx < -me.PatrolRange) patrolDir = 1;

        if (me.CanSeePlayer())
        {
            // 玩家可见 → 一律原地待命，不巡逻（moveInput 必须在分支开头就清，
            // 防止本帧 OnFixedUpdate 仍用旧巡逻速度移动）
            me.moveInput = 0f;

            // 攻击框内（且冷却就绪）→ 攻击入口（核心入口判断）；框外 → 加速移动
            // 框内但冷却中 → 原地等冷却归零，下帧自动切攻击（不再巡逻走路）
            if (me.PlayerInAnyAttackRect() && me.attackCooldownTimer <= 0f)
                me.Fsm.ChangeState(me.CreateChaseState());
            else if (!me.PlayerInAnyAttackRect())
                me.Fsm.ChangeState(new RangedRushState(owner, stateMachine, anim));
            return;
        }

        // 玩家不可见才正常巡逻
        // [悬崖检测] 前方脚下无地面（悬崖/空洞）→ 转向防掉落。
        // 转向后必须重置 changeDirTimer，防止悬崖边每帧重复检测反复转向。
        if (!me.HasGroundAhead(patrolDir))
        {
            patrolDir *= -1;
            changeDirTimer = Random.Range(2f, 4f);
            pauseTimer = Random.Range(1f, 2f);   // 停顿 1-2 秒再走
        }

        me.moveInput = patrolDir * 0.5f;
    }

    public override void OnExit()
    {
        var me = (EnemyRangedController)owner;
        me.moveInput = 0f;
    }
}
