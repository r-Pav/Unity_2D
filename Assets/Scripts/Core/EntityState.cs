using UnityEngine;

/// <summary>
/// 实体状态基类 — 继承 IState，内置 Animator 参数绑定 + 输入锁定标记
/// 属于行为层，不直接操作物理（物理由 CharacterBase 子类方法提供）
/// </summary>
public abstract class EntityState : IState
{
    // ── 持有引用 ──
    protected readonly CharacterBase owner;        // 宿主（PlayerController 或 EnemyControllerBase）
    protected readonly StateMachine stateMachine;  // 所属状态机
    protected readonly Animator anim;              // 动画控制器（可为 null：纯逻辑状态如 Freeze/Invincible 不传，OnEnter/OnExit 已 null-safe）

    // ── 动画参数绑定 ──
    // 每个 EntityState 子类在构造函数中声明自己控制的 animBool/animTrigger
    protected readonly string[] animBoolNames;     // Enter=true, Exit=false
    protected readonly string[] animTriggerNames;  // Enter 时触发一次

    // ── 输入锁定 ──
    // 返回 true 表示此状态锁定玩家移动/跳跃/攻击输入
    // PlayerController.IsActionLocked() 聚合 FSM.CurrentState.LocksInput
    public virtual bool LocksInput => false;

    // ── 生命周期 ──
    protected EntityState(CharacterBase owner, StateMachine stateMachine,
        Animator anim = null, string[] animBoolNames = null, string[] animTriggerNames = null)
    {
        this.owner = owner;
        this.stateMachine = stateMachine;
        this.anim = anim;
        this.animBoolNames = animBoolNames ?? new string[0];
        this.animTriggerNames = animTriggerNames ?? new string[0];
    }

    public virtual void OnEnter()
    {
        foreach (var b in animBoolNames)
            anim?.SetBool(b, true);
        foreach (var t in animTriggerNames)
            anim?.SetTrigger(t);
    }

    public virtual void OnExit()
    {
        foreach (var b in animBoolNames)
            anim?.SetBool(b, false);
    }

    public abstract void OnUpdate();
}
