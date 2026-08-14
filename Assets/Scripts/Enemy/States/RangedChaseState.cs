/*
 * ═══════════════════════════════════════════════════════════════════════
 * RangedChaseState.cs — 已停用（内容注释保留，禁止删除本文件）
 * ═══════════════════════════════════════════════════════════════════════
 *
 * 远程敌人双攻击系统（2026-08-14 定稿）删除"后退保持距离 + 追击"逻辑，
 * 改为：巡逻 + 加速移动(Rush) + 判框双攻击(attack1 近战 / attack2 远程)。
 * 原追击状态由以下替代：
 *   - 追击行为 → RangedRushState（加速移动接近）
 *   - 攻击行为 → RangedAttackState（OnEnter 判框选动画）
 * 原实现引用 retreat 矩形字段，随字段删除已无法编译，故整体注释保留备查。
 *
 * ─────────────────────────── 原代码 ───────────────────────────
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
 * ─────────────────────────── 原代码结束 ───────────────────────────
 */
