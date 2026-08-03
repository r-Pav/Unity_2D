# 近战攻击切换方案

> 基于 PlayerCombat.cs (200行) / PlayerController.cs (366行) / SkillData.cs (56行) / ActiveSkillData.cs (107行) 实际代码分析
> 输出日期: 2026-07-21
> 约束: 只读分析，不改代码；PlayerController 尽量少改；复用 attackCooldown

---

## 一、现状分析

### 1.1 当前攻击系统

```
PlayerController.OnUpdate()
  └→ UpdateSubModules()
       └→ combat.OnPlayerUpdate(this)   // PlayerCombat 每帧入口

PlayerCombat.OnPlayerUpdate():
  ├─ attackCooldownTimer -= deltaTime × speedMult   // 攻速修饰器
  ├─ combatTimeoutTimer 倒计时                        // 战斗态超时
  └─ if (左键按下 && CD就绪 && !dash中 && !burst中)
       └→ StartCoroutine(BurstFire())

BurstFire():
  ├─ OnAttack?.Invoke()                              // 战斗态标记
  ├─ 散射计算 (bulletSpreadAngle)
  └─ PlayerProjectile.Spawn(...) × shotsPerClick     // 对象池子弹
```

**关键常量/字段 (PlayerCombat)**:
| 字段 | 默认值 | 说明 |
|------|--------|------|
| attackCooldown | 0.3s | 两次单击间隔 |
| shotsPerClick | 1 | 每次单击子弹数 |
| burstInterval | 0.05s | 连发间隔 |
| bulletSpreadAngle | 5° | 散射角度 |
| bulletSpeed | 10 | 子弹速度 |
| enemyLayer | ~0 | 敌人Layer |
| wallLayer | 0 | 墙Layer |

**已有修饰器集成**:
- `GetAttackSpeedMultiplier()` → StatId.AttackSpeedMultiplier
- `GetEffectiveDamage()` → attackDamage × StatId.DamageMultiplier
- `RollDodge()` → StatId.DodgeChance
- `ApplyDamageReduction()` → StatId.DamageReduction

### 1.2 攻击输入路径

```
Input.GetMouseButtonDown(0)           // 硬编码在 PlayerCombat.OnPlayerUpdate() L105
  → 条件: attackCooldownTimer≤0 && !owner.IsDashing() && !isBursting
  → attackCooldownTimer = attackCooldown
  → StartCoroutine(BurstFire())
```

输入检测在 PlayerCombat 内部，PlayerController 只负责调用 `combat.OnPlayerUpdate(this)`。这意味着新增近战模式不必改 PlayerController 的输入分发逻辑。

### 1.3 现有可复用的基础设施

| 机制 | 位置 | 复用方式 |
|------|------|---------|
| attackCooldown 冷却计时 | PlayerCombat L86-89 | 近战/远程共用同一个 cooldownTimer |
| OnAttack 事件 | PlayerCombat L66, L129 | 近战攻击同样触发，战斗态锁定不变 |
| combatTimeoutTimer | PlayerCombat L60-63 | 共用，近战攻击后同样重置 |
| GetEffectiveDamage() | PlayerCombat L174-181 | 近战伤害复用同一方法 |
| GetAttackSpeedMultiplier() | PlayerCombat L167-171 | 近战CD受攻速修饰器同样影响 |
| enemyLayer | PlayerCombat L33 | 近战 OverlapBox 检测同层 |
| isBursting 互斥锁 | PlayerCombat L54, L108 | 近战攻击期间同样设置，防止同时触发 |
| StatModifierManager | PlayerCombat L57 | 所有修饰器查询同样可用 |

---

## 二、新增系统架构

### 2.1 设计原则：攻击接口注入（为装备系统预留）

**核心思路：** PlayerCombat 不直接判断 `if (mode == Melee)` 硬分支。引入内部接口 `IAttackExecutor`，近战和远程各实现一次。攻击触发时统一调用 `_currentExecutor.Execute(owner)`。装备系统来的时候，接口从「内部创建」改为「装备注入」——不用重写 PlayerCombat。

### 2.2 整体数据流

```
                              滚轮 / Tab键
                                  │
                                  ▼
┌─────────────────────────────────────────────────────┐
│ PlayerCombat.OnPlayerUpdate()                       │
│   ├─ CheckModeSwitch() → 切换 _currentExecutor      │
│   ├─ 左键按下 → _currentExecutor.Execute(owner)     │
│   │        │                                        │
│   │        ├─ RangedAttackExecutor.Execute()        │
│   │        │    └→ BurstFire() → PlayerProjectile   │
│   │        │                                        │
│   │        └─ MeleeAttackExecutor.Execute()         │
│   │             ├─ 1. OverlapBox 伤害判定           │
│   │             └─ 2. 视觉(按visualMode)            │
│   │                  ├─ Rectangle → SpawnMeleeRect() │
│   │                  └─ Animation → Animator(预留)   │
│   │                                                  │
│   └─ [预留] SetAttackExecutor(IAttackExecutor)      │
│        ← 装备系统外部注入                            │
└─────────────────────────────────────────────────────┘
                                  │
                                  ▼
                    EventBus.Trigger(AttackModeSwitchedEvent)
                                  │
                                  ▼
                          PlayerHUD 更新模式图标
```

### 2.3 接口定义（PlayerCombat 内部）

```csharp
/// <summary>
/// 攻击执行器接口 — 当前在 PlayerCombat 内部定义，
/// 后续装备系统可提取到独立文件并外部注入。
/// </summary>
private interface IAttackExecutor
{
    void Execute(PlayerCombat combat, PlayerController owner);
}
```

### 2.4 文件清单

| 操作 | 文件 | 说明 |
|------|------|------|
| **新增** | `Assets/Scripts/Player/MeleeRectAutoDestroy.cs` | 临时矩形自毁组件 (15行) |
| **新增** | `Assets/ScriptableObjects/MeleeAttackConfig.asset` | 近战配置SO |
| **新增 SO 脚本** | `Assets/Scripts/Skills/MeleeAttackConfigSO.cs` | SO 类定义 (30行) |
| **修改** | `Assets/Scripts/Player/PlayerCombat.cs` | 主战场: +IAttackExecutor接口, +2个实现类, +滚动检测, +模式切换 |
| **修改** | `Assets/Scripts/UI/PlayerHUD.cs` | +attackMode图标字段, +订阅事件 |
| **修改** | `Assets/Scripts/Framework/Events.cs` | +AttackModeSwitchedEvent |
| **轻微修改** | `Assets/Scripts/Player/PlayerController.cs` | +ScrollBlocked属性 (可选) |

---

## 三、代码改动明细

### 3.1 PlayerCombat.cs — 核心改动 (~100行新增)

**3.1.1 新增：攻击执行器接口 + 两个实现类（PlayerCombat 内部）**

```csharp
// ============================================================
// 攻击执行器接口（内部 — 装备系统可提取到独立文件）
// ============================================================

private interface IAttackExecutor
{
    void Execute(PlayerCombat combat, PlayerController owner);
}

/// <summary>远程攻击执行器 — 封装现有 BurstFire 逻辑</summary>
private class RangedAttackExecutor : IAttackExecutor
{
    public void Execute(PlayerCombat combat, PlayerController owner)
    {
        owner.StartCoroutine(combat.BurstFire());
    }
}

/// <summary>近战攻击执行器 — 封装 OverlapBox + 视觉</summary>
private class MeleeAttackExecutor : IAttackExecutor
{
    public void Execute(PlayerCombat combat, PlayerController owner)
    {
        combat.ExecuteMeleeAttack();
    }
}
```

> **装备系统预留路径：** 当前 executor 是内部 new 出来的。装备系统接入时，把 `IAttackExecutor` 提取为 public 接口，装备 SO 携带 executor 实例，PlayerCombat 暴露 `SetAttackExecutor(IAttackExecutor executor)` 接收外部注入。内部代码零改动。

**3.1.2 新增字段**

```csharp
// ============================================================
// 攻击模式
// ============================================================

public enum AttackMode { Ranged, Melee }

[Header("攻击模式")]
[SerializeField] private AttackMode startMode = AttackMode.Ranged;
[SerializeField] private MeleeAttackConfigSO meleeConfig;

// 攻击执行器（接口注入）
private IAttackExecutor _currentExecutor;
private readonly RangedAttackExecutor _rangedExec = new RangedAttackExecutor();
private readonly MeleeAttackExecutor _meleeExec = new MeleeAttackExecutor();

/// <summary>当前攻击模式（公开属性，供HUD读取）</summary>
public AttackMode CurrentMode { get; private set; }

/// <summary>
/// [预留] 装备系统入口 — 外部注入自定义攻击执行器。
/// 调用后内部 exec 不再自动切换，由装备系统控制。
/// </summary>
public void SetAttackExecutor(IAttackExecutor executor)
{
    _currentExecutor = executor;
}
```

**3.1.3 Awake() — 初始化默认执行器**

```csharp
private void Awake()
{
    // ... 原有逻辑 ...
    CurrentMode = startMode;
    _currentExecutor = CurrentMode == AttackMode.Melee
        ? (IAttackExecutor)_meleeExec : _rangedExec;
}
```

**3.1.4 修改 OnPlayerUpdate() — 统一入口**

```csharp
// 攻击输入（统一入口，不区分模式）
if (Input.GetMouseButtonDown(0)
    && attackCooldownTimer <= 0f
    && !owner.IsDashing()
    && !isBursting)
{
    attackCooldownTimer = attackCooldown;
    if (!playerCombatFlag)
    {
        passiveEquipManager?.SetCombatState(true);
        playerCombatFlag = true;
    }
    combatTimeoutTimer = CombatTimeoutDuration;

    _currentExecutor.Execute(this, owner);  // ← 唯一攻击入口
}
```

**3.1.5 修改 CheckModeSwitch() — 切换 executor**

```csharp
private void CheckModeSwitch()
{
    float scroll = Input.mouseScrollDelta.y;
    if (Mathf.Abs(scroll) > 0.01f)
    {
        CurrentMode = scroll > 0 ? AttackMode.Melee : AttackMode.Ranged;
        _currentExecutor = CurrentMode == AttackMode.Melee
            ? (IAttackExecutor)_meleeExec : _rangedExec;
        OnModeSwitched();
        return;
    }

    if (meleeConfig != null
        && meleeConfig.alternateSwitchKey != KeyCode.None
        && Input.GetKeyDown(meleeConfig.alternateSwitchKey))
    {
        CurrentMode = CurrentMode == AttackMode.Melee
            ? AttackMode.Ranged : AttackMode.Melee;
        _currentExecutor = CurrentMode == AttackMode.Melee
            ? (IAttackExecutor)_meleeExec : _rangedExec;
        OnModeSwitched();
    }
}
```

**3.1.6 BurstFire() — 改为 internal（供 RangedAttackExecutor 调用）**

```csharp
// 原来: private IEnumerator BurstFire()
// 改为:
internal IEnumerator BurstFire()  { /* 逻辑不变 */ }
```

**3.1.7 ExecuteMeleeAttack() / OnMeleeDamageWindow() / SpawnMeleeRect() — 逻辑不变**

与原始方案相同，详见下方 3.1.8。

### 3.1.8 近战攻击执行体（逻辑不变）

```csharp
/// <summary>
/// 近战攻击执行 — 解耦伤害判定与视觉表现
/// 1. Physics2D.OverlapBox 即时判定伤害
/// 2. visualMode 决定视觉反馈（Rectangle / Animation）
/// </summary>
private void ExecuteMeleeAttack()
{
    OnAttack?.Invoke();  // 战斗态标记

    if (meleeConfig == null)
    {
        Debug.LogError("[PlayerCombat] meleeConfig 未赋值！");
        return;
    }

    // ── Step 1: 伤害判定（逻辑层，总是执行）──
    Vector2 attackCenter = (Vector2)transform.position
        + Vector2.right * facing * meleeConfig.attackRange
        + Vector2.up * meleeConfig.verticalOffset;
    Vector2 boxSize = new Vector2(meleeConfig.attackWidth, meleeConfig.attackHeight);

    Collider2D[] hits = Physics2D.OverlapBoxAll(attackCenter, boxSize, 0f, enemyLayer);
    float finalDamage = GetEffectiveDamage();

    foreach (var col in hits)
    {
        // EnemyControllerBase 或 EnemyController 组件
        var enemy = col.GetComponent<EnemyControllerBase>();
        if (enemy != null)
        {
            Vector2 knockbackDir = ((Vector2)(col.transform.position - transform.position)).normalized;
            enemy.TakeDamage(finalDamage, knockbackDir);
        }
    }

    // ── Step 2: 视觉表现（表现层，按 visualMode 走不同路径）──
    switch (meleeConfig.visualMode)
    {
        case MeleeVisualMode.Rectangle:
            SpawnMeleeRect(attackCenter, boxSize);
            break;

        case MeleeVisualMode.Animation:
            // [预留] Animator.SetTrigger("MeleeAttack")
            // 伤害时机由 AnimationEvent 回调 → OnMeleeDamageWindow()
            // 矩形生成保留做 fallback
            SpawnMeleeRect(attackCenter, boxSize);  // fallback
            Debug.LogWarning("[PlayerCombat] visualMode=Animation 暂未实现，使用 Rectangle fallback");
            break;
    }
}

/// <summary>
/// [预留] 动画伤害窗口回调 — AnimationEvent 在动画关键帧调用
/// 当 visualMode==Animation 时，动画事件调用此方法替代 OverlapBox 即时检测
/// </summary>
public void OnMeleeDamageWindow()
{
    // 与 ExecuteMeleeAttack Step1 相同逻辑
    if (meleeConfig == null) return;

    Vector2 attackCenter = (Vector2)transform.position
        + Vector2.right * facing * meleeConfig.attackRange
        + Vector2.up * meleeConfig.verticalOffset;
    Vector2 boxSize = new Vector2(meleeConfig.attackWidth, meleeConfig.attackHeight);

    Collider2D[] hits = Physics2D.OverlapBoxAll(attackCenter, boxSize, 0f, enemyLayer);
    float finalDamage = GetEffectiveDamage();

    foreach (var col in hits)
    {
        var enemy = col.GetComponent<EnemyControllerBase>();
        if (enemy != null)
        {
            Vector2 knockbackDir = ((Vector2)(col.transform.position - transform.position)).normalized;
            enemy.TakeDamage(finalDamage, knockbackDir);
        }
    }
}

/// <summary>
/// 生成半透明矩形视觉反馈 — 自动销毁
/// </summary>
private void SpawnMeleeRect(Vector2 center, Vector2 size)
{
    GameObject rect = new GameObject("MeleeRect");
    rect.transform.position = center;

    SpriteRenderer sr = rect.AddComponent<SpriteRenderer>();
    sr.sprite = CreateRectSprite();  // 1×1 白色方块
    sr.color = meleeConfig.rectangleColor;
    sr.sortingOrder = 5;

    rect.transform.localScale = size;  // 拉伸到攻击盒尺寸

    // 自动销毁
    MeleeRectAutoDestroy autoDestroy = rect.AddComponent<MeleeRectAutoDestroy>();
    autoDestroy.lifetime = meleeConfig.rectangleLifetime;
}

/// <summary>生成 1×1 白色方块 Sprite（缓存避免每帧创建）</summary>
private static Sprite _cachedRectSprite;
private static Sprite CreateRectSprite()
{
    if (_cachedRectSprite != null) return _cachedRectSprite;
    Texture2D tex = new Texture2D(1, 1);
    tex.SetPixel(0, 0, Color.white);
    tex.Apply();
    _cachedRectSprite = Sprite.Create(tex, new Rect(0, 0, 1, 1), new Vector2(0.5f, 0.5f));
    return _cachedRectSprite;
}
```

### 3.2 新增 MeleeRectAutoDestroy.cs (独立文件)

```csharp
using UnityEngine;

/// <summary>
/// 近战矩形视觉反馈 — 指定秒数后自动销毁 GameObject
/// 挂到 MeleeRect 临时对象上
/// </summary>
public class MeleeRectAutoDestroy : MonoBehaviour
{
    public float lifetime = 0.15f;

    private void Start()
    {
        Destroy(gameObject, lifetime);
    }
}
```

> 可改为用协程 WaitForSeconds + Destroy（取决于项目偏好）。直接用 Destroy(gameObject, delay) 最简单。

### 3.3 新增 MeleeAttackConfigSO.cs (独立文件)

```csharp
using UnityEngine;

/// <summary>近战视觉表现模式</summary>
public enum MeleeVisualMode
{
    Rectangle,  // 半透明矩形（当前实现）
    Animation   // Animator 驱动（预留）
}

/// <summary>
/// 近战攻击配置 ScriptableObject
/// 策划在 Inspector 调整所有近战参数，无需改代码
/// CreateAssetMenu: Game/MeleeAttackConfig
/// </summary>
[CreateAssetMenu(fileName = "MeleeAttackConfig", menuName = "Game/MeleeAttackConfig")]
public class MeleeAttackConfigSO : ScriptableObject
{
    [Header("攻击盒")]
    [Tooltip("攻击宽度（水平方向，单位）")]
    public float attackWidth = 1.5f;
    [Tooltip("攻击高度（垂直方向，单位）")]
    public float attackHeight = 1.5f;
    [Tooltip("攻击距离（从玩家中心前推的偏移，单位）")]
    public float attackRange = 1.0f;
    [Tooltip("垂直偏移（向上微调攻击盒中心）")]
    public float verticalOffset = 0.3f;

    [Header("视觉")]
    [Tooltip("视觉表现模式")]
    public MeleeVisualMode visualMode = MeleeVisualMode.Rectangle;
    [Tooltip("矩形颜色（含透明度）")]
    public Color rectangleColor = new Color(1f, 0.3f, 0f, 0.4f);
    [Tooltip("矩形存活时间（秒）")]
    public float rectangleLifetime = 0.15f;

    [Header("动画（预留）")]
    [Tooltip("动画触发名 — visualMode==Animation 时调用 Animator.SetTrigger 的参数")]
    public string animatorTriggerName = "MeleeAttack";
    [Tooltip("伤害判定窗口（秒）— Animation 模式下 AnimationEvent 回调的有效窗口")]
    public float damageWindow = 0.1f;

    [Header("切换")]
    [Tooltip("备用切换键（None = 不用）")]
    public KeyCode alternateSwitchKey = KeyCode.Tab;
}
```

### 3.4 Events.cs — 新增事件

在 Events.cs 末尾追加:

```csharp
// ============================================================
// 近战攻击模式切换事件
// ============================================================

/// <summary>攻击模式切换事件 — HUD 模式图标订阅此事件更新显示</summary>
public readonly struct AttackModeSwitchedEvent
{
    public readonly PlayerCombat.AttackMode newMode;

    public AttackModeSwitchedEvent(PlayerCombat.AttackMode newMode)
    {
        this.newMode = newMode;
    }
}
```

### 3.5 PlayerHUD.cs — HUD 模式图标

**新增字段 (紧接现有 mpText 字段)**:

```csharp
[Header("攻击模式")]
[SerializeField] private Image attackModeIcon;
[SerializeField] private Sprite rangedModeSprite;  // 远程图标
[SerializeField] private Sprite meleeModeSprite;   // 近战图标
```

**OnEnable / OnDisable 新增订阅**:

```csharp
void OnEnable()
{
    EventBus.Subscribe<PlayerHealthChangedEvent>(OnHPChanged);
    EventBus.Subscribe<PlayerManaChangedEvent>(OnMPChanged);
    EventBus.Subscribe<AttackModeSwitchedEvent>(OnAttackModeSwitched);  // 新增
}

void OnDisable()
{
    EventBus.Unsubscribe<PlayerHealthChangedEvent>(OnHPChanged);
    EventBus.Unsubscribe<PlayerManaChangedEvent>(OnMPChanged);
    EventBus.Unsubscribe<AttackModeSwitchedEvent>(OnAttackModeSwitched);  // 新增
}
```

**新增回调**:

```csharp
void OnAttackModeSwitched(AttackModeSwitchedEvent e)
{
    if (attackModeIcon == null) return;
    attackModeIcon.sprite = e.newMode == PlayerCombat.AttackMode.Melee
        ? meleeModeSprite
        : rangedModeSprite;
}
```

### 3.6 PlayerController.cs — 最小改动

**需要改动（仅1处）**:

在 `UpdateSubModules()` 中的 `combat?.OnPlayerUpdate(this)` 调用保持不变——因为滚轮检测已经内置在 PlayerCombat.OnPlayerUpdate 内部了。

**如果需要阻止 UI 滚轮干扰**: 可以考虑在 PanelManager 打开时设置一个标志 `ScrollBlocked`，PlayerCombat 的 `CheckModeSwitch()` 检查此标志:

```csharp
// PlayerController 新增:
public bool ScrollBlocked { get; set; }

// PlayerCombat.CheckModeSwitch() 开头加:
if (owner != null && owner.ScrollBlocked) return;
```

---

## 四、MeleeAttackConfig.asset 创建清单

### 4.1 资产位置

```
Assets/ScriptableObjects/MeleeAttackConfig.asset
```

### 4.2 推荐初始参数

| 字段 | 初始值 | 理由 |
|------|--------|------|
| attackWidth | 1.5 | 与 EnemyMeleeAttack 保持一致 |
| attackHeight | 1.5 | 覆盖玩家前方站立高度 |
| attackRange | 1.0 | 从玩家中心前推1单位 |
| verticalOffset | 0.3 | 与子弹生成位置一致 |
| visualMode | Rectangle | 当前实现 |
| rectangleColor | (1, 0.3, 0, 0.4) | 橙红色半透明 |
| rectangleLifetime | 0.15 | 1/6秒，足够肉眼可见 |
| animatorTriggerName | "MeleeAttack" | 以后接动画用 |
| damageWindow | 0.1 | 动画伤害帧窗口 |
| alternateSwitchKey | Tab | 滚轮外的备用键 |

### 4.3 Unity Editor 创建步骤

1. Project 窗口 → `Assets/ScriptableObjects/` 目录
2. 右键 → Create → Game → MeleeAttackConfig
3. 命名为 `MeleeAttackConfig`
4. 按上表填入参数
5. 拖入 Player GameObject 上 PlayerCombat 组件的 `meleeConfig` 字段

---

## 五、场景改动清单

### 5.1 Player GameObject — Inspector

**PlayerCombat 组件新增字段**:
- `Attack Mode` → 默认 Ranged（下拉枚举）
- `Melee Config` → 拖入 `MeleeAttackConfig.asset`

> 不需要新增组件。MeleeRectAutoDestroy 是运行时动态添加的临时组件。

### 5.2 Canvas / PlayerHUD GameObject — Inspector

**PlayerHUD 组件新增字段**:
- `Attack Mode Icon` → 拖入一个 Image UI 元素
- `Ranged Mode Sprite` → 拖入远程图标
- `Melee Mode Sprite` → 拖入近战图标

**需要在 Canvas 下新增一个 Image 子对象**:
```
Canvas
└── HUD (PlayerHUD 组件)
    └── AttackModeIcon (Image, 放到血条蓝条旁边)
        ├── 锚点: 左上或右上
        ├── 大小: 48×48 (建议)
        └── 初始 Sprite: rangedModeSprite
```

### 5.3 Physics 2D 设置确认

无需修改。`OverlapBoxAll` 依赖 `enemyLayer`（PlayerCombat 已有），不需要新增 Layer。

---

## 六、切换交互规格

### 6.1 切换方式

| 输入 | 行为 | 优先级 |
|------|------|--------|
| 鼠标滚轮**上滚** | 切换到近战模式 | 主切换 |
| 鼠标滚轮**下滚** | 切换到远程模式 | 主切换 |
| Tab 键 | 近战/远程 toggle（由 MeleeAttackConfigSO.alternateSwitchKey 配置） | 备用 |

### 6.2 切换行为

- **战斗中可切换**: 不需要退出战斗态
- **Dash 中可切换**: 滚轮切换不走攻击CD，仅修改 attackMode 字段
- **切换不重置CD**: 切换后 attackCooldownTimer 保持当前值
- **UI 打开时**: 如果实现了 ScrollBlocked 标志，PanelManager 打开时阻止切换

### 6.3 HUD 反馈

- 切换瞬间 → `AttackModeSwitchedEvent` → PlayerHUD 更新图标
- 近战模式图标: 剑/拳等近战图标
- 远程模式图标: 弓/枪等远程图标
- 两个 Sprite 建议用白色剪影 + 模式切换时改变颜色（近战=暖色，远程=冷色）

### 6.4 攻击行为差异

| 行为 | 远程 (Ranged) | 近战 (Melee) |
|------|--------------|-------------|
| 左键触发 | BurstFire() → PlayerProjectile.Spawn | ExecuteMeleeAttack() → OverlapBox |
| 伤害判定 | 子弹飞行碰撞 (延迟) | 即时 OverlapBox (零延迟) |
| 受攻速影响 | 是 (burstInterval) | 是 (attackCooldown) |
| 受伤害倍率影响 | 是 (GetEffectiveDamage) | 是 (同一方法) |
| 视觉反馈 | 子弹 SpriteRenderer | 矩形 SpriteRenderer (临时) |
| 触发战斗态 | 是 | 是 |

---

## 七、动画替换路径（预留）

### 7.1 切换流程

当后续 `visualMode` 从 `Rectangle` 改为 `Animation` 时:

```
MeleeAttackConfigSO.visualMode = Animation
  ↓
ExecuteMeleeAttack() Step2 走 Animation 分支:
  ├─ animator.SetTrigger(meleeConfig.animatorTriggerName)
  ├─ 伤害判定**不**在 ExecuteMeleeAttack 中执行
  ├─ 动画播放到伤害帧时 → AnimationEvent → OnMeleeDamageWindow()
  └─ Rectangle fallback 保留（动画缺失时不白屏）
```

### 7.2 需要额外准备的资源

1. **Animator Controller** — 添加 "MeleeAttack" Trigger 参数
2. **近战动画 Clip** — 带 AnimationEvent 标记伤害帧
3. **OnMeleeDamageWindow()** 已经实现（见 3.1），无需额外代码

### 7.3 矩形 Fallback 保留

`visualMode == Animation` 分支内仍调用 `SpawnMeleeRect()` 作为 fallback，确保动画缺失时仍有视觉反馈。

---

## 八、注意事项与边界情况

### 8.1 CD 系统

- 近战和远程**共用同一个** `attackCooldownTimer`（0.3s）
- 攻速修饰器对两种模式同样生效（`GetAttackSpeedMultiplier()` 在冷却递减中调用）
- 如果以后需要近战独立的 attackCooldown，在 MeleeAttackConfigSO 加 `cooldownOverride` 字段即可

### 8.2 伤害与碰撞

- `OverlapBoxAll` 检测 `enemyLayer` —— 与子弹同层
- 对每个命中的敌人调用 `enemy.TakeDamage(damage, knockbackDir)`
- 需要确认 `EnemyControllerBase` 有 `TakeDamage(float, Vector2)` 方法（从 `EnemyMeleeAttack.cs L53` 的使用推测存在）
- 如果没有击退版 `TakeDamage`，退化为单参数版本

### 8.3 性能

- `Texture2D.CreateRectSprite()` 用静态缓存，全游戏生命周期仅创建一次
- `MeleeRectAutoDestroy` 用 `Destroy(obj, delay)` 最简方案
- 如需更严格性能控制，后续改用 `ObjectPool<GameObject>` 管理矩形对象

### 8.4 Save/Load（存档预留）

- `attackMode` 是运行时字段，建议存档时记录，Load 时恢复
- SaveSystem 序列化时加 `"attackMode": "Melee"` 字段

### 8.5 与 Skill 系统交互

- 近战攻击与技能系统**解耦**——近战不走 SkillManager 的冷却/法力管线
- 近战左键是纯普通攻击，不消耗法力
- 技能快捷键 (Q/E/R/F) 的检测在 SkillManager.CheckHotkeys() 中，与 PlayerCombat 输入并行

---

## 九、改动量估算

| 文件 | 操作 | 新增行数 | 修改行数 |
|------|------|---------|---------|
| PlayerCombat.cs | 修改 | ~110 | ~25 |
| MeleeRectAutoDestroy.cs | 新增 | ~15 | 0 |
| MeleeAttackConfigSO.cs | 新增 | ~30 | 0 |
| Events.cs | 修改 | ~12 | 0 |
| PlayerHUD.cs | 修改 | ~18 | ~5 |
| PlayerController.cs | 可选修改 | ~2 | 0 |
| **合计** | | **~187** | **~30** |

> PlayerController 的 `ScrollBlocked` 属性可推迟到 UI 面板实现后再加，当前版本可跳过。
