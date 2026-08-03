# 格挡/弹反系统 — 分析报告

> 基于项目现有代码: PlayerCombat.cs / PlayerController.cs / PlayerHealth.cs / EnemyControllerBase.cs / EnemyMeleeAttack.cs / EnemyRangedAttack.cs / EnemyMeleeController.cs / EnemyRangedController.cs / EnemyStunState.cs / Projectile.cs / PlayerProjectile.cs / EnemyProjectile.cs / StatModifierManager.cs / StatId.cs / Events.cs
> 约束: 不修改项目代码，仅输出核心设计与接口

---

## 一、现状分析 — 输入管线与攻防系统

### 1.1 当前按键占用情况

```
左键 (Mouse0): PlayerCombat → 远程 BurstFire / 近战 ExecuteMeleeAttack
右键 (Mouse1): 【未使用】 ← 格挡/弹反的天然输入通道
滚轮:        PlayerCombat → 攻击模式切换 (近战/远程)
Tab:         PlayerCombat → 攻击模式切换 (备用键)
Space:       墙跳 (WallJumpState) / 普通跳跃 (PlayerJump)
Shift:      冲刺 (PlayerDash)
Q/E/R/F:    技能快捷键 (SkillManager)
```

### 1.2 玩家受伤管线 (完整链路)

```
伤害来源:
  ├─ EnemyMeleeAttack.PerformAttack → pc.TakeDamageWithKnockback(damage, attackDir)
  │                                    → PlayerController → PlayerHealth
  └─ EnemyRangedAttack.PerformAttack  → EnemyProjectile.Spawn → 飞行 → OnTriggerEnter2D
                                         → TryDealDamage → player.TakeDamageWithKnockback
         
PlayerHealth.TakeDamage(amount):
  ├─ 1. RollDodge() → StatId.DodgeChance → Random.value < dodgeChance → return (无伤)
  ├─ 2. OnDamaged?.Invoke() → PlayerController 进入战斗态
  ├─ 3. ApplyArmorReduction(amount) → amount = max(1, amount - armor)
  ├─ 4. ApplyDamageReduction(amount) → amount *= (1 - StatId.DamageReduction)
  ├─ 5. currentHealth -= amount
  ├─ 6. if dead → Die → DropAllOnDeath → PlayerDeathEvent
  └─ 7. PlayerHealthChangedEvent → HUD 更新

PlayerHealth.TakeDamageWithKnockback:
  └─ TakeDamage(amount)
     + rb.AddForce(knockDir * 10f, Impulse)
     + KnockbackRoutine(0.2s * (1 - controlReduction))
```

### 1.3 敌人攻击帧窗口 (当前实现)

**近战敌人 (EnemyMeleeController.AttackState)**:

```
OnEnter (0ms):
  timer = 0.5f, attacked = false

OnUpdate:
  timer -= deltaTime
  if (!attacked && timer <= 0.3f)   ← 攻击执行点 (OnEnter后0.2s)
    attacked = true
    attackModule.PerformAttack(owner)
  if (timer <= 0f)                   ← 状态结束
    → Chase or Patrol
```

当前攻击是**瞬间判定**——PerformAttack 调用一次 OverlapBox 就完成。无「攻击帧窗口」概念。

**远程敌人 (EnemyRangedController.AttackState)**: 同上，0.5s 状态中在 0.3s 时发射一颗子弹。子弹飞行后才命中。

### 1.4 敌人硬直系统 (已存在)

```
EnemyControllerBase.EnterStunState():
  ├─ stunCooldownTimer = 0.5s (CD防止连续眩晕)
  └─ fsm.ChangeState(stunState)

EnemyStunState:
  ├─ OnEnter: moveInput=0, color=黄色, timer=0.5s, attackCooldownTimer=0
  ├─ OnUpdate: timer倒计时 → CanSeePlayer? → Chase or Fallback
  └─ OnExit: (无特殊处理)
```

已有的 击退 + 硬直: `TakeDamageFrom` 执行 `rb.AddForce(knockDir * 3f, Impulse)` 后调用 `EnterStunState()`。

### 1.5 减伤管道 (已存在)

```
StatModifierManager.GetFinalValue(baseValue, statId):
  result = baseValue × (1 + ΣPercent) + ΣFlat
  ClampConfig 钳制:
    DamageReduction: [0, 0.8]
    DodgeChance:     [0, 0.6]
```

减伤率通过 `AddModifier(Modifier)` 注入，同 source 覆盖旧值。已有 `StatId.DamageReduction`。

---

## 二、输入判定 — 点按(弹反) vs 长按(格挡)

### 2.1 判定策略

右键当前完全未被占用，可以直接在 PlayerCombat 中检测。

**判定逻辑**:

```
On Mouse1 Down:
  holdTimer = 0
  isHoldingBlock = true
  → 格挡减伤立即生效 (AddModifier "Blocking", 50%)

On Mouse1 (每帧持续):
  holdTimer += deltaTime

On Mouse1 Up:
  isHoldingBlock = false
  → 移除格挡减伤 (RemoveModifier "Blocking")
  
  if (holdTimer < parryThreshold):   ← 短按判定为弹反
    AttemptParry()
  // 否则只是格挡结束，无事发生
```

**阈值**: `parryThreshold = 0.2s` — 200ms 以内释放视为点按(弹反)，超过视为格挡。

### 2.2 为什么不用 GetMouseButtonDown 直接判断弹反?

`GetMouseButtonDown(1)` 只在按下**第一帧**返回 true。如果弹反窗口期是敌人攻击帧(约0.2s)，玩家需要在精确时机按下右键。用 Up 事件 + holdTimer 的方式更宽容——玩家可以提前按住右键进入格挡，然后在攻击帧内释放来弹反。但这会引入一个问题:

**格挡中释放 = 弹反?**
- 如果玩家先按住格挡(长按)，然后在敌人攻击帧内松手 → 这应该是格挡结束，而非弹反
- 解决方案: 引入 `_blockingStartTime`，仅在格挡开始后极短时间内松手才判定为弹反

**改进版判定**:

```
On Mouse1 Down:
  blockStartTime = Time.time
  isBlocking = true
  → 注入 "Blocking" 减伤 50%

On Mouse1 Up:
  holdDuration = Time.time - blockStartTime
  isBlocking = false
  → 移除 "Blocking" 减伤
  
  if (holdDuration <= parryMaxWindow)   // 0.2s 内
    AttemptParry()
```

### 2.3 格挡期间的持续减伤

格挡减伤通过 `StatModifierManager` 注入:

```
Modifier blockingMod = new Modifier(
    targetStat: StatId.DamageReduction,
    value: 0.5f,                    // 50% 减伤
    type: ModifierType.Percent,
    source: "Blocking",
    priority: 500                   // 高于装备(0)/被动(100)，确保优先叠加
);
```

效果: `TakeDamage` 中的 `ApplyDamageReduction` 自动读取 `GetFinalValue(0f, StatId.DamageReduction)` → 0.5。与装备的 DamageReduction 叠加时: `base(0) + 装备(0.2) + 格挡(0.5) = 0.7`，钳制在 [0, 0.8] 内。

> 注意: ClampConfig 中 DamageReduction 上限 = 0.8，格挡50% + 装备20% = 70%，未超上限。

### 2.4 格挡状态对其他系统的影响

| 系统 | 格挡中行为 |
|------|-----------|
| 移动 | 允许移动，但 moveSpeed 降低 (可选: 注入 MoveSpeed 减益) |
| 冲刺 | 允许冲刺，冲刺瞬间取消格挡 |
| 攻击 | 格挡中左键仍可攻击 (不互斥) |
| 跳跃 | 允许跳跃 |

---

## 三、弹反判定 — 与敌人攻击帧的衔接

### 3.1 攻击帧定义 (预留接口)

当前敌人攻击没有「帧窗口」概念——是瞬间 OverlapBox。需要预留接口供后续动画事件填充。

**EnemyControllerBase 新增字段 (预留)**:

```
/// <summary>是否处于攻击判定帧内 (供弹反系统查询)。当前由 PerformAttack 临时置位，后续由 AnimationEvent 驱动。</summary>
public bool IsInAttackFrame { get; set; }
```

**EnemyMeleeAttack.PerformAttack 占位实现**:

```
public void PerformAttack(EnemyControllerBase owner)
{
    owner.IsInAttackFrame = true;
    
    // ... 现有 OverlapBox 检测 + 伤害 ...
    
    owner.IsInAttackFrame = false;
}
```

> 后续替换为 AnimationEvent: 动画的 "AttackHit" 帧调用 `owner.IsInAttackFrame = true`，动画结束帧调用 `owner.IsInAttackFrame = false`。零代码改动，只改动画资源。

### 3.2 弹反判定逻辑

```
AttemptParry():
  // 1. 检测范围内是否有正在攻击帧的敌人
  Collider2D[] hits = OverlapBox(parryRange)
  
  foreach hit:
    enemy = hit.GetComponent<EnemyControllerBase>()
    if enemy != null && enemy.IsInAttackFrame:
      // 弹反成功!
      OnParrySuccess(enemy)
      return
  
  // 2. 范围内无敌人在攻击帧 → 弹反失败
  // 无惩罚，仅浪费一次右键点击
```

### 3.3 弹反判定范围

使用 `MeleeRangeIndicator` 已有的 Center + Size（约 1.5×1.5 矩形，由 Transform 定义）。

与近战攻击共用一个范围指示器——弹反的判定区域 = 近战攻击盒。

### 3.4 弹反对远程子弹

**不可弹反**。弹反仅对近战攻击帧有效。远程子弹通过 Projectile 碰撞系统处理，不受 `IsInAttackFrame` 影响。

但近战攻击可以**消除子弹** (见第五章)。

---

## 四、弹反成功 Buff — 重击

### 4.1 Buff 类型: 一次性

**一次性**: 弹反成功后，下一次近战攻击自动升级为「重击」。攻击后 Buff 自动消失。

选择「一次性」而非「持续 N 秒」的理由:
- 弹反是高风险高回报操作，回报应立即可控
- 持续 N 秒可能因敌人跑开而浪费 Buff
- 一次性 Buff 的实现更简单，无计时器管理

### 4.2 重击效果

| 属性 | 值 |
|------|-----|
| 伤害 | 正常近战伤害 (不变，已有暴击系统) |
| 击退 | `rb.AddForce(knockDir * **8f** (原3f)`, Impulse) |
| 硬直 | `EnterStunState()` (复用现有) |
| 视觉 | `MeleeRangeIndicator.Flash()` + 特殊颜色 (如金色) |

重击 = 正常近战伤害 + 强化击退力(3f→8f) + 强制硬直。

### 4.3 重击的硬直复用

当前 `EnemyControllerBase.TakeDamageFrom` 已经会调用 `EnterStunState()`:

```
TakeDamageFrom(amount, attackSource):
  currentHealth -= amount
  FlashHit()
  EnterStunState()           ← 0.5s 眩晕
  rb.AddForce(knockDir * 3f) ← 现有击退力
```

重击只需改为 `rb.AddForce(knockDir * 8f)`，硬直逻辑完全复用。

### 4.4 Buff 数据结构

```
// PlayerCombat 新增运行时状态
private bool _hasParryBuff;    // 弹反成功后置 true，近战攻击后置 false

ExecuteMeleeAttack():
  if (_hasParryBuff)
    ExecuteHeavyMeleeAttack()   // 击退力 8f + 强制硬直
    _hasParryBuff = false
  else
    ExecuteNormalMeleeAttack()  // 现有逻辑
```

不需要新增 StatId 或 Modifier——这是一个消耗性标记，非属性修饰。

### 4.5 弹反对 PlayerCombat 的影响

弹反成功触发事件，供 HUD 显示 Buff 图标:

```
EventBus.Trigger(new ParrySuccessEvent());       // HUD 显示金色 Buff 图标
EventBus.Trigger(new ParryBuffConsumedEvent());  // 重击后 HUD 隐藏图标
```

---

## 五、Projectile — 近战消除子弾

### 5.1 Projectile 基类新增标记

```
// Projectile 新增字段
/// <summary>是否可被近战攻击消除 (玩家近战 OverlapBox 额外检测)</summary>
protected bool canBeDestroyedByMelee = true;  // 默认 true
```

- `PlayerProjectile`: 默认继承 `canBeDestroyedByMelee = true` (玩家子弹可被敌人近战消除？按需)
- `EnemyProjectile`: 默认 `canBeDestroyedByMelee = true` (敌人子弹可被玩家近战消除)

### 5.2 PlayerCombat.ExecuteMeleeAttack 增强

在现有 OverlapBoxAll(enemyLayer) 之后，追加第二次 OverlapBoxAll 检测子弹:

```
ExecuteMeleeAttack():
  // Step 1: 现有敌人伤害判定 (不变)
  Collider2D[] enemyHits = OverlapBoxAll(..., enemyLayer);
  foreach → enemy.TakeDamageFrom(...)

  // Step 2: [新增] 子弾消除
  Collider2D[] projHits = OverlapBoxAll(..., projectileLayer);
  foreach col:
    proj = col.GetComponent<Projectile>()
    if proj != null && proj.canBeDestroyedByMelee:
      proj.ReturnToPool()  // 需将 ReturnToPool 从 protected 提升为 internal/public
```

需要新增一个 Layer `Projectile` (或复用 `PlayerBullet` / `EnemyBullet` Layer) 来让 OverlapBox 命中子弹。

**推荐方案**: 不新增 Layer，直接在 OverlapBoxAll 使用 `enemyLayer | projectileLayer` 合并检测，在循环中分流处理。

### 5.3 Projectile 访问修饰符调整

`ReturnToPool()` 当前是 `protected abstract`。弹反系统需要外部调用。改为:

```
// Projectile
public void ReturnToPool() { ... }  // protected → public
```

或新增 public 包装方法:

```
public void DestroyByMelee()
{
    if (canBeDestroyedByMelee)
        ReturnToPool();
}
```

### 5.4 子弾穿透格挡?

**不穿透**。格挡减伤 50% 对子弹伤害同样生效——`ApplyDamageReduction` 在 `TakeDamage` 中排在闪避之后，所有伤害来源都经过它。

如果后续需要「重型子弹穿透格挡」效果，可在 EnemyProjectile 上新增 `bool ignoresBlock` 标记，在 `PlayerHealth.TakeDamage` 中检查。

---

## 六、数据流与衔接点总结

### 6.1 新增数据流

```
右键按下 (Mouse1)
  │
  ├─→ [格挡] StatModifierManager.AddModifier("Blocking", DamageReduction +50%)
  │         └─→ PlayerHealth.TakeDamage → ApplyDamageReduction 自动生效
  │
  └─→ [弹反 (短按松手)] AttemptParry()
        ├─ 敌人 IsInAttackFrame? → ParrySuccess → _hasParryBuff = true
        │                                         → ParrySuccessEvent → HUD
        └─ 否 → 无效果
             
左键近战 (_hasParryBuff == true)
  └─→ ExecuteHeavyMeleeAttack()
        ├─ 伤害 = 正常近战伤害 + RollCrit
        ├─ rb.AddForce(knockDir * 8f) (强击退)
        ├─ EnterStunState() (硬直，复用)
        └─ _hasParryBuff = false → ParryBuffConsumedEvent → HUD

左键近战 (同时检测子弾)
  └─→ OverlapBoxAll(enemyLayer | projectileLayer)
        ├─ EnemyControllerBase → 伤害判定
        └─ Projectile + canBeDestroyedByMelee → ReturnToPool()
```

### 6.2 与现有系统的衔接点

| 现有系统 | 衔接方式 | 改动量 |
|---------|---------|--------|
| **PlayerCombat** | 新增 Mouse1 检测、格挡状态、弹反判定、重击变体、子弹消除 | ~120行新增 |
| **PlayerHealth** | 无需改动——格挡通过 Modifier 管道生效 | 0行 |
| **StatModifierManager** | 无需改动——格挡减伤走现有 `AddModifier("Blocking")` | 0行 |
| **EnemyControllerBase** | 新增 `IsInAttackFrame` 属性 | ~3行 |
| **EnemyMeleeAttack** | PerformAttack 中临时置位 `IsInAttackFrame` | ~2行 |
| **EnemyRangedAttack** | 无需改动 (远程不可弹反) | 0行 |
| **EnemyStunState** | 无需改动 (重击复用 EnterStunState) | 0行 |
| **Projectile** | 新增 `canBeDestroyedByMelee` 字段 + ReturnToPool 访问修饰符调整 | ~5行 |
| **MeleeRangeIndicator** | 无需改动 (弹反判定复用自己的 Center/Size) | 0行 |
| **Events** | 新增 `ParrySuccessEvent` + `ParryBuffConsumedEvent` | ~15行 |
| **PlayerHUD** | 新增 Buff 图标显示/隐藏 | ~10行 |
| **StatId** | 无需新增 (格挡用已有 DamageReduction) | 0行 |

### 6.3 关键设计决策

| 决策点 | 选项 | 选择及理由 |
|--------|------|-----------|
| 判定方式 | GetMouseButtonDown vs Up+timer | **Up+timer**: 容错更高，区分长短按 |
| 判定阈值 | 150ms / 200ms / 300ms | **200ms**: 低于人类反应时间上限(250ms)，足够短 |
| Buff 类型 | 一次性 vs 持续N秒 | **一次性**: 简单可控，无计时器风险 |
| 重击伤害 | 正常 vs 加成 | **正常**: 已有暴击系统，重击的差异化在击退+硬直 |
| 子弹消除 | 独立Layer vs 合并检测 | **合并检测**: 不新增 Layer，OverlapBoxAll 一次完成 |
| 子弹穿透格挡 | 穿透 vs 不穿透 | **不穿透**: 格挡对所有伤害统一减伤 |
| 格挡减伤注入 | Modifier vs 直接乘法 | **Modifier**: 复用管道，自动触发事件，可与其他减伤叠加 |

---

## 七、数据结构设计

### 7.1 PlayerCombat 新增字段

```
[Header("格挡/弹反")]
[SerializeField] private float parryMaxWindow = 0.2f;    // 弹反判定最大时长(秒)
[SerializeField] private float blockDamageReduction = 0.5f; // 格挡减伤率
[SerializeField] private LayerMask projectileLayer;      // 子弾 Layer (EnemyBullet/PlayerBullet)

// 运行时
private bool isBlocking;
private bool hasParryBuff;
private float blockStartTime;
```

### 7.2 EnemyControllerBase 新增字段

```
/// <summary>是否处于攻击判定帧内 (弹反系统查询)</summary>
public bool IsInAttackFrame { get; set; }
```

### 7.3 Projectile 新增字段

```
/// <summary>是否可被近战攻击消除</summary>
protected bool canBeDestroyedByMelee = true;
public bool CanBeDestroyedByMelee => canBeDestroyedByMelee;
```

### 7.4 新增事件

```
/// <summary>弹反成功事件 — HUD Buff 图标 + 音效订阅</summary>
public readonly struct ParrySuccessEvent { }

/// <summary>弹反 Buff 消耗事件 (重击已打出) — HUD 隐藏图标</summary>
public readonly struct ParryBuffConsumedEvent { }
```

---

## 八、输入状态机

```
                    ┌──────────────────┐
                    │   IDLE (未按右键)  │
                    └────────┬─────────┘
                             │ Mouse1 Down
                             ▼
                    ┌──────────────────┐
                    │   BLOCKING        │
                    │   blockStartTime  │
                    │   AddModifier     │
                    │   ("Blocking")    │
                    └────────┬─────────┘
                             │
              ┌──────────────┼──────────────┐
              │              │              │
        Mouse1 Up      Dash/死亡      持续按住
      holdDuration     触发取消          │
         ≤200ms?         │              │
         │    │          ▼              ▼
         ▼    └──→  ┌────────┐    保持BLOCKING
    ┌─────────┐     │  IDLE   │   (减伤持续)
    │ ATTEMPT │     └────────┘
    │  PARRY  │
    └────┬────┘
         │
    ┌────┴────┐
    │         │
  敌方在     敌方不在
  攻击帧     攻击帧
    │         │
    ▼         ▼
 PARRY_OK  PARRY_MISS
 hasBuff   → IDLE
 = true
 换重击
```

---

## 九、开发优先级与文件改动量

| 优先级 | 功能 | 涉及文件 | 新增行数 |
|--------|------|---------|---------|
| **P0** | 输入判定 (长短按) | PlayerCombat.cs | ~40 |
| **P0** | 格挡减伤 (Modifier注入) | PlayerCombat.cs | ~20 |
| **P1** | 弹反判定 (IsInAttackFrame) | PlayerCombat.cs + EnemyControllerBase.cs + EnemyMeleeAttack.cs | ~30 |
| **P1** | 重击 (hasParryBuff) | PlayerCombat.cs | ~30 |
| **P2** | 子弾消除 | PlayerCombat.cs + Projectile.cs | ~20 |
| **P2** | HUD 图标 | PlayerHUD.cs + Events.cs | ~25 |
| **合计** | | 6 文件 | ~165 |

---

## 十、风险与注意事项

1. **IsInAttackFrame 当前是瞬时值**: PerformAttack 中设为 true → 执行 → 设为 false，总时长约 1 帧。弹反窗口极窄。后续接 AnimationEvent 驱动后窗口可拉长到动画的伤害帧区间(约 0.1~0.2s)。

2. **格挡减伤的叠加顺序**: 格挡(50%) + 装备减伤(0~20%) = 70%，在 ClampConfig 上限 0.8 以内。但如果后续有新系统叠加百分比减伤，可能触及上限。考虑将 ClampConfig.DamageReduction 上限从 0.8 提至 0.85。

3. **弹反与闪避的优先级**: 弹反的 OverlapBox 检测先于敌人攻击命中。如果弹反判定在敌人攻击帧内且玩家在范围内，弹反成功。如果玩家同时有高闪避率，弹反成功后 buff 标记先设置，敌人攻击是否命中不影响弹反结果。

4. **右键功能冲突**: 当前右键未使用，未来如有新系统需要使用右键，需评估与格挡弹反的冲突。建议将格挡弹反设为右键的**默认行为**，其他系统通过配置或条件覆盖。

5. **格挡中可做其他动作**: 格挡不阻止移动/跳跃/攻击——这提供了战术深度（边格挡边近战），但也意味着玩家可能误操作。可考虑在 MeleeAttackConfigSO 中加 `blockStopsMovement` 开关。
