using UnityEngine;

/// <summary>
/// 远程敌人加速移动状态（Rush）— 玩家在攻击框外/受击被击退后快速接近：
///   OnEnter：SetMoveSpeedOverride(CurrentMoveSpeed * rushMultiplier) 加速（默认 1.5 倍）
///   OnUpdate：玩家进攻击框（近战/远程）且冷却就绪 → 攻击入口；超时（rushDuration 默认 2s）
///             无条件回巡逻（进框+冷却就绪已处理掉，剩余冷却中/框外都不该继续追）
///   OnExit：SetMoveSpeedOverride(null) 恢复原速（禁止直接改 baseMoveSpeed）
/// 注：受击时由 OnHitBy 直接切走（CreateAttackEntryState），本状态不感知受击。
/// </summary>
public class RangedRushState : EntityState
{
    /// <summary>加速持续时间（秒），超时且未检测到玩家/框外回巡逻（默认 2s）</summary>
    private float rushDuration = 2f;

    /// <summary>加速倍率（乘当前移速；通过 SetMoveSpeedOverride 生效，默认 1.5）</summary>
    private float rushMultiplier = 1.5f;

    private float timer;

    public RangedRushState(CharacterBase owner, StateMachine stateMachine, Animator anim = null)
        : base(owner, stateMachine, anim)
    {
    }

    public override void OnEnter()
    {
        var me = (EnemyRangedController)owner;
        timer = 0f;
        me.OnEnterCombatState();
        // me.ApplyStateColor(new Color(1.0f, 0.6f, 0.0f)); // 橙色（加速）[状态色已移除]

        // 加速移动：临时覆盖移速（OnExit 恢复），禁止直接改 baseMoveSpeed
        me.SetMoveSpeedOverride(me.CurrentMoveSpeed * rushMultiplier);

        // 朝向玩家冲刺
        me.moveInput = me.DirectionToPlayer();
        me.UpdateFacing(me.moveInput);
    }

    public override void OnUpdate()
    {
        var me = (EnemyRangedController)owner;
        if (me.IsDead) return;

        // 玩家进攻击框（近战或远程）且冷却就绪 → 攻击入口（判框选动画）
        if (me.PlayerInAnyAttackRect() && me.attackCooldownTimer <= 0f)
        {
            me.Fsm.ChangeState(me.CreateChaseState());
            return;
        }

        // 持续冲刺：保持朝向玩家
        me.moveInput = me.DirectionToPlayer();

        timer += Time.deltaTime;
        if (timer >= rushDuration)
        {
            // 超时无条件回巡逻：进框+冷却就绪已在分支开头处理掉，
            // 剩下的要么冷却中要么框外，都不该继续追（防"一直走"循环）
            me.Fsm.ChangeState(new RangedPatrolState(owner, stateMachine, anim));
        }
    }

    public override void OnExit()
    {
        var me = (EnemyRangedController)owner;
        me.SetMoveSpeedOverride(null); // 恢复原速
        me.moveInput = 0f;
    }
}
