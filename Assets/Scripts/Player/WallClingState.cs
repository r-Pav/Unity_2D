using UnityEngine;

/// <summary>
/// 贴墙状态 — 下滑（默认/S加速）/ 力驱动攀爬（W）/ 蹬墙跳（Space）+ 额外跳跃计数 / 翻顶
/// 每帧用射线维持与墙面的固定间隙
/// </summary>
public class WallClingState : IState
{
    private readonly PlayerCharacterBase player;
    private readonly StateMachine stateMachine;
    private readonly Rigidbody2D rb;

    // ---- 蹬墙跳冷却 ----
    private float _kickCooldown;

    // ---- 翻顶去重 ----
    private bool _vaultTriggered;

    // ---- 墙面间隙控制 ----
    private const float WallGap = 0.02f;
    private const float GapAdjustSpeed = 8f;

    // ---- 公开访问器 ----
    public bool IsWallKicking { get; private set; }

    public WallClingState(PlayerCharacterBase player, StateMachine stateMachine)
    {
        this.player = player;
        this.stateMachine = stateMachine;
        this.rb = player.Rb;
    }

    public void OnEnter()
    {
        player.SetVelocityPublic(x: 0f, y: 0f);
        _kickCooldown = 0f;
        _vaultTriggered = false;
        IsWallKicking = false;

        // 贴墙自动面向墙面(左墙面朝左、右墙面朝右),不依赖玩家输入
        if (player.WallDirection != 0)
            player.UpdateFacing(player.WallDirection);
    }

    public void OnUpdate()
    {
        if (CheckExit()) return;

        // 贴墙期间锁定朝向朝墙(防方向键把玩家翻成背对墙下滑的奇怪视觉;
        // 蹬墙跳时由 WallKick 改成弹出方向,之后朝向交给输入)
        if (player.WallDirection != 0)
            player.UpdateFacing(player.WallDirection);

        _kickCooldown -= Time.deltaTime;

        if (Input.GetKeyDown(KeyCode.Space) && _kickCooldown <= 0f)
        {
            WallKick();
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

    public void OnExit()
    {
        IsWallKicking = false;
    }

    // ---- 内部方法 ----

    private bool CheckExit()
    {
        if (!player.IsTouchingWall || player.IsGrounded)
        {
            stateMachine.ChangeState(null);
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
        stateMachine.ChangeState(null);
    }

    private void CheckVault()
    {
        if (!player.CheckWallTop() && player.CanVault() && !_vaultTriggered)
        {
            _vaultTriggered = true;
            TriggerVault();
        }
    }

    private void TriggerVault()
    {
        var pc = player as PlayerController;
        if (pc == null) return;

        Vector2 vaultTarget = (Vector2)player.transform.position
                            + Vector2.up * player.VaultUpOffset
                            + Vector2.right * player.WallDirection * player.VaultForwardOffset;

        rb.position = vaultTarget;
        pc.FreezeTimer = 0.15f;
        stateMachine.ChangeState(null);
    }
}
