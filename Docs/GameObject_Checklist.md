# GameObject 创建清单 — 2D 横板平台动作游戏

> 项目: `G:/unity/Tuanjie project/My project 2D/project`  
> 引擎: 团结引擎 1.8.4（Unity 2D 模板）  
> 基于: CharacterBase.cs、PlayerController.cs、3D_to_2D_Conversion.md  
> 日期: 2026-07-08

---

## 一、场景根结构

```
SampleScene
├── --- MANAGERS ---
│   ├── GameManager (空GameObject)
│   │   └── [脚本] GameManager.cs (待创建)
│   └── EventBus (空GameObject)
│       └── [脚本] EventBus.cs (单例)
├── --- PLAYER ---
│   └── Player
│       └── (见第二节)
├── --- ENEMIES ---
│   └── (敌人实例或空父节点)
├── --- TERRAIN ---
│   ├── Ground (Tilemap 或多个 BoxCollider2D 平台)
│   └── Walls
├── --- UI ---
│   └── Canvas
│       └── (见第五节)
├── --- CAMERA ---
│   └── Main Camera
└── --- POOLS ---
    ├── BulletPool
    └── ObstaclePool
```

---

## 二、Player GameObject

### 2.1 Player 根节点

| GameObject | 父级 | 组件 | 说明 |
|---|---|---|---|
| **Player** | 场景根 | `Rigidbody2D`, `CapsuleCollider2D`, `PlayerController`, `PlayerJump`, `PlayerDash`, `PlayerHealth`, `PlayerCombat`, `PlayerGroundPound`, `SkillManager`, `SpriteRenderer` | 玩家主体 |

**Rigidbody2D 关键字段：**

| 字段 | 推荐值 | 说明 |
|------|--------|------|
| Body Type | Dynamic | |
| Gravity Scale | 3 | 匹配 3D 重力感，后续调参 |
| Constraints | ☑ Freeze Rotation | 防止角色旋转 |
| Interpolate | Interpolate | 平滑物理插值 |
| Collision Detection | Continuous | 防止高速穿透 |

**CapsuleCollider2D 关键字段：**

| 字段 | 推荐值 | 说明 |
|------|--------|------|
| Size | (0.6, 1.5) | 匹配角色身高（试调） |
| Offset | (0, -0.2) | 胶囊中心偏移 |
| Direction | Vertical | |

**PlayerController 关键字段（Inspector 中配置）：**

| 字段 | 推荐值 | 说明 |
|------|--------|------|
| Move Speed | 6 | |
| Jump Force | 7 | |
| Ground Layer | Ground | Layer 3 |
| Ground Check Dist | 0.1 | |
| Wall Check Foot Height | 0.1 | |
| Wall Check Head Height | 1.5 | |
| Wall Check Distance | 0.5 | |
| Wall Slide Speed | 2 | |
| Wall Fast Slide Multiplier | 2 | |
| Wall Climb Speed | 1 | |
| Wall Climb Hold Time | 1 | |
| Vault Up Offset | 2 | |
| Vault Forward Offset | 0.5 | |
| Enable Wall Detection | ☑ | |

**SkillManager 关键字段：**

| 字段 | 推荐值 | 说明 |
|------|--------|------|
| Skill Slots | 4 | 拖入已有 SkillData 或不填（运行时装备） |
| Max Mana | 100 | |

---

### 2.2 Player 子对象

| GameObject | 父级 | 组件 | 说明 |
|---|---|---|---|
| **GroundCheck** | Player | `Transform` | 地面检测起点空节点，位置=脚底 |
| **WallCheckFoot** | Player | `Transform` | 脚部墙检测起点 |
| **WallCheckHead** | Player | `Transform` | 头部墙检测起点 |
| **PlayerSprite** | Player | `SpriteRenderer` | 角色动画 Sprite（可选：独立子对象方便翻转） |
| **AimLine** | Player | `LineRenderer` | 瞄准线（由 PlayerAimLine.cs 控制） |
| **HitEffectRoot** | Player | `Transform` | 受击特效挂载点 |

**子对象位置参考（相对 Player 的 localPosition）：**

| 子对象 | Local Position (x, y, z) | 说明 |
|--------|--------------------------|------|
| GroundCheck | (0, -0.75, 0) | 脚底稍下方（取决于碰撞体高度） |
| WallCheckFoot | (0.3, -0.7, 0) | 面朝方向 × 脚高 |
| WallCheckHead | (0.3, 0.6, 0) | 面朝方向 × 头高 |
| PlayerSprite | (0, 0, 0) | |
| AimLine | (0, 0, 0) | |
| HitEffectRoot | (0, 0, 0) | |

> ⚠️ WallCheckFoot 和 WallCheckHead 的 X 值用 `0.3` 是因为 `wallCheckDistance` 已经覆盖了面朝方向的距离。如果不使用这两个 Transform（设为 None），CharacterBase 会 fallback 到基于 wallCheckFootHeight/wallCheckHeadHeight 的计算。

---

## 三、Enemy 预制体

### 3.1 Enemy 根节点（预制体）

| GameObject | 父级 | 组件 | 说明 |
|---|---|---|---|
| **Enemy** | 场景/预制体根 | `Rigidbody2D`, `CapsuleCollider2D`, `EnemyController`, `SpriteRenderer` | 敌人主体 |

**Rigidbody2D 关键字段：**

| 字段 | 推荐值 | 说明 |
|------|--------|------|
| Body Type | Dynamic | |
| Gravity Scale | 3 | |
| Constraints | ☑ Freeze Rotation | |
| Collision Detection | Discrete | 省性能，敌人不需要 Continuous |

**CapsuleCollider2D 关键字段：**

| 字段 | 推荐值 | 说明 |
|------|--------|------|
| Size | (0.6, 1.5) | 与玩家类似 |
| Direction | Vertical | |

**EnemyController 关键字段（Inspector 中配置）：**

| 字段 | 推荐值 | 说明 |
|------|--------|------|
| Move Speed | 3 | 略慢于玩家 |
| Patrol Range | 5 | |
| Chase Range | 8 | |
| Attack Range | 1.5 | |
| Max Health | 50 | |
| Ground Layer | Ground | |
| Wall Layer | Ground | 敌人物墙共用 Layer |
| Enable Wall Detection | ☐ | 关闭省性能 |

---

### 3.2 Enemy 子对象

| GameObject | 父级 | 组件 | 说明 |
|---|---|---|---|
| **EnemySprite** | Enemy | `SpriteRenderer` | 敌人动画 Sprite |
| **AttackHitBox** | Enemy | `BoxCollider2D`（isTrigger=true）, `EnemyMeleeAttack` 或 `EnemyRangedAttack` | 近战攻击判定区 / 远程射击点 |
| **HealthBar** | Enemy | `SpriteRenderer` 或 `Canvas(World Space)` | 血条（可选） |

---

### 3.3 敌人攻击子组件

| 组件 | 挂载位置 | 关键字段 |
|------|---------|---------|
| `EnemyMeleeAttack` | AttackHitBox 或 Enemy | Damage=15, Cooldown=1.5s, AttackRange=1.5 |
| `EnemyRangedAttack` | Enemy | Damage=10, Cooldown=3s, AttackRange=8, ProjectilePrefab=拖入子弹预制体 |

---

## 四、子弹（Projectile）预制体

### 4.1 PlayerProjectile 预制体

| GameObject | 父级 | 组件 | 说明 |
|---|---|---|---|
| **PlayerBullet** | 预制体根 | `Rigidbody2D`, `CircleCollider2D`, `PlayerProjectile`, `SpriteRenderer` | 玩家子弹 |

**Rigidbody2D 关键字段：**

| 字段 | 值 | 说明 |
|------|-----|------|
| Body Type | Kinematic | 子弹手动位移，不参与重力 |
| Gravity Scale | 0 | |

**CircleCollider2D 关键字段：**

| 字段 | 值 | 说明 |
|------|-----|------|
| Is Trigger | ☑ | 触发检测 |
| Radius | 0.25 | |

**PlayerProjectile 关键字段：**

| 字段 | 推荐值 | 说明 |
|------|--------|------|
| Speed | 10 | |
| Damage | 20 | |
| Life Time | 5 | 子弹存活时间（秒） |

**SpriteRenderer 关键字段：**

| 字段 | 值 | 说明 |
|------|-----|------|
| Sprite | 圆形 Sprite（或代码动态生成） | 详见 §4.3 |
| Color | (1, 1, 0.5, 1) | 淡黄色 |

---

### 4.2 EnemyProjectile 预制体

| GameObject | 父级 | 组件 | 说明 |
|---|---|---|---|
| **EnemyBullet** | 预制体根 | `Rigidbody2D`, `CircleCollider2D`, `EnemyProjectile`, `SpriteRenderer` | 敌人子弹 |

| 字段 | 推荐值 | 说明 |
|------|--------|------|
| Speed | 6 | 比玩家子弹慢 |
| Damage | 10 | |
| Life Time | 5 | |
| Sprite Color | (1, 0.3, 0.3, 1) | 红色 |

---

### 4.3 子弹 Sprite（方案）

在 `Projectile.Awake()` 中程序化生成圆形 Sprite（参见 3D_to_2D_Conversion.md §3.4），**无需在 Inspector 手动配置 Sprite**。但为了开发和调试，可提前准备：

| 素材 | 路径 | 说明 |
|------|------|------|
| `bullet_player.png` | `Assets/Sprites/` | 玩家子弹 64×64 圆形 |
| `bullet_enemy.png` | `Assets/Sprites/` | 敌人子弹 64×64 圆形 |

> 如果使用预置 Sprite，需修改 Projectile.cs 中的 Awake() 逻辑（从"动态生成"改为"SpriteRenderer.sprite = 你的Sprite"）。

---

## 五、ObstacleBall 预制体

| GameObject | 父级 | 组件 | 说明 |
|---|---|---|---|
| **ObstacleBall** | 预制体根 | `Rigidbody2D`, `CircleCollider2D`, `ObstacleBall`, `SpriteRenderer` | 障碍球 |

**Rigidbody2D：**

| 字段 | 值 |
|------|-----|
| Body Type | Dynamic |
| Gravity Scale | 0 |
| Constraints | ☑ Freeze Rotation |

**CircleCollider2D：**

| 字段 | 值 |
|------|-----|
| Is Trigger | ☐ |
| Radius | 由 ObstacleBall.radius 控制 |

**ObstacleBall 关键字段：**

| 字段 | 推荐值 | 说明 |
|------|--------|------|
| Speed | 8 | |
| Radius | 0.5 | |
| Knockback Force | 10 | |
| Life Time | 10 | |
| Ball Color | (0.3, 0.6, 1, 1) | 蓝色 |

---

## 六、Camera

| GameObject | 父级 | 组件 | 说明 |
|---|---|---|---|
| **Main Camera** | 场景根 | `Camera`, `CameraFollow`, `AudioListener` | 主相机 |

**Camera 关键字段：**

| 字段 | 值 | 说明 |
|------|-----|------|
| Projection | Orthographic | 2D 必须正交 |
| Size | 5 | 正交相机半高（调至合适视口） |
| Background | (0.1, 0.1, 0.15, 1) | 深色背景 |
| Near | 0.1 | |
| Far | 100 | |

**CameraFollow 关键字段：**

| 字段 | 推荐值 | 说明 |
|------|--------|------|
| Target | 拖入 Player Transform | |
| Smooth Speed | 5 | |
| Vertical Offset | 1 | |
| Dead Zone X | 0.5 | |
| Dead Zone Y | 0.3 | |
| Shake Intensity | 0.2 | |

---

## 七、地形（Terrain）

### 7.1 地面（Ground）

| GameObject | 父级 | 组件 | 说明 |
|---|---|---|---|
| **Ground** | Terrain | `BoxCollider2D`（或 `TilemapCollider2D` + `CompositeCollider2D`） | 单个平台或地面 |

| 字段 | 值 | 说明 |
|------|-----|------|
| Layer | Ground (3) | |
| Tag | Ground | |
| Static | ☑ | 静态地形不需要 Rigidbody2D |

### 7.2 墙壁（Wall）

| GameObject | 父级 | 组件 | 说明 |
|---|---|---|---|
| **Wall** | Terrain | `BoxCollider2D` | 垂直墙面 |

| 字段 | 值 |
|------|-----|
| Layer | Ground (3) |
| Tag | Wall |
| Static | ☑ |

### 7.3 地形建议方式

- **简单关卡**：直接用 `BoxCollider2D` 拼接平台
- **复杂关卡**：使用 `Tilemap` + `TilemapCollider2D` + `CompositeCollider2D`（团结引擎内置支持）
- **Layer**：所有地形统一使用 `Ground` Layer（Player 的 `groundLayer` 和 `wallLayer` 在 Inspector 中可独立配置，本项目中两者指向同一 Layer）

---

## 八、UI Canvas 结构

| GameObject | 父级 | 组件 | 说明 |
|---|---|---|---|
| **Canvas** | 场景根 | `Canvas`, `CanvasScaler`, `GraphicRaycaster` | UI 根 |

**Canvas 关键字段：**

| 字段 | 值 |
|------|-----|
| Render Mode | Screen Space - Overlay |
| Canvas Scaler → UI Scale Mode | Scale With Screen Size |
| Reference Resolution | 1920 × 1080 |

### 8.1 HUD 子结构

| GameObject | 父级 | 组件 | 说明 |
|---|---|---|---|
| **HUD** | Canvas | `RectTransform`, `PlayerHUD` | HUD 控制器 |
| **HealthBar** | HUD | `Slider`, `Image` | 血条 |
| **ManaBar** | HUD | `Slider`, `Image` | 蓝条 |
| **SkillSlots** | HUD | `HorizontalLayoutGroup` | 技能槽容器 |
| **SkillSlot_0** / **1** / **2** / **3** | SkillSlots | `Image`, `Button`, `Text (TMP)` | 4个技能槽 |
| **SkillCooldown_0** / ... | SkillSlot_X | `Image`（fill 遮罩） | 冷却遮罩 |
| **WeaponIndicator** | HUD | `Image`, `Text (TMP)` | 当前武器图标 |
| **SkillPointsDisplay** | HUD | `Text (TMP)` | 技能点数显示 |
| **PlayerLevelText** | HUD | `Text (TMP)` | 等级显示 |

### 8.2 技能树面板（后续开发）

| GameObject | 父级 | 组件 | 说明 |
|---|---|---|---|
| **SkillTreePanel** | Canvas | `Image`, `SkillTreeUI` | 全屏技能树面板（Tab 打开） |
| **PassiveSection** | SkillTreePanel | `VerticalLayoutGroup` | 被动技能区域（5层） |
| **Tier_I** ~ **Tier_V** | PassiveSection | `ToggleGroup` | 每层的5选3装配 |
| **ActiveSection** | SkillTreePanel | `GridLayoutGroup` | 主动技能分支 |
| **WeaponSection** | SkillTreePanel | `Image` | 武器技能显示 |
| **ComboSection** | SkillTreePanel | `GridLayoutGroup` | 组合技能合成 |
| **CloseButton** | SkillTreePanel | `Button` | 关闭面板 |

---

## 九、技能相关 GameObject

### 9.1 障碍球屏障（BarrierSkill 使用）

| GameObject | 父级 | 组件 | 说明 |
|---|---|---|---|
| **Barrier** | 场景/预制体 | `CircleCollider2D`（isTrigger=false）, `SpriteRenderer` | 屏障碰撞体 |

### 9.2 技能特效根

| GameObject | 父级 | 组件 | 说明 |
|---|---|---|---|
| **SkillEffects** | 场景根 | `Transform` | 技能特效/粒子统一父节点 |

---

## 十、Physics 2D 设置

`Edit → Project Settings → Physics 2D`

### 10.1 全局设置

| 设置项 | 推荐值 | 说明 |
|--------|--------|------|
| Gravity Y | -9.81 | 或调整为 -30 获得更有"手感"的重力 |
| Default Material | None | |
| Queries Hit Triggers | ☑ | |
| Queries Start In Colliders | ☐ | |
| Velocity Iterations | 8 | |
| Position Iterations | 3 | |

### 10.2 Layer Collision Matrix

| Layer A × Layer B | 碰撞？ | 说明 |
|-------------------|:------:|------|
| Player × Ground | ✅ | 玩家站在地面上 |
| Player × Enemy | ✅ | 触发伤害 |
| Enemy × Ground | ✅ | 敌人站在地面上 |
| Obstacle × Ground | ✅ | 障碍球碰墙停下 |
| Obstacle × Enemy | ✅ | 障碍球碰敌人停下+击退 |
| Obstacle × Player | ❌ | 玩家可穿过自己的障碍球 |
| Player × Player | ❌ | 单玩家 |
| Enemy × Enemy | ❌ | 敌人之间不碰撞 |
| PlayerBullet × Enemy | ✅ | 玩家子弹打敌人 |
| EnemyBullet × Player | ✅ | 敌人子弹打玩家 |
| PlayerBullet × Ground | ✅ | 子弹碰墙消失 |
| EnemyBullet × Ground | ✅ | |

### 10.3 Layer 配置

`Edit → Project Settings → Tags and Layers`

| Layer | 编号 | 用途 |
|-------|:----:|------|
| Default | 0 | |
| Ground | 3 | 地面/墙壁 |
| Player | 6 | 玩家 |
| Enemy | 7 | 敌人 |
| Obstacle | 8 | 障碍球/屏障 |
| PlayerBullet | 9 | 玩家子弹 |
| EnemyBullet | 10 | 敌人子弹 |

---

## 十一、创建优先级与顺序

| 阶段 | 创建内容 | 预估时间 |
|:----:|---------|:--------:|
| **P0** | Layers 配置 + Physics 2D 设置 | 5 min |
| **P0** | 基础地形（1块 Ground + 1面 Wall） | 5 min |
| **P0** | Player（含所有组件+子对象） | 15 min |
| **P0** | Main Camera（含 CameraFollow） | 5 min |
| **P0** | Canvas + HUD 基础结构 | 10 min |
| **P1** | Enemy 预制体（含子对象） | 10 min |
| **P1** | PlayerBullet 预制体 | 5 min |
| **P1** | EnemyBullet 预制体 | 5 min |
| **P1** | 在地形上放置 Enemy 实例验证 | 5 min |
| **P2** | ObstacleBall 预制体 | 5 min |
| **P2** | Barrier 预制体 | 5 min |
| **P2** | SkillEffects / Effects 父节点 | 2 min |
| **P3** | SkillTreePanel UI（后续Phase） | 待定 |

---

## 附录：完整组件索引

按组件列出所有需挂载的 GameObject：

| 组件 | 挂载到 |
|------|--------|
| `Rigidbody2D` | Player, Enemy, PlayerBullet, EnemyBullet, ObstacleBall |
| `CapsuleCollider2D` | Player, Enemy |
| `BoxCollider2D` | Ground, Wall, AttackHitBox (trigger) |
| `CircleCollider2D` | PlayerBullet, EnemyBullet, ObstacleBall, Barrier |
| `SpriteRenderer` | Player, Enemy, PlayerBullet, EnemyBullet, ObstacleBall |
| `PlayerController` | Player |
| `PlayerJump` | Player (自动创建) |
| `PlayerDash` | Player (自动创建) |
| `PlayerHealth` | Player (自动创建) |
| `PlayerCombat` | Player |
| `PlayerGroundPound` | Player |
| `PlayerAimLine` | Player / AimLine 子对象 |
| `PlayerHitFeedback` | Player |
| `SkillManager` | Player |
| `EnemyController` | Enemy |
| `EnemyMeleeAttack` | Enemy / AttackHitBox |
| `EnemyRangedAttack` | Enemy |
| `PlayerProjectile` | PlayerBullet |
| `EnemyProjectile` | EnemyBullet |
| `ObstacleBall` | ObstacleBall |
| `CameraFollow` | Main Camera |
| `PlayerHUD` | HUD (Canvas 子对象) |
| `CanvasScaler` | Canvas |
