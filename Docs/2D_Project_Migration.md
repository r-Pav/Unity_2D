# 2D 项目迁移方案

> 源项目: `G:/unity/Tuanjie project/My project`（3D 物理横板）  
> 目标项目: `G:/unity/Tuanjie project/My project 2D/project`（2D 模板）  
> 基于: [3D_to_2D_Conversion.md](../My%20project/Docs/3D_to_2D_Conversion.md)  
> 日期: 2026-07-08

---

## 一、迁移总览

```
3D项目(My project)                    2D项目(My project 2D/project)
├─ 复制文件（不改动）        ───→     ├─ 原样复制
├─ 复制文件（API改写）       ───→     ├─ 按转换文档修改
├─ 新建文件                 ───→     ├─ 2D特有资产
└─ 场景重配（手动）         ───→     └─ Inspector中替换组件
```

### 迁移策略

**一次性全量迁移**：3D/2D 物理 API 互斥（Rigidbody 和 Rigidbody2D 无法共生），分阶段会导致中间态不可运行。

---

## 二、从 3D 项目复制的文件清单

### 2.1 不改动的文件（原样复制）

以下文件**不包含任何 3D 物理 API 调用**，可直接从 3D 项目复制到 2D 项目。

#### 框架层 (Framework/)

| 源路径 | 目标路径 | 说明 |
|--------|---------|------|
| `Assets/Scripts/Framework/FSM.cs` | `Assets/Scripts/Framework/FSM.cs` | 状态机框架 |
| `Assets/Scripts/Framework/EventBus.cs` | `Assets/Scripts/Framework/EventBus.cs` | 事件总线 |
| `Assets/Scripts/Framework/ObjectPool.cs` | `Assets/Scripts/Framework/ObjectPool.cs` | 通用对象池 |

#### 技能系统 (Skills/)

| 源路径 | 目标路径 | 说明 |
|--------|---------|------|
| `Assets/Scripts/Skills/SkillManager.cs` | `Assets/Scripts/Skills/SkillManager.cs` | 技能管理器 |
| `Assets/Scripts/Skills/SkillData.cs` | `Assets/Scripts/Skills/SkillData.cs` | 技能数据 SO |
| `Assets/Scripts/Skills/SkillSlot.cs` | `Assets/Scripts/Skills/SkillSlot.cs` | 技能槽 |
| `Assets/Scripts/Skills/ISkill.cs` | `Assets/Scripts/Skills/ISkill.cs` | 技能接口 |
| `Assets/Scripts/Skills/BarrierSkill.cs` | `Assets/Scripts/Skills/BarrierSkill.cs` | 屏障技能 |
| `Assets/Scripts/Skills/BarrierSkillData.cs` | `Assets/Scripts/Skills/BarrierSkillData.cs` | 屏障数据 |
| `Assets/Scripts/Skills/SynergyConfig.cs` | `Assets/Scripts/Skills/SynergyConfig.cs` | 联动配置 |

#### UI

| 源路径 | 目标路径 | 说明 |
|--------|---------|------|
| `Assets/Scripts/UI/PlayerHUD.cs` | `Assets/Scripts/UI/PlayerHUD.cs` | 玩家HUD |
| `Assets/Scripts/Player/PlayerAimLine.cs` | `Assets/Scripts/Player/PlayerAimLine.cs` | 瞄准线 |
| `Assets/Scripts/Player/PlayerHitFeedback.cs` | `Assets/Scripts/Player/PlayerHitFeedback.cs` | 命中反馈 |

#### 效果/战斗

| 源路径 | 目标路径 | 说明 |
|--------|---------|------|
| `Assets/Scripts/Framework/HitStopController.cs` | `Assets/Scripts/Framework/HitStopController.cs` | 击停效果 |
| `Assets/Scripts/Framework/HitEffectManager.cs` | `Assets/Scripts/Framework/HitEffectManager.cs` | 命中特效 |
| `Assets/Scripts/Player/PlayerCombat.cs` | `Assets/Scripts/Player/PlayerCombat.cs` | 玩家战斗 |
| `Assets/Scripts/Enemy/IEnemyAttack.cs` | `Assets/Scripts/Enemy/IEnemyAttack.cs` | 敌人攻击接口 |

#### 其他

| 源路径 | 目标路径 | 说明 |
|--------|---------|------|
| `Assets/Move.cs` | `Assets/Move.cs` | 早期移动脚本 |
| `Assets/SkillData/*.asset` | `Assets/SkillData/*.asset` | 技能SO资产 |
| `Assets/Prefab/SKill_S1.prefab` | `Assets/Prefab/SKill_S1.prefab` | 技能预制体 |
| `Assets/TextMesh Pro/` | `Assets/TextMesh Pro/` | TMP 字体资源 |

#### 设计文档

| 源路径 | 目标路径 |
|--------|---------|
| `Docs/SkillTreeDesign.md` | `Docs/SkillTreeDesign.md` (已更新v2.0) |
| `Docs/SkillSystemDesign.md` | `Docs/SkillSystemDesign.md` |
| `Docs/3D_to_2D_Conversion.md` | `Docs/3D_to_2D_Conversion.md` |

**小计：约 23 个文件，0 处 API 修改。**

---

### 2.2 需 API 改写的文件（复制后按转换文档修改）

以下文件包含 3D 物理 API 调用，复制后需按 [3D_to_2D_Conversion.md](../My%20project/Docs/3D_to_2D_Conversion.md) 逐行修改。

| 优先级 | 源路径 | 目标路径 | 改动量 | 主要变更 |
|:------:|--------|---------|:------:|---------|
| **P0** | `Assets/Scripts/CharacterBase.cs` | `Assets/Scripts/CharacterBase.cs` | ★★★ | 所有物理 API 根：Rigidbody2D、Collider2D、Physics2D.Raycast、constraints |
| **P0** | `Assets/Scripts/Framework/Events.cs` | `Assets/Scripts/Framework/Events.cs` | ★ | 事件结构体 Vector3→Vector2 |
| **P1** | `Assets/Scripts/Player/PlayerController.cs` | `Assets/Scripts/Player/PlayerController.cs` | ★★ | Rigidbody 类型、Vector3 dir、ForceMode |
| **P1** | `Assets/Scripts/Player/PlayerJump.cs` | `Assets/Scripts/Player/PlayerJump.cs` | ★ | Rigidbody 类型 |
| **P1** | `Assets/Scripts/Player/PlayerDash.cs` | `Assets/Scripts/Player/PlayerDash.cs` | ★★ | VelocityChange→直接设速、ForceMode2D |
| **P1** | `Assets/Scripts/Player/PlayerHealth.cs` | `Assets/Scripts/Player/PlayerHealth.cs` | ★★ | Vector3→Vector2、ForceMode2D |
| **P1** | `Assets/Scripts/Player/PlayerGroundPound.cs` | `Assets/Scripts/Player/PlayerGroundPound.cs` | ★★ | Physics2D.Raycast、Rigidbody2D 类型 |
| **P1** | `Assets/Scripts/Enemy/EnemyController.cs` | `Assets/Scripts/Enemy/EnemyController.cs` | ★★★ | RaycastAll、Vector3→Vector2、ForceMode2D |
| **P1** | `Assets/Scripts/Enemy/EnemyRangedAttack.cs` | `Assets/Scripts/Enemy/EnemyRangedAttack.cs` | ★ | Vector3→Vector2 |
| **P1** | `Assets/Scripts/Enemy/EnemyMeleeAttack.cs` | `Assets/Scripts/Enemy/EnemyMeleeAttack.cs` | ★ | Vector3→Vector2 |
| **P2** | `Assets/Scripts/Projectile/Projectile.cs` | `Assets/Scripts/Projectile/Projectile.cs` | ★★★ | Collider、Trigger回调、Mesh→Sprite |
| **P2** | `Assets/Scripts/Projectile/PlayerProjectile.cs` | `Assets/Scripts/Projectile/PlayerProjectile.cs` | ★ | Spawn 签名 Vector3→Vector2 |
| **P2** | `Assets/Scripts/Projectile/EnemyProjectile.cs` | `Assets/Scripts/Projectile/EnemyProjectile.cs` | ★ | Spawn 签名 Vector3→Vector2 |
| **P2** | `Assets/Scripts/Skills/ObstacleBall.cs` | `Assets/Scripts/Skills/ObstacleBall.cs` | ★★★ | Rigidbody、Collision回调、Mesh→Sprite |
| **P2** | `Assets/Scripts/Player/States/WallSlideStateBase.cs` | `Assets/Scripts/Player/States/WallSlideStateBase.cs` | ★ | Rigidbody 类型 |
| **P2** | `Assets/Scripts/Player/States/WallClimbState.cs` | `Assets/Scripts/Player/States/WallClimbState.cs` | ★ | Rigidbody 类型 |
| **P2** | `Assets/Scripts/Player/States/WallJumpState.cs` | `Assets/Scripts/Player/States/WallJumpState.cs` | ★ | ForceMode2D |
| **P2** | `Assets/Scripts/Player/States/WallVaultState.cs` | `Assets/Scripts/Player/States/WallVaultState.cs` | ★ | Rigidbody 类型、Vector3→Vector2 |
| **P3** | `Assets/Scripts/CameraFollow.cs` | `Assets/Scripts/CameraFollow.cs` | ≈0 | 几乎不变（Camera 始终用 Vector3 位置） |

**小计：约 19 个文件，约 80 处 API 变更。**

---

## 三、2D 项目文件夹结构（建议）

```
My project 2D/project/
├── Assets/
│   ├── Scripts/
│   │   ├── CharacterBase.cs              ← P0 先改
│   │   ├── Framework/
│   │   │   ├── FSM.cs                    ← 不改
│   │   │   ├── EventBus.cs               ← 不改
│   │   │   ├── ObjectPool.cs             ← 不改
│   │   │   ├── Events.cs                 ← P0 后改
│   │   │   ├── HitStopController.cs      ← 不改
│   │   │   └── HitEffectManager.cs       ← 不改
│   │   ├── Player/
│   │   │   ├── PlayerController.cs       ← P1
│   │   │   ├── PlayerJump.cs             ← P1
│   │   │   ├── PlayerDash.cs             ← P1
│   │   │   ├── PlayerHealth.cs           ← P1
│   │   │   ├── PlayerGroundPound.cs      ← P1
│   │   │   ├── PlayerCombat.cs           ← 不改
│   │   │   ├── PlayerAimLine.cs          ← 不改
│   │   │   ├── PlayerHitFeedback.cs      ← 不改
│   │   │   └── States/
│   │   │       ├── WallSlideStateBase.cs ← P2
│   │   │       ├── WallSlideState.cs     ← (无独立物理API，继承Base)
│   │   │       ├── WallFastSlideState.cs ← (同上)
│   │   │       ├── WallClimbState.cs     ← P2
│   │   │       ├── WallJumpState.cs      ← P2
│   │   │       └── WallVaultState.cs     ← P2
│   │   ├── Enemy/
│   │   │   ├── EnemyController.cs        ← P1
│   │   │   ├── EnemyRangedAttack.cs      ← P1
│   │   │   ├── EnemyMeleeAttack.cs       ← P1
│   │   │   └── IEnemyAttack.cs           ← 不改
│   │   ├── Projectile/
│   │   │   ├── Projectile.cs             ← P2
│   │   │   ├── PlayerProjectile.cs       ← P2
│   │   │   └── EnemyProjectile.cs        ← P2
│   │   ├── Skills/
│   │   │   ├── SkillManager.cs           ← 不改
│   │   │   ├── SkillData.cs              ← 不改
│   │   │   ├── SkillSlot.cs              ← 不改
│   │   │   ├── ISkill.cs                 ← 不改
│   │   │   ├── BarrierSkill.cs           ← 不改
│   │   │   ├── BarrierSkillData.cs       ← 不改
│   │   │   ├── SynergyConfig.cs          ← 不改
│   │   │   └── ObstacleBall.cs           ← P2
│   │   ├── UI/
│   │   │   └── PlayerHUD.cs              ← 不改
│   │   └── CameraFollow.cs               ← P3
│   ├── Prefab/
│   │   └── SKill_S1.prefab               ← 不改
│   ├── SkillData/
│   │   └── *.asset                       ← 不改
│   ├── Scenes/
│   │   └── SampleScene.scene             ← 2D模板自带
│   ├── TextMesh Pro/
│   │   └── ...                           ← 不改
│   └── (2D 新增文件夹)
│       ├── Sprites/                      ← 角色/敌人/子弹/背景 Sprite
│       ├── Physics Materials 2D/         ← 2D 物理材质（摩擦力/弹性）
│       ├── Animations/                   ← 2D 动画控制器
│       └── Tilemaps/                     ← 2D 瓦片地图（可选）
├── Packages/
│   └── manifest.json
├── ProjectSettings/
│   └── ...
└── Docs/
    ├── SkillTreeDesign.md                ← v2.0
    ├── SkillSystemDesign.md              ← 从3D复制
    ├── 3D_to_2D_Conversion.md            ← 从3D复制
    ├── 2D_Project_Migration.md           ← 本文档
    └── GameObject_Checklist.md           ← 创建清单
```

---

## 四、实施步骤

### 阶段 A：冻结 + 备份（5 min）

```bash
# 在 3D 项目目录
cd "G:/unity/Tuanjie project/My project"
git add -A
git commit -m "backup: before 3D→2D migration"
```

### 阶段 B：基础文件复制（15 min）

**步骤 B1**：从 3D 项目复制"不改动"文件到 2D 项目（保持目录结构）

```bash
# 示例（逐个目录复制）
cp -r "G:/unity/Tuanjie project/My project/Assets/Scripts/Framework/FSM.cs" \
      "G:/unity/Tuanjie project/My project 2D/project/Assets/Scripts/Framework/"
# ... 逐一复制 §2.1 中所有文件
```

> **推荐做法**：在 Unity Editor 中打开两个项目，用文件管理器直接拖拽 `.cs` 文件到目标项目的 `Assets/Scripts/` 对应目录。Unity 会自动生成 `.meta` 文件。

**步骤 B2**：复制"需改写"文件到 2D 项目

```bash
# 同样方式复制 §2.2 中所有文件
cp -r "G:/unity/Tuanjie project/My project/Assets/Scripts/CharacterBase.cs" \
      "G:/unity/Tuanjie project/My project 2D/project/Assets/Scripts/"
# ...
```

### 阶段 C：API 改写（60-90 min）

严格按 [3D_to_2D_Conversion.md](./3D_to_2D_Conversion.md) 逐文件修改。**执行顺序必须按依赖关系从底向上**：

| 顺序 | 文件 | 预估 | 依赖 |
|:----:|------|:----:|------|
| C1 | `CharacterBase.cs` | 20 min | 无（是所有类的基类） |
| C2 | `Events.cs` | 5 min | 无 |
| C3 | `PlayerController.cs` | 10 min | CharacterBase |
| C4 | `PlayerJump.cs` | 3 min | PlayerController |
| C5 | `PlayerDash.cs` | 5 min | PlayerController |
| C6 | `PlayerHealth.cs` | 5 min | PlayerController |
| C7 | `PlayerGroundPound.cs` | 5 min | PlayerController |
| C8 | `EnemyController.cs` | 10 min | CharacterBase |
| C9 | `EnemyRangedAttack.cs` | 3 min | EnemyController |
| C10 | `EnemyMeleeAttack.cs` | 3 min | EnemyController |
| C11 | `WallSlideStateBase.cs` | 3 min | CharacterBase |
| C12 | `WallClimbState.cs` | 3 min | CharacterBase |
| C13 | `WallJumpState.cs` | 3 min | CharacterBase |
| C14 | `WallVaultState.cs` | 3 min | CharacterBase |
| C15 | `Projectile.cs` | 10 min | 无（独立） |
| C16 | `PlayerProjectile.cs` | 3 min | Projectile |
| C17 | `EnemyProjectile.cs` | 3 min | Projectile |
| C18 | `ObstacleBall.cs` | 10 min | 无 |
| C19 | `CameraFollow.cs` | 2 min | 无 |

### 阶段 D：编译验证（30 min）

在 Unity Editor 中打开 2D 项目，逐轮编译修复：

1. 打开 Unity → 等待首次编译
2. 查看 Console 错误，按 **根因优先** 修复
   - 常见错误：`CS0029`（Vector3→Vector2 类型不匹配）
   - `CS1503`（参数类型）、`CS0117`（方法不存在）
   - 参考 [附录 B 编译修复快速参考](./3D_to_2D_Conversion.md#附录-b编译修复快速参考)
3. 每轮修复后重新编译直到 0 errors

### 阶段 E：场景 + Inspector 重配（30-45 min）

> ⚠️ 此阶段**只能手动在 Unity Editor 中操作**，无法通过脚本批量处理。

参见 [GameObject_Checklist.md](./GameObject_Checklist.md) 获取详细的 GameObject 创建和组件清单。核心操作：

1. **替换 Player 上的组件**：Rigidbody→Rigidbody2D, Collider→Collider2D
2. **替换 Enemy 预制体上的组件**：同上
3. **替换地形 Collider**：所有静态地形 Collider→Collider2D
4. **配置 Physics 2D Settings**：Gravity、Layer Collision Matrix
5. **创建 Sprite**：子弹圆形 Sprite、角色 Sprite、背景 Sprite
6. **重新挂载脚本引用**：Inspector 中缺失的引用

### 阶段 F：运行验证（30 min）

逐项 Play 测试：

| 测试项 | 检查点 |
|--------|--------|
| 基础移动 | A/D 键角色水平移动顺畅，无抖动 |
| 跳跃 | Space 跳跃正常，高度/手感合适 |
| 地面检测 | 落地后 grounded=true |
| 贴墙下滑 | 贴墙后下滑、加速下滑 |
| 爬墙 | 按 W 上爬、翻顶 |
| 墙跳 | Space 墙跳方向正确 |
| 冲刺 | Shift 冲刺 |
| 砸地 | Q 砸地 AOE |
| 敌人 AI | 巡逻/追击/攻击 |
| 子弹 | 飞行、命中、回池 |
| 障碍球 | 发射、碰撞、阻挡 |
| 相机 | 跟随、死区、震屏 |

---

## 五、风险与注意事项

### 高风险 🔴

| 风险 | 缓解 |
|------|------|
| **2D 物理手感差异** | 转换后 jumpForce、moveSpeed 等参数需重新调参，预留 1-2h |
| **子弹外观渲染** | 从 Mesh→Sprite 需创建圆形纹理（已有代码方案） |
| **Collider2D 形状不匹配** | CapsuleCollider2D 参数需在场景中手动调整 |
| **Layer 碰撞矩阵** | 3D/2D 物理有独立碰撞矩阵（Project Settings → Physics 2D），必须重新配置 |

### 中风险 🟡

| 风险 | 缓解 |
|------|------|
| ClosestPoint 缺失 | ProjectileHitEvent.hitPoint 用 transform.position 替代 |
| ForceMode.VelocityChange 不存在 | PlayerDash 改用直接赋值 velocity |
| Physics2D.Raycast 无 out 参数 | 改用 RaycastHit2D struct 返回 |

### 不可逆操作 ⚠️

- **先 git commit 备份**：场景文件修改后 3D 组件参数丢失不可恢复
- **Physics Settings 独立**：3D Physics 设置不会自动迁移到 2D

---

## 附录：文件分类速查

| 分类 | 文件数 | 操作 |
|------|:------:|------|
| 零改动（原样复制） | 23 | 直接复制 |
| 小改（★） | 10 | 复制后改 Vector3→Vector2/ForceMode |
| 中改（★★） | 5 | 复制后改多项物理 API |
| 大改（★★★） | 4 | CharacterBase、EnemyController、Projectile、ObstacleBall |
| 新建（2D特有） | N | Sprites、Physics Material 2D、Tilemaps 等 |
