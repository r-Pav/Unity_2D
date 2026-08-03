# 二段跳系统 · 策划案

> 版本：1.0  
> 项目：2D 横板平台动作  
> 引擎：团结引擎 1.8.4  

---

## 1. 需求

- **按一次空格跳一次**：每次按下 Space 消耗一次跳跃次数，执行一次跳跃。
- **空中可再跳一次**：离地后最多还能跳 1 次（含地面起跳，全程总共 2 次）。
- **落地重置**：双脚真正接触地面时，跳跃次数回到 2。空中不重置。
- **简单可靠**：不要 buffer、coyote time、预输入等花活。逻辑直白，易于调试。

---

## 2. 规则定义

### 2.1 核心参数

| 参数 | 值 | 说明 |
|------|-----|------|
| `maxJumps` | `2` | 每次离地周期内的最大跳跃次数 |
| `jumpsRemaining` | `0..2` | 当前剩余跳跃次数 |
| `wasGrounded` | `bool` | 上一帧是否在地面（用于边缘检测） |

### 2.2 跳跃消耗规则

```
地面状态       按下 Space 的行为
──────────────────────────────────────────
站在地面上     → jumpsRemaining 必须 ≥ 1，执行跳跃，jumpsRemaining -= 1
在空中         → jumpsRemaining 必须 ≥ 1，执行跳跃，jumpsRemaining -= 1
jumpsRemaining = 0 → 忽略输入，不跳跃
```

### 2.3 落地重置规则

> 使用**边缘检测**：`grounded` 从 `false` 变为 `true` 的那一帧触发重置。

```
if (grounded && !wasGrounded)  →  jumpsRemaining = 2
```

- 只在"刚接触地面"那一帧重置，不会在持续站在地面时反复重置（虽然结果一致，但边缘检测语义更清晰）。
- 空中即便 `grounded` 因碰撞抖动短暂为 `true` 再变回 `false`，也只会产生一次重置，不会造成"空中闪烁恢复次数"的漏洞（因为闪烁期间 `grounded` 可能只持续 1 帧，但重置发生在 `true` 的每一帧，如果下一帧又变 `false`，重置已经发生——不过这刚好符合"碰到地面就重置"的物理直觉；若需防闪烁，见 §2.4）。

### 2.4 防地面闪烁（可选增强）

如果物理碰撞导致 `grounded` 在单帧内闪烁（例如高速落地时的穿透/弹跳），可加一层极简保护：

```
if (grounded)
    jumpsRemaining = 2
```

即：只要当前帧 `grounded == true`，直接重置。空中闪烁到 `true` 会重置，但这在物理上等价于"脚蹭到了地面"，重置是合理行为。**不引入帧计数器、不引入缓冲，保持简单。**

建议先使用 §2.3 的边缘检测；如果实际测试中发现闪烁问题，再切换为此方案。

### 2.5 离地规则

**走到悬崖边掉下去（未按跳跃）**：`jumpsRemaining` 保持为 2。玩家在空中有 2 次跳跃机会。

> 设计理由：走到边缘掉下去不算"跳跃"，不消耗次数。玩家在空中仍有完整的二段跳能力。这是最简单的规则，也最宽容。

---

## 3. 状态机 / 参数流转

```
┌─────────────────────────────────────────────────┐
│                  每帧 OnPlayerUpdate              │
│                                                   │
│  1. 读取 owner.IsGrounded()  →  grounded          │
│                                                   │
│  2. 落地检测                                       │
│     if (grounded && !wasGrounded)                  │
│         jumpsRemaining = 2                         │
│                                                   │
│  3. 跳跃输入                                       │
│     if (Input.GetKeyDown(Space)                    │
│         && jumpsRemaining > 0                      │
│         && 不在墙状态机中)                           │
│     {                                              │
│         jumpsRemaining--                           │
│         owner.ExecuteJump(force)                   │
│     }                                              │
│                                                   │
│  4. 记忆状态                                       │
│     wasGrounded = grounded                         │
└─────────────────────────────────────────────────┘
```

**状态转换图：**

```
             落地 (grounded && !wasGrounded)
        ┌──────────────────────────────────────┐
        │                                      │
        ▼                                      │
   ┌─────────┐    按 Space + jumps>0      ┌─────────┐
   │  地面   │ ──────────────────────────→ │  空中   │
   │ jumps=2 │                             │ jumps=1 │
   └─────────┘                             └────┬────┘
        ▲                                       │
        │        按 Space + jumps>0              │
        │    ┌──────────────────────────┐       │
        │    │                          ▼       │
        │    │                     ┌─────────┐  │
        │    │                     │  空中   │  │
        │    │                     │ jumps=0 │  │
        │    │                     └─────────┘  │
        │    │                          │       │
        │    │      走到边缘掉下去       │       │
        │    └──────────────────────────┘       │
        │         (不消耗次数)                   │
        │                                       │
        └───────────────────────────────────────┘
             落地 (grounded && !wasGrounded)
```

---

## 4. 伪代码

```csharp
public class PlayerJump : MonoBehaviour
{
    [SerializeField] private int maxJumps = 2;
    // 移除 airJumpMultiplier —— 简单方案统一力度

    private int jumpsRemaining;
    private bool wasGrounded;
    private PlayerController owner;

    private void Awake()
    {
        jumpsRemaining = maxJumps;
        // wasGrounded 初始值不重要，第一个 grounded 帧会触发边缘检测
    }

    public void OnPlayerUpdate(PlayerController pc)
    {
        owner = pc;
        bool grounded = owner.IsGrounded();

        // ── 墙状态机活跃时跳过（墙跳由 WallJumpState 自行处理）──
        if (owner.WallStateMachine.CurrentState != null && !grounded)
        {
            wasGrounded = grounded;
            return;
        }

        // ── 1. 落地重置 ──
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

        // ── 3. 记忆本帧地面状态 ──
        wasGrounded = grounded;
    }
}
```

---

## 5. 与现有系统的集成点

### 5.1 调用链

```
CharacterBase.Update()
  └→ HandleGroundCheck()          // 设置 grounded
  └→ OnUpdate()                   // → PlayerController.OnUpdate()
       └→ UpdateCooldowns()
       └→ dash.OnPlayerUpdate()   // dash 中直接 return，跳过后续
       └→ DetectWallEntry()
       └→ WallStateMachine.Update()
       └→ CheckWallExit()
       └→ jump.OnPlayerUpdate()   // ★ 二段跳逻辑在这里
       └→ health.OnPlayerUpdate()
       └→ UpdateSubModules()
```

### 5.2 与 CharacterBase 的接口

| 使用的接口 | 来源 | 用途 |
|-----------|------|------|
| `owner.IsGrounded()` | `CharacterBase.grounded` | 每帧读取地面状态 |
| `owner.ExecuteJump(force)` | `PlayerController` | 执行跳跃物理 |
| `owner.JumpForce` | `CharacterBase.jumpForce` | 获取跳跃力度参数 |
| `owner.WallStateMachine.CurrentState` | `PlayerController` | 判断是否在墙状态中 |

### 5.3 与墙跳的交互

- `WallJumpState` 完全独立，直接操作 `SetVelocityPublic` + `AddForce`，**不经过** `PlayerJump`。
- 当墙状态机活跃（slide/climb/vault/jump）时，`PlayerJump.OnPlayerUpdate` 提前 return，不处理输入。
- 墙跳后玩家处于空中，`wasGrounded` 保持 false，下次落地时正常触发重置。
- **墙跳不消耗也不重置二段跳次数**：墙跳是独立系统，跳完后玩家仍保有当前的 `jumpsRemaining`。

### 5.4 与 Dash 的交互

- Dash 在 `OnUpdate` 流水线中先于 `PlayerJump` 执行。Dash 中 `return`，跳跃输入不被处理。
- 这是正确行为：冲刺中不应跳跃。

### 5.5 与 FreezeTimer 的交互

- `FreezeTimer` 在 `OnFixedUpdate` 中冻结水平移动，但不影响 `OnUpdate` 中的跳跃输入检测。
- 如果需要冻结期间也禁止跳跃，可在 `PlayerJump.OnPlayerUpdate` 中增加 `if (owner.FreezeTimer > 0f) return;`。

---

## 6. 边界情况分析

| 场景 | 预期行为 | 实现保证 |
|------|---------|---------|
| 地面按跳 → 空中再按跳 | 两段跳正常执行 | `jumpsRemaining: 2→1→0` |
| 地面按跳 → 空中不按 → 落地 → 再跳 | 落地重置，新一轮 2 跳 | 落地触发 `grounded && !wasGrounded` |
| 连续快速按跳（地面） | 第一下起跳，第二下在空中触发二段跳 | `GetKeyDown` 保证每下只触发一次 |
| 走到边缘掉下去 → 空中按跳 | 有 2 次跳跃可用 | `jumpsRemaining` 不因离地而减少 |
| 空中二段跳后碰到墙进入 slide | slide 结束后仍在空中→无跳跃可用 | `jumpsRemaining=0`，正确 |
| 墙上 slide → 墙跳 → 在空中 | 墙跳不消耗次数，`jumpsRemaining` 保持原值 | 墙跳绕过 `PlayerJump` |
| 起跳后立刻落地（低矮平台） | 落地帧重置为 2 | 边缘检测触发 |
| 地面闪烁（碰撞抖动） | 可能多触发一次重置，但不影响可用次数 | 闪烁到 `true` 时重置为 2，合理 |

---

## 7. 相比旧版 PlayerJump 的改动

| 项目 | 旧版 | 新版 |
|------|------|------|
| 重置方式 | `groundedFrames >= 3`（3 帧延迟） | `grounded && !wasGrounded`（边缘检测，0 帧延迟） |
| 空中跳力度 | `airJumpMultiplier = 0.8` | 移除，统一用 `jumpForce`（简化） |
| 离地检测 | 无（走到边缘掉下去还能跳 2 次吗？未定义） | 明确定义：离地不消耗次数 |
| 防闪烁 | 3 帧计数器 | 边缘检测（可选改用直接 `grounded` 重置） |
| 复杂度 | 3 个状态变量 + 帧计数器 | 2 个状态变量，无线程/协程 |

---

## 8. 待确认事项

1. **空中跳力度**：当前设计统一使用 `jumpForce`。是否需要空中跳力度衰减？如需，可加回 `airJumpMultiplier` 参数，仅影响 `jumpsRemaining == 1` 时的跳跃（即空中跳）。
2. **墙跳后是否重置二段跳**：当前设计墙跳不重置次数。如需"墙跳刷新二段跳"，可由 `WallJumpState.OnEnter` 调用 `PlayerJump` 的公开重置方法。
3. **FreezeTimer 冻结期间**：当前不禁止跳跃输入。如需禁止，一行 `if` 即可。
