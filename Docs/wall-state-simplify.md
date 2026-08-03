# 墙状态机精简方案

> 本方案在上一次重构方案（Docs/墙状态系统重构方案.md）基础上进一步激进精简。
> 上次方案：5状态 → 4状态（合并 Slide+FastSlide，保留 Climb/Jump/Vault 为状态）
> 本次方案：5状态 → 1状态 + 2方法（合并 Slide/FastSlide/Climb，Jump/Vault 退化为方法）

---

## 一、为什么还能进一步精简

基于对8个文件的完整反拆，精简化空间来源于一个关键观察：

### 1.1 Slide / FastSlide / Climb 本质是同一状态

三者共享完全相同的骨架：

| | Slide | FastSlide | Climb |
|---|---|---|---|
| 退出条件 | `!wall \|\| grounded` | `!wall \|\| grounded` | `!wall \|\| grounded` |
| 水平约束 | 朝墙方向=0 | 朝墙方向=0 | 朝墙方向=0 |
| Y轴速度 | `clamp(vy, -slideSpeed)` | `clamp(vy, -slideSpeed * 2)` | 固定 `+climbSpeed` |
| W键行为 | 按住计时→Climb | 按住计时→Climb | 松开→回Slide |
| Space键 | →WallJump | →WallJump | 不响应 |
| S键行为 | S→FastSlide | 松S→回Slide | 不适用 |
| 翻顶检测 | 无 | 无 | 有 |

**三者差异仅为两个参数：** Y轴速度值和当前模式枚举。其余逻辑完全相同。

### 1.2 WallJump / WallVault 不配当状态

两者均满足"瞬时动作"的全部特征：

- `OnEnter` 内调用 `stateMachine.ChangeState(null)` —— 进入即退出
- `OnUpdate` 为空方法体
- `OnExit` 为空方法体
- 不维护任何跨帧状态

**它们本质是带 FreezeTimer + ClearWallContact 副作用的方法调用。** 套上 IState 壳的唯一原因是需要 `player as PlayerController` 来访问 FreezeTimer 和墙跳力参数——这是架构异味，不是合理的设计理由。

### 1.3 折算

| 现状 | 精简后 | 消除的复杂度 |
|---|---|---|
| 5个状态类 + 1个基类 | 1个状态类 | 5个文件消失 |
| 8处 `as PlayerController` cast | 0处 | 消除全部反向依赖 |
| 状态间切换：Slide→FastSlide→Climb→Slide（3条边） | 同一状态内模式切换（无状态机参与） | 减少状态机调用 |
| WallJump/Vault 两个 IState | 两个静态方法 | 消除空壳类 |

---

## 二、精简后状态设计

### 2.1 唯一状态：WallContact

```
WallContact : IState
├── 字段
│   ├── WallMode _mode          // Slide | FastSlide | Climb
│   ├── float _climbHoldTimer   // W键按住累计
│   └── WallSystem _wallSystem  // 注入，消除所有cast
│
├── OnEnter
│   └── _mode = Slide; _climbHoldTimer = 0f
│
├── OnUpdate
│   ├── 1. CommonExit → ChangeState(null); return
│   ├── 2. 翻顶检测（仅 Climb 模式）→ ExecuteVault(); return
│   ├── 3. Space → ExecuteWallJump(); return
│   ├── 4. W键处理（计时/松W切换模式）
│   ├── 5. S键处理（切快滑/慢滑）
│   └── 6. ApplyPhysics（根据 _mode 决定Y速度）
│
└── OnExit
    └── _climbHoldTimer = 0f
```

#### 模式枚举

```csharp
enum WallMode
{
    Slide,      // 慢速下滑，Y = clamp(vy, -slideSpeed)
    FastSlide,  // 快速下滑，Y = clamp(vy, -slideSpeed * multiplier)
    Climb       // 上爬，Y = +climbSpeed
}
```

#### OnUpdate 伪代码

```
OnUpdate():
    // ── 第1层：退出条件（最高优先级）──
    if (!_wallSystem.IsTouchingWall || player.IsGrounded):
        stateMachine.ChangeState(null); return

    // ── 第2层：Climb模式专属——翻顶检测 ──
    if _mode == Climb && _wallSystem.CheckWallTop() == false && _wallSystem.CanVault():
        WallActions.ExecuteVault(player, _wallSystem); return

    // ── 第3层：跳跃（Slide/FastSlide通用，Climb不响应Space）──
    if Input.GetKeyDown(KeyCode.Space) && _mode != Climb:
        float inputH = Input.GetAxisRaw("Horizontal")
        WallActions.ExecuteWallJump(player, _wallSystem, inputH); return

    // ── 第4层：W键模式切换 ──
    bool wHeld = Input.GetKey(KeyCode.W)
    if wHeld:
        _climbHoldTimer += dt
        if _climbHoldTimer >= wallConfig.climbHoldTime:
            _mode = Climb
    else:
        _climbHoldTimer = 0f
        if _mode == Climb:
            _mode = Slide       // 松W→回慢滑

    // ── 第5层：S键速度切换（仅Slide/FastSlide）──
    float inputV = Input.GetAxisRaw("Vertical")
    if _mode == Slide && inputV < -0.1f:
        _mode = FastSlide
    else if _mode == FastSlide && inputV > -0.1f:
        _mode = Slide

    // ── 第6层：物理应用 ──
    float targetY;
    switch _mode:
        case Slide:     targetY = clamp(vy, -slideSpeed); break
        case FastSlide: targetY = clamp(vy, -slideSpeed * fastMultiplier); break
        case Climb:     targetY = +climbSpeed; break

    float h = Input.GetAxisRaw("Horizontal")
    if sign(h) == wallDirection:
        SetVelocity(x=0, y=targetY)         // 朝墙方向阻止水平
    else:
        SetVelocity(y=targetY)              // 远离墙方向不阻止
```

### 2.2 方法：ExecuteWallJump

```csharp
/// <summary>
/// 三方向墙跳。不进入状态，直接施加力后退出墙系统。
/// </summary>
static void ExecuteWallJump(PlayerCharacterBase player, WallSystem ws, float inputH)
{
    float forceX, forceY:
    if abs(inputH) < 0.1f:                // ① 不按方向 → 远离墙弹出
        forceX = -ws.WallDirection * ws.Config.awayForceX
        forceY = ws.Config.awayForceY
    else if sign(inputH) != ws.WallDirection:  // ② 反向 → 登墙跳
        forceX = -ws.WallDirection * ws.Config.pushForceX
        forceY = ws.Config.pushForceY
    else:                                  // ③ 同向 → 正上方跳
        forceX = 0
        forceY = ws.Config.upForce

    player.SetVelocity(x: forceX, y: 0)
    player.Rb.AddForce(Vector2.up * forceY, ForceMode2D.Impulse)
    ws.FreezeTimer = 0.1f
    ws.ClearWallContact()
    // 注意：不调用 stateMachine.ChangeState(null)
    // 因为 ExecuteWallJump 是从 WallContact.OnUpdate 调用的，
    // OnUpdate return 后自然不会再处理物理。
    // 但需要退出 WallContact 状态——由调用者处理。
}
```

调用约定：ExecuteWallJump 被 WallContact.OnUpdate 调用后，调用者负责退出状态（ChangeState(null)），因为跳跃后必然离开墙面。

### 2.3 方法：ExecuteVault

```csharp
/// <summary>
/// 翻顶瞬移。不进入状态，直接移动位置并退出墙系统。
/// </summary>
static void ExecuteVault(PlayerCharacterBase player, WallSystem ws)
{
    Vector2 target = player.transform.position
                   + Vector2.up * ws.Config.vaultUpOffset
                   + Vector2.right * ws.WallDirection * ws.Config.vaultForwardOffset
    player.Rb.position = target
    ws.FreezeTimer = 0.15f
    // 状态退出由调用者（WallContact.OnUpdate）处理
}
```

---

## 三、状态转换图

```
                            ┌──────────────────────────┐
                            │     WallContact           │
                            │                          │
                            │  ┌────────────────────┐  │
              [贴墙+!Space] │  │  Slide              │  │
    None ──────────────────→│  │  │ S键 ──→ FastSlide│  │
                ↑           │  │  │ 松S ──→ Slide    │  │
                │           │  │  │ W计时 ──→ Climb  │  │
                │           │  │  │ 松W ──→ Slide    │  │
                │           │  └────────┬───────────┘  │
                │           │           │              │
                │           │     [Space]              │
                │           └───────────│──────────────┘
                │                       ↓
                │              ExecuteWallJump()
                │                    → None
                │
                │           ┌───────────┐
                │           │  Climb    │
                │           │ 模式内    │
                │           │ 翻顶检测  │
                │           └─────│─────┘
                │                 ↓
                │          ExecuteVault()
                │              → None
                │
                │  (不贴墙或落地)
                └──────────────────

特殊入口（不经过 WallContact）：
    None ──[矮墙: 脚命中+头未命中]──→ ExecuteVault() → None
    None ──[贴墙+Space]─────────────→ ExecuteWallJump() → None
```

**模式切换 vs 状态切换的关键区别：** Slide→FastSlide→Climb 三者之间的切换不触发状态机的 OnEnter/OnExit，只是修改 `_mode` 字段。只有进出 WallContact 本身才涉及状态机。

---

## 四、输入到行为映射表

### 4.1 非墙状态时的入口行为

| 输入条件 | 行为 | 机制 |
|---|---|---|
| `isTouchingWall && !grounded && !Space` | 进入 WallContact(mode=Slide) | 状态机 |
| `isTouchingWall && !grounded && Space` | ExecuteWallJump() | 方法调用 |
| 矮墙检测（脚命中+头未命中） | ExecuteVault() | 方法调用 |
| `!isTouchingWall \|\| grounded` | 无动作 | — |

### 4.2 WallContact 状态内的行为

| 输入 | 当前模式 | 行为 |
|---|---|---|
| Space | Slide / FastSlide | ExecuteWallJump() → 退出状态 |
| W 按住 > holdTime | Slide / FastSlide | `_mode = Climb` |
| S 按下 (< -0.1) | Slide | `_mode = FastSlide` |
| S 松开 (> -0.1) | FastSlide | `_mode = Slide` |
| 松W | Climb | `_mode = Slide` |
| CheckWallTop()==false && CanVault() | Climb | ExecuteVault() → 退出状态 |
| `!isTouchingWall \|\| grounded` | 任意 | ChangeState(null) |
| Space | Climb | **无响应**（保留原有设计——爬墙中不响应跳跃） |

---

## 五、输入处理链简化

### 现状
```
OnUpdate → DetectWallEntry → TryEnterWallState → WallStateMachine.Update → CheckWallExit
              │                    │
              ├─CheckShortWallVault│
              └─RaycastWallDual    └─WallJump或Slide
```

### 精简后
```
OnUpdate → DetectWallEntry → WallStateMachine.Update → CheckWallExit
              │                    │
              ├─矮墙→ExecuteVault  │ 只驱动1个状态
              ├─墙跳→ExecuteJump   │ WallContact.OnUpdate
              └─贴墙→EnterContact  │
```

`DetectWallEntry` 不再需要 `TryEnterWallState` 子方法——逻辑直接内联，三个分支清晰（Vault / Jump / Contact）。

`CheckWallExit` 保持不变——全局退出检查是合理的安全网。

---

## 六、与 WallSystem 的关系

本方案兼容上次重构的 WallSystem 架构，并在此基础上进一步减少状态：

```
WallSystem
├── WallConfig config                  // SO聚合18参数
├── StateMachine StateMachine          // 只管理1个状态
├── WallContactState wallContactState  // 唯一状态实例
│
├── CheckWall()                        // 唯一墙检测入口
├── CheckWallTop() / CanVault()        // 翻顶检测
├── DetectEntry()                      // 入口调度（含矮墙）
├── CheckExit()                        // 全局退出
│
├── IsTouchingWall / WallDirection     // 墙状态
├── FreezeTimer                        // 输入冻结
│
└── GetState<T>()                      // 泛型访问（现在只返回WallContact）
```

WallActions（新类或 WallSystem 的静态方法组）：

```
static class WallActions
    static void ExecuteWallJump(PlayerCharacterBase, WallSystem, float inputH)
    static void ExecuteVault(PlayerCharacterBase, WallSystem)
```

### DetectEntry 伪代码

```
DetectEntry():
    if StateMachine.CurrentState != null → return
    if player.IsGrounded → return
    if FreezeTimer > 0 → return

    // 1. 矮墙翻顶
    if TryShortWallVault():         // 脚命中+头未命中
        WallActions.ExecuteVault(player, this)
        return
        // 注意：Vault 后 FreezeTimer 阻止下一帧立即重入

    if !IsTouchingWall → return

    // 2. 贴墙跳跃 → 方法调用
    if Input.GetKeyDown(KeyCode.Space):
        float h = Input.GetAxisRaw("Horizontal")
        WallActions.ExecuteWallJump(player, this, h)
        return

    // 3. 贴墙 → 进入状态
    StateMachine.ChangeState(wallContactState)
```

---

## 七、改动文件清单

| 操作 | 文件 | 改动量 | 说明 |
|---|---|---|---|
| NEW | `Player/States/WallContactState.cs` | ~120行 | 合并 Slide+FastSlide+Climb 的单一状态类 |
| NEW | `Player/WallActions.cs` | ~60行 | ExecuteWallJump + ExecuteVault 静态方法 |
| DELETE | `Player/States/WallSlideStateBase.cs` | -103行 | 基类不再需要 |
| DELETE | `Player/States/WallSlideState.cs` | -24行 | 并入 WallContactState |
| DELETE | `Player/States/WallFastSlideState.cs` | -24行 | 并入 WallContactState |
| DELETE | `Player/States/WallClimbState.cs` | -78行 | 并入 WallContactState |
| DELETE | `Player/States/WallJumpState.cs` | -61行 | 退化为 WallActions.ExecuteWallJump |
| DELETE | `Player/States/WallVaultState.cs` | -37行 | 退化为 WallActions.ExecuteVault |
| MODIFY | `Player/WallSystem.cs` | 小幅 | DetectEntry 逻辑简化，状态引用从4个减为1个，增加 WallActions 调用 |
| MODIFY | `Player/PlayerController.cs` | 小幅 | Awake 中删4个状态实例创建，改为1个 WallContactState |
| — | `Player/WallConfig.cs` | 不变 | SO 设计不变 |
| — | `PlayerCharacterBase.cs` | 不变 | 墙逻辑已全部移入 WallSystem（上次重构已处理） |

### 改动量汇总

```
新增: 2 文件  (~180行)
删除: 6 文件  (-327行)
修改: 2 文件  (小改)
净减少: 4 文件, ~147行代码

状态类数量: 5 → 1
跨文件cast: 8处 → 0处
状态间边: 3条模式切换边 → 1个枚举字段赋值
```

---

## 八、与上次重构方案的关系

| 维度 | 上次重构（t_fa7efad8） | 本次精简 |
|---|---|---|
| 状态数量 | 4（Slide/Climb/Jump/Vault） | 1（WallContact） |
| Jump/Vault 形态 | IState 状态类 | 静态方法 |
| Climb 形态 | 独立 IState 状态类 | WallContact 的一个枚举模式 |
| 状态机管理 | 4状态间的切换 | 仅管理 WallContact ↔ None |
| 模式切换 | Slide→Climb 经过状态机 | 枚举赋值，不涉及状态机 |
| WallConfig | 需要 | 需要（不变） |
| WallSystem | 需要 | 需要（简化引用） |

**本方案是上次重构的自然递进**——上次已经打下了 WallConfig + WallSystem 的基础，消除了参数分散和 cast 依赖。本方案进一步利用这个基础，把"已经不该是状态的伪状态"彻底退化为方法。

---

## 九、边界情况检查

| 场景 | 行为 | 状态 |
|---|---|---|
| 贴墙中落地 | CheckExit → None | 正确 |
| 贴墙中离开墙面 | CheckExit → None | 正确 |
| 墙跳后立即再贴墙 | FreezeTimer=0.1s 阻止 | 正确 |
| 翻顶后站平台 | FreezeTimer=0.15s + 位置已移上平台 | 正确 |
| 爬墙中输入Space | Climb模式不响应Space | 正确（原设计如此） |
| 快滑中按W | 进入Climb | 正确（原快滑也是W计时→Climb） |
| Climb中按S | 无响应（Climb模式不处理S） | 正确 |
| 矮墙检测时序 | DetectEntry 内先于普通贴墙入口 | 正确（原逻辑不变） |
| FreezeTimer 过期 | 非墙状态时 FreezeTimer 在 UpdateCooldowns 中自然递减 | 正确 |

---

## 十、风险点

1. **模式切换丢失 OnEnter/OnExit 副作用**
   Slide→FastSlide 和 FastSlide→Slide 原本经过状态机的 ChangeState，会触发 OnExit/OnEnter。但实际代码中两状态类都没有 OnEnter/OnExit 特有逻辑（只在基类中有 climbHoldTimer 操作，合并后统一管理）。无风险。

2. **Climb 模式松W回到 Slide 而非 FastSlide**
   原设计 Climb→Slide（调用 `pc.WallSlideState`），本方案松W后 `_mode = Slide`（慢滑）。行为一致。

3. **ExecuteWallJump 退出链**
   ExecuteWallJump 设置 FreezeTimer=0.1s。从 WallContact.OnUpdate 调用后，OnUpdate return 即可——当前帧结束后状态机已无 CurrentState（调用方负责 ChangeState(null)）。注意：ExecuteWallJump 本身不应调用 ChangeState(null)，因为 DetectEntry 路径（None状态直接跳跃）中没有 CurrentState 可清。退出职责由调用方承担。

4. **矮墙翻顶后 DetectEntry 不会立即重入**
   ExecuteVault 设置 FreezeTimer=0.15s + 瞬移后 `!isTouchingWall`（通常）。即使仍贴墙，FreezeTimer 阻止 DetectEntry。安全。

---

## 十一、结论

**5状态 → 1状态 + 2方法**。核心逻辑全部收拢在一个 ~120行的 WallContactState 中，模式切换退化为枚举赋值，WallJump/Vault 退化为静态方法。消除全部8处 cast，净删除6个文件（327行），无功能丢失。
