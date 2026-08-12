using UnityEngine;

/// <summary>
/// 技能释放状态 — 技能激活成功后进入(由 SkillManager.TryActivate 成功路径触发 ChangeState)
/// 只做"释放期间的输入锁定与表现":LocksInput=true 锁输入,固定时长后回 Idle/Move
/// 具体技能效果逻辑不迁移 — 技能是数据层(SkillManager 保留:法力/冷却/事件)
/// 说明:当前技能无独立动画参数/释放事件(SkillManager 仅有 SkillActivatedEvent),
///      退出条件采用固定时长兜底(释放结束回移动;后续可改为 SkillData.castTime 注入)
/// </summary>
public class PlayerSkillCastState : EntityState
{
    /// <summary>技能释放固定时长(秒):当前所有技能 castTime=0(瞬发),用固定时长提供释放期输入锁定窗口</summary>
    private const float CastDuration = 0.25f;

    private float enterTime;   // 进入时间戳(固定时长判定用)

    public override bool LocksInput => true;

    public PlayerSkillCastState(CharacterBase owner, StateMachine stateMachine, Animator anim)
        : base(owner, stateMachine, anim)
    {
        // 无技能动画参数 → 不绑定 animBoolNames(技能表现由 SkillActivatedEvent 订阅方处理)
    }

    public override void OnEnter()
    {
        base.OnEnter();
        enterTime = Time.time;
    }

    public override void OnUpdate()
    {
        var pc = (PlayerController)owner;

        // 释放时长结束 → 恢复移动(Idle/Move;若空中进入,Idle/Move 的 !grounded 分支会立即转 Fall)
        if (Time.time - enterTime >= CastDuration)
        {
            float h = Input.GetAxisRaw("Horizontal");
            stateMachine.ChangeState(Mathf.Abs(h) > 0.1f ? pc.MoveState : pc.IdleState);
        }
    }
}
