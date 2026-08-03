# 二段跳 + 爬墙系统 · 完整策划案

> 版本：2.0（推翻重写）
> 项目：2D 横板平台动作
> 引擎：团结引擎 1.8.4
> 日期：2026-07-08

---

## 0. 旧版问题根因分析

在动手重写之前，先搞清楚现有代码为什么反复修不好。

### 0.1 跳跃系统："从高处落下时跳不了"

**根因：输入被墙状态机抢先消费。**

OnUpdate 流水线：
```
DetectWallEntry() → WallStateMachine.Update() → CheckWallExit() → jump.OnPlayerUpdate()
```

当玩家从高处落下经过墙壁时：
1. `isTouchingWall` 被 `CheckWall()` 设为 true（哪怕只蹭到一帧）
2. `TryEnterWallState()` 检测到贴墙+朝墙推+wHeld → 进入 WallSlideState
3. `WallSlideStateBase.OnUpdate()` 中调用 `Input.GetKeyDown(KeyCode.Space)` → 进入 WallJumpState
4. WallJumpState.OnEnter() 立即 `ChangeState(null)` 退出
5. 到了 `jump.OnPlayerUpdate()` 时，Space 已被"消费"但墙状态机已经退出
6. `jumpsLeft > 0` 仍为 true，但 `GetKeyDown` 在同一帧内仍然返回 true（Unity 行为：同帧内所有调用都返回 true）
7. **结果：一帧内同时触发墙跳 + 空中跳，产生双跳 Bug，或墙跳覆盖了正常跳**

**根因：`isTouchingWall` 的粘滞逻辑。**

```csharp
// CheckWall() 当前逻辑：
if (footHit && headHit)      { isTouchingWall = true;  wallDirection = facing; }
else if (isTouchingWall && (footHit || headHit)) { }  // 保持 true！
else                         { isTouchingWall = false; wallDirection = 0; }
```

一旦贴墙，只要还有一条射线命中就不清除。这导致玩家离开墙壁后 `isTouchingWall` 仍为 true 若干帧，墙状态机反复进出，Space 被反复拦截。

**其他次级问题：**
- `wasGrounded` 初始化在 `Awake()` 中未设置，依赖 `bool` 默认值 `false`，逻辑不明确
- 空中跳力度计算在 `jumpsLeft--` 之后判断，逻辑绕
- `ExecuteJump` 只在 `rb.velocity.y < 0` 时清零 Y 速度（基类是总是清零），上升中按跳会叠加动量，跳跃高度不一致

### 0.2 爬墙系统："逻辑完全不对"

**根因 1：状态机与外部检测的职责混乱。**

`PlayerController` 同时做了三件事：
- `DetectWallEntry()` — 决定是否进入墙状态（外部判断）
- `WallStateMachine.Update()` — 驱动当前状态（内部判断）
- `CheckWallExit()` — 强制退出墙状态（外部判断）

这三者在同一帧内顺序执行，内部状态切换 + 外部强制退出的组合导致不可预测的行为。

**根因 2：WallSlideStateBase.OnUpdate() 内直接检测 Space 输入。**

所有下滑状态的基类 `OnUpdate()` 里直接调 `Input.GetKeyDown(KeyCode.Space)`，导致：
- 输入被墙状态"拦截"，跳跃模块收不到
- Space→WallJump→ChangeState(null) → 同帧 `jump.OnPlayerUpdate` 也看到 Space，重复执行

**根因 3：WallJumpState 和 WallVaultState 在 OnEnter() 内立即 ChangeState(null)。**

这意味着这些状态的生命周期只有一帧。如果进入 WallJumpState 时 `WallStateMachine.Update()` 返回后 `CheckWallExit()` 还没执行，逻辑上是通的；但如果状态机内部又有 ShorWallVault 触发的 Vault，时序就更复杂。

**根因 4：ShortWallVault 有独立的去重标记 `vaultTriggered`。**

这个标记的清除条件 (`!footHit && !headHit`) 与 `CheckWall()` 的粘滞逻辑不一致，使用 `RaycastWallDual()` 重新做射线，相当于维护了两套墙检测逻辑。

**根因 5：翻顶时用 `rb.position = vaultTarget` 瞬移。**

这可能与物理引擎冲突（Rigidbody2D 的位置被代码覆盖），尤其是当瞬移位置恰好在地面以下时，下一帧会被碰撞推出。

---

## 1. 设计原则（铁律）

| # | 原则 | 说明 |
|---|------|------|
| 1 | **单一输入消费者** | Space/WASD 每帧只能被一个系统处理，优先级：墙状态机 > 跳跃系统 |
| 2 | **状态机自主管理** | 墙状态机内部自行判断进入/退出，外部不强制干涉 |
| 3 | **地面检测即真理** | 所有系统的 grounded 判断来自同一数据源（CharacterBase.grounded），不各自做检测 |
| 4 | **墙检测不粘滞** | 贴墙状态严格由当前帧射线结果决定，不留历史态（除非在墙状态机内部需要） |
| 5 | **跳跃次数独立追踪** | 二段跳剩余次数由跳跃系统独占管理，不与墙跳、冲刺等系统耦合 |
| 6 | **简单优先** | 不做 coyote time、input buffer、pre-jump 等花活。逻辑直白，每帧可追踪 |

---

## 2. 二段跳系统

### 2.1 核心概念

**跳跃是一个"消耗资源"的动作，而不是"状态"。**

玩家拥有一个整数资源 `jumpsRemaining`：
- 站在地面上时 = 2
- 每按一次 Space 执行一次跳跃，-1
- 减到 0 后按 Space 无效
- 落地时重置为 2

### 2.2 精确规则

#### 规则 1：跳跃消耗

```
IF Input.GetKeyDown(KeyCode.Space)   // 按下空格
   AND jumpsRemaining > 0            // 还有剩余次数
   AND 不在墙状态机中                 // 墙系统优先
   AND 不在冲刺中                     // Dash 优先
THEN
   jumpsRemaining -= 1
   执行跳跃（力度 = jumpForce）
```

#### 规则 2：落地重置

```
IF grounded == true                  // 当前帧在地面
   AND wasGrounded == false          // 上一帧不在
THEN
   jumpsRemaining = maxJumps (2)
```

> `wasGrounded` 是跳跃模块自己维护的上帧地面状态，不是 CharacterBase 的 grounded 历史。

#### 规则 3：走到边缘掉下去

```
走到悬崖边 → 脚离开地面 → grounded 变 false
此时 jumpsRemaining 保持为 2（因为从未按过 Space）
玩家在空中可以跳 2 次
```

> 不区分"跳离地面"和"走出地面"，两种都是离地。jumpsRemaining 只有按 Space 才会减少。

#### 规则 4：空中二段跳

```
玩家第一跳（地面起跳后用掉 1 次）→ jumpsRemaining = 1
在空中再按 Space → 第二跳 → jumpsRemaining = 0
```

#### 规则 5：力度统一（简化）

```
所有跳跃使用同一个 jumpForce，不区分地面跳/空中跳
移除 airJumpMultiplier
```

> 如后续需要空中跳力度差异，加回参数即可，但当前保持简单。

### 2.3 状态流转

```
┌──────────────────────────────────────────────────────────┐
│                                                          │
│   游戏开始                                                │
│   jumpsRemaining = 2                                     │
│   wasGrounded = false（将在地面第一帧触发重置）             │
│       │                                                  │
│       ▼                                                  │
│   ┌─────────┐   走到边缘 / 按 Space 起跳    ┌─────────┐  │
│   │  地面   │ ──────────────────────────→  │  空中   │  │
│   │ jumps=2 │                              │ jumps=N │  │
│   └─────────┘                              └────┬────┘  │
│       ▲          落地 (grounded && !wasGrounded)  │      │
│       │                                          │      │
│       │          按 Space（jumpsRemaining > 0）    │      │
│       │          jumpsRemaining -= 1              │      │
│       │          ExecuteJump(jumpForce)           │      │
│       │                                          │      │
│       │          按 Space（jumpsRemaining == 0）   │      │
│       │          → 忽略，什么都不做                  │      │
│       │                                          │      │
│       └──────────────────────────────────────────┘      │
│                   落地 (grounded && !wasGrounded)         │
│                                                          │
└──────────────────────────────────────────────────────────┘
```

### 2.4 伪代码

```csharp
public class PlayerJump : MonoBehaviour
{
    [Header("跳跃配置")]
    [SerializeField] private int maxJumps = 2;

    // ── 运行时状态 ──
    private int jumpsRemaining;
    private bool wasGrounded;
    private PlayerController owner;

    // ============================================================
    // 公开接口（供墙状态机等外部系统使用）
    // ============================================================

    /// <summary>剩余跳跃次数（只读）</summary>
    public int JumpsRemaining => jumpsRemaining;

    /// <summary>重置跳跃次数到最大值（供墙跳等外部系统调用）</summary>
    public void ResetJumps()
    {
        jumpsRemaining = maxJumps;
    }

    /// <summary>消耗一次跳跃（供墙跳等外部系统调用，走统一的跳物理）</summary>
    public bool TryConsumeJump()
    {
        if (jumpsRemaining <= 0) return false;
        jumpsRemaining--;
        return true;
    }

    // ============================================================
    // 生命周期
    // ============================================================

    void Awake()
    {
        jumpsRemaining = maxJumps;
        wasGrounded = false; // 显式初始化，不依赖 bool 默认值
    }

    /// <summary>
    /// 每帧由 PlayerController.OnUpdate 调用。
    /// 调用时机：WallStateMachine.Update() 之后。
    /// 墙状态机活跃时，本方法提前 return，不处理输入。
    /// </summary>
    public void OnPlayerUpdate(PlayerController pc)
    {
        owner = pc;
        bool grounded = owner.IsGrounded();

        // ── 墙状态机活跃 → 跳跃输入由墙系统处理，这里跳过 ──
        if (owner.WallStateMachine.CurrentState != null)
        {
            wasGrounded = grounded;
            return;
        }

        // ── 冲刺中 → 跳过 ──
        if (owner.IsDashing())
        {
            wasGrounded = grounded;
            return;
        }

        // ── FreezeTimer 冻结中 → 跳过（翻顶/墙跳后短暂禁用输入）──
        if (owner.FreezeTimer > 0f)
        {
            wasGrounded = grounded;
            return;
        }

        // ── 1. 落地重置（边缘检测：false→true 的那一帧）──
        if (grounded && !wasGrounded)
        {
            jumpsRemaining = maxJumps;
        }

        // ── 2. 跳跃输入 ──
        if (Input.GetKeyDown(KeyCode.Space) && jumpsRemaining > 0)
        {
            jumpsRemaining--;
            owner.ExecuteJump(owner.JumpForce);
        }

        // ── 3. 记录本帧地面状态 ──
        wasGrounded = grounded;
    }
}
```

### 2.5 关键设计决策

| 决策 | 理由 |
|------|------|
| 不区分地面跳/空中跳力度 | 减少参数，简化调试。空中跳自然比地面跳高度低（起始 Y 速度可能为负/零，地面跳起始 Y=0） |
| 走到边缘不消耗次数 | 最常见的 2D 平台跳跃惯例，对玩家友好 |
| `wasGrounded` 在模块内自维护 | 不依赖外部系统的时序，独立可测 |
| 墙状态活跃时 return | 彻底解决输入被双重消费的 Bug |
| `FreezeTimer` 期间也 return | 防止翻顶/墙跳后同帧触发二段跳 |

---

## 3. 爬墙系统

### 3.1 核心概念

**墙状态机是一个独立的状态机，完全自治。**

- 进入：外部在特定条件下"推送"进入初始状态（Slide 或 Vault）
- 运行：状态机内部根据输入 + 物理条件自主切换
- 退出：状态机内部判断何时退出到 idle（ChangeState(null)）
- 外部不再有 `CheckWallExit()` 这样的强制退出逻辑

### 3.2 状态机总览

```
                    ┌──────────────┐
                    │    Idle      │  (WallStateMachine.CurrentState == null)
                    │   (非墙态)    │
                    └──────┬───────┘
                           │
            ┌──────────────┼──────────────┐
            │ 贴墙+朝墙推    │ 短墙(脚命中)   │
            │ + 不按Space   │ + 头未命中     │
            ▼              │               ▼
    ┌──────────────┐      │      ┌──────────────┐
    │ WallSlide    │◄─────┘      │ WallVault    │
    │  贴墙下滑      │             │  翻顶         │
    └──┬───┬───┬───┘             └──────────────┘
       │   │   │                   瞬移+冻结 → idle
       │   │   │
       │   │   └── Space ──→ WallJump ──→ idle (瞬时)
       │   │
       │   └── 按S ──→ WallFastSlide
       │       松S ──→ 回 WallSlide
       │
       └── 长按W(holdTime) ──→ WallClimb
             松W ──→ 回 WallSlide
             头顶无墙+空间够 ──→ WallVault ──→ idle
```

### 3.3 各状态详细规范

#### 3.3.1 WallSlide（贴墙下滑）

| 属性 | 值 |
|------|-----|
| **进入条件** | `isTouchingWall==true && !grounded && 玩家朝墙方向推摇杆 && 没按Space` |
| **退出条件** | `!isTouchingWall` 或 `grounded` → idle；按S → FastSlide；长按W超过holdTime → Climb；按Space → WallJump |
| **物理行为** | Y速度钳制：`Mathf.Max(rb.velocity.y, -wallSlideSpeed)`；阻止朝墙方向水平移动（X=0）；允许反向离开墙壁 |
| **输入** | W: 开始计时；S: 转FastSlide；Space: 转WallJump；左右: 反向可离开墙 |

> 注意：`!isTouchingWall` 的判定必须实时（当前帧射线），不能用粘滞逻辑。如果脚+头两条射线都不命中，立刻认为离开墙壁。

#### 3.3.2 WallFastSlide（加速下滑）

| 属性 | 值 |
|------|-----|
| **进入条件** | WallSlide 中按 S |
| **退出条件** | `!isTouchingWall` 或 `grounded` → idle；松S（且未按W）→ 回WallSlide；长按W超过holdTime → Climb；按Space → WallJump |
| **物理行为** | Y速度钳制：`Mathf.Max(rb.velocity.y, -wallSlideSpeed * fastSlideMultiplier)` |
| **输入** | 与 WallSlide 相同，但不因 yVel>0 退出 |

> 加速下滑是"快速下落"的主动操作，即使物理反弹导致 Y 速度变正也不退出，只有玩家松手或离开墙才退出。

#### 3.3.3 WallClimb（上爬）

| 属性 | 值 |
|------|-----|
| **进入条件** | Slide/FastSlide 中长按W超过 `wallClimbHoldTime`（默认1秒）|
| **退出条件** | `!isTouchingWall` → idle；松W → 回WallSlide；翻顶条件满足 → Vault |
| **物理行为** | `SetVelocity(y: wallClimbSpeed)`（匀速上爬） |
| **翻顶判定** | 每帧调用 `CheckWallTop()` + `CanVault()`：头顶上方射线不再命中墙 AND 翻顶目标位置无障碍 → 进入Vault |

#### 3.3.4 WallVault（翻顶）

| 属性 | 值 |
|------|-----|
| **进入条件** | 短墙翻顶：`!isTouchingWall && !grounded && 脚射线命中 && 头射线未命中`（矮墙自动翻）；上爬翻顶：Climb中`!CheckWallTop() && CanVault()` |
| **退出条件** | OnEnter 执行瞬移后立即 ChangeState(null) |
| **行为** | `rb.position = 计算好的翻顶目标位置`；设置 `FreezeTimer = 0.15f`；立刻退出到 idle |
| **翻顶目标位置** | `transform.position + Vector2.up * vaultUpOffset + Vector2.right * wallDirection * vaultForwardOffset` |

> 短墙翻顶（矮墙自动翻）只在 `WallStateMachine.CurrentState == null` 时检测（即不在任何墙状态中）。如果已在 Slide/Climb 中，走 Climb→Vault 路径。

#### 3.3.5 WallJump（墙跳）

| 属性 | 值 |
|------|-----|
| **进入条件** | Slide/FastSlide/Climb 中按 Space |
| **退出条件** | OnEnter 执行跳跃后立即 ChangeState(null) |
| **行为** | 根据水平输入判定方向：<br>• 朝墙推（输入方向==墙方向）→ "远离墙弹出"（小水平力 + 垂直力，弧线离开）<br>• 拉反向（输入方向!=墙方向）→ "登墙跳推离"（大水平力 + 垂直力，强力弹开）<br>• 无水平输入 → "远离墙弹出" |
| **跳跃次数** | 墙跳**不消耗**二段跳次数。玩家墙跳后在空中仍有完整的 `jumpsRemaining` 可用。 |
| **执行方式** | 直接操作 `SetVelocity(x, y:0)` + `AddForce(y, Impulse)`，不经过 `PlayerJump` |

### 3.4 墙检测改进

**核心改动：取消粘滞逻辑。**

```csharp
// 新 CheckWall() 逻辑：
protected virtual void CheckWall()
{
    Vector2 footOrigin = (Vector2)transform.position + Vector2.up * wallCheckFootHeight;
    Vector2 headOrigin = (Vector2)transform.position + Vector2.up * wallCheckHeadHeight;
    Vector2 dir = Vector2.right * facing;

    bool footHit = Physics2D.Raycast(footOrigin, dir, wallCheckDistance, wallLayer);
    bool headHit = Physics2D.Raycast(headOrigin, dir, wallCheckDistance, wallLayer);

    // ★ 仅当脚+头都命中时才认为贴墙（严格判定，不粘滞）
    if (footHit && headHit)
    {
        isTouchingWall = true;
        wallDirection = facing;
    }
    else
    {
        isTouchingWall = false;
        wallDirection = 0;
    }
}
```

> 改为仅 `footHit && headHit` 才算贴墙，单条射线命中不算。这样离开墙壁时立刻清除标志，避免旧版的粘滞问题。

### 3.5 墙状态机伪代码

#### 3.5.1 入口检测（在 PlayerController 中）

```csharp
/// <summary>
/// 墙状态入口检测。仅在 WallStateMachine 空闲时运行。
/// 优先级：短墙翻顶 > 贴墙下滑
/// </summary>
private void DetectWallEntry()
{
    // 已在墙状态中 → 不重复进入
    if (WallStateMachine.CurrentState != null) return;

    // 在地面 → 不需要墙状态
    if (grounded) return;

    // ── 1. 短墙翻顶检测（脚命中+头未命中=矮墙）──
    if (!isTouchingWall && FreezeTimer <= 0f)
    {
        // 用独立射线检测（不依赖 isTouchingWall）
        var (footHit, headHit) = RaycastWallDual();
        if (footHit && !headHit)
        {
            WallStateMachine.ChangeState(WallVaultState);
            return;
        }
    }

    // ── 2. 贴墙 + 朝墙推 → 进入 Slide ──
    if (isTouchingWall)
    {
        float h = Input.GetAxisRaw("Horizontal");
        if (Mathf.Abs(h) > 0.1f && Mathf.Sign(h) == wallDirection)
        {
            // 朝墙推：进入 Slide（不在这里判断 Space，由 Slide 内部处理）
            WallStateMachine.ChangeState(WallSlideState);
        }
    }
}
```

#### 3.5.2 WallSlideStateBase（下滑基类）

```csharp
public abstract class WallSlideStateBase : IState
{
    protected readonly CharacterBase player;
    protected readonly StateMachine stateMachine;
    protected readonly Rigidbody2D rb;
    protected float climbHoldTimer;

    protected abstract float MaxSpeed { get; }

    public void OnEnter()
    {
        climbHoldTimer = 0f;
        // 进入下滑时把 Y 速度压低到 MaxSpeed，避免从高处落下贴墙时直接穿透
        if (rb.velocity.y < MaxSpeed)
            player.SetVelocityPublic(y: MaxSpeed);
    }

    public void OnUpdate()
    {
        // ── 退出条件 ──
        if (!player.IsTouchingWall || player.IsGrounded)
        {
            stateMachine.ChangeState(null);
            return;
        }

        // ── 输入 ──
        float inputV = Input.GetAxisRaw("Vertical");
        bool wHeld = Input.GetKey(KeyCode.W);
        bool sHeld = Input.GetKey(KeyCode.S);
        bool jumpDown = Input.GetKeyDown(KeyCode.Space);

        // W 按住计时 → Climb
        if (wHeld)
        {
            climbHoldTimer += Time.deltaTime;
            if (climbHoldTimer >= player.WallClimbHoldTime)
            {
                TransitionTo(WallClimbState);
                return;
            }
        }
        else
        {
            climbHoldTimer = 0f;
        }

        // S → FastSlide（子类覆写）
        if (OnSlideInput(inputV, sHeld))
            return;

        // Space → WallJump
        if (jumpDown)
        {
            TransitionTo(WallJumpState);
            return;
        }

        // ── 子类允许的 Y 速退出条件 ──
        if (ShouldExitFromVelocity() && rb.velocity.y > 0f)
        {
            stateMachine.ChangeState(null);
            return;
        }

        // ── 物理钳制 ──
        float clampedY = Mathf.Max(rb.velocity.y, MaxSpeed);
        float h = Input.GetAxisRaw("Horizontal");
        if (Mathf.Sign(h) == player.WallDirection)
            player.SetVelocityPublic(x: 0f, y: clampedY);
        else
            player.SetVelocityPublic(y: clampedY);
    }

    public void OnExit()
    {
        climbHoldTimer = 0f;
    }

    /// <summary>状态切换辅助（避免 PlayerController 类型转换散布各处）</summary>
    protected void TransitionTo(IState targetState)
    {
        stateMachine.ChangeState(targetState);
    }

    protected virtual bool ShouldExitFromVelocity() => true;
    protected virtual bool OnSlideInput(float inputV, bool sHeld) => false;
}
```

#### 3.5.3 WallClimbState

```csharp
public class WallClimbState : IState
{
    private readonly CharacterBase player;
    private readonly StateMachine stateMachine;
    private readonly Rigidbody2D rb;

    public void OnEnter()
    {
        // 进入上爬时先清零 Y 速度，避免下滑惯性
        player.SetVelocityPublic(y: 0f);
    }

    public void OnUpdate()
    {
        // ── 退出条件：离开墙 → idle ──
        if (!player.IsTouchingWall)
        {
            stateMachine.ChangeState(null);
            return;
        }

        // ── 松 W → 回 Slide ──
        if (!Input.GetKey(KeyCode.W))
        {
            TransitionTo(WallSlideState);
            return;
        }

        // ── Space → WallJump ──
        if (Input.GetKeyDown(KeyCode.Space))
        {
            TransitionTo(WallJumpState);
            return;
        }

        // ── 上爬物理 ──
        player.SetVelocityPublic(y: player.WallClimbSpeed);

        // ── 翻顶检测 ──
        if (!player.CheckWallTop() && player.CanVault())
        {
            TransitionTo(WallVaultState);
        }
    }

    public void OnExit() { }
}
```

#### 3.5.4 WallVaultState

```csharp
public class WallVaultState : IState
{
    private readonly CharacterBase player;
    private readonly StateMachine stateMachine;
    private readonly Rigidbody2D rb;

    public void OnEnter()
    {
        Vector2 vaultTarget = (Vector2)player.transform.position
                            + Vector2.up * player.VaultUpOffset
                            + Vector2.right * player.WallDirection * player.VaultForwardOffset;

        // 瞬移到翻顶目标位置
        rb.position = vaultTarget;

        // 冻结输入
        var pc = player as PlayerController;
        if (pc != null)
            pc.FreezeTimer = 0.15f;

        // 退出到 idle
        stateMachine.ChangeState(null);
    }

    public void OnUpdate() { }
    public void OnExit() { }
}
```

#### 3.5.5 WallJumpState

```csharp
public class WallJumpState : IState
{
    private readonly CharacterBase player;
    private readonly StateMachine stateMachine;
    private readonly Rigidbody2D rb;

    public void OnEnter()
    {
        var pc = player as PlayerController;
        if (pc == null) return;

        float inputH = Input.GetAxisRaw("Horizontal");
        float forceX, forceY;

        // 判定方向
        if (Mathf.Abs(inputH) > 0.1f && Mathf.Sign(inputH) != player.WallDirection)
        {
            // 拉反向 → 登墙跳（强力推离）
            forceX = -player.WallDirection * pc.WallJumpPushForceX;
            forceY = pc.WallJumpPushForceY;
        }
        else
        {
            // 不按 或 朝墙推 → 远离墙弹出（弧线离开）
            forceX = -player.WallDirection * pc.WallJumpAwayForceX;
            forceY = pc.WallJumpAwayForceY;
        }

        // 执行跳跃（直接操作物理，不经过 PlayerJump）
        player.SetVelocityPublic(x: forceX, y: 0f);
        rb.AddForce(Vector2.up * forceY, ForceMode2D.Impulse);

        // ★ 墙跳不消耗二段跳次数
        // 如需"墙跳重置二段跳"，在此调用 playerJump.ResetJumps()

        // 冻结 + 退出
        pc.FreezeTimer = 0.1f;
        stateMachine.ChangeState(null);
    }

    public void OnUpdate() { }
    public void OnExit() { }
}
```

---

## 4. 跳跃与爬墙的交互规则

### 4.1 输入优先级

```
每帧 Space 输入的处理顺序：

1. Dash 中？         → 忽略 Space（Dash 先 return）
2. FreezeTimer > 0？  → 忽略 Space
3. 墙状态机活跃？     → 由墙状态机处理（Slide→WallJump, Climb→WallJump）
4. 否则              → 由 PlayerJump 处理（二段跳）
```

**关键：Step 3 和 Step 4 互斥。** 墙状态活跃时 Space 不会传到 PlayerJump，反之亦然。

### 4.2 墙跳后的跳跃次数

| 场景 | behavior |
|------|----------|
| 地面起跳（-1次）→ 贴墙 Slide → 墙跳 | 墙跳后 `jumpsRemaining = 1`（空中还剩 1 跳） |
| 空中二段跳（-1次）→ 贴墙 Slide → 墙跳 | 墙跳后 `jumpsRemaining = 0`（已无跳可用） |
| 走到边缘掉下 → 贴墙 Slide → 墙跳 | 墙跳后 `jumpsRemaining = 2`（完整二段跳可用） |

> 墙跳不消耗二段跳次数。这是最常见的设计惯例，让墙跳成为"免费"的机动动作。

### 4.3 爬墙中按 Space 的行为

| 当前状态 | 按 Space 的行为 |
|----------|----------------|
| WallSlide | 进入 WallJumpState → 执行墙跳 → 退出到 idle |
| WallFastSlide | 同上 |
| WallClimb | 进入 WallJumpState → 执行墙跳 → 退出到 idle |

> 爬墙状态下 Space 永远触发墙跳，不触发普通跳。

### 4.4 翻顶后的跳跃次数

| 场景 | behavior |
|------|----------|
| 地面起跳 → 短墙自动翻顶 | `jumpsRemaining` 保持翻顶前的值（翻顶不消耗次数） |
| Climb 上爬 → 翻顶 | 同上 |

> 翻顶是自由动作，不消耗也不重置跳跃次数。

### 4.5 落地后墙状态自动退出

落地时（`grounded == true`），所有墙状态都在 `OnUpdate` 中检测到 `player.IsGrounded` 立即 `ChangeState(null)`。

跳跃模块在同一帧检测到 `grounded && !wasGrounded`，重置 `jumpsRemaining = 2`。

---

## 5. 与现有系统的集成点

### 5.1 数据流（一帧内的完整时序）

```
CharacterBase.Update()
│
├── HandleGroundCheck()
│   └── grounded = Raycast(col.bounds.center, down, groundCheckDist, groundLayer)
│
├── CheckWall()  (if enableWallDetection)
│   └── isTouchingWall = footHit && headHit  // ★ 新逻辑：双射线都命中才算
│
└── OnUpdate() → PlayerController.OnUpdate()
    │
    ├── UpdateCooldowns()
    │   └── FreezeTimer -= dt
    │
    ├── dash.OnPlayerUpdate(this)
    │   └── 如果在Dash中 → return（跳过后续所有）
    │
    ├── DetectWallEntry()           // ★ 重写：只在 CurrentState==null 时运行
    │   ├── CheckShortWallVault()   //   短墙 → Vault
    │   └── TryEnterWallState()     //   贴墙+朝墙推 → Slide
    │
    ├── WallStateMachine.Update()   // ★ 驱动墙状态，内部自主切换
    │   └── (不再有外部 CheckWallExit)
    │
    ├── jump.OnPlayerUpdate(this)   // ★ 墙状态活跃时 return，不处理输入
    │   ├── 落地重置 (grounded && !wasGrounded)
    │   ├── 按 Space → ExecuteJump
    │   └── wasGrounded = grounded
    │
    ├── health.OnPlayerUpdate(this)
    │
    └── UpdateSubModules()
        ├── combat.OnPlayerUpdate()
        ├── groundPound.OnPlayerUpdate()
        └── skillManager.OnPlayerUpdate()

CharacterBase.FixedUpdate()
│
└── OnFixedUpdate() → PlayerController.OnFixedUpdate()
    ├── Dash中？ → return
    ├── FreezeTimer > 0？ → return
    ├── 贴墙+朝墙推？ → 阻止水平移动（h = 0）
    ├── 墙状态活跃？ → return（物理由状态类自己处理）
    └── 普通移动（h * moveSpeed）
```

### 5.2 需要修改的文件清单

| 文件 | 改动内容 |
|------|---------|
| `CharacterBase.cs` | `CheckWall()` 改为 `footHit && headHit`（取消粘滞） |
| `PlayerController.cs` | 删除 `CheckWallExit()`；重写 `DetectWallEntry()`；删除 `vaultTriggered` 字段；移除 `WallStateMachine.Update()` 前后的退出检测 |
| `PlayerJump.cs` | 完全重写（见 §2.4 伪代码） |
| `WallSlideStateBase.cs` | 小幅重构：统一 `TransitionTo()` 辅助方法；在 `OnEnter` 中钳制初始 Y 速度 |
| `WallSlideState.cs` | 微调：配合父类改动 |
| `WallFastSlideState.cs` | 微调 |
| `WallClimbState.cs` | 增加 Space→WallJump；在 `OnEnter` 中清零 Y 速度 |
| `WallVaultState.cs` | 微调 |
| `WallJumpState.cs` | 微调 |

### 5.3 CharacterBase 接口使用一览

| 接口 | 调用方 | 用途 |
|------|--------|------|
| `IsGrounded` | PlayerJump, 所有 WallState | 地面判定 |
| `IsTouchingWall` | PlayerController, 所有 WallState | 墙接触判定 |
| `WallDirection` | PlayerController, 所有 WallState | 墙在哪个方向 |
| `SetVelocityPublic(x, y)` | 所有 WallState, WallJumpState | 速度钳制/设置 |
| `Rb` (Rigidbody2D) | WallJumpState, WallVaultState | 直接加力/瞬移 |
| `ExecuteJump(force)` | PlayerJump | 执行普通跳跃 |
| `WallSlideSpeed` | WallSlideState | 下滑速度参数 |
| `WallFastSlideMultiplier` | WallFastSlideState | 加速倍率 |
| `WallClimbSpeed` | WallClimbState | 上爬速度 |
| `WallClimbHoldTime` | WallSlideStateBase | W 长按时间阈值 |
| `CheckWallTop()` | WallClimbState | 翻顶检测 |
| `CanVault()` | WallClimbState | 翻顶空间验证 |
| `VaultUpOffset` / `VaultForwardOffset` | WallVaultState | 翻顶目标计算 |
| `JumpForce` | PlayerJump | 跳跃力度 |

### 5.4 PlayerController 新增/修改的公开接口

| 接口 | 用途 |
|------|------|
| `ExecuteJump(float force)` | PlayerJump 调用，执行跳跃物理。保留基类 `Jump` 的覆写（下落时清零Y速度） |
| `WallStateMachine` (StateMachine) | 墙状态机实例 |
| `WallSlideState` / `WallFastSlideState` / `WallClimbState` / `WallVaultState` / `WallJumpState` | 各状态实例引用，供内部 TransitionTo 使用 |
| `FreezeTimer` | 翻顶/墙跳后冻结输入的计时器 |
| `IsDashing()` | PlayerJump 判断是否在冲刺中 |

---

## 6. 调试与验证

### 6.1 单位测试场景

| # | 场景 | 预期结果 |
|---|------|---------|
| 1 | 地面按 Space | 起跳，jumpsRemaining=1 |
| 2 | 地面按 Space → 空中再按 Space | 二段跳，jumpsRemaining=0 |
| 3 | 地面按 Space → 空中再按 Space → 再按 Space | 第三下无反应 |
| 4 | 走到悬崖边缘掉下去 → 空中按 Space | 起跳，jumpsRemaining=1（还可再跳1次） |
| 5 | 走到悬崖边缘掉下去 → 空中按 Space → 再按 Space | 二次跳跃正常 |
| 6 | 场景5后落地 | jumpsRemaining重置为2 |
| 7 | 空中二段跳后落地 | jumpsRemaining重置为2 |
| 8 | 跳起贴墙+朝墙推 | 进入WallSlide，沿墙下滑 |
| 9 | 场景8中按 Space | 执行墙跳，离开墙壁 |
| 10 | 场景9后在空中再按 Space | 二段跳正常（墙跳不消耗次数） |
| 11 | 贴墙下滑+按S | 加速下滑 |
| 12 | 加速下滑+松S | 回到普通下滑 |
| 13 | 贴墙下滑+长按W（1秒） | 进入上爬 |
| 14 | 上爬+松W | 回到下滑 |
| 15 | 上爬到墙顶 | 自动翻顶，落在平台上方 |
| 16 | 跑步撞上矮墙（低于头顶） | 自动翻顶 |
| 17 | 场景16后 | jumpsRemaining保持原值不变 |
| 18 | 墙跳后立刻落地 | 落地帧重置jumpsRemaining=2 |

### 6.2 Gizmos 调试建议

启用 `CharacterBase` 的 Gizmos（选中 GameObject 即可）：
- 绿色射线：地面检测（确认 groundCheckDist 足够长）
- 黄色射线×2：墙检测（确认射线确实碰到墙面 Layer）
- 品红色射线：翻顶检测

### 6.3 Console 调试日志（建议增加）

```csharp
// 在 PlayerJump.OnPlayerUpdate 中：
Debug.Log($"[Jump] grounded={grounded} wasGrounded={wasGrounded} jumpsLeft={jumpsRemaining} wallState={owner.WallStateMachine.CurrentState?.GetType().Name ?? "null"}");

// 在 WallSlideStateBase.OnUpdate 中：
Debug.Log($"[WallSlide] touchingWall={player.IsTouchingWall} grounded={player.IsGrounded} yVel={rb.velocity.y:F2} climbTimer={climbHoldTimer:F2}");

// 在 DetectWallEntry 中：
Debug.Log($"[WallEntry] isTouchingWall={isTouchingWall} wallDir={wallDirection} h={Input.GetAxisRaw("Horizontal")} state={WallStateMachine.CurrentState?.GetType().Name ?? "null"}");
```

---

## 7. 参数建议值

| 参数 | 建议值 | 说明 |
|------|--------|------|
| `maxJumps` | 2 | 二段跳 |
| `jumpForce` | 7 | 需配合 Rigidbody2D.gravityScale 调整 |
| `wallSlideSpeed` | 2 | 贴墙下滑最大速度 |
| `wallFastSlideMultiplier` | 2.0 | 加速下滑倍率 |
| `wallClimbSpeed` | 1.0 | 上爬速度（建议慢于下滑，体现攀爬费力） |
| `wallClimbHoldTime` | 1.0 | W 按住多久开始上爬（秒） |
| `wallCheckDistance` | 0.5 | 墙检测射线长度 |
| `wallCheckFootHeight` | 0.1 | 脚部射线距脚底高度 |
| `wallCheckHeadHeight` | 1.5 | 头部射线距脚底高度 |
| `wallJumpAwayForceX` | 4 | 远离墙弹出：水平力 |
| `wallJumpAwayForceY` | 10 | 远离墙弹出：垂直力 |
| `wallJumpPushForceX` | 8 | 登墙跳推离：水平力 |
| `wallJumpPushForceY` | 12 | 登墙跳推离：垂直力 |
| `vaultUpOffset` | 2.0 | 翻顶垂直位移 |
| `vaultForwardOffset` | 0.5 | 翻顶水平位移 |
| `groundCheckDist` | 需根据碰撞体高度调整 | 从 col.bounds.center 到脚底 + 一点冗余 |
| `FreezeTimer` (Vault) | 0.15 | 翻顶后冻结输入（秒） |
| `FreezeTimer` (WallJump) | 0.1 | 墙跳后冻结输入（秒） |

---

## 8. 实现顺序建议

1. **先修 `CharacterBase.CheckWall()`** — 去掉粘滞逻辑，改为 `footHit && headHit`
2. **重写 `PlayerJump.cs`** — 按 §2.4 伪代码，先独立跑通二段跳
3. **重写 `PlayerController.DetectWallEntry()`** — 去掉 `CheckWallExit()`，简化为纯入口检测
4. **微调墙状态类** — 按 §3 规范逐个调整
5. **联调测试** — 跑 §6.1 的全部场景

---

## 附录 A：为什么不用 Coyote Time / Input Buffer

这些是提升手感的高级技术，但当前阶段：
- 基础系统还没稳定，加入 buffer 会增加状态空间，更难调试
- 我们的地面检测（边缘检测 `grounded && !wasGrounded`）延迟为 0 帧，响应已经足够灵敏
- 等基础系统全部跑通、手感验证通过后，可以在此基础上加 coyote time（在 `PlayerJump` 中加一个 `coyoteTimer` 即可，不影响其他系统）

## 附录 B：如果"脚蹭到墙顶边缘无法翻顶"怎么办

如果翻顶检测中 `CanVault()` 返回 false（翻顶目标位置有障碍），玩家会卡在墙顶边缘上下抖动。处理方案：

1. **扩大 `vaultForwardOffset`** — 让翻顶目标位置更靠内
2. **在 ClimbState 顶部加一个微小推离力** — 当 `CheckWallTop()` 返回 false 但 `CanVault()` 也返回 false 时，略微推离墙壁让玩家自然落下
3. **降低翻顶目标的空间检测距离**（`ceilingCheckDist` 从 1.0 降到 0.3）
