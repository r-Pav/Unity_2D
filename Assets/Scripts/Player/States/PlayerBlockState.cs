using UnityEngine;

/// <summary>
/// 格挡/弹反状态 — 右键按住进入,松手退出
/// 弹反窗口 0.2s(parryMaxWindow,原 PlayerCombat 序列化值):短按(≤窗口)松手判定弹反,长按正常结束
/// 减伤修饰器走 StatModifierManager(PlayerCombat.StartBlocking/CancelBlock 保留)
/// 视觉闪色(弹反成功闪烁)逻辑保留在 PlayerCombat(状态类非 MonoBehaviour 无法跑协程),由本状态驱动时机
/// </summary>
public class PlayerBlockState : EntityState
{
    private readonly PlayerCombat combat;
    private readonly float parryMaxWindow;
    private float blockStartTime;

    public PlayerBlockState(CharacterBase owner, StateMachine stateMachine, Animator anim,
        PlayerCombat combat, float parryMaxWindow)
        : base(owner, stateMachine, anim)
    {
        this.combat = combat;
        this.parryMaxWindow = parryMaxWindow;
    }

    public override void OnEnter()
    {
        base.OnEnter();
        blockStartTime = Time.time;
        // 注入格挡减伤修饰器 + 换色(原 PlayerCombat.HandleBlockParryInput → StartBlocking)
        combat?.StartBlocking();
    }

    /// <summary>是否处于弹反窗口内（格挡按下时长 ≤ parryMaxWindow）— 供 CombatResolver 玩家侧 TryParry 查询</summary>
    public bool IsInParryWindow => Time.time - blockStartTime <= parryMaxWindow;

    public override void OnUpdate()
    {
        var pc = (PlayerController)owner;

        // P3b:冲刺打断格挡(原 PlayerDash.HandleDashInput:Dash 中取消格挡迁入;
        // ChangeState(DashState) 会先调本状态 OnExit → combat.CancelBlock 清理减伤/恢复颜色)
        if (Input.GetKeyDown(KeyCode.LeftShift) && pc.Dash != null && pc.Dash.CooldownReady)
        {
            stateMachine.ChangeState(pc.DashState);
            return;
        }

        // 松手 → 判定弹反/正常结束(原 PlayerCombat.HandleBlockParryInput)
        if (Input.GetMouseButtonUp(1))
        {
            float holdDuration = Time.time - blockStartTime;

            // 先退出格挡(OnExit 统一清理:移除减伤 + 恢复颜色),再做弹反判定(成功时闪色覆盖恢复色)
            float h = Input.GetAxisRaw("Horizontal");
            stateMachine.ChangeState(Mathf.Abs(h) > 0.1f ? pc.MoveState : pc.IdleState);

            // 短按判定为弹反(原 AttemptParry:范围内敌人处于攻击帧则成功)
            if (holdDuration <= parryMaxWindow)
                combat?.AttemptParry();
            return;
        }
    }

    public override void OnExit()
    {
        base.OnExit();
        // 移除格挡减伤修饰器 + 恢复原色(原 CancelBlock;防重:内部 isBlocking 守卫)
        combat?.CancelBlock();
    }
}
