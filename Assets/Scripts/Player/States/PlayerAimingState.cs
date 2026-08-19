using UnityEngine;

/// <summary>
/// 玩家瞄准选点状态（技能组阶段 7,7.3）— 传送后慢动作选点期间锁定输入，瞄准线跟随鼠标。
///
/// LocksInput=true：锁移动/攻击（PlayerController 锁定态跳过子模块更新 → SkillManager.CheckHotkeys 不跑），
/// 因此「再次按技能键确认选点」由本状态自行轮询对应热键并调用 SkillManager.TryActivate：
/// 该技能 interceptsStateAfterActivate=true → TryActivate 不切 PlayerSkillCastState，
/// 执行器收到 SkillActivatedEvent 后执行确认传送（不被打回 0.25s 释放态，B9 出口生效）。
///
/// 超时（Begin 传入）→ 技能干净结束：回调执行器清理（慢动作/瞄准标记）+ 恢复 Idle/Move。
/// 充能按已消耗结算不返还（超时语义：本次释放结束，决策确认）。
/// </summary>
public class PlayerAimingState : EntityState
{
    private readonly PlayerController pc;
    private PlayerAimLine aimLine;
    private float enterTime;
    private float timeout;        // 瞄准超时（秒）
    private float aimDistance;    // 瞄准最大距离
    private LayerMask wallLayers; // 穿墙判定层
    private int slotIndex = -1;   // 所属技能槽
    private System.Action onTimeout;  // 超时回调（执行器清理慢动作/瞄准标记）
    private System.Action onConfirm;  // 左键确认回调（执行器直接传送,不走 TryActivate——确认不消耗充能,1 点/轮,saika 2026-08-19 定稿）

    /// <summary>确认传送后的退出标记:OnExit 只隐藏线、跳过清理回调(慢动作保留到 0 充能,由执行器管)</summary>
    public bool confirmedCleanup;

    public override bool LocksInput => true;

    public PlayerAimingState(PlayerController owner, StateMachine stateMachine, Animator anim)
        : base(owner, stateMachine, anim)
    {
        pc = owner;
    }

    /// <summary>
    /// 开始瞄准：配置参数（超时/距离/墙层/槽位/超时回调）。
    /// 由执行器在传送后调用；随后 FSM 切入本状态（OnEnter 显示瞄准线）。
    /// </summary>
    public void Begin(float timeoutSeconds, float distance, LayerMask walls,
        int skillSlotIndex, System.Action timeoutCallback, System.Action confirmCallback = null)
    {
        timeout = Mathf.Max(0.5f, timeoutSeconds);
        aimDistance = distance;
        wallLayers = walls;
        slotIndex = skillSlotIndex;
        onTimeout = timeoutCallback;
        onConfirm = confirmCallback;
        // 重置超时起点（支持瞄准态内链式重进入：确认传送后仍有充能 → 再次 Begin 重新计时）
        enterTime = Time.time;
    }

    public override void OnEnter()
    {
        base.OnEnter();
        enterTime = Time.time;
        if (aimLine == null) aimLine = pc != null ? pc.GetComponent<PlayerAimLine>() : null;
        if (aimLine != null) aimLine.ConfigureAim(aimDistance, wallLayers);
    }

    public override void OnUpdate()
    {
        // 超时：技能干净结束（充能按已消耗结算不返还；执行器回调清慢动作/瞄准标记）
        if (Time.time - enterTime >= timeout)
        {
            onTimeout?.Invoke();
            ExitToMoveState();
            return;
        }

        // 左键确认选点(saika 2026-08-19 定稿:技能键进入瞄准态消耗 1 充能,鼠标移动瞄准线,左键释放=传送)
        // 直接调执行器确认回调,不走 TryActivate(确认不消耗充能,1 点/轮)
        if (Input.GetMouseButtonDown(0))
        {
            onConfirm?.Invoke();
        }
    }

    public override void OnExit()
    {
        base.OnExit();
        // 隐藏瞄准线（确认传送 / 超时 / 受击打断统一清理）
        if (aimLine != null) aimLine.Hide();
        // 确认传送:跳过清理回调(慢动作保留到 0 充能,由执行器 DoTeleportToAimPoint 管理);否则超时/受击兜底清理
        if (confirmedCleanup)
        {
            confirmedCleanup = false;
            return;
        }
        onTimeout?.Invoke();
    }

    /// <summary>恢复移动（Idle/Move；若空中进入，Idle/Move 的 !grounded 分支会立即转 Fall）</summary>
    private void ExitToMoveState()
    {
        float h = Input.GetAxisRaw("Horizontal");
        stateMachine.ChangeState(Mathf.Abs(h) > 0.1f ? pc.MoveState : pc.IdleState);
    }
}
