# 技能树系统策划案 — 横板2D平台动作游戏

> 版本: v2.0  
> 日期: 2026-07-08  
> 引擎: 团结引擎 1.8.4  
> 项目: My project（横板2D平台动作游戏）  
> 前置文档: [SkillSystemDesign.md](./SkillSystemDesign.md)（Phase 1 已实现）  
> 本版更新: 同步 DrawIO SkillTree_v2.drawio 被动技能数值（HP恢复替换HP上限、闪避替换跳跃、法力恢复新增等）

---

## 目录

1. [框架总览](#1-框架总览)
2. [数据结构设计](#2-数据结构设计)
3. [策划案核心——技能节点详细设计](#3-策划案核心技能节点详细设计)
4. [与现有 SkillManager 的集成方案](#4-与现有-skillmanager-的集成方案)
5. [UI 交互流程](#5-ui-交互流程)
6. [数据存储方案](#6-数据存储方案)
7. [文件结构规划](#7-文件结构规划)
8. [实现阶段规划](#8-实现阶段规划)

---

## 1. 框架总览

### 1.1 技能树三大类别

```
┌──────────────────────────────────────────────────────────────────────────────┐
│                              技能树总览                                        │
├─────────────────┬────────────────────────────────┬────────────────────────────┤
│   被动技能×5    │         主动技能×3              │       组合技能×3           │
│ (层级解锁+装配) │  (Lv1 → 分叉 → Lv3)            │  (同等级2节点合成)          │
├─────────────────┼────────────────────────────────┼────────────────────────────┤
│                 │  主动一: 能量投射               │  组合一(Lv1素材)           │
│  层级 I (Lv1)   │  Lv1                           │  Lv1 → Lv2                 │
│  1pt 解锁       │  ├─ Lv2左 → Lv3左              │                            │
│  → 5选3装配     │  └─ Lv2右 → Lv3右              │  组合二(Lv2素材)           │
│                 │                                │  Lv1 → Lv2                 │
│  层级 II (Lv2)  │  主动二: 灵动身法               │                            │
│  1pt 解锁       │  Lv1                           │  组合三(Lv3素材)           │
│  → 5选3装配     │  ├─ Lv2左 → Lv3左              │  Lv1 → Lv2                 │
│                 │  └─ Lv2右 → Lv3右              │                            │
│  层级 III (Lv3) │                                │                            │
│  主动三: 武器技能 ★新增         │                            │
│  (根据装备武器获取)              │                            │
│  剑→横斩 / 弓→瞄准射击          │                            │
│  法杖→魔法弹 / 大锤→撼地猛击     │                            │
│  双刀→十字斩 / ...              │                            │
│  (每武器1技能，无分支无升级)      │                            │
├─────────────────┴────────────────────────────────┴────────────────────────────┤
│  获取方式:                                                                    │
│  · 被动: 消耗1技能点解锁层级 → 从该层5个被动中任选3个装配生效                   │
│  · 主动(12线): 消耗技能点直接解锁/升级                                        │
│  · 主动(武器): 根据当前装备的武器自动获取1个对应技能，不消耗技能点                │
│  · 组合: 拥有同等级任意2个主动节点(含武器技能) → 消耗技能点合成 → 再升级一次    │
│  · 组合后原素材节点保留，不受影响                                               │
└──────────────────────────────────────────────────────────────────────────────┘
```

### 1.2 核心规则

| 规则 | 说明 |
|------|------|
| **被动层级解锁** | 消耗1技能点解锁一个层级；层级必须按 I→II→III→IV→V 顺序解锁，不可跳级 |
| **被动装配** | 每层解锁后，从该层级的5个被动节点中选择3个装配（装配即生效）；可随时回技能树面板更换装配，不消耗技能点 |
| **主动技解锁** | Lv1 必须先解锁；Lv2 选择左右之一（不可同时选）；Lv3 须在已选 Lv2 对应分支 |
| **武器技能获取** | 根据玩家当前装备的武器类型自动获得1个对应技能；切换武器即切换可用技能；不消耗技能点 |
| **组合技合成** | 需拥有同等级任意2个已解锁的主动节点（含武器技能节点）；合成后素材保留 |
| **组合技升级** | 合成获得 Lv1 → 可再消耗技能点升到 Lv2 |
| **技能点来源** | 玩家升级获得（具体数量由关卡策划配置，默认每级+2） |
| **分支不可逆** | 主动技（非武器）一旦选择左/右分支，不可重置（除非用重置道具） |
| **被动装配可换** | 已解锁层级的装配选择可随时更换，无需消耗（鼓励玩家根据场景调整Build） |

---

## 2. 数据结构设计

### 2.1 核心 ScriptableObject

#### SkillTreeConfig — 技能树总配置

```csharp
/// <summary>
/// 技能树总配置 (ScriptableObject)
/// 策划在 Inspector 中配置整棵技能树的节点与连接关系
/// 挂载到 SkillTreeManager 组件上
/// </summary>
[CreateAssetMenu(fileName = "SkillTreeConfig", menuName = "Game/SkillTree/SkillTreeConfig")]
public class SkillTreeConfig : ScriptableObject
{
    [Header("被动技能层级")]
    public PassiveTier[] passiveTiers;              // 固定 5 层 (I~V)

    [Header("主动技能分支")]
    public ActiveSkillBranch[] activeBranches;      // 固定 2 条 (不含武器技能)

    [Header("武器技能")]
    public WeaponSkill[] weaponSkills;              // N 个 (剑/弓/法杖/大锤/双刀等，每武器1技能)

    [Header("组合技能配方")]
    public ComboSkillRecipe[] comboRecipes;         // 固定 3 条 (素材可含武器技能节点)

    [Header("全局配置")]
    public int skillPointsPerLevel = 2;             // 每升一级获得技能点数
    public int maxPlayerLevel = 30;                 // 玩家最大等级
    public int passiveNodesPerTier = 5;             // 每层共5个被动节点
    public int passiveEquipSlotsPerTier = 3;        // 每层可装配3个
}
```

#### SkillTreeNode — 单个节点数据

```csharp
/// <summary>技能树节点类型</summary>
public enum SkillNodeType
{
    Passive,    // 被动节点 — 层级解锁后装配生效
    Active,     // 主动节点 — 解锁后可装备到槽位
    Weapon,     // 武器节点 — 根据装备武器获得，可装备到槽位
    Combo       // 组合节点 — 通过合成获得
}

/// <summary>节点在主动分支中的位置</summary>
public enum BranchSide
{
    None,       // 非分支节点 (Lv1 或被动线)
    Left,       // 左分支 (Lv2左, Lv3左)
    Right       // 右分支 (Lv2右, Lv3右)
}

/// <summary>
/// 技能树节点 — 树中的一个节点
/// 策划在 SkillTreeConfig 的 Inspector 中直接配置
/// </summary>
[System.Serializable]
public class SkillTreeNode
{
    [Header("标识")]
    public string nodeId;                         // 唯一ID，如 "passive_vitality_1"
    public string nodeName;                       // 节点显示名，如 "生命恢复 I"
    [TextArea(2, 4)]
    public string description;                    // 节点效果描述
    public Sprite icon;                           // 节点图标

    [Header("类型与等级")]
    public SkillNodeType nodeType;                // 被动/主动/武器/组合
    public int nodeLevel;                         // 当前节点等级 (1-5)
    public int maxNodeLevel = 1;                  // 此节点可达到的最大等级 (被动=1, 组合最多2)

    [Header("被动层级归属 (仅被动节点)")]
    public int tierIndex = -1;                    // 所属层级: 0=I, 1=II, ..., 4=V

    [Header("解锁条件")]
    public int skillPointCost = 1;                // 解锁消耗技能点 (武器节点=0)
    public int requiredPlayerLevel;               // 玩家最低等级要求
    public string[] prerequisiteNodeIds;          // 前置节点ID（必须全部解锁后才能解锁此节点）
    public BranchSide branchSide;                 // 分支方向 (仅主动/武器节点使用)
    public string parentBranchNodeId;             // 父节点ID（分叉点，用于判断分支互斥）

    [Header("效果配置")]
    public SkillEffectConfig[] effects;           // 此节点解锁/装配后赋予的效果

    [Header("技能绑定 (仅主动/武器/组合节点)")]
    public SkillData boundSkillData;              // 关联的技能 SO（主动技能放入槽位时引用）
    public SkillCategory displayedCategory;       // 在UI中显示的分类
}
```

#### SkillEffectConfig — 节点效果

```csharp
/// <summary>效果数值类型</summary>
public enum EffectTarget
{
    MaxHealth,          // 最大生命值
    MaxMana,            // 最大法力值
    HealthRegen,        // 生命回复
    ManaRegen,          // 法力回复
    DamageMultiplier,   // 伤害倍率 (1.0 = 100%)
    AttackSpeed,        // 攻击速度倍率
    MoveSpeed,          // 移动速度倍率
    DodgeChance,        // 闪避几率 (0.15 = 15%)
    CooldownReduction,  // 冷却缩减 (0.1 = 10%缩减)
    DamageResist,       // 伤害减免 (0.1 = 10%减免)
    ManaCostReduction,  // 法力消耗降低
    HitStunReduction,   // 硬直时间减少
    ControlReduction,   // 控制效果减少
    Special             // 特殊效果 (由代码逻辑处理)
}

/// <summary>效果应用方式</summary>
public enum EffectApplication
{
    Additive,           // 加法叠加 (如 +10% 生命)
    Multiplicative,     // 乘法叠加 (如 ×1.1 伤害)
    Override            // 覆写 (直接设置值)
}

/// <summary>
/// 技能效果配置 — 一个节点可以有多个效果
/// 策划在 Inspector 中配置每个效果的类型和数值
/// </summary>
[System.Serializable]
public class SkillEffectConfig
{
    public EffectTarget target;
    public EffectApplication application;
    public float value;                           // 效果数值
    public string specialKey;                     // Special 类型使用的key，如 "cheat_death"
}
```

#### PassiveTier — 被动层级（替代原 PassiveSkillBranch）

```csharp
/// <summary>
/// 被动技能层级 — 5条被动线按等级分组
/// Tier 0=I(Lv1), Tier 1=II(Lv2), ..., Tier 4=V(Lv5)
/// 每层共5个节点（分别来自5条被动线），解锁后装配3个
/// </summary>
[System.Serializable]
public class PassiveTier
{
    public string tierId;                         // 如 "tier_1"
    public string tierName;                       // 如 "层级 I · 入门"
    public int tierIndex;                         // 0-4
    public int skillPointCost = 1;                // 解锁此层级消耗技能点
    public int equipSlots = 3;                    // 可装配数量 (默认3)

    [Tooltip("此层级包含的5个被动节点 (分别来自5条线)")]
    public SkillTreeNode[] nodes;                 // 固定5个节点
}
```

#### ActiveSkillBranch — 主动分支（含分叉）

```csharp
/// <summary>主动技能分支 — Lv1分叉为左右两支，各到Lv3</summary>
[System.Serializable]
public class ActiveSkillBranch
{
    public string branchId;                       // 如 "active_projection"
    public string branchName;                     // 如 "能量投射"
    public Sprite branchIcon;

    [Header("Lv1 — 共用起点")]
    public SkillTreeNode lv1Node;

    [Header("左分支 — Lv2左 → Lv3左")]
    public SkillTreeNode lv2Left;
    public SkillTreeNode lv3Left;

    [Header("右分支 — Lv2右 → Lv3右")]
    public SkillTreeNode lv2Right;
    public SkillTreeNode lv3Right;
}
```

#### WeaponSkill — 武器技能（每武器1技能）

```csharp
/// <summary>
/// 武器技能 — 一把武器对应一个技能
/// 根据装备武器类型自动获得，不消耗技能点，切换武器即切换可用技能
/// 无分支、无升级线，技能本身即为最终形态
/// 武器技能可参与组合技合成
/// </summary>
[System.Serializable]
public class WeaponSkill
{
    public string weaponType;                     // 武器类型标识，如 "Sword", "Bow", "Staff"
    public string weaponName;                     // 显示名，如 "剑", "弓", "法杖"
    public Sprite weaponIcon;

    [Header("唯一技能节点")]
    public SkillTreeNode skillNode;               // 此武器对应的唯一技能节点
}
```

#### ComboSkillRecipe — 组合配方

```csharp
/// <summary>
/// 组合技能配方 — 同等级任意2个主动节点合成
/// 素材可以来自主动技能线或武器技能
/// </summary>
[System.Serializable]
public class ComboSkillRecipe
{
    public string recipeId;                       // 如 "combo_lv1"
    public string recipeName;                     // 如 "协同打击"

    [Header("合成条件")]
    [Tooltip("需从哪个等级选素材节点")]
    public int requiredSourceLevel;               // 1, 2, 3
    [Tooltip("需要多少个同等级主动/武器节点")]
    public int requiredNodeCount = 2;
    [Tooltip("是否允许武器技能节点作为素材")]
    public bool allowWeaponNodes = true;

    [Header("产出")]
    [Tooltip("合成后获得的技能 (Lv1)")]
    public SkillTreeNode resultSkillLv1;
    [Tooltip("消耗技能点可升至 Lv2")]
    public SkillTreeNode resultSkillLv2;

    [Header("组合技能绑定 (可选，覆盖节点内的 boundSkillData)")]
    public SkillData comboSkillDataLv1;
    public SkillData comboSkillDataLv2;
}
```

### 2.2 运行时状态

#### SkillTreeState — 玩家进度

```csharp
/// <summary>
/// 技能树运行时状态 — 记录玩家已解锁的节点和技能点
/// 序列化后用于存档
/// </summary>
[System.Serializable]
public class SkillTreeState
{
    /// <summary>已解锁的节点ID列表（被动层级解锁+主动节点解锁+组合合成）</summary>
    public List<string> unlockedNodeIds = new List<string>();

    /// <summary>已解锁的被动层级索引列表，如 [0,1] 表示已解锁层级I和II</summary>
    public List<int> unlockedTierIndices = new List<int>();

    /// <summary>
    /// 每个层级当前装配的被动节点ID（3个/层）
    /// key=tierIndex, value=已装配的3个nodeId
    /// </summary>
    public List<PassiveEquipEntry> passiveEquipEntries = new List<PassiveEquipEntry>();

    /// <summary>组合技能当前等级，key=recipeId, value=当前等级(1或2，0=未合成)</summary>
    public List<ComboSkillSaveEntry> comboSaveEntries = new List<ComboSkillSaveEntry>();

    /// <summary>当前可用技能点</summary>
    public int availableSkillPoints;

    /// <summary>玩家当前等级</summary>
    public int playerLevel;

    /// <summary>当前装备的武器类型（用于确定可用武器技能）</summary>
    public string equippedWeaponType;
}

[System.Serializable]
public class PassiveEquipEntry
{
    public int tierIndex;                          // 层级索引 0-4
    public List<string> equippedNodeIds;           // 装配的3个节点ID
}

[System.Serializable]
public class ComboSkillSaveEntry
{
    public string recipeId;
    public int currentLevel;                      // 0=未合成, 1=Lv1, 2=Lv2
    public List<string> sourceNodeIds;            // 合成时使用的素材节点ID
}
```

#### SkillTreeManager — 运行时管理器（组件）

```csharp
/// <summary>
/// 技能树管理器 — 挂在 Player GameObject 上
/// 职责：
///   1. 管理技能树状态（被动层级解锁/装配、主动解锁/升级、合成）
///   2. 计算并应用被动效果到玩家属性（仅已装配的被动节点生效）
///   3. 管理"已解锁主动技能池"，供 SkillManager 槽位引用
///   4. 监听武器切换，自动热更新可用武器技能
///   5. 持久化/加载技能树进度
/// </summary>
public class SkillTreeManager : MonoBehaviour
{
    [SerializeField] private SkillTreeConfig treeConfig;
    [SerializeField] private SkillTreeState currentState;

    // 被动效果累加值（运行时计算，每次状态变化时刷新）
    private Dictionary<EffectTarget, float> passiveEffectCache;

    // 已解锁的主动技能 → 可供 SkillManager 槽位引用的 SkillData 列表
    public List<SkillData> UnlockedActiveSkills { get; private set; }

    // 当前武器技能节点（根据装备武器动态变化）
    public SkillTreeNode CurrentWeaponSkillNode { get; private set; }

    // 事件
    public event Action<SkillTreeNode> OnNodeUnlocked;
    public event Action<SkillTreeNode> OnNodeUpgraded;
    public event Action<ComboSkillRecipe, int> OnComboCrafted;
    public event Action<int, string[]> OnPassiveTierUnlocked;    // tierIndex, nodeIds
    public event Action<int, string[]> OnPassiveEquipChanged;   // tierIndex, 新装配nodeIds
    public event Action<string> OnWeaponChanged;                // weaponType

    // 核心方法
    public bool CanUnlockTier(int tierIndex);
    public bool UnlockTier(int tierIndex);
    public bool CanEquipPassive(int tierIndex, string nodeId);
    public bool EquipPassive(int tierIndex, string nodeId);
    public bool UnequipPassive(int tierIndex, string nodeId);
    public string[] GetEquippedPassivesForTier(int tierIndex);
    public bool CanUnlockNode(string nodeId);
    public bool UnlockNode(string nodeId);
    public bool CanCraftCombo(string recipeId, string[] selectedSourceNodeIds);
    public bool CraftCombo(string recipeId, string[] selectedSourceNodeIds);
    public bool CanUpgradeCombo(string recipeId);
    public bool UpgradeCombo(string recipeId);
    public float GetPassiveEffectValue(EffectTarget target);
    public void AddSkillPoints(int amount);
    public void OnPlayerLevelUp(int newLevel);
    public void OnWeaponEquipped(string weaponType);
    public WeaponSkill GetWeaponSkill(string weaponType);
    public void RefreshPassiveEffects();
    public void SaveState();
    public void LoadState();
}
```

---

## 3. 策划案核心——技能节点详细设计

### 3.1 被动技能：层级解锁 + 装配制

#### 总览

被动技能不再按"5条独立线"各自线性升级，改为**5个层级**。每层包含5个被动节点（来自5条技能线的对应等级），解锁层级后从中选择3个装配。

```
┌─────────────────────────────────────────────────────────────────────────┐
│                         被动技能层级总览 (v2.0 — 同步DrawIO)               │
├──────────┬──────────┬──────────┬──────────┬──────────┬──────────────────┤
│ 层级 I   │ 层级 II  │ 层级 III │ 层级 IV  │ 层级 V   │                  │
│ (TI)     │ (TII)    │ (TIII)   │ (TIV)    │ (TV)     │                  │
├──────────┼──────────┼──────────┼──────────┼──────────┼──────────────────┤
│ HP恢复+1%│ HP恢复+2%│ HP恢复+3%│ HP恢复+4%│ HP恢复+5%│ 恢复线           │
│ 伤害+8%  │ 伤害+15% │ 伤害+22% │ 伤害+28% │ 伤害+35% │ 输出线           │
│          │          │ 攻速+10% │ 攻速+15% │ 攻速+20% │                  │
│ 移速+6%  │ 移速+12% │ 移速+18% │ 移速+24% │ 移速+30% │ 机动线           │
│          │          │ 闪避+15% │ 闪避+20% │ 闪避+30% │                  │
│ 减伤+5%  │ 减伤+10% │ 减伤+15% │ 减伤+20% │ 减伤+25% │ 防御线           │
│          │          │ 硬直-20% │ 控制-25% │ 低血加防 │                  │
│法力恢复+1%│法力恢复+2%│法力恢复+3%│法力恢复+4%│法力恢复+5%│ 资源线          │
│          │法力+20%  │法力+22%  │法力+25%  │法力+30%  │                  │
│          │          │CD-5%     │CD-8%     │CD-10%    │                  │
│          │          │          │          │消耗降低3%│                  │
├──────────┼──────────┼──────────┼──────────┼──────────┼──────────────────┤
│ 1pt 解锁  │ 1pt 解锁  │ 1pt 解锁  │ 1pt 解锁  │ 1pt 解锁  │                  │
│ →3个装配  │ →3个装配  │ →3个装配  │ →3个装配  │ →3个装配  │                  │
└──────────┴──────────┴──────────┴──────────┴──────────┴──────────────────┘
```

#### 层级解锁规则

| 规则 | 说明 |
|------|------|
| 解锁消耗 | 每层 **1 技能点** |
| 解锁顺序 | 必须按 I→II→III→IV→V 顺序，不可跳级 |
| 装配数量 | 每层解锁后从中选择 **3 个** 节点装配 |
| 装配切换 | 可随时回技能树面板更换装配，不消耗技能点 |
| 节点互斥 | 同一层级内不能装配同一节点多次；不同层级可装配同一技能线的不同等级 |
| 总计 | 5层 × 1pt = **5技能点** 点满被动（大幅降低，鼓励Build多样性） |

#### 层级 I (TI) — 入门级

| 节点ID | 节点名称 | 所属线 | 效果 |
|--------|----------|--------|------|
| `passive_regen_1` | 生命恢复 I | 恢复 | 每秒恢复最大生命值的 1% |
| `passive_focus_1` | 战斗专注 I | 输出 | 伤害 +8% |
| `passive_agility_1` | 敏捷身法 I | 机动 | 移动速度 +6% |
| `passive_ironwill_1` | 坚韧护盾 I | 防御 | 伤害减免 +5% |
| `passive_energy_1` | 能量掌控 I | 资源 | 每秒恢复最大法力值的 1% |

#### 层级 II (TII) — 进阶级

| 节点ID | 节点名称 | 效果 |
|--------|----------|------|
| `passive_regen_2` | 生命恢复 II | 每秒恢复最大生命值的 2% |
| `passive_focus_2` | 战斗专注 II | 伤害 +15% |
| `passive_agility_2` | 敏捷身法 II | 移动速度 +12% |
| `passive_ironwill_2` | 坚韧护盾 II | 伤害减免 +10% |
| `passive_energy_2` | 能量掌控 II | 每秒恢复法力 2%；最大法力值 +20% |

#### 层级 III (TIII) — 专家级

| 节点ID | 节点名称 | 效果 |
|--------|----------|------|
| `passive_regen_3` | 生命恢复 III | 每秒恢复最大生命值的 3% |
| `passive_focus_3` | 战斗专注 III | 伤害 +22%；攻击速度 +10% |
| `passive_agility_3` | 敏捷身法 III | 移动速度 +18%；闪避几率 +15% |
| `passive_ironwill_3` | 坚韧护盾 III | 伤害减免 +15%；受击硬直时间 -20% |
| `passive_energy_3` | 能量掌控 III | 每秒恢复法力 3%；最大法力值 +22%；技能冷却缩减 5% |

#### 层级 IV (TIV) — 大师级

| 节点ID | 节点名称 | 效果 |
|--------|----------|------|
| `passive_regen_4` | 生命恢复 IV | 每秒恢复最大生命值的 4% |
| `passive_focus_4` | 战斗专注 IV | 伤害 +28%；攻击速度 +15% |
| `passive_agility_4` | 敏捷身法 IV | 移动速度 +24%；闪避几率 +20% |
| `passive_ironwill_4` | 坚韧护盾 IV | 伤害减免 +20%；控制效果持续时间 -25% |
| `passive_energy_4` | 能量掌控 IV | 每秒恢复法力 4%；最大法力值 +25%；技能冷却缩减 8% |

#### 层级 V (TV) — 传说级

| 节点ID | 节点名称 | 效果 |
|--------|----------|------|
| `passive_regen_5` | 生命恢复 V | 每秒恢复最大生命值的 5% |
| `passive_focus_5` | 战斗专注 V | 伤害 +35%；攻击速度 +20% |
| `passive_agility_5` | 敏捷身法 V | 移动速度 +30%；闪避几率 +30% |
| `passive_ironwill_5` | 坚韧护盾 V | 伤害减免 +25%；生命值低于30%时额外获得防御加成 |
| `passive_energy_5` | 能量掌控 V | 每秒恢复法力 5%；最大法力值 +30%；冷却缩减 10%；所有技能法力消耗降低 3% |

> **设计说明:** 同等级节点之间效果互相覆盖（取max），不同等级可以叠加。例如装配生命恢复I(1%)和生命恢复II(2%)时，实际每秒恢复3%。这样设计鼓励玩家在不同层级间搭配不同线的节点，而非无脑堆同一条线。

#### 典型Build示例

| Build名称 | 层级I (3选) | 层级II (3选) | 层级III (3选) | 层级IV (3选) | 层级V (3选) |
|-----------|:-----------:|:------------:|:-------------:|:------------:|:-----------:|
| **坦克** | 恢复+战斗+坚韧 | 恢复+战斗+坚韧 | 恢复+坚韧+敏捷 | 恢复+坚韧+能量 | 恢复+坚韧+敏捷 |
| **刺客** | 战斗+敏捷+能量 | 战斗+敏捷+恢复 | 战斗+敏捷+能量 | 战斗+敏捷+恢复 | 战斗+敏捷+能量 |
| **法师** | 能量+战斗+恢复 | 能量+战斗+坚韧 | 能量+战斗+敏捷 | 能量+战斗+恢复 | 能量+战斗+坚韧 |
| **均衡** | 恢复+战斗+能量 | 恢复+敏捷+坚韧 | 战斗+能量+敏捷 | 恢复+坚韧+能量 | 战斗+敏捷+能量 |

---

### 3.2 武器技能（新增）

#### 概述

武器技能是第3条主动技能线。**不消耗技能点**，根据玩家当前装备的武器类型自动获取**1个**对应技能。切换武器即切换可用技能。武器技能可参与组合技合成（作为素材节点之一）。

**核心原则：一把武器对应一个技能。没有分支、没有Lv1/Lv2/Lv3分叉、没有升级线。**

```
┌──────────────────────────────────────────────────────────────────────────┐
│                            武器技能总览                                    │
├─────────┬─────────┬─────────┬────────────┬────────────┬──────────────────┤
│   剑    │   弓    │  法杖   │   大锤     │   双刀     │   (可扩展...)     │
│ (Sword) │  (Bow)  │ (Staff) │ (Hammer)   │(DualBlade) │                  │
├─────────┼─────────┼─────────┼────────────┼────────────┼──────────────────┤
│  横斩   │瞄准射击 │ 魔法弹  │ 撼地猛击   │  十字斩    │  枪·突刺         │
│ 近战·   │ 远程·   │ 远程·   │  近战·     │  近战·     │  盾·盾击         │
│  范围   │  精准   │  元素   │   控制     │   连击     │  ...             │
└─────────┴─────────┴─────────┴────────────┴────────────┴──────────────────┘
```

#### 武器切换规则

| 规则 | 说明 |
|------|------|
| **获取方式** | 装备对应武器时自动获得该武器的1个技能，无需消耗技能点 |
| **无分支无升级** | 每武器仅1个技能，无分支选择、无升级线，技能效果固定 |
| **即时切换** | 切换武器即时切换可用技能，无需额外操作 |
| **组合技素材** | 武器技能节点可作为组合技素材，与其他主动节点地位相同 |
| **槽位占用** | 武器技能占用 SkillManager 的4个技能槽之一 |
| **平衡设计** | 武器技能强度 ≈ 其他主动技能 Lv2 水平（因为不消耗技能点且无升级空间） |

---

#### 3.2.1 剑 (Sword) — 近战范围

**武器类型ID:** `weapon_sword`  
**定位:** 近战·范围·爆发  
**默认快捷键:** R（可在技能装备面板自定义）

| 节点ID | 名称 | 技能点 | 等级要求 | 绑定的 SkillData |
|--------|------|--------|----------|------------------|
| `weapon_sword` | 横斩 | 0 | 1 | `Skill_SwordSlash` |

**横斩效果:** 向前方扇形区域（120°，2m）挥剑斩击，造成 45 伤害，命中敌人轻微击退。CD 2s，法力8。

> **设计定位:** 通用近战AOE — 范围中等、CD短、法力消耗低，适合清小怪和补伤害。

---

#### 3.2.2 弓 (Bow) — 远程精准

**武器类型ID:** `weapon_bow`  
**定位:** 远程·精准·高射速  
**默认快捷键:** R

| 节点ID | 名称 | 技能点 | 等级要求 | 绑定的 SkillData |
|--------|------|--------|----------|------------------|
| `weapon_bow` | 瞄准射击 | 0 | 1 | `Skill_AimShot` |

**瞄准射击效果:** 向鼠标方向射出一支箭矢，伤害 35，飞行速度快，可穿透1个敌人（伤害衰减至50%）。CD 1.5s，法力5。

> **设计定位:** 远程消耗 — 射速快、法力消耗极低，适合风筝和远程补刀。

---

#### 3.2.3 法杖 (Staff) — 元素魔法

**武器类型ID:** `weapon_staff`  
**定位:** 远程·元素·追踪  
**默认快捷键:** R

| 节点ID | 名称 | 技能点 | 等级要求 | 绑定的 SkillData |
|--------|------|--------|----------|------------------|
| `weapon_staff` | 魔法弹 | 0 | 1 | `Skill_MagicBolt` |

**魔法弹效果:** 发射一枚追踪魔法弹（自导引，转向速率中等），命中造成 30 伤害 + 1s 减速20%。CD 2s，法力10。

> **设计定位:** 稳定命中 — 追踪特性保证命中率，附带软控，适合移动战和拉扯。

---

#### 3.2.4 大锤 (Hammer) — 近战控制

**武器类型ID:** `weapon_hammer`  
**定位:** 近战·控制·击飞  
**默认快捷键:** R

| 节点ID | 名称 | 技能点 | 等级要求 | 绑定的 SkillData |
|--------|------|--------|----------|------------------|
| `weapon_hammer` | 撼地猛击 | 0 | 3 | `Skill_GroundSmash` |

**撼地猛击效果:** 猛击地面产生冲击波（自身周围半径2.5m），伤害 40，附带 0.5s 击飞。CD 3.5s，法力15。

> **设计定位:** 近身自保 — AOE击飞创造安全空间，适合被包围时脱困和打断敌人动作。

---

#### 3.2.5 双刀 (Dual Blade) — 近战连击

**武器类型ID:** `weapon_dualblade`  
**定位:** 近战·连击·高速  
**默认快捷键:** R

| 节点ID | 名称 | 技能点 | 等级要求 | 绑定的 SkillData |
|--------|------|--------|----------|------------------|
| `weapon_dualblade` | 十字斩 | 0 | 3 | `Skill_CrossSlash` |

**十字斩效果:** 快速释放2段斩击（间隔0.15s），每段伤害 25（共50），第2段命中时自身获得 1s 内下次普攻伤害+20%。CD 2.5s，法力12。

> **设计定位:** 连击节奏 — 短间隔双段伤害衔接普攻，适合高手速操作和单体爆发。

---

#### 武器技能对比总览

| 维度 | 剑·横斩 | 弓·瞄准射击 | 法杖·魔法弹 | 大锤·撼地猛击 | 双刀·十字斩 |
|------|:------:|:---------:|:---------:|:----------:|:---------:|
| 攻击类型 | 近战AOE | 远程穿透 | 远程追踪 | 近战AOE | 近战连击 |
| 单体伤害 | ★★★ (45) | ★★☆ (35) | ★★☆ (30) | ★★☆ (40) | ★★★ (50) |
| 群体伤害 | ★★★ | ★☆☆ | ★☆☆ | ★★★ | ★★☆ |
| 控制能力 | ★☆☆ (轻击退) | ☆☆☆ | ★★☆ (减速) | ★★★ (击飞) | ★☆☆ |
| CD | 2s | 1.5s | 2s | 3.5s | 2.5s |
| 法力消耗 | 8 | 5 | 10 | 15 | 12 |
| 操作难度 | ★☆☆ | ★★☆ | ★☆☆ | ★★☆ | ★★★ |
| 适合场景 | 通用清怪 | 风筝消耗 | 稳定输出 | 自保脱困 | 单体爆发 |

> **扩展预留:** 后续可增加「枪·突刺」（直线穿透）、「盾·盾击」（格挡反击）、「镰刀·收割」（吸血AOE）等武器类型，每个武器仅需设计1个技能即可接入系统。

---

### 3.3 主动技能一：能量投射（Energy Projection）

**分支ID:** `active_projection`  
**定位:** 远程攻击 — 弹幕/穿透/AOE  
**默认快捷键:** Q

#### Lv1 — 共用起点

| 节点ID | 名称 | 技能点 | 等级要求 | 绑定的 SkillData |
|--------|------|--------|----------|------------------|
| `active_proj_1` | 能量弹 | 3 | 3 | `Skill_EnergyBolt` |

**能量弹效果:** 向鼠标方向发射一枚能量弹，命中敌人造成 35 伤害。CD 3s，法力15。

---

#### 左分支：散射弹幕（Barrage Path）

| 节点ID | 名称 | 技能点 | 等级要求 | 绑定的 SkillData |
|--------|------|--------|----------|------------------|
| `active_proj_2L` | 散射弹幕 | 3 | 8 | `Skill_ScatterBolt` |
| `active_proj_3L` | 弹幕风暴 | 4 | 14 | `Skill_BoltStorm` |

| 等级 | 效果 |
|------|------|
| Lv2左 | 扇形发射 3 枚能量弹，每枚伤害 25（全部命中 = 75）。CD 4s，法力20 |
| Lv3左 | 扇形发射 5 枚能量弹，每枚伤害 30（全部命中 = 150）。CD 3.5s，法力25 |

> **分支特色:** 清场路线 — 对群优秀，单发伤害低但命中多枚时爆发高。适合刷怪场景。

---

#### 右分支：穿透狙击（Pierce Path）

| 节点ID | 名称 | 技能点 | 等级要求 | 绑定的 SkillData |
|--------|------|--------|----------|------------------|
| `active_proj_2R` | 穿透狙击 | 3 | 8 | `Skill_PierceBolt` |
| `active_proj_3R` | 毁灭射线 | 4 | 14 | `Skill_DestroyRay` |

| 等级 | 效果 |
|------|------|
| Lv2右 | 发射一枚大型穿透弹，伤害 55，穿透 3 个敌人，弹道宽度 1.5 倍。CD 5s，法力25 |
| Lv3右 | 大型穿透弹伤害 90，穿透 5 个敌人，命中墙壁反弹 1 次。CD 4.5s，法力30 |

> **分支特色:** 狙击路线 — 对单/对线伤害极高，但需要瞄准。适合BOSS战和地形利用。

---

#### 分支对比

| 维度 | 左分支 (散射) | 右分支 (穿透) |
|------|:-----------:|:-----------:|
| 对单体伤害 | ★★☆ | ★★★ |
| 对群体伤害 | ★★★ | ★★☆ |
| 瞄准难度 | ★☆☆ (扇形自动覆盖) | ★★☆ (需瞄准) |
| 清图效率 | ★★★ | ★★☆ |
| BOSS战表现 | ★★☆ | ★★★ |
| 地形利用 | ★☆☆ | ★★★ (反弹) |

---

### 3.4 主动技能二：灵动身法（Agile Movement）

**分支ID:** `active_movement`  
**定位:** 位移 — 进攻位移 vs 闪避位移  
**默认快捷键:** E

#### Lv1 — 共用起点

| 节点ID | 名称 | 技能点 | 等级要求 | 绑定的 SkillData |
|--------|------|--------|----------|------------------|
| `active_move_1` | 冲击步 | 3 | 3 | `Skill_ImpactStep` |

**冲击步效果:** 向角色朝向冲刺 3m，路径上敌人受到 20 伤害并轻微击退。CD 3s，法力12。

---

#### 左分支：进攻位移（Offensive Path）— 突进斩杀

| 节点ID | 名称 | 技能点 | 等级要求 | 绑定的 SkillData |
|--------|------|--------|----------|------------------|
| `active_move_2L` | 突进斩 | 3 | 8 | `Skill_DashSlash` |
| `active_move_3L` | 双闪连斩 | 4 | 14 | `Skill_DoubleSlash` |

| 等级 | 效果 |
|------|------|
| Lv2左 | 冲刺距离 +50%(4.5m)，路径伤害 30，终点自动追加一次 AOE 斩击(20伤害,半径2m)。CD 3.5s，法力18 |
| Lv3左 | 冲刺变为2段充能制，每段路径伤害 35，终点AOE伤害 30+击飞。充能CD 4s/段，法力15/段 |

> **分支特色:** 高机动 + 爆发，适合贴脸输出型玩家。2段充能给予极大灵活性。

---

#### 右分支：闪避位移（Evasive Path）— 灵巧生存

| 节点ID | 名称 | 技能点 | 等级要求 | 绑定的 SkillData |
|--------|------|--------|----------|------------------|
| `active_move_2R` | 灵巧闪避 | 3 | 8 | `Skill_QuickDodge` |
| `active_move_3R` | 虚空步伐 | 4 | 14 | `Skill_VoidStep` |

| 等级 | 效果 |
|------|------|
| Lv2右 | 冲刺距离 3m，冲刺期间无敌帧。CD 2s，法力10。冲刺后 1s 内下次普攻伤害 +30% |
| Lv3右 | 冲刺无敌帧延长至全位移过程 + 位移结束后向后退半步(虚空步消后摇)。CD 1.5s，法力8。冲刺后普攻伤害 +50%，且该次普攻必定暴击 |

> **分支特色:** 高风险高回报 — 精准闪避配合普攻打出高额爆发。适合操作型玩家。

---

#### 分支对比

| 维度 | 左分支 (进攻) | 右分支 (闪避) |
|------|:-----------:|:-----------:|
| 伤害输出 | ★★★ | ★★☆ (需要衔接普攻) |
| 生存能力 | ★★☆ | ★★★ |
| 操作难度 | ★★☆ | ★★★ |
| 灵活性 | ★★★ (2段充能) | ★★★ (低CD + 无敌) |
| BOSS战表现 | ★★☆ | ★★★ (弹幕躲避) |

---

### 3.5 组合技能一：协同打击（Synergy Strike）

**配方ID:** `combo_lv1`  
**合成条件:** 拥有 Lv1 等级的任意 2 个主动节点（含武器技能节点）  
**消耗技能点:** 4

| 等级 | 节点ID | 名称 | 额外技能点 | 效果 |
|------|--------|------|-----------|------|
| Lv1 | `combo_1_lv1` | 协同打击 | — (含在合成中) | 在鼠标位置召唤能量爆发，范围 3m，伤害 50。CD 8s，法力25 |
| Lv2 | `combo_1_lv2` | 双重协同 | 3 | 能量爆发变为2连击（间隔0.3s），每次伤害 40。范围+30%。CD 7s |

**绑定的 SkillData:** `Skill_SynergyStrike` / `Skill_DualSynergy`

> **设计说明:** 第一个组合技能，作为远程+位移（或远程+武器）融合的产物，给予玩家一个额外的定点AOE能力。

---

### 3.6 组合技能二：法则领域（Law Domain）

**配方ID:** `combo_lv2`  
**合成条件:** 拥有 Lv2 等级的任意 2 个主动节点  
**消耗技能点:** 5

| 等级 | 节点ID | 名称 | 额外技能点 | 效果 |
|------|--------|------|-----------|------|
| Lv1 | `combo_2_lv1` | 法则领域 | — | 以自身为中心展开持续 4s 的能量领域(半径 4.5m)。领域内敌人每秒受到 25 伤害并减速 20%。CD 18s，法力40 |
| Lv2 | `combo_2_lv2` | 法则领域·极 | 4 | 领域持续 6s，伤害提升至每秒 35，减速提升至 30%。结束时引爆——领域内敌人额外受到 60 伤害 + 1s 眩晕。CD 15s |

**绑定的 SkillData:** `Skill_LawDomain` / `Skill_LawDomainMax`

---

### 3.7 组合技能三：终焉审判（Final Judgment）

**配方ID:** `combo_lv3`  
**合成条件:** 拥有 Lv3 等级的任意 2 个主动节点  
**消耗技能点:** 6

| 等级 | 节点ID | 名称 | 额外技能点 | 效果 |
|------|--------|------|-----------|------|
| Lv1 | `combo_3_lv1` | 终焉审判 | — | 大范围能量爆发(半径 7m)，伤害 120，附带 1.5s 眩晕。CD 30s，法力60 |
| Lv2 | `combo_3_lv2` | 终焉审判·灭 | 5 | 伤害提升至 180，眩晕提升至 2.5s + 击飞。附带屏幕震动和全屏闪白特效。CD 25s |

**绑定的 SkillData:** `Skill_FinalJudgment` / `Skill_FinalJudgmentMax`

> **设计说明:** 终极大招。需要投入大量技能点（6+5=11），是后期终极追求。全屏AOE + 硬控，视觉效果拉满。

---

### 3.8 技能点经济总览

| 类别 | 节点数 | 技能点总计 | 备注 |
|------|--------|-----------|------|
| 被动层级 I | 1层解锁 | **1** | 解锁后5选3装配 |
| 被动层级 II | 1层解锁 | **1** | 同上 |
| 被动层级 III | 1层解锁 | **1** | 同上 |
| 被动层级 IV | 1层解锁 | **1** | 同上 |
| 被动层级 V | 1层解锁 | **1** | 同上 |
| 主动一 Lv1+分支Lv2+Lv3 | 3节点 | 3+3+4 = **10** | |
| 主动二 Lv1+分支Lv2+Lv3 | 3节点 | 3+3+4 = **10** | |
| 武器技能 (N把武器) | N节点 | **0** | 武器切换获得，每武器1技能 |
| 组合一 (合成+Lv2) | 2等级 | 4+3 = **7** | |
| 组合二 (合成+Lv2) | 2等级 | 5+4 = **9** | |
| 组合三 (合成+Lv2) | 2等级 | 6+5 = **11** | |
| **合计（理论最大）** | **—** | **43+N** | |

> 玩家满级30级，每级2点=60点基础。被动5pt + 主动10+10=20pt + 组合7+9+11=27pt = 总计 ~52pt+N。新版技能点充足，Build多样性来自"被动层级的5选3"。

---

## 4. 与现有 SkillManager 的集成方案

（与 v1.2 保持一致，此处省略架构分层和集成流程细节，见原文档。）

### 4.5 复用与不改动的部分

| 现有模块 | 是否改动 | 说明 |
|----------|---------|------|
| `SkillData.cs` | **扩展** | 新增 `skillTreeBindingId` 字段，标识对应技能树节点 |
| `SkillManager.cs` | **扩展** | 新增装备/卸下接口、武器技能自动装备，法力/冷却不变 |
| `SkillSlot.cs` | **扩展** | 新增 `boundNodeId` 记录来源节点 |
| `ISkill.cs` | **不改** | 接口不感知技能来源（树解锁 vs 初始拥有） |
| `EventBus.cs` | **不改** | 只新增事件类型，框架本身不变 |
| `SynergyConfig.cs` | **扩展** | 可新增"已解锁节点数≥N"触发条件（可选） |
| `PlayerController.cs` | **微改** | 新增一行调用 |
| `BarrierSkill.cs` / `ObstacleBall.cs` | **不改** | 作为示例技能保留，不受影响 |

---

## 5. UI 交互流程

（与 v1.2 保持一致，详见原文档第5章。）

---

## 6. 数据存储方案

（与 v1.2 保持一致，使用 JsonUtility + File 读写。）

---

## 7. 文件结构规划

（与 v1.2 保持一致，详见原文档第7章。）

---

## 8. 实现阶段规划

（与 v1.2 保持一致，详见原文档第8章。）

---

## 附录 A：节点 ID 速查表 (v2.0)

### 被动节点（按层级排列）

| 层级 | 节点ID | 名称 | 效果关键词 |
|:----:|--------|------|-----------|
| I | `passive_regen_1` | 生命恢复 I | HP恢复+1%/秒 |
| I | `passive_focus_1` | 战斗专注 I | 伤害+8% |
| I | `passive_agility_1` | 敏捷身法 I | 移速+6% |
| I | `passive_ironwill_1` | 坚韧护盾 I | 减伤+5% |
| I | `passive_energy_1` | 能量掌控 I | 法力恢复+1%/秒 |
| II | `passive_regen_2` | 生命恢复 II | HP恢复+2%/秒 |
| II | `passive_focus_2` | 战斗专注 II | 伤害+15% |
| II | `passive_agility_2` | 敏捷身法 II | 移速+12% |
| II | `passive_ironwill_2` | 坚韧护盾 II | 减伤+10% |
| II | `passive_energy_2` | 能量掌控 II | 法力恢复+2%/秒, 法力+20% |
| III | `passive_regen_3` | 生命恢复 III | HP恢复+3%/秒 |
| III | `passive_focus_3` | 战斗专注 III | 伤害+22%, 攻速+10% |
| III | `passive_agility_3` | 敏捷身法 III | 移速+18%, 闪避+15% |
| III | `passive_ironwill_3` | 坚韧护盾 III | 减伤+15%, 硬直-20% |
| III | `passive_energy_3` | 能量掌控 III | 法力恢复+3%/秒, 法力+22%, CD-5% |
| IV | `passive_regen_4` | 生命恢复 IV | HP恢复+4%/秒 |
| IV | `passive_focus_4` | 战斗专注 IV | 伤害+28%, 攻速+15% |
| IV | `passive_agility_4` | 敏捷身法 IV | 移速+24%, 闪避+20% |
| IV | `passive_ironwill_4` | 坚韧护盾 IV | 减伤+20%, 控制-25% |
| IV | `passive_energy_4` | 能量掌控 IV | 法力恢复+4%/秒, 法力+25%, CD-8% |
| V | `passive_regen_5` | 生命恢复 V | HP恢复+5%/秒 |
| V | `passive_focus_5` | 战斗专注 V | 伤害+35%, 攻速+20% |
| V | `passive_agility_5` | 敏捷身法 V | 移速+30%, 闪避+30% |
| V | `passive_ironwill_5` | 坚韧护盾 V | 减伤+25%, 低血加防 |
| V | `passive_energy_5` | 能量掌控 V | 法力恢复+5%/秒, 法力+30%, CD-10%, 消耗-3% |

### 主动节点

| 类别 | 节点ID | 名称 | 等级 |
|------|--------|------|------|
| 主动一 | `active_proj_1` | 能量弹 | Lv1 |
| 主动一 | `active_proj_2L` | 散射弹幕 | Lv2左 |
| 主动一 | `active_proj_3L` | 弹幕风暴 | Lv3左 |
| 主动一 | `active_proj_2R` | 穿透狙击 | Lv2右 |
| 主动一 | `active_proj_3R` | 毁灭射线 | Lv3右 |
| 主动二 | `active_move_1` | 冲击步 | Lv1 |
| 主动二 | `active_move_2L` | 突进斩 | Lv2左 |
| 主动二 | `active_move_3L` | 双闪连斩 | Lv3左 |
| 主动二 | `active_move_2R` | 灵巧闪避 | Lv2右 |
| 主动二 | `active_move_3R` | 虚空步伐 | Lv3右 |

### 武器技能节点

| 武器 | 节点ID | 名称 |
|------|--------|------|
| 剑 | `weapon_sword` | 横斩 |
| 弓 | `weapon_bow` | 瞄准射击 |
| 法杖 | `weapon_staff` | 魔法弹 |
| 大锤 | `weapon_hammer` | 撼地猛击 |
| 双刀 | `weapon_dualblade` | 十字斩 |

### 组合节点

| 类别 | 节点ID | 名称 | 等级 |
|------|--------|------|------|
| 组合 | `combo_1_lv1` / `combo_1_lv2` | 协同打击 / 双重协同 | Lv1 / Lv2 |
| 组合 | `combo_2_lv1` / `combo_2_lv2` | 法则领域 / 法则领域·极 | Lv1 / Lv2 |
| 组合 | `combo_3_lv1` / `combo_3_lv2` | 终焉审判 / 终焉审判·灭 | Lv1 / Lv2 |

---

## 附录 B：SkillData 扩展字段

```csharp
// SkillData.cs 新增字段
[Header("技能树绑定")]
[Tooltip("对应技能树节点ID，空=不属于技能树(初始技能)")]
public string skillTreeBindingId;

[Tooltip("此技能是否为组合技能")]
public bool isComboSkill;

[Tooltip("组合技能对应的配方ID")]
public string comboRecipeId;

[Tooltip("此技能是否为武器技能")]
public bool isWeaponSkill;

[Tooltip("武器技能对应的武器类型(如 Sword/Bow/Staff)")]
public string weaponType;
```

---

## 附录 C：关键设计决策

| 决策 | 选择 | 理由 |
|------|------|------|
| 被动机制 | **层级解锁 + 5选3装配** | 大幅降低点数消耗(70→5)，Build深度来自选择而非够不够点 |
| 被动线1 (原生存) | **HP恢复%/秒** 替代 HP上限% | 强调持续作战能力，与"恢复"主题匹配 |
| 被动线3 (原机动) | **闪避几率** 替代 跳跃高度 | 2D场景下闪避比跳跃增强更有策略深度 |
| 被动线5 (资源) | **法力恢复%/秒** 新增 | 新增可持续性维度，与CD缩减形成互补 |
| 树配置形式 | **单个 SkillTreeConfig SO** | 策划在一个面板看到整棵树，减少资产管理负担 |
| 节点技能绑定 | **间接引用 (nodeId → bindingId → SkillData)** | 松耦合，SkillData 可独立测试和调整 |
| 被动效果计算 | **运行时累加 + 缓存 (仅已装配节点)** | 避免每帧遍历所有节点，只在状态变更时刷新 |
| 武器技能获取 | **装备武器自动获得1个技能，0技能点** | 武器本身就是投资成本，不应额外消耗技能点 |
| 武器节点与组合 | **武器节点可作组合素材** | 增加武器切换的策略深度，丰富Build可能性 |
| 分支互斥 | **解锁时锁定另一分支** | 简单可靠，不需要额外状态字段 |
| 存档格式 | **JSON (JsonUtility)** | 轻量，可读，与团结引擎完全兼容 |
| UI 实现 | **UGUI + 自定义节点渲染** | 团结引擎原生UI系统 |
| 技能槽上限 | **4个（不变）** | 保持与现有 SkillManager 一致 |

---

## 附录 D：被动技能全览速查 (v2.0)

| 装配位 | 层级 I (1pt) | 层级 II (1pt) | 层级 III (1pt) | 层级 IV (1pt) | 层级 V (1pt) |
|:------:|:------------:|:-------------:|:--------------:|:-------------:|:------------:|
| **位1** | HP恢复+1%/秒 | HP恢复+2%/秒 | HP恢复+3%/秒 | HP恢复+4%/秒 | HP恢复+5%/秒 |
| **位2** | 伤害+8% | 伤害+15% | 伤害+22% 攻速+10% | 伤害+28% 攻速+15% | 伤害+35% 攻速+20% |
| **位3** | 移速+6% | 移速+12% | 移速+18% 闪避+15% | 移速+24% 闪避+20% | 移速+30% 闪避+30% |
| **位4** | 减伤+5% | 减伤+10% | 减伤+15% 硬直-20% | 减伤+20% 控制-25% | 减伤+25% 低血加防 |
| **位5** | 法力恢复+1%/秒 | 法力恢复+2%/秒 法力+20% | 法力恢复+3%/秒 法力+22% CD-5% | 法力恢复+4%/秒 法力+25% CD-8% | 法力恢复+5%/秒 法力+30% CD-10% 消耗-3% |
| **装配** | **5选3** | **5选3** | **5选3** | **5选3** | **5选3** |

> 满被动仅需 **5技能点**。同一技能线的高低等级同时装配时取最高值（如生命恢复I 1%/秒 + 生命恢复II 2%/秒 = 3%/秒生效），不同线可叠加。

---

## 附录 E：v1.2 → v2.0 变更记录

| 变更项 | v1.2 (旧) | v2.0 (新) | 依据 |
|--------|----------|----------|------|
| 被动列1 (生存线) | HP上限+15%~+60% | HP恢复+1%~+5%/秒 | DrawIO v2 |
| 被动列2 (输出线) | 伤害+8%~35%, 攻速+10%~20% | **不变** | DrawIO v2 |
| 被动列3 (机动线) | 移速+6%~30%, 跳跃+15%~25%, 二段跳 | 移速+6%~30%, 闪避+15%~30% | DrawIO v2 |
| 被动列4 (防御线) | 减伤+5%~25%, 硬直-20%~40%, 控制-25%~40% | 减伤+5%~25%, 硬直-20%, 控制-25%, 低血加防 | DrawIO v2 (简化) |
| 被动列5 (资源线) | 法力上限+10%~50%, CD-8%~18%, 消耗-10%~15% | 法力恢复+1%~5%/秒, 法力+20%~30%, CD-5%~10%, 消耗降低3% | DrawIO v2 |
| 被动节点ID | `passive_vitality_1~5` | `passive_regen_1~5` | 语义匹配新效果 |
| 主动技能 | 不变 | 不变 | DrawIO v2 未改 |
| 武器技能 | 不变 | 不变 | DrawIO v2 未改 |
| 组合技能 | 不变 | 不变 | DrawIO v2 未改 |
| 新增枚举值 | — | `DodgeChance`, `HitStunReduction`, `ControlReduction`, `ManaCostReduction` | 支持新被动效果类型 |
| 框架 | 不变 | 不变 | 装配制规则不变 |

---
