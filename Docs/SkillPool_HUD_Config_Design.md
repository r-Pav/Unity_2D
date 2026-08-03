# SkillPool + HUD技能栏 + 技能配置页 — 架构分析与设计方案

> 文档日期：2026-07-20
> 项目路径：`Assets/Scripts/Skills/` & `Assets/Scripts/UI/`
> 状态：纯分析文档，不修改任何代码

---

## 目录

1. [现有架构梳理](#一现有架构梳理)
2. [SkillPool 设计方案](#二skillpool-设计方案)
3. [HUD 技能栏方案](#三hud-技能栏方案)
4. [技能配置页面方案](#四技能配置页面方案)
5. [改动影响范围](#五改动影响范围)
6. [数据流总览图](#六数据流总览图)

---

## 一、现有架构梳理

### 1.1 核心类一览

| 类名 | 类型 | 挂载位置 | 职责 |
|------|------|---------|------|
| **SkillManager** | MonoBehaviour | Player | 技能槽管理、冷却/法力、输入检测、协同联动 |
| **SkillSlot** | [Serializable] class | SkillManager 内嵌 | 单个槽位数据结构（data: SkillData） |
| **SkillData** | ScriptableObject | 资产文件 | 技能配置基类（名称/图标/热键/CD/法力） |
| **ActiveSkillData** | ScriptableObject (继承) | 资产文件 | 主动分支技能（Lv1~3 分支数据） |
| **CombinationSkillData** | ScriptableObject (继承) | 资产文件 | 组合技能（Lv2固定） |
| **WeaponSkillData** | ScriptableObject (继承) | 资产文件 | 武器专属技能 |
| **PassiveSkillData** | ScriptableObject (继承) | 资产文件 | 被动技能节点 |
| **BranchUpgradeSystem** | [Serializable] class | SkillManager 子模块 | 分支选择流程、升级消耗校验 |
| **CombinationCraftSystem** | MonoBehaviour | Player | 合成材料池、配方匹配、消耗产出 |
| **WeaponSkillLink** | MonoBehaviour | Player | 武器技能引用管理（不走 SkillSlot） |
| **SkillPointManager** | MonoBehaviour | Player | 技能点 CRUD |
| **StatModifierManager** | MonoBehaviour | Player | 属性修饰器管线 |
| **SaveSystem** | MonoBehaviour | Player | 序列化/反序列化存档 |

### 1.2 现有数据结构：SkillSlot

```csharp
[Serializable]
public class SkillSlot
{
    public SkillData data;   // 技能配置 SO 引用
    // (注释掉的) public ISkill skillInstance;
}
```

- 由 SkillManager 持有 `SkillSlot[] skillSlots = new SkillSlot[4]`
- SkillData.hotkey（KeyCode）决定了按键触发方式
- 4 个槽位分别对应 Q/E/（后两个未使用）
- 当前仅 slot[0]、slot[1] 有实际用途，slot[2]、slot[3] 闲置

### 1.3 SkillManager 核心数据流

```
PlayerController.OnUpdate()
  └── UpdateSubModules()
        └── SkillManager.OnPlayerUpdate(pc)
              ├── UpdateMana()          → EventBus.Trigger(PlayerManaChangedEvent)
              ├── UpdateCooldowns()     → 逐槽递减 cooldownTimers[i]
              └── CheckHotkeys()        → Input.GetKeyDown(slot.data.hotkey) → TryActivate(i)
```

**当前按键布局：**

| 槽位索引 | 当前绑定 | 检测方式 |
|---------|---------|---------|
| 0 | Q | `skillSlots[0].data.hotkey` (SO 字段) |
| 1 | E | `skillSlots[1].data.hotkey` (SO 字段) |
| 2 | — | 闲置 |
| 3 | — | 闲置 |

**问题**：热键绑定在 SO 资产上（`SkillData.hotkey`），不是运行时配置的。要改按键就要改 SO 文件——不灵活。

### 1.4 SkillManager 关键接口

| 方法 | 签名 | 用途 |
|------|------|------|
| GetSlotData | `SkillData GetSlotData(int slotIndex)` | 获取槽位技能数据 |
| SetSlot | `void SetSlot(int slotIndex, SkillData data, int level)` | 设置槽位并刷新联动 |
| ClearSlot | `void ClearSlot(int slotIndex)` | 清空槽位 |
| IsSlotEmpty | `bool IsSlotEmpty(int slotIndex)` | 检查槽位是否为空 |
| GetSkillLevel | `int GetSkillLevel(int slotIndex)` | 获取槽位技能等级 |
| GetCooldownTimer | `float GetCooldownTimer(int index)` | 冷却剩余（秒） |
| GetCooldownRatio | `float GetCooldownRatio(int index)` | 冷却比例（0~1） |
| LevelUp | `bool LevelUp(int slotIndex)` | 升级槽位技能 |
| TryActivate | `void TryActivate(int index)` | 激活技能（冷却→法力→事件） |
| SlotCount | `int SlotCount` | 槽位总数（4） |
| RefreshSynergy | `void RefreshSynergy()` | 刷新协同联动 Bonus |

### 1.5 CombinationCraftSystem 数据流

```
CraftUI.OpenMaterialList(targetSlot)
  └── craftSystem.GetAvailableMaterials()
        ├── 遍历 skillManager（所有 ActiveSkillData 槽位）
        └── weaponSkillLink（武器技能，视为 Lv1）
            → 返回 List<MaterialInfo>

CraftUI.Refresh()
  └── craftSystem.ValidateRecipe(m1, m2)
        └── 取 min(m1.level, m2.level)
            → recipeLv1 / recipeLv2 / recipeLv3

ConfirmCraft()
  └── craftSystem.Craft(m1, m2)
        ├── ValidateRecipe 校验
        ├── FindEmptySlot() → 查找空闲 SkillSlot
        └── skillManager.SetSlot(targetSlot, resultData, resultData.combinationLevel)
```

**关键发现**：
- `Craft()` 方法**没有消耗材料逻辑**（注释写明"已删消耗逻辑"）
- 产出直接分配到 SkillManager 的空闲槽位（`SetSlot`）
- 这意味着合成后材料技能仍然留在原槽位——这是一个待修复的遗留问题

### 1.6 SkillTreeUI 重要约束

```csharp
// 硬编码只渲染 slot 0 和 slot 1
for (int slot = 0; slot < 2; slot++)    // ← 只迭代前两个！
    RefreshSkill(slot);
```

以及对应的节点数组按 `slot * 5 + node` 映射（共 10 个节点 = 2 技能 × 5 层级）。

### 1.7 存档系统 SaveSystem

存档结构 `SlotSaveData`：
- `skillName`：技能名称字符串
- `level`：技能等级
- `chosenBranch`：分支选择（null/Left/Right）

通过 `FindSkillDataByName()` 在已挂载的 `skillSlots` 中查找 SO 引用。

**关键约束**：读档时依赖 skillSlots 中已拖入的 SO（通过名称匹配）。如果技能不在 skillSlots 的初始配置中，读档无法恢复。

### 1.8 现有依赖关系图

```
                   ┌──────────────────┐
                   │  PlayerController │
                   └────────┬─────────┘
                            │ OnUpdate()
         ┌──────────────────┼──────────────────┐
         ▼                  ▼                  ▼
  ┌─────────────┐  ┌──────────────┐  ┌──────────────────────┐
  │ SkillManager │  │ WeaponSkill   │  │ CombinationCraft     │
  │              │  │ Link          │  │ System               │
  │ skillSlots[] │  │ _currentSkill │  │ GetAvailableMaterials│
  │ slotLevels[] │  │ (独立引用)     │  │ ValidateRecipe       │
  │ cooldowns[]  │  │              │  │ Craft()              │
  └──────┬───────┘  └──────┬───────┘  └──────────┬───────────┘
         │                  │                      │
         │         ┌────────┴────────┐             │
         │         ▼                 ▼             │
         │  ┌────────────┐  ┌──────────────┐      │
         │  │ EventBus    │  │ PanelManager │      │
         │  │ (static)    │  │              │      │
         │  └──────┬──────┘  └──────────────┘      │
         │         │                                │
         ▼         ▼                                │
  ┌─────────────┐  ┌──────────────┐                │
  │ PlayerHUD   │  │ SkillTreeUI  │                │
  │ (HP/MP Bar) │  │ (slot 0/1)   │                │
  └─────────────┘  └──────────────┘                │
                               ┌────────────────────┘
                               ▼
                      ┌──────────────┐
                      │   CraftUI    │
                      └──────────────┘
```

---

## 二、SkillPool 设计方案

### 2.1 设计目标

将"已拥有技能"从 SkillManager 的 skillSlots（4 个固定槽位）中**解耦**出来，变为一个独立管理的技能池。所有技能获取渠道（初始解锁、合成产出、后续开放等）统一汇入 SkillPool。

核心原则：
- **SkillPool 管"拥有"**：记录玩家获得了哪些技能（数据引用 + 等级）
- **SkillManager 管"装备"**：4 个 HUD 槽位指向 SkillPool 中的哪些技能
- **SkillPool 是唯一真相源**（source of truth）用于技能拥有状态

### 2.2 数据结构

```csharp
/// <summary>
/// [P7] 技能池中的单个技能条目
/// </summary>
[Serializable]
public class OwnedSkillEntry
{
    public string id;               // 唯一标识（Guid 或 skillName）
    public SkillData skillData;     // SO 引用
    public int level;               // 当前等级
    public string source;           // 获得来源："initial" / "craft" / "unlock" / "quest"
    public DateTime acquiredAt;     // 获取时间（用于排序/展示）
}

/// <summary>
/// [P7] 技能池管理器 — 挂在 Player GameObject 上
/// 单例组件，统一管理所有"已拥有"技能
/// </summary>
public class SkillPool : MonoBehaviour
{
    // ============================================================
    // 序列化配置
    // ============================================================

    [Header("初始技能")]
    [Tooltip("游戏开始时自动拥有的技能列表")]
    [SerializeField] private List<SkillData> initialSkills;

    [Header("运行时状态")]
    [SerializeField] private List<OwnedSkillEntry> ownedSkills = new();

    // ============================================================
    // HUD 槽位配置
    // ============================================================

    /// <summary>4 个 HUD 槽位，每个槽位存储 OwnedSkillEntry 的索引，-1=空</summary>
    private int[] hudSlotAssignments = new int[4] { -1, -1, -1, -1 };

    // ============================================================
    // 事件
    // ============================================================

    public event Action OnPoolChanged;          // 技能池内容变化
    public event Action<int> OnHudSlotChanged;  // 指定 HUD 槽位变化

    // ============================================================
    // 公共接口 — 技能池管理
    // ============================================================

    /// <summary>添加技能到池子（重复技能则升级/合并）</summary>
    public bool AddSkill(SkillData skillData, int level = 1, string source = "unknown");

    /// <summary>移除技能（不可逆，如合成消耗）</summary>
    public bool RemoveSkill(string skillId);

    /// <summary>获取所有已拥有技能</summary>
    public List<OwnedSkillEntry> GetOwnedSkills();

    /// <summary>按 skillId 查找</summary>
    public OwnedSkillEntry FindSkill(string skillId);

    // ============================================================
    // 公共接口 — HUD 槽位绑定
    // ============================================================

    /// <summary>获取指定 HUD 槽位的技能（null=空）</summary>
    public OwnedSkillEntry GetHudSkill(int hudIndex);

    /// <summary>将技能装备到 HUD 槽位</summary>
    public bool EquipToHud(int hudIndex, string skillId);

    /// <summary>清空 HUD 槽位</summary>
    public void ClearHudSlot(int hudIndex);

    /// <summary>获取所有 HUD 槽位分配情况</summary>
    public int[] GetHudAssignments();

    // ============================================================
    // 公共接口 — 查询
    // ============================================================

    /// <summary>是否拥有某技能</summary>
    public bool HasSkill(string skillId);

    /// <summary>技能池中技能总数</summary>
    public int OwnedCount { get; }
}
```

### 2.3 与现有系统的对接点

#### 2.3.1 SkillManager 改造方向

当前 SkillManager 将"拥有"和"装备"混为一谈（skillSlots 既表达拥有又表达装备）。改造后：

```
改前：SkillManager.skillSlots[i].data = 某个 SO
     → 既表示"拥有此技能"，又表示"装备在第 i 槽"

改后：SkillPool.ownedSkills  = [火球术, 冰箭术, 雷击术]  ← "拥有"
     SkillManager.skillSlots[i].data = SkillPool.GetHudSkill(i)?.skillData  ← "装备"
```

具体改动：
- 移除 `SkillManager.skillSlots[]` 的 Inspector 手动拖拽配置
- 新增 `SkillPool skillPool` 引用
- `GetSlotData(i)` 改为从 `skillPool.GetHudSkill(i)` 查
- `SetSlot(i, data, level)` 改为 `skillPool.EquipToHud(i, skillId)`
- `ClearSlot(i)` 改为 `skillPool.ClearHudSlot(i)`

**但注意**：需保持向后兼容——`BranchUpgradeSystem` 直接读写 `skillSlots[]`，改造需涉及多文件。

**建议最小侵入方案**：保持 SkillManager 的 `skillSlots[]` 作为"HUD 装备槽位"运行时状态，但在 SkillPool 中新增拥有池。SkillManager 的 SetSlot/ClearSlot 内部同步调用 SkillPool。

#### 2.3.2 CombinationCraftSystem 改造

```
改前：Craft() → skillManager.SetSlot(targetSlot, resultData, level)
     → 合成产物直接占领一个 SkillSlot

改后：Craft() → skillPool.AddSkill(resultData, level, source:"craft")
     → 合成产物进入技能池，不自动装备到 HUD
     → 玩家需要进入技能配置页面手动装备
```

`GetAvailableMaterials()` 改为从 `skillPool.GetOwnedSkills()` 获取（过滤出可作为材料的类型），而非从 `skillManager.SlotCount` 轮询。

#### 2.3.3 SaveSystem 改造

存档新增 `ownedSkills` 池列表 + `hudAssignments` 数组：

```csharp
// 新增存档字段
public PoolSaveData[] ownedSkills;       // 技能池中所有技能
public int[] hudSlotAssignments;        // HUD 槽位绑定（存 skillId 而非直接存 SO 名）
```

`FindSkillDataByName()` 的逻辑需改为在 `SkillPool.ownedSkills` 中查找。

#### 2.3.4 SkillTreeUI 改造

当前硬编码只渲染 slot 0/1。改造后：
- 仍从 SkillManager 获取当前装备的技能引用（这是正确的——技能树展示的是装备中的主动技能的分支升级）
- 但如果未来需要展示所有已拥有的主动技能（不限于装备中的），就需要遍历 SkillPool

---

## 三、HUD 技能栏方案

### 3.1 组件结构

```
Canvas
└── HUD（已有 PlayerHUD.cs）
      └── SkillBarPanel（新增 Panel，HUD 子元素，常驻显示）
            ├── Slot_Q
            │     ├── Icon (Image)
            │     ├── KeyLabel (TMP_Text)
            │     ├── CooldownOverlay (Image, fillMethod=Radial360)
            │     ├── CooldownText (TMP_Text)
            │     └── Button (Button, 可用于点击触发)
            │
            ├── Slot_E（同上结构）
            ├── Slot_R（同上结构）
            └── Slot_F（同上结构）
```

### 3.2 脚本设计：SkillBarHUD.cs

```csharp
/// <summary>
/// [P7] HUD 技能栏 — 挂在 Canvas/HUD/SkillBarPanel 上
/// 职责：读取 SkillPool + SkillManager 的当前装备数据，
///       更新 4 个槽位的图标/冷却/按键提示。
/// 通过 Inspector 暴露 4 组 UI 元素让用户手动拖拽绑定。
/// </summary>
public class SkillBarHUD : MonoBehaviour
{
    // ============================================================
    // Inspector 绑定 — 4 组槽位 UI 元素
    // ============================================================

    [Header("槽位 0 (Q)")]
    [SerializeField] private Image slot0Icon;
    [SerializeField] private TMP_Text slot0KeyText;
    [SerializeField] private Image slot0CooldownOverlay;
    [SerializeField] private TMP_Text slot0CooldownText;
    [SerializeField] private Button slot0Button;

    [Header("槽位 1 (E)")]
    [SerializeField] private Image slot1Icon;
    [SerializeField] private TMP_Text slot1KeyText;
    [SerializeField] private Image slot1CooldownOverlay;
    [SerializeField] private TMP_Text slot1CooldownText;
    [SerializeField] private Button slot1Button;

    [Header("槽位 2 (R)")]
    [SerializeField] private Image slot2Icon;
    [SerializeField] private TMP_Text slot2KeyText;
    [SerializeField] private Image slot2CooldownOverlay;
    [SerializeField] private TMP_Text slot2CooldownText;
    [SerializeField] private Button slot2Button;

    [Header("槽位 3 (F)")]
    [SerializeField] private Image slot3Icon;
    [SerializeField] private TMP_Text slot3KeyText;
    [SerializeField] private Image slot3CooldownOverlay;
    [SerializeField] private TMP_Text slot3CooldownText;
    [SerializeField] private Button slot3Button;

    [Header("默认按键文字（未绑定技能时显示）")]
    [SerializeField] private string[] defaultKeyLabels = { "Q", "E", "R", "F" };

    // ============================================================
    // 运行时引用
    // ============================================================

    private SkillManager skillManager;
    private SkillPool skillPool;

    // ============================================================
    // 生命周期
    // ============================================================

    private void Awake()
    {
        var player = FindObjectOfType<PlayerController>();
        if (player != null)
        {
            skillManager = player.GetComponent<SkillManager>();
            skillPool = player.GetComponent<SkillPool>();
        }
    }

    private void OnEnable()
    {
        // 订阅技能变化事件
        if (skillPool != null)
        {
            skillPool.OnPoolChanged += RefreshAll;
            skillPool.OnHudSlotChanged += RefreshSlot;
        }
        EventBus.Subscribe<SkillCooldownEndEvent>(OnCooldownEnd);
        EventBus.Subscribe<SkillLevelChangedEvent>(OnSkillLevelChanged);
        RefreshAll();
    }

    private void OnDisable()
    {
        if (skillPool != null)
        {
            skillPool.OnPoolChanged -= RefreshAll;
            skillPool.OnHudSlotChanged -= RefreshSlot;
        }
        EventBus.Unsubscribe<SkillCooldownEndEvent>(OnCooldownEnd);
        EventBus.Unsubscribe<SkillLevelChangedEvent>(OnSkillLevelChanged);
    }

    private void Update()
    {
        // 每帧更新冷却覆盖（冷却持续变化，不适合纯事件驱动）
        UpdateCooldownDisplay(0);
        UpdateCooldownDisplay(1);
        UpdateCooldownDisplay(2);
        UpdateCooldownDisplay(3);
    }

    // ============================================================
    // 刷新逻辑
    // ============================================================

    private void RefreshAll()
    {
        for (int i = 0; i < 4; i++) RefreshSlot(i);
    }

    /// <summary>刷新单个槽位：图标 + 按键文字</summary>
    private void RefreshSlot(int index)
    {
        var elements = GetSlotElements(index);
        if (elements.icon == null) return; // 未在 Inspector 中绑定

        var ownedSkill = skillPool?.GetHudSkill(index);
        bool hasSkill = ownedSkill != null && ownedSkill.skillData != null;

        // 图标
        elements.icon.enabled = hasSkill;
        elements.icon.sprite = hasSkill ? ownedSkill.skillData.icon : null;

        // 按键文字
        if (elements.keyText != null)
        {
            elements.keyText.text = hasSkill
                ? GetKeyLabel(index)
                : defaultKeyLabels[index];
        }

        // 按钮（可点击激活技能 → 对于移动端或鼠标点击释放）
        if (elements.button != null)
        {
            elements.button.interactable = hasSkill;
            elements.button.onClick.RemoveAllListeners();
            if (hasSkill)
            {
                int capturedIndex = index;
                elements.button.onClick.AddListener(() => skillManager?.TryActivate(capturedIndex));
            }
        }
    }

    private void UpdateCooldownDisplay(int index)
    {
        if (skillManager == null) return;
        var elements = GetSlotElements(index);
        if (elements.cooldownOverlay == null && elements.cooldownText == null) return;

        float ratio = skillManager.GetCooldownRatio(index);
        float remaining = skillManager.GetCooldownTimer(index);

        if (elements.cooldownOverlay != null)
        {
            elements.cooldownOverlay.fillAmount = ratio;
            elements.cooldownOverlay.enabled = ratio > 0.01f;
        }

        if (elements.cooldownText != null)
        {
            elements.cooldownText.text = remaining > 0.1f ? remaining.ToString("F1") : "";
            elements.cooldownText.enabled = remaining > 0.1f;
        }
    }

    // ============================================================
    // 辅助方法
    // ============================================================

    private string GetKeyLabel(int index) => index switch
    {
        0 => "Q", 1 => "E", 2 => "R", 3 => "F", _ => ""
    };

    // 事件回调
    private void OnCooldownEnd(SkillCooldownEndEvent e) { } // 由 Update 持续刷新，不需要额外处理
    private void OnSkillLevelChanged(SkillLevelChangedEvent e) => RefreshSlot(e.slotIndex);

    // ============================================================
    // 槽位元素辅助结构（减少重复代码）
    // ============================================================

    private SlotElements GetSlotElements(int index) => index switch
    {
        0 => new SlotElements(slot0Icon, slot0KeyText, slot0CooldownOverlay, slot0CooldownText, slot0Button),
        1 => new SlotElements(slot1Icon, slot1KeyText, slot1CooldownOverlay, slot1CooldownText, slot1Button),
        2 => new SlotElements(slot2Icon, slot2KeyText, slot2CooldownOverlay, slot2CooldownText, slot2Button),
        3 => new SlotElements(slot3Icon, slot3KeyText, slot3CooldownOverlay, slot3CooldownText, slot3Button),
        _ => new SlotElements(null, null, null, null, null)
    };

    private struct SlotElements
    {
        public Image icon;
        public TMP_Text keyText;
        public Image cooldownOverlay;
        public TMP_Text cooldownText;
        public Button button;

        public SlotElements(Image i, TMP_Text k, Image c, TMP_Text ct, Button b)
        {
            icon = i; keyText = k; cooldownOverlay = c; cooldownText = ct; button = b;
        }
    }
}
```

### 3.3 Inspector 暴露方式

| 字段 | 类型 | 说明 |
|------|------|------|
| `slot0~3Icon` | Image | 技能图标 |
| `slot0~3KeyText` | TMP_Text | 按键提示文字（Q/E/R/F） |
| `slot0~3CooldownOverlay` | Image | 冷却遮罩（Radial360 fill） |
| `slot0~3CooldownText` | TMP_Text | 冷却倒计时数字 |
| `slot0~3Button` | Button | 可点击触发技能 |

用户在 Inspector 中从 Hierarchy 拖拽对应的 UI 元素到这些字段即可，无需写代码。

### 3.4 更新逻辑

```
OnEnable:
  → 订阅 SkillPool.OnPoolChanged, SkillPool.OnHudSlotChanged
  → RefreshAll()

OnDisable:
  → 取消订阅

Update (每帧):
  → 4 次 UpdateCooldownDisplay(i)  // 更新冷却覆盖 + 数字

SkillPool 事件触发:
  OnPoolChanged → RefreshAll()     // 技能池增删
  OnHudSlotChanged(i) → RefreshSlot(i)  // HUD 装备变更
```

---

## 四、技能配置页面方案

### 4.1 面板结构

```
Canvas
└── SkillConfigPanel（新增 Panel，初始 inactive）
      ├── Header
      │     ├── Title (TMP_Text "技能配置")
      │     ├── SkillPointLabel (TMP_Text "技能点: 5")
      │     └── CloseBtn (Button)
      │
      └── Body (两栏布局)
            ├── LeftColumn（已拥有技能列表）
            │     ├── ScrollView
            │     │     └── Content
            │     │           ├── SkillListItem_Prefab (技能条目模板)
            │     │           │     ├── Icon (Image)
            │     │           │     ├── Name (TMP_Text)
            │     │           │     ├── Level (TMP_Text)
            │     │           │     ├── TypeBadge (Image)
            │     │           │     └── EquipBtn (Button)
            │     │           ├── SkillListItem_1
            │     │           ├── SkillListItem_2
            │     │           └── ...
            │     │
            │     └── EmptyHint (TMP_Text "尚未拥有任何技能")
            │
            └── RightColumn（HUD 槽位绑定）
                  ├── SlotConfig_0 (Q)
                  │     ├── KeyLabel (TMP_Text "Q")
                  │     ├── Icon (Image)
                  │     ├── Name (TMP_Text)
                  │     ├── Level (TMP_Text)
                  │     └── ChangeBtn (Button "更换")
                  │
                  ├── SlotConfig_1 (E)
                  ├── SlotConfig_2 (R)
                  └── SlotConfig_3 (F)
```

### 4.2 脚本设计：SkillConfigUI.cs

```csharp
/// <summary>
/// [P7] 技能配置页面 — 挂在 SkillConfigPanel 上
/// 左栏：SkillPool 中所有已拥有技能
/// 右栏：4 个 HUD 槽位当前绑定，点击可弹出技能列表选技能替换
/// </summary>
public class SkillConfigUI : MonoBehaviour, IPanel
{
    PanelType IPanel.PanelType => PanelType.FullScreen;
    bool IPanel.PauseGame => true;
    bool IPanel.LockInput => true;
    bool IPanel.ShowCursor => true;

    // ============================================================
    // Inspector 绑定 — 左栏
    // ============================================================

    [SerializeField] private Transform skillListContainer;
    [SerializeField] private GameObject skillListItemPrefab;  // 技能条目模板
    [SerializeField] private TMP_Text emptyHint;

    // ============================================================
    // Inspector 绑定 — 右栏（4 个 HUD 槽配置区域）
    // ============================================================

    [Header("HUD Slot 0 (Q)")]
    [SerializeField] private TMP_Text slot0KeyLabel;
    [SerializeField] private Image slot0Icon;
    [SerializeField] private TMP_Text slot0Name;
    [SerializeField] private TMP_Text slot0Level;
    [SerializeField] private Button slot0ChangeBtn;

    [Header("HUD Slot 1 (E)")]
    [SerializeField] private TMP_Text slot1KeyLabel;
    [SerializeField] private Image slot1Icon;
    [SerializeField] private TMP_Text slot1Name;
    [SerializeField] private TMP_Text slot1Level;
    [SerializeField] private Button slot1ChangeBtn;

    [Header("HUD Slot 2 (R)")]
    [SerializeField] private TMP_Text slot2KeyLabel;
    [SerializeField] private Image slot2Icon;
    [SerializeField] private TMP_Text slot2Name;
    [SerializeField] private TMP_Text slot2Level;
    [SerializeField] private Button slot2ChangeBtn;

    [Header("HUD Slot 3 (F)")]
    [SerializeField] private TMP_Text slot3KeyLabel;
    [SerializeField] private Image slot3Icon;
    [SerializeField] private TMP_Text slot3Name;
    [SerializeField] private TMP_Text slot3Level;
    [SerializeField] private Button slot3ChangeBtn;

    [Header("技能选择弹窗")]
    [SerializeField] private GameObject skillSelectPopup;   // 弹出面板
    [SerializeField] private Transform popupListContainer;
    [SerializeField] private GameObject popupItemPrefab;
    [SerializeField] private Button popupCloseBtn;

    // ============================================================
    // 运行时引用
    // ============================================================

    private SkillManager skillManager;
    private SkillPool skillPool;
    private int pendingHudSlot = -1;  // 当前正在等待选择技能的 HUD 槽位

    // ============================================================
    // 生命周期
    // ============================================================

    private void Awake()
    {
        var player = FindObjectOfType<PlayerController>();
        if (player != null)
        {
            skillManager = player.GetComponent<SkillManager>();
            skillPool = player.GetComponent<SkillPool>();
        }

        // 绑定 4 个更换按钮
        slot0ChangeBtn?.onClick.AddListener(() => OpenSkillPicker(0));
        slot1ChangeBtn?.onClick.AddListener(() => OpenSkillPicker(1));
        slot2ChangeBtn?.onClick.AddListener(() => OpenSkillPicker(2));
        slot3ChangeBtn?.onClick.AddListener(() => OpenSkillPicker(3));

        popupCloseBtn?.onClick.AddListener(CloseSkillPicker);

        if (skillSelectPopup != null) skillSelectPopup.SetActive(false);
    }

    private void OnEnable()
    {
        if (skillPool != null)
        {
            skillPool.OnPoolChanged += RefreshAll;
            skillPool.OnHudSlotChanged += RefreshHudSlot;
        }
        RefreshAll();
    }

    private void OnDisable()
    {
        if (skillPool != null)
        {
            skillPool.OnPoolChanged -= RefreshAll;
            skillPool.OnHudSlotChanged -= RefreshHudSlot;
        }
    }

    // ============================================================
    // 刷新逻辑
    // ============================================================

    private void RefreshAll()
    {
        RefreshLeftList();
        RefreshRightSlots();
    }

    /// <summary>左栏：刷新已拥有技能列表</summary>
    private void RefreshLeftList()
    {
        // 清空旧列表
        foreach (Transform child in skillListContainer)
            Destroy(child.gameObject);

        var owned = skillPool?.GetOwnedSkills();
        if (owned == null || owned.Count == 0)
        {
            if (emptyHint != null) emptyHint.gameObject.SetActive(true);
            return;
        }

        if (emptyHint != null) emptyHint.gameObject.SetActive(false);

        foreach (var entry in owned)
        {
            var item = Instantiate(skillListItemPrefab, skillListContainer);
            // 填充图标、名称、等级、类型标记
            var itemScript = item.GetComponent<SkillListItem>();
            if (itemScript != null)
            {
                itemScript.Setup(entry);
            }
        }
    }

    /// <summary>右栏：刷新 4 个 HUD 槽位显示</summary>
    private void RefreshRightSlots()
    {
        for (int i = 0; i < 4; i++) RefreshHudSlot(i);
    }

    private void RefreshHudSlot(int index)
    {
        var ownedSkill = skillPool?.GetHudSkill(index);
        bool hasSkill = ownedSkill != null && ownedSkill.skillData != null;

        SetSlotDisplay(index, hasSkill ? ownedSkill.skillData.icon : null,
                       hasSkill ? ownedSkill.skillData.skillName : "空",
                       hasSkill ? $"Lv{ownedSkill.level}" : "");
    }

    // ============================================================
    // 技能选择弹窗
    // ============================================================

    /// <summary>打开技能选择器（点击右栏"更换"按钮时）</summary>
    private void OpenSkillPicker(int hudSlotIndex)
    {
        if (skillPool == null || skillSelectPopup == null) return;

        pendingHudSlot = hudSlotIndex;

        // 清空旧列表
        foreach (Transform child in popupListContainer)
            Destroy(child.gameObject);

        var owned = skillPool.GetOwnedSkills();
        var currentEquippedId = GetHudSkillId(hudSlotIndex);

        foreach (var entry in owned)
        {
            // 跳过当前已装备在此槽位的技能（可选）
            // 跳过已在其他 HUD 槽位装备的技能（避免重复装备）

            var item = Instantiate(popupItemPrefab, popupListContainer);
            var itemScript = item.GetComponent<SkillPickerItem>();
            if (itemScript != null)
            {
                itemScript.Setup(entry, isCurrentlyEquipped: entry.id == currentEquippedId,
                    onSelected: () => OnSkillSelected(entry.id));
            }
        }

        skillSelectPopup.SetActive(true);
    }

    private void CloseSkillPicker()
    {
        pendingHudSlot = -1;
        if (skillSelectPopup != null) skillSelectPopup.SetActive(false);
    }

    private void OnSkillSelected(string skillId)
    {
        if (skillPool != null && pendingHudSlot >= 0)
        {
            skillPool.EquipToHud(pendingHudSlot, skillId);
        }
        CloseSkillPicker();
        RefreshAll();
    }
}
```

### 4.3 打开方式

在 `HotkeyManager` 或 `PanelManager` 中新增一条快捷键（如 K 键），将 SkillConfigPanel 注册为 Panel：

```
HotkeyManager Inspector 新增一行：panel=SkillConfigPanel, key=K
PanelManager.panels 新增：SkillConfigPanel (type=FullScreen, pause=true, key=K)
```

### 4.4 与 SkillPool 交互流程

```
用户按 K → SkillConfigPanel 打开
  ├── OnEnable → RefreshAll()
  │     ├── 左栏：skillPool.GetOwnedSkills() → 生成技能条目列表
  │     └── 右栏：skillPool.GetHudSkill(0~3) → 显示 4 槽当前装备
  │
  └── 用户点击槽位 E 的 "更换" 按钮
        └── OpenSkillPicker(1)
              ├── 弹出技能选择列表（可滚动的技能名+图标）
              └── 用户点击某技能 → OnSkillSelected(skillId)
                    └── skillPool.EquipToHud(1, skillId)
                          ├── hudSlotAssignments[1] = skillId
                          ├── 触发 OnHudSlotChanged(1)
                          │     ├── SkillBarHUD.RefreshSlot(1)  // HUD 技能栏更新
                          │     └── SkillConfigUI.RefreshHudSlot(1)  // 配置页右栏更新
                          └── 触发 OnPoolChanged（如涉及装备状态）
                                └── SkillConfigUI.RefreshLeftList()  // 左栏更新（如标记"已装备"）
```

---

## 五、改动影响范围

### 5.1 新增文件

| 文件 | 位置 | 用途 |
|------|------|------|
| `SkillPool.cs` | `Assets/Scripts/Skills/` | 技能池管理器（MonoBehaviour，挂 Player） |
| `OwnedSkillEntry.cs` | `Assets/Scripts/Skills/` | 技能池条目数据结构 |
| `SkillBarHUD.cs` | `Assets/Scripts/UI/` | HUD 技能栏组件（挂 SkillBarPanel） |
| `SkillConfigUI.cs` | `Assets/Scripts/UI/` | 技能配置页面组件（挂 SkillConfigPanel） |

### 5.2 需修改的现有文件

#### SkillManager.cs — 中度修改

| 改动项 | 改动类型 | 说明 |
|--------|---------|------|
| 新增 `SkillPool skillPool` 引用 | 新增字段 | Awake 中 GetComponent |
| 修改 `GetSlotData(i)` | 逻辑修改 | 改为 `skillPool.GetHudSkill(i).skillData` |
| 修改 `SetSlot(i, data, level)` | 逻辑修改 | 改为 `skillPool.EquipToHud(i, skillId)` |
| 修改 `ClearSlot(i)` | 逻辑修改 | 改为 `skillPool.ClearHudSlot(i)` |
| 修改 `IsSlotEmpty(i)` | 逻辑修改 | 改为检查 skillPool 的 HUD 分配 |
| 修改 `CheckHotkeys()` | 逻辑修改 | 热键从固定映射改为：Q/E/R/F → slotIndex 0/1/2/3 |
| `SkillData.hotkey` 字段 | 废弃 | 不再从 SO 读取热键，改为固定 Q/E/R/F 映射 |

**保持不变的接口**（向后兼容）：`TryActivate()`, `UpdateCooldowns()`, `GetCooldownRatio()`, `LevelUp()`, `RefreshSynergy()`, `GetSkillLevel()`, `SpendMana()`, `HasMana()`

#### CombinationCraftSystem.cs — 中度修改

| 改动项 | 改动类型 | 说明 |
|--------|---------|------|
| 新增 `SkillPool skillPool` 引用 | 新增字段 | Awake 中 GetComponent |
| 修改 `GetAvailableMaterials()` | 逻辑修改 | 从 `skillPool.GetOwnedSkills()` 获取，而非 `skillManager` |
| 修改 `Craft()` | 逻辑修改 | 产出改为 `skillPool.AddSkill()`，不再占用 SkillSlot |
| 恢复材料消耗逻辑 | 逻辑修改 | `Craft()` 需调用 `skillPool.RemoveSkill()` 消耗材料 |
| 删除 `FindEmptySlot()` | 删除方法 | 不需要再查找空闲 SkillSlot |

#### CraftUI.cs — 小幅修改

| 改动项 | 改动类型 | 说明 |
|--------|---------|------|
| 新增 `SkillPool skillPool` 引用 | 新增字段 | 用于在材料列表刷新时获取最新数据 |
| 修复 Awake 依赖 | 逻辑修改 | 除 `craftSystem` 外也获取 `skillPool` |

#### SkillTreeUI.cs — 小幅修改

| 改动项 | 改动类型 | 说明 |
|--------|---------|------|
| 对 slot 的假设 | 逻辑修改 | 当前硬编码只处理 slot 0/1，改为循环所有 4 槽（但只渲染有 ActiveSkillData 的槽位） |

#### SaveSystem.cs — 中度修改

| 改动项 | 改动类型 | 说明 |
|--------|---------|------|
| 新增 `SkillPool skillPool` 引用 | 新增字段 | Awake 中 GetComponent |
| 修改 `CollectSkillSlots()` | 逻辑修改 | 改为保存 HUD 分配关系 + 技能池 |
| 新增 `CollectSkillPool()` | 新增方法 | 序列化 ownedSkills 列表 |
| 修改 `RestoreSkillSlots()` | 逻辑修改 | 从存档恢复技能池 + HUD 绑定 |
| 新增 `RestoreSkillPool()` | 新增方法 | 反序列化技能池 |
| `FindSkillDataByName()` | 保留但修改 | 需能在 SkillPool 中查找 |

#### Events.cs — 小幅修改

| 改动项 | 改动类型 | 说明 |
|--------|---------|------|
| 新增 `SkillPoolChangedEvent` | 新增事件 | 技能池增删时触发 |
| 新增 `HudSlotChangedEvent` | 新增事件 | HUD 槽位装备变更时触发 |
| 新增 `SkillAddedEvent` | 新增事件 | 新技能获得时触发（可选，用于特效/提示） |

#### HotkeyManager.cs — 小幅修改

| 改动项 | 改动类型 | 说明 |
|--------|---------|------|
| 新增一行配置 | 新增字段 | 用于 K 键（或其他键）打开 SkillConfigPanel |

或者更简单地，在 `PanelManager.panels` 数组中新增 SkillConfigPanel 条目即可。

#### PlayerController.cs — 小幅修改

| 改动项 | 改动类型 | 说明 |
|--------|---------|------|
| 新增 `SkillPool skillPool` 引用 | 新增字段 | Awake 中 GetComponent（供 UI 查询时快速获取） |

### 5.3 不改动的文件

以下文件无需改动：
- **SkillSlot.cs** — 数据结构保持，但作为"HUD 装备槽"使用
- **SkillData.cs / ActiveSkillData.cs / CombinationSkillData.cs / WeaponSkillData.cs / PassiveSkillData.cs** — SO 数据模型不变
- **BranchUpgradeSystem.cs** — 仍然引用 SkillManager 的 slotLevels，逻辑不变
- **PassiveEquipManager.cs** — 被动系统独立，不变
- **StatModifierManager.cs** — 属性系统独立，不变
- **SkillPointManager.cs** — 技能点系统独立，不变
- **WeaponSkillLink.cs** — 武器技能仍然独立于 SkillPool 之外（作为材料来源）
- **EventBus.cs** — 基础框架不变
- **PlayerHUD.cs** — 血条/蓝条不变
- **ISkill.cs** — 接口不变

### 5.4 Scene 改动

- Canvas 下新增 `SkillBarPanel` GameObject（HUD 子元素）
- Canvas 下新增 `SkillConfigPanel` GameObject（初始 inactive）
- SkillBarPanel 挂 `SkillBarHUD.cs`，拖拽绑定 4 组 UI 元素
- SkillConfigPanel 挂 `SkillConfigUI.cs`，拖拽绑定左右栏元素
- PanelManager 的 panels 数组新增 `SkillConfigPanel`

---

## 六、数据流总览图

### 6.1 改造后的完整数据流

```
                          ┌──────────────────────────────────────┐
                          │           SkillPool                   │
                          │  (Player 单例, 唯一真相源)              │
                          │                                      │
                          │  ownedSkills: List<OwnedSkillEntry>   │
                          │  hudSlotAssignments: int[4]           │
                          │                                      │
                          │  AddSkill() / RemoveSkill()           │
                          │  EquipToHud() / ClearHudSlot()        │
                          │  GetHudSkill() / GetOwnedSkills()     │
                          └────┬──────┬──────┬───────────────────┘
                               │      │      │
              ┌────────────────┘      │      └────────────────┐
              ▼                       ▼                       ▼
  ┌───────────────────┐   ┌──────────────────┐   ┌───────────────────────┐
  │  CombinationCraft  │   │   SkillManager    │   │  UI 层 (多个消费者)     │
  │  System            │   │                   │   │                       │
  │                    │   │ skillSlots[]      │   │ ┌───────────────────┐ │
  │ GetAvailable       │   │ (运行时缓存,       │   │ │ SkillBarHUD       │ │
  │ Materials() ◄──────│   │  指向 SkillPool)   │   │ │ (HUD 技能栏)      │ │
  │                    │   │                   │   │ └───────────────────┘ │
  │ Craft() ───────────┤   │ GetSlotData() ────│   │ ┌───────────────────┐ │
  │   → AddSkill()     │   │ SetSlot() ────────│   │ │ SkillConfigUI     │ │
  │   → RemoveSkill()  │   │ ClearSlot() ──────│   │ │ (技能配置页)       │ │
  └────────────────────┘   │ IsSlotEmpty() ────│   │ └───────────────────┘ │
                            │ CheckHotkeys()    │   │ ┌───────────────────┐ │
                            │ TryActivate()     │   │ │ SkillTreeUI       │ │
                            │ GetCooldown()     │   │ │ (技能树, 仅显示    │ │
                            └───────────────────┘   │ │  装备中的主动技能)  │ │
                                                    │ └───────────────────┘ │
                                                    │ ┌───────────────────┐ │
                                                    │ │ CraftUI            │ │
                                                    │ │ (合成UI, 选材料)    │ │
                                                    │ └───────────────────┘ │
                                                    └───────────────────────┘
```

### 6.2 技能获取流程

```
技能来源:
  ├── 初始解锁 → SkillPool.AddSkill(data, 1, "initial")
  ├── 合成产出 → SkillPool.AddSkill(resultData, 2, "craft")
  ├── 任务奖励 → SkillPool.AddSkill(data, 1, "quest")
  └── 商店购买 → SkillPool.AddSkill(data, 1, "shop")

         │
         ▼
    SkillPool 统一汇入
         │
         ├── 触发 OnPoolChanged → UI 刷新
         │
         └── 玩家进入配置页手动装备到 HUD
              └── EquipToHud(index, skillId)
                    └── 触发 OnHudSlotChanged → HUD 技能栏更新
```

### 6.3 按键映射新方案

| HUD 槽位 | 按键 | 数据来源 |
|---------|------|---------|
| 0 | Q | SkillPool.GetHudSkill(0) |
| 1 | E | SkillPool.GetHudSkill(1) |
| 2 | R | SkillPool.GetHudSkill(2) |
| 3 | F | SkillPool.GetHudSkill(3) |

热键**不再从 SkillData.hotkey 读取**，而是固定映射。SkillManager.CheckHotkeys() 改为：
```csharp
KeyCode[] hudKeys = { KeyCode.Q, KeyCode.E, KeyCode.R, KeyCode.F };
for (int i = 0; i < 4; i++)
{
    if (Input.GetKeyDown(hudKeys[i]))
        TryActivate(i);
}
```

---

## 七、实施建议

### 7.1 分阶段推进

| 阶段 | 内容 | 依赖 |
|------|------|------|
| **Phase A** | 新建 `SkillPool.cs` + `OwnedSkillEntry.cs`，Player 挂载，Awake 中初始化初始技能 | 无 |
| **Phase B** | 修改 `SkillManager` → 通过 SkillPool 桥接 skillSlots | Phase A |
| **Phase C** | 新建 `SkillBarHUD.cs` + Scene 搭建 SkillBarPanel | Phase B |
| **Phase D** | 修改 `CombinationCraftSystem` → 对接 SkillPool | Phase A |
| **Phase E** | 新建 `SkillConfigUI.cs` + Scene 搭建 SkillConfigPanel | Phase B |
| **Phase F** | 修改 `SaveSystem` → 序列化 SkillPool + HUD 绑定 | Phase B |
| **Phase G** | 修改 `CraftUI` + `SkillTreeUI` → 适配新接口 | Phase D |

### 7.2 关键风险点

1. **BranchUpgradeSystem 直接读写 skillSlots[]** — 需确认是否改为从 SkillPool 获取，或者保持现状（SkillManager 作为中间层）
2. **SaveSystem.FindSkillDataByName()** — 当前通过 skillSlots 中已拖入的 SO 按名称查找；改为 SkillPool 后需在池中查找，需确保池中的 SO 引用正确
3. **WeaponSkillLink 的独立技能引用** — 武器技能不走 SkillSlot，也不应进 SkillPool（因为装备/卸下时动态变化）；但合成系统从 WeaponSkillLink 获取材料——这个逻辑保持不变
4. **热键从 SO 移到代码** — 需要同步更新所有现有 ActiveSkillData SO 的 hotkey 字段（可以注释掉或保留为文档参考）
