using UnityEngine;

/// <summary>
/// 贴墙状态 — 下滑（默认/S加速）/ 力驱动攀爬（W）/ 蹬墙跳（Space）+ 额外跳跃计数 / 翻顶
/// 每帧用射线维持与墙面的固定间隙
/// P1 改造:继承 EntityState 挂入统一 PlayerFsm(anim 不绑定,IsMove 相关由 PlayerAnimation 处理)
/// </summary>
public class WallClingState : EntityState
{
    private readonly PlayerCharacterBase player;
    private readonly Rigidbody2D rb;

    // ---- 蹬墙跳冷却 ----
    private float _kickCooldown;

    // ---- 抓墙缓冲(进入贴墙后短暂停住,给玩家反应时间按 Space) ----
    private float _grabTimer;
    private const float GrabHoldTime = 0.15f;

    // ---- 翻顶去重:2026-08-14 移至 PlayerCharacterBase(_vaultTriggered),此处不再持有 ----

    // ---- 墙面间隙控制 ----
    private const float WallGap = 0.02f;
    private const float GapAdjustSpeed = 8f;

    // ---- 公开访问器 ----
    public bool IsWallKicking { get; private set; }

    public WallClingState(PlayerCharacterBase player, StateMachine stateMachine)
        : base(player, stateMachine, null)
    {
        this.player = player;
        this.rb = player.Rb;
    }

    public override void OnEnter()
    {
        player.SetVelocityPublic(x: 0f, y: 0f);
        _kickCooldown = 0f;
        player.ResetVaultFlag();   // 防翻顶触发后卡住不复位(去重标记在 PlayerCharacterBase,进贴墙即复位)
        IsWallKicking = false;
        _grabTimer = GrabHoldTime;   // 抓墙缓冲:先停住,给反应时间

        // 贴墙自动面向墙面(左墙面朝左、右墙面朝右),不依赖玩家输入
        if (player.WallDirection != 0)
            player.UpdateFacing(player.WallDirection);
    }

    public override void OnUpdate()
    {
        if (CheckExit()) return;

        // 贴墙期间锁定朝向朝墙(防方向键把玩家翻成背对墙下滑的奇怪视觉;
        // 蹬墙跳时由 WallKick 改成弹出方向,之后朝向交给输入)
        if (player.WallDirection != 0)
            player.UpdateFacing(player.WallDirection);

        _kickCooldown -= Time.deltaTime;

        if (Input.GetKeyDown(KeyCode.Space) && _kickCooldown <= 0f)
        {
            // 墙顶优先翻顶:TryVault(框+射线)成功 → 翻顶成功,传送完成;
            // 状态切换由调用方判断(贴墙调用方切 FallState 自然落地);失败 → 蹬墙跳
            if (player.TryVault())
            {
                var pc = player as PlayerController;
                stateMachine.ChangeState(pc != null ? pc.FallState : null);
                return;
            }
            WallKick();
            return;
        }

        // 抓墙缓冲:进入贴墙后短暂停住(速度 0),给玩家按 Space 的反应时间,
        // 缓冲结束才进入攀爬/下滑,避免"一瞬间就滑下去"窗口太短
        if (_grabTimer > 0f)
        {
            _grabTimer -= Time.deltaTime;
            player.SetVelocityPublic(y: 0f);
            MaintainWallGap();
            return;
        }

        if (Input.GetKey(KeyCode.W))
        {
            player.SetVelocityPublic(y: player.WallClimbSpeed);
            CheckVault();
        }
        else if (Input.GetAxisRaw("Vertical") < -0.1f)
        {
            float speed = -player.WallSlideSpeed * player.WallFastSlideMultiplier;
            player.SetVelocityPublic(y: speed);
        }
        else
        {
            player.SetVelocityPublic(y: -player.WallSlideSpeed);
        }

        MaintainWallGap();
    }

    public override void OnExit()
    {
        IsWallKicking = false;
    }

    // ---- 内部方法 ----

    private bool CheckExit()
    {
        if (!player.IsTouchingWall || player.IsGrounded)
        {
            // 退出贴墙回下落状态:落地由 PlayerFallState.OnUpdate 立刻切回 Idle/Move
            var pc = player as PlayerController;
            stateMachine.ChangeState(pc != null ? pc.FallState : null);
            return true;
        }
        return false;
    }

    /// <summary>每帧用射线测墙面距离，平滑推到固定间隙</summary>
    private void MaintainWallGap()
    {
        if (player.Col == null || player.WallDirection == 0) return;

        Vector2 dir = Vector2.right * player.WallDirection;
        Vector2 origin = (Vector2)player.transform.position + Vector2.up * player.WallCheckFootHeight;
        RaycastHit2D hit = Physics2D.Raycast(origin, dir, player.WallGapRayDistance, player.WallLayer);
        if (!hit) return;

        float halfWidth = player.Col.bounds.extents.x;
        float targetX = hit.point.x - player.WallDirection * (halfWidth + WallGap);

        Vector2 pos = rb.position;
        pos.x = Mathf.MoveTowards(pos.x, targetX, GapAdjustSpeed * Time.deltaTime);
        rb.position = pos;
    }

    private void WallKick()
    {
        var pc = player as PlayerController;
        if (pc == null) return;

        float forceX = -player.WallDirection * pc.WallKickForceX;
        float forceY = pc.WallKickForceY;

        player.SetVelocityPublic(x: forceX, y: 0f);
        rb.AddForce(Vector2.up * forceY, ForceMode2D.Impulse);

        // 蹬墙跳自动朝向:面向弹出方向(离墙,朝对面墙飞)
        if (player.WallDirection != 0)
            player.UpdateFacing(-player.WallDirection);

        _kickCooldown = 0.3f;
        IsWallKicking = true;

        pc.FreezeTimer = 0.1f;
        pc.ClearWallContact();
        stateMachine.ChangeState(pc.FallState);
    }

    /// <summary>攀爬 W 每帧尝试翻顶:TryVault(框+射线),失败静默(继续爬)</summary>
    private void CheckVault()
    {
        if (player.TryVault())
        {
            var pc = player as PlayerController;
            stateMachine.ChangeState(pc != null ? pc.FallState : null);
        }
    }
}
