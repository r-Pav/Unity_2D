# Enemy 动画统一框架方案（melee 先行）

> 目标：把 melee 四个新动画（Idle站立 / Jump移动 / Attack / Death）接入 FSM，沉淀一套 melee/ranged/boss 共用的统一动画框架，后续新增 enemy 类型与动画时零代码或仅加参数。

## 一、现状盘点

### 动画资源（已就位）
| 文件 | 时长 | Loop | 事件 |
|---|---|---|---|
| Anim/Enemy/Enemy_melee_mush/Idle.anim | 0.667s | 循环 ✓ | 无 |
| Anim/Enemy/Enemy_melee_mush/Jump.anim | 0.6s | 循环 ✓ | 无 |
| Anim/Enemy/Enemy_melee_mush/Attack.anim | 0.583s | **循环 ✗（需改）** | **无（需挂）** |
| Anim/Enemy/Enemy_melee_mush/Death.anim | 0.556s | **循环 ✗（需改）** | **无（需挂）** |
| Anim/Enemy/Enemy_melee_mush/Anim.controller | — | 4 状态平铺，**零参数、零过渡、DefaultState=Attack（错误）** | — |

ranged wizard（6 clips）、Boss/First（5 clips）的 controller 同为空壳，本次不动，框架兼容。

### 代码现状
- FSM 基类 `EntityState` 已支持 `animBoolNames` / `animTriggerNames` 构造注入（OnEnter SetBool=true/SetTrigger，OnExit SetBool=false）——**框架已就绪，状态类没在用**
- `EnemyControllerBase.UpdateAnimation()` 只设 Speed，注释自述"后续有动画 Clip 时可扩展 IsAttacking/IsDead"
- `MeleeAttackState` 纯代码计时（0.5s 状态 / 0.3s 触发攻击）——与动画不同步
- `Die()` 直接 Destroy，无死亡动画
- `AnimationRelay` 只转发 Player 事件（PlayerCombat/PlayerHealth/WeaponThrow）
- `AnimParams` 常量已全部具备：IsMove / IsAttacking / IsDead / IsHurt / AttackIndex / Speed —— **零新增**

### 缺口清单
1. prefab（Enemy_Melee 及 Lv1/Lv2/Lv3）未挂 Animator 组件
2. Attack/Death 未取消循环、未挂动画事件
3. controller 未接线（参数/路由/出口/DefaultState）
4. 状态类未绑定动画 Bool，攻击/死亡无事件驱动

## 二、统一框架设计（melee/ranged/boss 共用）

### 1. Animator 参数集（IsIdle 为新增常量，其余已存在于 AnimParams）

| 参数 | 类型 | 语义 | 写入方 |
|---|---|---|---|
| IsIdle | Bool | 空闲站立（Locomotion 态 A，与 IsMove 互斥） | EnemyControllerBase.UpdateAnimation 每帧聚合 |
| IsMove | Bool | 移动中（Locomotion 态 B，与 IsIdle 互斥） | EnemyControllerBase.UpdateAnimation 每帧聚合 |
| IsAttacking | Bool | 攻击中 | 攻击状态 OnEnter/OnExit |
| IsDead | Bool | 死亡（置位不退出） | 死亡状态 OnEnter |
| IsHurt | Bool | 受击硬直（有 hurt 动画的类型用；melee 暂不接） | 受击状态 OnEnter/OnExit |
| AttackIndex | Int | 多段攻击路由（ranged Attack1/2 后续用） | 攻击状态 |
| Speed | Float | 速度档位（ranged Run 后续用） | UpdateAnimation |

### 2. 控制器结构（每个 enemy 一个 controller，结构统一）

```
Entry → Death:  IsDead == true
Entry → Attack: IsAttacking == true
Entry → Hurt:   IsHurt == true        （无 hurt 动画的类型省略此条）
Entry → Idle:   IsIdle == true
Entry → Move:   IsMove == true        （状态名随类型：melee=Jump / ranged=Run / boss=Fly）
```

正向 Bool 驱动，**每个状态单条件退出**（对应 Bool 取反），不堆 OR 链：

```
Idle → Exit:   IsIdle == false
Move → Exit:   IsMove == false
Attack → Exit: IsAttacking == false
Hurt → Exit:   IsHurt == false
Death: 终态无出口
```

打断机理：busy 时聚合把 IsIdle/IsMove 全置 false → 当前 Locomotion 状态必然退出 → Entry 重判命中 IsAttacking/IsDead/IsHurt。Entry 不在运行中打断状态，只在回流后重判——与玩家框架同构。

铁律：Has Exit Time 全部为 0，纯条件驱动。DefaultState = Idle。

### 3. 代码架构（改动集中在 3 个既有文件 + 新增 1 个 EnemyDeadState.cs）

**EnemyControllerBase.cs — 统一动画驱动层**
- UpdateAnimation() 每帧聚合 IsIdle / IsMove（互斥双参数）：
```csharp
bool moving = Mathf.Abs(moveInput) > 0.01f;
bool busy = isDead
    || _animator.GetBool(AnimParams.IsAttacking)
    || _animator.GetBool(AnimParams.IsHurt);
_animator.SetBool(AnimParams.IsIdle, !moving && !busy);
_animator.SetBool(AnimParams.IsMove, moving && !busy);
```
  攻击时 moveInput=0 且 busy=true → IsIdle/IsMove 全 false → 当前 Locomotion 状态 Exit → Entry 重判进 Attack。stun 时 moveInput=0 → 回落 Idle。死亡后由 IsDead 接管。
- 通用动画事件方法（virtual，子类可覆盖）：
```csharp
// 基类实现：转发给当前攻击状态（IEnemyAttackState 接口，ranged/boss 后续攻击状态实现同一接口）
public interface IEnemyAttackState
{
    void OnHitFrame();   // 命中帧：执行攻击
    void OnAnimEnd();    // 攻击动画结束：退出攻击
}
public virtual void OnAttackHitFrame()     => (fsm.CurrentState as IEnemyAttackState)?.OnHitFrame();
public virtual void OnAttackAnimationEnd() => (fsm.CurrentState as IEnemyAttackState)?.OnAnimEnd();
public virtual void OnDeathAnimationEnd()  // 死亡动画播完：执行原 Die() 内容
```
  IEnemyAttackState.cs 放 Scripts/Enemy/（与既有 IEnemyAttack.cs 同目录）。
- Die() 改造：置 isDead + `fsm.ChangeState(new EnemyDeadState(...))`（旧状态 OnExit 自动清 IsAttacking） + 死亡超时兜底协程 → 动画事件 OnDeathAnimationEnd 到达后执行原 Die 内容（VFX/掉落/EventBus/Destroy）

**新增 EnemyDeadState.cs — 与 PlayerDeadState 对称**
```csharp
public class EnemyDeadState : EntityState
{
    public EnemyDeadState(CharacterBase owner, StateMachine stateMachine, Animator anim)
        : base(owner, stateMachine, anim, new[] { AnimParams.IsDead }) { }
    public override void OnUpdate() { } // 等待动画事件/超时兜底
}
```

**MeleeAttackState.cs — 代码计时 → 动画事件驱动**
- 构造：`animBoolNames = new[] { AnimParams.IsAttacking }`（OnEnter 自动 SetBool true / OnExit false）
- OnEnter：moveInput=0 + 启动攻击超时兜底协程（Attack clip 时长 + 0.2s）
- 删 0.3s/0.5s 代码计时；`PerformAttack` 移入 OnAttackHitFrame
- **OnAttackAnimationEnd：只 `fsm.ChangeState(new MeleeIdleState(...))`，不做 Chase/Patrol 判断** —— 回到 Idle 状态的核心入口判断（timer→Patrol / CanSeePlayer→Chase），避免攻击状态尾部堆逻辑链
- 超时兜底：事件链路断时强制退出（AirHurt 超时同款模式，防卡状态）

**AnimationRelay.cs — 加 enemy 转发**
```csharp
private EnemyControllerBase _enemy;   // Awake: GetComponentInParent<EnemyControllerBase>()
public void OnEnemyAttackHitFrame() => _enemy?.OnAttackHitFrame();
public void OnEnemyAttackEnd()      => _enemy?.OnAttackAnimationEnd();
public void OnEnemyDeathEnd()       => _enemy?.OnDeathAnimationEnd();
```

**EnemyMeleeController.cs**：无逻辑改动。EnemyStunState 无 hurt 动画时 moveInput=0 → IsMove=false → 自然显示 Idle。

### 4. 动画资源改造（编辑器手动，melee）

- **Attack.anim**：取消 Loop Time + 挂 2 事件
  - 命中帧 `OnEnemyAttackHitFrame`（挥击瞬间，约 0.25s 第 4 帧，以编辑器实际挥击帧为准）
  - 结束帧 `OnEnemyAttackEnd`（末帧 0.583s）
- **Death.anim**：取消 Loop Time + 挂 1 事件 `OnEnemyDeathEnd`（末帧 0.556s）
- **Idle.anim / Jump.anim**：保持循环，无事件
- **Anim.controller**：按 §2 结构搭线（参数 3 个 Bool：IsMove/IsAttacking/IsDead；Entry 路由；出口；DefaultState=Idle）

### 5. Prefab 改造

- Enemy_Melee.prefab 及 Lv1/Lv2/Lv3 变体：根物体挂 **Animator**（controller = Anim/Enemy/Enemy_melee_mush/Anim.controller）+ 同物体挂 **AnimationRelay**
- CharacterBase._animator 用 GetComponentsInChildren 找第一个有 controller 的 Animator → 挂根物体即被找到

## 三、melee 接入步骤（本次任务范围）

| # | 内容 | 执行方 |
|---|---|---|
| 1 | controller 搭线（参数 IsIdle/IsMove/IsAttacking/IsDead + Entry 路由 + 单条件出口 + DefaultState=Idle） | 编辑器手动 |
| 2 | Attack.anim 取消循环 + 挂 2 事件；Death.anim 取消循环 + 挂 1 事件 | 编辑器手动（用户挂） |
| 3 | AnimParams.cs：加 `IsIdle` 常量 | kanban→programer |
| 4 | EnemyControllerBase.cs：IsIdle/IsMove 聚合 + 3 个通用事件方法 + Die() 改造 | kanban→programer |
| 5 | 新增 EnemyDeadState.cs（与 PlayerDeadState 对称，新 cs 不带 meta） | kanban→programer |
| 6 | MeleeAttackState.cs：事件驱动化 + 超时兜底 + 结束回 IdleState | kanban→programer |
| 7 | AnimationRelay.cs：enemy 转发 3 方法 | kanban→programer |
| 8 | Prefab（4 变体）挂 Animator + AnimationRelay | 编辑器手动 |
| 9 | 运行验证 | tester |

验证点：待机站立 / patrol+chase 跳跃移动 / 攻击动画与命中同步 / 攻击结束回 Idle 再判断 / 死亡动画播完销毁 / 踩头 stun 显示 Idle / 攻击中被踩头正确回 Idle。

## 四、后续扩展（框架验证，零代码或仅加参数）

- **Ranged**（Idle/Run/Hurt/Attack1/Attack2/Death）：Hurt → IsHurt 状态；Attack1/2 → AttackIndex 路由（Entry 加 AttackIndex Equals 1/2 子机内切段，复用玩家三连击方案）；Run → IsMove
- **Boss**（Idle/Fly/Hurt/Attack/Magic）：同一参数集；boss 无 Death.anim → BossControllerBase 覆盖 FinishDeath 走特殊结算

## 五、坑清单（task body 必列）

1. Attack/Death 必须取消 Loop Time（m_LoopTime=0）——循环 clip 事件只触发首轮，Bool 不复位会卡状态
2. 动画事件必须挂 **AnimationRelay** 的转发方法（Animator 与 relay 同物体；挂根组件会静默失败）
3. 攻击/死亡超时兜底必须有（事件链路断防卡死，AirHurt 超时同款）
4. 新增 .cs（EnemyDeadState.cs）不带 meta（团结引擎 CS0246）
5. 团结引擎 .controller 是 YAML（yousandi.cn tag），过渡改动以编辑器为准
6. UpdateFacing 用 localScale.x 翻转，sprite 交换动画兼容，无需额外处理
7. **动画器信息（controller 过渡/参数/事件/clip Loop 等）以用户口头输入为基准，不读 yaml 文件核实（yaml 有滞后）**——需确认信息直接反馈用户查

## 六、待确认点（已确认记录）

1. ~~攻击命中帧挂哪帧~~ → **用户挂载：等代码事件方法就绪后由用户挂事件，具体帧用户在编辑器确认**
2. ~~Attack 结束行为~~ → **用户裁决：结束回 IdleState 走核心判断（timer→Patrol / CanSeePlayer→Chase），不在 Attack 状态做后续判断**
3. ~~stun 表现~~ → **用户裁决：hurt 动画预留，melee 本次不接，后续加动画时启用 IsHurt**
