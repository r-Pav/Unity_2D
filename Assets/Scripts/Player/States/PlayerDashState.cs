using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 冲刺状态 — Shift 进入(Idle/Move/Jump/Fall 状态类检测后切换),dashDuration(0.15s) 计时后退出
/// OnEnter:清速度 + 设冲刺速度(facing × dashSpeed) + 消耗充能(由 PlayerDash.DoDash 执行)
/// 超时:落地 → Idle/Move,空中 → Fall(与改造前 PlayerDash.OnPlayerUpdate 超时分支一致)
/// 充能恢复统一在 PlayerController.UpdateCooldowns 调 PlayerDash.TickCooldown 递减
/// 阶段 3(树 B lv1):冲刺伤害判定 — DashDamageEnabled 时每帧 OverlapBox 检测冲刺路径上的 enemy,
/// 同一 enemy 本次冲刺只结算一次(hitThisDash,OnEnter 清空);攻击标签 "Dash"(不进敌人硬直分流,只伤害+击退)。
/// </summary>
public class PlayerDashState : EntityState
{
    private readonly PlayerDash dash;
    private readonly float dashDuration;   // 冲刺时长(原 PlayerDash 序列化值,由 PlayerController 注入)
    private float dashTimer;               // 冲刺剩余计时

    // ── 冲刺伤害(阶段 3,树 B lv1)──
    private readonly HashSet<ICombatant> hitThisDash = new(); // 本次冲刺已结算的 enemy(OnEnter 清空,同 enemy 单次冲刺只受击一次)
    private readonly ICombatant playerCombatant;             // player 侧 ICombatant(PlayerHealth 实现,CombatResolver 的 source)
    private readonly ElementModule elementModule;            // 元素模块(伤害触发时刻读 CurrentElement,决策 N5)
    private Vector2 dashStartPos;                            // 冲刺起点(OnEnter 记录;DashEndedEvent 传起点,嘲讽幻象留在原地,不与落点玩家重叠)

    public override bool LocksInput => true;

    public PlayerDashState(CharacterBase owner, StateMachine stateMachine, Animator anim,
        PlayerDash dash, float dashDuration)
        : base(owner, stateMachine, anim, new[] { AnimParams.IsDashing })
    {
        this.dash = dash;
        this.dashDuration = dashDuration;
        playerCombatant = owner.GetComponent<ICombatant>();
        elementModule = owner.GetComponent<ElementModule>();
    }

    public override void OnEnter()
    {
        // IsDashing=true → Dash 动画(控制器参数存在但未用于路由,保持设置)
        base.OnEnter();
        dashTimer = dashDuration;
        hitThisDash.Clear(); // 新一次冲刺重新计命中
        dashStartPos = owner.transform.position; // 记录冲刺起点(B-01 嘲讽幻象留原地,防与落点玩家重叠)
        dash?.DoDash((PlayerController)owner);
    }

    public override void OnUpdate()
    {
        var pc = (PlayerController)owner;

        // 阶段 3:冲刺伤害判定(未启用 DashDamageEnabled 时完全跳过,零开销保持现状)
        if (dash != null && dash.DashDamageEnabled)
            TryHitEnemies(pc);

        dashTimer -= Time.deltaTime;
        if (dashTimer <= 0f)
        {
            // 冲刺结束：发布 DashEndedEvent（树 B B-01 执行器订阅，在冲刺起点生成嘲讽幻象）
            // 注意在切状态前触发：事件处理器读到的仍是冲刺结束位置/朝向
            EventBus.Trigger(new DashEndedEvent(
                dashStartPos,                                // 起点:嘲讽幻象留在冲刺开始处,不与终点玩家重叠
                Vector2.right * pc.GetFacing()));

            // 冲刺结束:落地 → Idle/Move;空中 → Fall(原 PlayerDash.OnPlayerUpdate 超时分支)
            float h = Input.GetAxisRaw("Horizontal");
            if (owner.IsGrounded)
                stateMachine.ChangeState(Mathf.Abs(h) > 0.1f ? pc.MoveState : pc.IdleState);
            else
                stateMachine.ChangeState(pc.FallState);
        }
    }

    /// <summary>冲刺伤害判定:OverlapBox 检测冲刺路径上的 enemy,同一 enemy 本次冲刺只结算一次。未启用时由调用方跳过(零回归)。</summary>
    private void TryHitEnemies(PlayerController pc)
    {
        Vector2 center = (Vector2)owner.transform.position
            + Vector2.right * pc.GetFacing() * dash.DashHitForwardOffset;
        Collider2D[] hits = Physics2D.OverlapBoxAll(center, dash.DashHitBoxSize, 0f, dash.DashHitLayers);

        foreach (Collider2D col in hits)
        {
            var defender = col.GetComponent<ICombatant>();
            if (defender == null || defender == playerCombatant) continue; // 只打 enemy,防自伤
            if (!hitThisDash.Add(defender)) continue; // 本次冲刺已结算过 → 跳过(单次冲刺只受击一次)

            DamageInfo info = new DamageInfo
            {
                amount = dash.DashDamage,
                source = playerCombatant,
                sourcePosition = (Vector2)owner.transform.position,
                attackLabel = "Dash",
                knockback = new Knockback
                {
                    direction = Vector2.right * pc.GetFacing(), // 沿冲刺方向
                    force = dash.DashKnockbackForce,
                    duration = 0f,
                    ignoreResistance = false
                },
                element = elementModule != null ? elementModule.CurrentElement : ElementType.None, // 触发时刻读取(决策 N5)
                canTriggerElementProc = true,   // player 攻击默认可触发元素 proc(雷 proc 落雷等,测试期 ProcChance=1f 每击触发)
                critMultiplier = 0f             // 冲刺不做暴击仲裁(与魔法弹一致,倍率烘焙进伤害值)
            };
            CombatResolver.Resolve(playerCombatant, defender, info);
        }
    }
}
