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

    // ---- 翻顶去重 ----
    private bool _vaultTriggered;

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
        _vaultTriggered = false;
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
            // 墙顶优先翻顶:接近墙顶(提前量)+ 落点无障碍 → 翻顶;否则蹬墙跳
            if (player.NearWallTop() && player.CanVault())
            {
                CheckVault();
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

    private void CheckVault()
    {
        if (player.NearWallTop() && player.CanVault() && !_vaultTriggered)
        {
            _vaultTriggered = true;
            TriggerVault();
        }
    }

    /// <summary>翻顶(贴墙/空中共用;PlayerJump 空中接近墙顶时也会调用)</summary>
    public void TriggerVault()
    {
        var pc = player as PlayerController;
        if (pc == null) return;

        int dir = player.WallDirection != 0 ? player.WallDirection : player.FacingDir;
        if (dir == 0) return;

        // 落点 = 当前墙顶正上方:贴着墙面向下找墙顶,玩家 x 保持(墙顶范围内),直接站上墙顶。
        // 之前用固定偏移(玩家.y + VaultUpOffset,墙外 VaultForwardOffset)会瞬移到空中再下落,
        // 偏移不足时落回墙边("传上去又回到墙上")。
        var col = player.Col;
        float halfH = col != null ? col.bounds.extents.y : 0.5f;
        float wallTopY = player.transform.position.y + player.VaultUpOffset;   // fallback:找不到墙顶用原逻辑
        float targetX = player.transform.position.x;                            // fallback:原 x
        float wallFaceX = 0f;
        if (col != null)
        {
            Vector2 origin = (Vector2)player.transform.position
                           + Vector2.right * dir * (col.bounds.extents.x + 0.05f)
                           + Vector2.up * (col.bounds.extents.y + player.VaultUpOffset);
            RaycastHit2D hit = Physics2D.Raycast(origin, Vector2.down, player.VaultUpOffset + 2f, player.WallLayer);
            if (hit && hit.point.y > player.transform.position.y)
            {
                wallTopY = hit.point.y;
                // 落点 x = 墙表面(玩家贴的面)向墙内偏移:玩家重心压进墙顶接触面内,站得住。
                // 之前用玩家.x(重心在墙顶左侧外)或 hit.point.x(重心在墙顶右侧外)都会滑落回墙边;
                // 0.2 比 0.1 更靠墙顶中间(不贴边),若墙顶更宽可继续调大
                wallFaceX = player.transform.position.x + dir * (col.bounds.extents.x + 0.02f);
                targetX = wallFaceX + dir * 0.2f;
            }
        }

        rb.position = new Vector2(targetX, wallTopY + halfH + 0.05f);
        pc.FreezeTimer = 0.15f;
        stateMachine.ChangeState(pc.FallState);
    }
}
