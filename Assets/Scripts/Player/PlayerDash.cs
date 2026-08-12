using UnityEngine;

/// <summary>
/// 冲刺执行器 — P3b 降级为纯逻辑执行器(IsDashing 状态迁至 PlayerDashState)
/// 仅提供:DoDash(清速度+设冲刺速度+启动冷却) / CooldownReady(冷却查询) / TickCooldown(冷却递减)
/// dashSpeed/dashDuration/dashCooldown 保留序列化配置;dashDuration 由 PlayerController 注入状态类
/// dashCooldownTimer 由 PlayerController.UpdateCooldowns 每帧调用 TickCooldown 递减(与改造前行为一致)
/// </summary>
public class PlayerDash : MonoBehaviour
{
    [Header("冲刺")]
    [SerializeField] private float dashSpeed = 10f;
    [SerializeField] private float dashDuration = 0.15f;
    [SerializeField] private float dashCooldown = 0.6f;

    private float dashCooldownTimer;

    /// <summary>冷却是否就绪(可由 FSM 状态类查询,决定 Shift 是否可触发冲刺)</summary>
    public bool CooldownReady => dashCooldownTimer <= 0f;

    /// <summary>冲刺时长(秒),注入 PlayerDashState 做超时退出</summary>
    public float DashDuration => dashDuration;

    /// <summary>执行冲刺:清速度 + 设冲刺速度(facing × dashSpeed) + 启动冷却(由 PlayerDashState.OnEnter 调用)</summary>
    public void DoDash(PlayerController owner)
    {
        dashCooldownTimer = dashCooldown;
        Rigidbody2D rb = owner.GetRigidbody();
        rb.velocity = Vector2.zero;
        rb.velocity = new Vector2(owner.GetFacing() * dashSpeed, 0);
    }

    /// <summary>冷却倒计时(PlayerController.UpdateCooldowns 每帧调用;原 OnPlayerUpdate 内递减逻辑迁出)</summary>
    public void TickCooldown()
    {
        if (dashCooldownTimer > 0f)
            dashCooldownTimer -= Time.deltaTime;
    }
}
