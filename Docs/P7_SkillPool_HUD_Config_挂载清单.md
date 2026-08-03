# P7 SkillPool + SkillBarHUD + SkillConfigUI 挂载清单

## 一、Player GameObject 操作

### 1.1 添加组件

在 Player GameObject 上 → Add Component → SkillPool
在 Player GameObject 上 → Add Component → CombinationCraftSystem（如未添加）

### 1.2 SkillPool 组件字段

在 Player 的 SkillPool 组件上：

拖入 `Assets/Scripts/Skills/` 下的 SkillData SO → `Initial Skills` 数组（按需配置初始技能）

### 1.3 CombinationCraftSystem 组件字段

在 Player 的 CombinationCraftSystem 组件上：

拖入 `CombinationSkillData` SO → `Recipe Lv1` 字段
拖入 `CombinationSkillData` SO → `Recipe Lv2` 字段
拖入 `CombinationSkillData` SO → `Recipe Lv3` 字段

### 1.4 SkillManager 组件字段（新增）

在 Player 的 SkillManager 组件上：

拖入 `SynergyConfig` SO → `Synergy Config` 字段
设置 `Initial Skill Points` = 10（如需覆盖默认）
拖入 BranchUpgradeSystem 引用（通常 Awake 自动创建，可留空） → `Branch System` 字段

---

## 二、Canvas/HUD/SkillBarPanel — 新建 + SkillBarHUD

### 2.1 创建节点

在 `Canvas/HUD` 下 → 右键 Create Empty → 命名为 `SkillBarPanel`
在 `SkillBarPanel` 上 → Add Component → SkillBarHUD
在 `SkillBarPanel` 下创建 4 个子节点，每个含 Image(Icon) + TMP_Text(Key) + Image(CooldownOverlay) + TMP_Text(CooldownText) + Button：

```
Canvas/HUD/SkillBarPanel          ← 挂 SkillBarHUD
├── Slot_Q (Image)
│   ├── Icon (Image)
│   ├── KeyText (TMP_Text)
│   ├── CooldownOverlay (Image)
│   └── CooldownText (TMP_Text)
├── Slot_E (同上结构)
├── Slot_R (同上结构)
└── Slot_F (同上结构)
```

Slot_Q/Slot_E/Slot_R/Slot_F 四个父节点需挂 Button 组件。

### 2.2 挂载 SkillBarHUD 字段

在 `Canvas/HUD/SkillBarPanel` 的 SkillBarHUD 组件上：

**槽位 0 (Q):**
拖入 `Canvas/HUD/SkillBarPanel/Slot_Q/Icon` → `Slot0 Icon`
拖入 `Canvas/HUD/SkillBarPanel/Slot_Q/KeyText` → `Slot0 KeyText`
拖入 `Canvas/HUD/SkillBarPanel/Slot_Q/CooldownOverlay` → `Slot0 CooldownOverlay`
拖入 `Canvas/HUD/SkillBarPanel/Slot_Q/CooldownText` → `Slot0 CooldownText`
拖入 `Canvas/HUD/SkillBarPanel/Slot_Q` → `Slot0 Button`

**槽位 1 (E):** 同上模式，`Slot_E` → `Slot1 *`

**槽位 2 (R):** 同上模式，`Slot_R` → `Slot2 *`

**槽位 3 (F):** 同上模式，`Slot_F` → `Slot3 *`

**Default Key Labels:** 保持默认 ["Q","E","R","F"] 或自定义

---

## 三、Canvas/SkillConfigPanel — 新建 + SkillConfigUI

### 3.1 创建节点

在 `Canvas` 下 → 右键 Create Empty → 命名为 `SkillConfigPanel` → 添加 Image(背景)
在 `SkillConfigPanel` 上 → Add Component → SkillConfigUI

```
Canvas/SkillConfigPanel                ← 挂 SkillConfigUI
├── LeftColumn (Image + VerticalLayoutGroup)
│   ├── SkillListContainer (ScrollRect → Content: 空, 挂 ContentSizeFitter)
│   ├── SkillListItemPrefab (模板, 初始inactive)
│   │   ├── Icon (Image)
│   │   ├── Name (TMP_Text)
│   │   └── Level (TMP_Text)
│   └── EmptyHint (TMP_Text, 初始active)
├── RightColumn (Image)
│   ├── Slot0_Q (Image)
│   │   ├── KeyLabel (TMP_Text, text="Q")
│   │   ├── Icon (Image)
│   │   ├── Name (TMP_Text)
│   │   ├── Level (TMP_Text)
│   │   └── ChangeBtn (Button)
│   ├── Slot1_E (同上,Q→E)
│   ├── Slot2_R (同上,Q→R)
│   └── Slot3_F (同上,Q→F)
├── SkillSelectPopup (Image, 初始inactive)
│   ├── PopupListContainer (ScrollRect → Content: 空)
│   ├── PopupItemPrefab (模板, 初始inactive)
│   │   ├── Icon (Image)
│   │   ├── Name (TMP_Text)
│   │   ├── Level (TMP_Text)
│   │   └── EquippedMark (GameObject)
│   └── PopupCloseBtn (Button)
└── 跳转按钮区域
    ├── ToCraftBtn (Button + TMP_Text: "合成")
    └── ToSkillTreeBtn (Button + TMP_Text: "技能树")
```

**注意**: `SkillListItemPrefab` 和 `PopupItemPrefab` 需要分别挂载 `SkillListEntry` 和 `SkillPickerItem` 组件（见下方第四/五节）。Button 组件挂在这些 Prefab 根节点上。

### 3.2 挂载 SkillConfigUI 字段

在 `Canvas/SkillConfigPanel` 的 SkillConfigUI 组件上：

**左栏 — 已拥有技能列表:**
拖入 `Canvas/SkillConfigPanel/LeftColumn/SkillListContainer` → `Skill List Container`
拖入 `Canvas/SkillConfigPanel/LeftColumn/SkillListItemPrefab` → `Skill List Item Prefab`
拖入 `Canvas/SkillConfigPanel/LeftColumn/EmptyHint` → `Empty Hint`

**HUD Slot 0 (Q):**
拖入 `Canvas/SkillConfigPanel/RightColumn/Slot0_Q/KeyLabel` → `Slot0 KeyLabel`
拖入 `Canvas/SkillConfigPanel/RightColumn/Slot0_Q/Icon` → `Slot0 Icon`
拖入 `Canvas/SkillConfigPanel/RightColumn/Slot0_Q/Name` → `Slot0 Name`
拖入 `Canvas/SkillConfigPanel/RightColumn/Slot0_Q/Level` → `Slot0 Level`
拖入 `Canvas/SkillConfigPanel/RightColumn/Slot0_Q/ChangeBtn` → `Slot0 ChangeBtn`

**HUD Slot 1 (E):** 同上 → `Slot1 *`

**HUD Slot 2 (R):** 同上 → `Slot2 *`

**HUD Slot 3 (F):** 同上 → `Slot3 *`

**技能选择弹窗:**
拖入 `Canvas/SkillConfigPanel/SkillSelectPopup` → `Skill Select Popup`
拖入 `Canvas/SkillConfigPanel/SkillSelectPopup/PopupListContainer` → `Popup List Container`
拖入 `Canvas/SkillConfigPanel/SkillSelectPopup/PopupItemPrefab` → `Popup Item Prefab`
拖入 `Canvas/SkillConfigPanel/SkillSelectPopup/PopupCloseBtn` → `Popup Close Btn`

**页面跳转:**
拖入 `Canvas/SkillConfigPanel/ToCraftBtn` → `To Craft Btn`
拖入 `Canvas/SkillConfigPanel/ToSkillTreeBtn` → `To Skill Tree Btn`
拖入 场景中的 `PanelManager` GameObject → `Panel Manager`
拖入 `Canvas/CraftPanel` → `Craft Panel`
拖入 `Canvas/SkillTreePanel` → `Skill Tree Panel`

---

## 四、SkillListItemPrefab — 挂 SkillListEntry

### 4.1 添加组件

在 `Canvas/SkillConfigPanel/LeftColumn/SkillListItemPrefab` 上 → Add Component → SkillListEntry

### 4.2 挂载字段

拖入 `SkillListItemPrefab/Icon` → `Icon`
拖入 `SkillListItemPrefab/Name` → `Name Text`
拖入 `SkillListItemPrefab/Level` → `Level Text`

---

## 五、PopupItemPrefab — 挂 SkillPickerItem

### 5.1 添加组件

在 `Canvas/SkillConfigPanel/SkillSelectPopup/PopupItemPrefab` 上 → Add Component → SkillPickerItem

### 5.2 挂载字段

拖入 `PopupItemPrefab/Icon` → `Icon`
拖入 `PopupItemPrefab/Name` → `Name Text`
拖入 `PopupItemPrefab/Level` → `Level Text`
拖入 `PopupItemPrefab/EquippedMark` → `Equipped Mark`
拖入 `PopupItemPrefab` 自身 → `Select Button`

---

## 六、SkillTreeUI 新增字段（Canvas/SkillTreePanel）

在 `Canvas/SkillTreePanel` 的 SkillTreeUI 组件上，展开「页面跳转」折叠区：

拖入 `Canvas/SkillConfigPanel/ToCraftBtn`（或新建一个跳转按钮） → `To Craft Btn`
拖入 `Canvas/PassivePanel`（或新建被动面板跳转按钮） → `To Passive Btn`
拖入 场景中的 `PanelManager` GameObject → `Panel Manager`
拖入 `Canvas/CraftPanel` → `Craft Panel`
拖入 `Canvas/PassivePanel` → `Passive Panel`

> 注意：SkillTreeUI 的 `toCraftBtn`/`toPassiveBtn` 需先在 SkillTreePanel 下创建对应 Button 节点。

---

## 七、SkillTreeUI — 节点数组扩容

现有 Hierarchy `Skill_Q_View` + `Skill_E_View` 共 10 节点 (2×5)。
P7 需支持 4 个 HUD 槽位 (Q/E/R/F = 4×5 = 20 节点)。

在 `Canvas/SkillTreePanel` 下：

复制 `Skill_Q_View` → 重命名为 `Skill_R_View`
复制 `Skill_E_View` → 重命名为 `Skill_F_View`

然后扩容 Inspector 中的 7 个数组（nodeButtons/nodeIcons/nodeNames/nodeLevels/nodeCostBadges/nodeBranchMasks/nodeGlows）从 10 → 20：

索引 10-14: `Skill_R_View/Node_Lv1` ~ `Node_Lv5` 按序拖入
索引 15-19: `Skill_F_View/Node_Lv1` ~ `Node_Lv5` 按序拖入

connectorLines 数组从 2 → 4：
索引 2: `Skill_R_View/ConnectorLines`
索引 3: `Skill_F_View/ConnectorLines`

---

## 八、SkillTreePanel — 新建页面跳转按钮

在 `Canvas/SkillTreePanel` 下：

创建 `ToCraftBtn` (Button + TMP_Text: "合成")
创建 `ToPassiveBtn` (Button + TMP_Text: "被动")

---

## 九、SkillConfigPanel — SkillListItemPrefab 需 Button

`SkillListItemPrefab` 根节点需挂 Button 组件（SkillPickerItem 代码中通过 GetComponent<Button> 获取，SkillListEntry 不强制但建议挂）。

---

## 十、PanelManager 注册

确保 SkillConfigPanel 挂载的 SkillConfigUI 实现 IPanel 接口，PanelManager 会在 Awake 时自动发现并注册。无需手动操作 PanelManager。
