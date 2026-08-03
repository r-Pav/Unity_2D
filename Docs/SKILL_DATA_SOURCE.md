# 技能数据来源文档

> 文档类型：技术参考文档
> 产出日期：2026-07-15
> 用途：说明所有技能数据的创建方式、数据结构、运行时流向、存档机制
> 读者：策划 / 新接手的 programmer

---

## 1. 数据总览

技能系统有 **5 种 ScriptableObject** 作为原始数据（静态数据），以及 **运行时状态** 由各 Manager 在内存中维护。

```
静态数据（ScriptableObject .asset 文件，放在 Resources/ 下）
  ├── ActiveSkillData        主动分支技能（Q/E，可升级 Lv1→Lv3）
  ├── PassiveSkillData       被动装备技能（25个：5线×5层，不可升级）
  ├── CombinationSkillData   组合合成技能（消耗两个材料合成，Lv2固定）
  ├── WeaponSkillData        武器专属技能（装备武器时获得，Lv1固定）
  └── SynergyConfig          协同联动配置（所有技能最低等级匹配 bonus）

运行时数据（内存 + PlayerPrefs 存档）
  ├── SkillManager          技能槽位、等级、冷却、法力
  ├── SkillPointManager     技能点数
  ├── PassiveEquipManager   被动槽位装备（5层×3槽 = 15槽）
  ├── BranchUpgradeSystem   分支选择状态（chosenBranch）
  ├── WeaponSkillLink       当前武器技能
  └── SaveSystem            JSON 存档读写
```

---

## 2. ScriptableObject 数据详解

### 2.1 SkillData（基类）

所有技能 SO 的基类。文件：`Assets/Scripts/Skills/SkillData.cs`

```csharp
[CreateAssetMenu(fileName = "Skill_", menuName = "Game/SkillData")]
public class SkillData : ScriptableObject
{
    // 基础信息
    public string skillName;        // 技能名称
    public string description;      // 技能描述
    public Sprite icon;             // 技能图标

    // 输入
    public KeyCode hotkey;          // 激活快捷键（Q/E 等）

    // 消耗与冷却
    public float cooldown;          // 冷却时间（秒）
    public float manaCost;          // 法力消耗

    // 分类
    public SkillType type;          // Active / Passive / Toggle
    public SkillCategory category;  // Attack / Movement / Defense / Support / Passive

    // 进阶
    public int unlockLevel;         // 解锁玩家等级
    public float castTime;          // 施法时间

    // 等级
    public int skillLevel = 1;      // 当前等级
    public int maxLevel = 5;        // 最大等级

    // 表现
    public GameObject vfxPrefab;    // 特效预制体
    public AudioClip sfxClip;       // 音效
}
```

**枚举类型：**
- `SkillType`：Active（按键触发+冷却）/ Passive（始终生效）/ Toggle（按键开关+持续消耗）
- `SkillCategory`：Attack / Movement / Defense / Support / Passive

### 2.2 ActiveSkillData（主动分支技能）

继承 `SkillData`。文件：`Assets/Scripts/Skills/ActiveSkillData.cs`

**数据结构：**

```
ActiveSkillData : SkillData
  ├── lv1Data        ActiveBranchData  ← Lv1 基础形态
  ├── lv2Left        ActiveBranchData  ← Lv2 左分支
  ├── lv2Right       ActiveBranchData  ← Lv2 右分支
  ├── lv3Left        ActiveBranchData  ← Lv3 左分支（lv2Left 升级线）
  ├── lv3Right       ActiveBranchData  ← Lv3 右分支（lv2Right 升级线）
  └── chosenBranch   string (运行时)   ← null / "Left" / "Right"，不序列化到 SO

ActiveBranchData（子结构，每个等级/分支一组独立参数）
  ├── branchName     string    分支显示名称（如「散射弹幕」「穿透狙击」）
  ├── damage         float     伤害值
  ├── cooldown       float     冷却时间（覆盖基类 cooldown）
  ├── manaCost       float     法力消耗（覆盖基类 manaCost）
  └── description    string    分支选择弹窗中显示的描述文本
```

**升级流程（BranchUpgradeSystem）：**
```
Lv1 → Lv2：消耗 1 SP → 弹窗选择 Left/Right → chosenBranch 记录
Lv2 → Lv3：消耗 2 SP → 根据 chosenBranch 自动升级到对应 Lv3 分支
分支选择不可逆，选后另一侧级联锁定
```

**当前 SO 资产：**

| 文件名 | 技能名 | 快捷键 | 位置 |
|--------|--------|--------|------|
| `Skill_Active_Q.asset` | 能量球 | Q | `Assets/Resources/Skills/Active/` |
| `Skill_Active_E.asset` | 冲进步 | E | `Assets/Resources/Skills/Active/` |

### 2.3 PassiveSkillData（被动装备技能）

继承 `SkillData`。文件：`Assets/Scripts/Skills/PassiveSkillData.cs`

**数据结构：**

```
PassiveSkillData : SkillData
  ├── layer    int (1~5)      层级（TI~TV）
  ├── lineId   int (0~4)      线ID（0=HP恢复, 1=伤害+攻速, 2=移速+闪避, 3=减伤+控制, 4=法力+CD）
  └── effects  PassiveEffect[]  属性效果数组

PassiveEffect（子结构）
  ├── targetStat   string        目标属性 ID（如 StatId.MaxHealth）
  ├── value        float         修饰值（Percent=比率, Flat=绝对值）
  └── type         ModifierType  Percent / Flat
```

**当前 SO 资产：25 个，命名规则 `Passive_L{layer}_L{lineId}.asset`**

| Layer | 文件名 | Line 0 | Line 1 | Line 2 | Line 3 | Line 4 |
|-------|--------|--------|--------|--------|--------|--------|
| 1 (TI) | `Passive_L1_L0~4.asset` | HP+1% | 伤害+8% | 移速+6% | 减伤+5% | 法力恢复+1% |
| 2 (TII) | `Passive_L2_L0~4.asset` | HP+2% | 伤害+15% | 移速+12% | 减伤+10% | 法力恢复+2%/+20法力 |
| 3 (TIII) | `Passive_L3_L0~4.asset` | HP+3% | 伤害+22%+攻速10% | 移速+18%+闪避15% | 减伤+15%+硬直-20% | 法力恢复+3%+22法力+CD-5% |
| 4 (TIV) | `Passive_L4_L0~4.asset` | HP+4% | 伤害+28%+攻速15% | 移速+24%+闪避20% | 减伤+20%+控制-25% | 法力恢复+4%+25法力+CD-8% |
| 5 (TV) | `Passive_L5_L0~4.asset` | HP+5% | 伤害+35%+攻速20% | 移速+30%+闪避30% | 减伤+25%+低血加防15% | 法力恢复+5%+30法力+CD-10%+法耗-3% |

所有 25 个 SO 存放在 `Assets/Resources/Skills/Passive/` 下。

### 2.4 CombinationSkillData（组合合成技能）

继承 `SkillData`。文件：`Assets/Scripts/Skills/CombinationSkillData.cs`

**数据结构：**

```csharp
CombinationSkillData : SkillData
{
    int combinationLevel = 2;   // 固定 Lv2，不可升级
    string effectType;          // 效果类型描述
    bool destroyOnUse = false;  // 使用后是否消耗
}
```

**当前 SO 资产：**

| 文件名 | 说明 | 位置 |
|--------|------|------|
| `Skill_Combo_DualSynergy.asset` | 双重协同 | `Assets/Resources/Skills/Combo/` |
| `Skill_Combo_LawDomain.asset` | 法则领域 | `Assets/Resources/Skills/Combo/` |
| `Skill_Combo_FinalJudgment.asset` | 终末审判 | `Assets/Resources/Skills/Combo/` |

### 2.5 WeaponSkillData（武器专属技能）

继承 `SkillData`。文件：`Assets/Scripts/Skills/WeaponSkillData.cs`

**数据结构：**

```csharp
WeaponSkillData : SkillData
{
    WeaponType weaponType;   // Sword/Bow/Staff/Hammer/DualBlades
    float damageBase;        // 基础伤害
    string effectDescription; // 效果描述
    // level = 1 固定，不可升级
}
```

**当前 SO 资产：**

| 文件名 | 武器 | 位置 |
|--------|------|------|
| `Skill_Weapon_Sword.asset` | 剑 | `Assets/Resources/Skills/Weapon/` |
| `Skill_Weapon_Bow.asset` | 弓 | `Assets/Resources/Skills/Weapon/` |
| `Skill_Weapon_Staff.asset` | 法杖 | `Assets/Resources/Skills/Weapon/` |
| `Skill_Weapon_Hammer.asset` | 大锤 | `Assets/Resources/Skills/Weapon/` |
| `Skill_Weapon_DualBlades.asset` | 双刀 | `Assets/Resources/Skills/Weapon/` |

---

## 3. SO 创建方式

### 3.1 主动技能（Q/E）

**方式：Unity Editor 菜单工具**

菜单栏 → `Tools → Create ActiveSkillData Assets (Q/E)`

执行流程：
1. 删除已有的 `Skill_Active_Q.asset` / `Skill_Active_E.asset`
2. 用 `ScriptableObject.CreateInstance<ActiveSkillData>()` 创建新实例
3. 硬编码数值（来源：`Docs/策划案_P3_主动.txt`）
4. 保存到 `Assets/Resources/Skills/Active/`

**实现文件：** `Assets/Scripts/Editor/CreateActiveSkillDataAssets.cs`

**数值修改方式：** 
- 改 `CreateActiveSkillDataAssets.cs` 中的硬编码数值
- 或者在 Inspector 中直接修改 `.asset` 文件

### 3.2 被动技能（25个）

**方式：Unity Editor 菜单工具**

菜单栏 → `Tools → Create All PassiveSkillData Assets`

执行流程：
1. 双层循环：5层 × 5线 = 25 个
2. 每个用 `ScriptableObject.CreateInstance<PassiveSkillData>()` 创建
3. 数值从代码内常量数组读取（`DamageLineValues[]` / `MoveSpeedValues[]` 等）
4. 保存到 `Assets/Resources/Skills/Passive/`

**实现文件：** `Assets/Scripts/Editor/CreatePassiveSkillDataAssets.cs`

**数值修改方式：**
- 改 `CreatePassiveSkillDataAssets.cs` 中的 `DamageLineValues` / `MoveSpeedValues` 等静态常量数组
- 或者在 Inspector 中修改单个 `.asset` 的 `effects` 数组

### 3.3 组合技能

手动创建：右键 → `Create → Game → SkillData → Combination`

或者在已有 `.asset` 的 Inspector 中修改。

### 3.4 武器技能

手动创建：右键 → `Create → Game → SkillData → Weapon`

或者在已有 `.asset` 的 Inspector 中修改。

---

## 4. 运行时数据流

### 4.1 组件依赖图

```
Player GameObject
  ├── SkillManager          ← 核心技能管理器
  │     ├── skillSlots[4]   ← 每个槽位拖入 SkillData SO
  │     ├── slotLevels[4]   ← 运行时等级（序列化到存档）
  │     ├── cooldownTimers[4]← 冷却计时器（不存档）
  │     └── currentMana     ← 法力值（不存档）
  │
  ├── SkillPointManager     ← 技能点管理
  │     └── currentSkillPoints ← 运行时点数（存档）
  │
  ├── PassiveEquipManager   ← 被动装备管理
  │     ├── allPassiveData[]← 全部 25 个 PassiveSkillData SO（Inspector 拖入）
  │     ├── slots[5][3]     ← 5层×3槽，存 lineId
  │     └── dataIndex       ← (layer, lineId) → SO 快速查找
  │
  ├── BranchUpgradeSystem   ← 分支升级（SkillManager 子模块）
  │     └── pendingSlotIndex← 等待分支选择的槽位（不存档）
  │
  ├── WeaponSkillLink       ← 武器技能联动
  │
  ├── CombinationCraftSystem← 组合合成系统
  │
  └── SaveSystem            ← 存档读写
        └── PlayerPrefs("PlayerSkillSave") → JSON
```

### 4.2 技能激活流程

```
1. 玩家按 Q/E
   ↓
2. SkillManager.CheckHotkeys()
   → Input.GetKeyDown(slot.data.hotkey)
   ↓
3. SkillManager.TryActivate(index)
   → 检查冷却 → 检查法力 → 扣法力 → 设冷却
   ↓
4. EventBus.Trigger(SkillActivatedEvent)
   → Phase 2 具体技能逻辑（BarrierSkill / Projectile 等）订阅执行
```

### 4.3 被动装备流程

```
1. 玩家点击被动槽位 → OpenLineDialog → 选线
   ↓
2. PassiveEquipManager.EquipPassive(layer, lineId, slotIndex)
   → 查找 (layer, lineId) 对应的 PassiveSkillData
   → 遍历 effects[] → 创建 Modifier → 送入 StatModifierManager
   ↓
3. StatModifierManager.AddModifier(mod)
   → 根据 targetStat 重算最终属性值
   ↓
4. EventBus.Trigger(PlayerStatRecalculatedEvent)
   → HUD / 移动速度等订阅方更新
```

### 4.4 分支升级流程

```
1. 玩家点击 SkillTreeUI 的升级按钮
   ↓
2. SkillManager.LevelUp(slotIndex)
   → 判断是 ActiveSkillData → 委托 BranchUpgradeSystem.TryUpgrade()
   ↓
3. BranchUpgradeSystem.TryUpgrade()
   └─ Lv1→Lv2：扣 SP → 设 pendingSlotIndex → 等待 UI 弹窗选择
   └─ Lv2→Lv3：扣 SP → 根据 chosenBranch 直接 ApplyLevelUp()
   ↓
4. SkillTreeUI.ShowBranchDialog() ← 检测 IsWaitingForBranchChoice
   → 弹窗显示 Lv2 左/右分支数据（branchName + description + cooldown/manaCost）
   ↓
5. 玩家选分支 → BranchUpgradeSystem.OnBranchChosen(slotIndex, "Left"/"Right")
   → 记录 chosenBranch（不可逆）→ ApplyLevelUp(2)
   → EventBus.Trigger(BranchChosenEvent) → SkillTreeUI 关闭弹窗+刷新
```

---

## 5. Inspector 配置清单

### 5.1 SkillManager

| 字段 | 操作 |
|------|------|
| `skillSlots[0]` | 拖入 `Skill_Active_Q.asset` |
| `skillSlots[1]` | 拖入 `Skill_Active_E.asset` |
| `skillSlots[2~3]` | 留空（供组合技能产出） |
| `maxMana` | 默认 100 |
| `manaRegenPerSec` | 默认 5 |
| `synergyConfig` | 可选 SO |
| `initialSkillPoints` | 默认 10 |

### 5.2 PassiveEquipManager

| 字段 | 操作 |
|------|------|
| `allPassiveData[0~24]` | 拖入全部 25 个 `Passive_L*.asset`，按任意顺序 |
| `unlockLevels` | `[1, 5, 8, 12, 16]` |

### 5.3 CombinationCraftSystem

| 字段 | 拖入 |
|------|------|
| `recipeLv1` | `Skill_Combo_DualSynergy.asset` |
| `recipeLv2` | `Skill_Combo_LawDomain.asset` |
| `recipeLv3` | `Skill_Combo_FinalJudgment.asset` |

---

## 6. 存档系统

### 6.1 存档内容

`SaveSystem` 挂 Player GameObject，数据以 JSON 存入 `PlayerPrefs["PlayerSkillSave"]`。

**存档结构：**
```json
{
  "skillPoints": 5,
  "slotData": [
    { "skillName": "能量球", "level": 2, "chosenBranch": "Right" },
    { "skillName": "冲进步", "level": 1, "chosenBranch": null },
    { "skillName": "", "level": 0, "chosenBranch": null },
    { "skillName": "", "level": 0, "chosenBranch": null }
  ],
  "passiveLayers": [
    { "lineIds": [0, -1, -1] },
    { "lineIds": [-1, -1, -1] },
    { "lineIds": [-1, 2, -1] },
    { "lineIds": [-1, -1, -1] },
    { "lineIds": [4, -1, -1] }
  ],
  "weapon": { "exists": true, "skillName": "剑·连斩", "weaponType": 0, "consumed": false }
}
```

### 6.2 存档时机

- 手动调用 `SaveSystem.SaveGame()`
- 暂未接入场景切换/退出自动存档（后续扩展）

### 6.3 读档恢复

`SaveSystem.LoadGame()` 恢复流程：
1. 从 PlayerPrefs 读 JSON
2. 恢复 SkillPointManager 技能点
3. 按 skillName 匹配 SO 引用 → 恢复技能槽位+等级+分支
4. 按 passiveLayers 数据 → 逐层逐槽 EquipPassive
5. 恢复武器 consumed 标记

**注意：** 存档不包含法力/冷却计时器（运行时状态，不从存档恢复）。

---

## 7. 数据创建完整流程（给策划）

### 新增一个主动技能（3分支，Lv1→Lv2分支→Lv3）

1. 编辑 `CreateActiveSkillDataAssets.cs`，在 `CreateAll()` 中添加新技能的创建方法
2. 或：右键 `Create → Game → SkillData → Active`，在 Inspector 中手动填写所有字段
3. 保存 SO 到 `Assets/Resources/Skills/Active/`
4. 在 SkillManager Inspector 中将新 SO 拖入空闲槽位

### 修改被动数值

1. 编辑 `CreatePassiveSkillDataAssets.cs` 中的数值常量数组
2. 菜单栏 → `Tools → Create All PassiveSkillData Assets`（重新批量生成）
3. 或：直接在 Inspector 中修改单个 `.asset` 的 `effects` 值

### 新增组合技能配方

1. 右键 `Create → Game → SkillData → Combination`
2. 填写参数 → 保存到 `Assets/Resources/Skills/Combo/`
3. 在 `CombinationCraftSystem` Inspector 中绑定 recipe

---

## 8. 文件索引

| 类别 | 文件名 | 路径 |
|------|--------|------|
| 基类 | `SkillData.cs` | `Assets/Scripts/Skills/` |
| 主动技能 | `ActiveSkillData.cs` | `Assets/Scripts/Skills/` |
| 被动技能 | `PassiveSkillData.cs` | `Assets/Scripts/Skills/` |
| 组合技能 | `CombinationSkillData.cs` | `Assets/Scripts/Skills/` |
| 武器技能 | `WeaponSkillData.cs` | `Assets/Scripts/Skills/` |
| 技能槽 | `SkillSlot.cs` | `Assets/Scripts/Skills/` |
| Manager | `SkillManager.cs` | `Assets/Scripts/Skills/` |
| 技能点 | `SkillPointManager.cs` | `Assets/Scripts/Skills/` |
| 被动管理 | `PassiveEquipManager.cs` | `Assets/Scripts/Skills/` |
| 分支系统 | `BranchUpgradeSystem.cs` | `Assets/Scripts/Skills/` |
| 存档 | `SaveSystem.cs` | `Assets/Scripts/Skills/` |
| UI-技能树 | `SkillTreeUI.cs` | `Assets/Scripts/UI/` |
| UI-被动 | `PassiveUI.cs` | `Assets/Scripts/UI/` |
| Editor-主动 | `CreateActiveSkillDataAssets.cs` | `Assets/Scripts/Editor/` |
| Editor-被动 | `CreatePassiveSkillDataAssets.cs` | `Assets/Scripts/Editor/` |
| SO-主动 | `Skill_Active_Q/E.asset` | `Assets/Resources/Skills/Active/` |
| SO-被动 | `Passive_L1~5_L0~4.asset` | `Assets/Resources/Skills/Passive/` |
| SO-组合 | `Skill_Combo_*.asset` | `Assets/Resources/Skills/Combo/` |
| SO-武器 | `Skill_Weapon_*.asset` | `Assets/Resources/Skills/Weapon/` |
