# UI 挂载手册

> 基于 `Assets/Editor/CreateUIHierarchy_Editor.cs` 实际创建的 Hierarchy 节点名，映射到 `Assets/Scripts/UI/` 下各脚本的 `[SerializeField]` 字段。
> 生成时间: 2026-07-14

---

## 一、Hierarchy 全貌

```
Canvas (Canvas + CanvasScaler + GraphicRaycaster)
├── HUD ───────────────────────────────────────── 6.1
│   ├── HP_Bar (Slider)                            ─ 血条
│   │   ├── Background (Image)
│   │   ├── Fill Area
│   │   │   └── Fill (Image)
│   ├── HP_Text (TextMeshProUGUI)                  ─ HP 数值文本
│   ├── MP_Bar (Slider)                            ─ 蓝条
│   │   ├── Background (Image)
│   │   ├── Fill Area
│   │   │   └── Fill (Image)
│   └── MP_Text (TextMeshProUGUI)                  ─ MP 数值文本
│
├── PassivePanel (Image) ────────────────────────── 6.2
│   ├── LayerRow_I (Image)
│   │   ├── Title (TextMeshProUGUI)                ─ TI 标题
│   │   ├── LockIcon (Image)                       ─ 锁定标记(初始inactive)
│   │   ├── Slot_0 (Button + Image)
│   │   │   ├── Icon (Image)
│   │   │   ├── LineName (TextMeshProUGUI)
│   │   │   ├── Effect (TextMeshProUGUI)
│   │   │   ├── LockOverlay (Image)                ─ 锁定遮罩(初始inactive)
│   │   │   └── UnlockLabel (TextMeshProUGUI)      ─ "LvX解锁"
│   │   ├── Slot_1 (同上结构)
│   │   └── Slot_2 (同上结构)
│   ├── LayerRow_II
│   ├── LayerRow_III
│   ├── LayerRow_IV
│   └── LayerRow_V
│
├── LineSelectDialog (Image) ────────────────────── 6.2附属
│   ├── Title (TextMeshProUGUI)
│   ├── Option_0 (Button + Image)
│   │   └── Label (TextMeshProUGUI)
│   ├── Option_1
│   ├── Option_2
│   ├── Option_3
│   ├── Option_4
│   └── CloseBtn (Button + Image)
│       └── Label (TextMeshProUGUI)
│
├── SkillTreePanel (Image) ──────────────────────── 6.3
│   ├── SkillPointLabel (TextMeshProUGUI)
│   ├── Skill_Q_View
│   │   ├── Node_Lv1 (Image + Button)
│   │   │   ├── Icon (Image)
│   │   │   ├── Name (TextMeshProUGUI)
│   │   │   ├── Level (TextMeshProUGUI)
│   │   │   ├── CostBadge (TextMeshProUGUI)        ─ 消耗标记(初始inactive)
│   │   │   ├── BranchMask (Image)                 ─ 分支遮罩(初始inactive)
│   │   │   └── Glow (Image)                       ─ 发光效果(初始inactive)
│   │   ├── Node_Lv2 (同上)
│   │   ├── Node_Lv3 (同上)
│   │   ├── Node_Lv4 (同上)
│   │   ├── Node_Lv5 (同上)
│   │   └── ConnectorLines (Image)
│   └── Skill_E_View
│       ├── Node_Lv1 .. Node_Lv5 (同Q结构)
│       └── ConnectorLines (Image)
│
├── BranchChoiceDialog (Image) ──────────────────── 6.3附属
│   ├── LeftCard (Button + Image)
│   │   ├── Lv2Info (TextMeshProUGUI)
│   │   └── Lv3Info (TextMeshProUGUI)
│   ├── RightCard (Button + Image)
│   │   ├── Lv2Info (TextMeshProUGUI)
│   │   └── Lv3Info (TextMeshProUGUI)
│   ├── ConfirmBtn (Button + Image)
│   │   └── Label (TextMeshProUGUI)
│   └── CloseBtn (Button + Image)
│       └── Label (TextMeshProUGUI)
│
├── CraftPanel (Image) ──────────────────────────── 6.4
│   ├── Slot_Left (Button + Image)
│   │   ├── Icon (Image)
│   │   ├── Name (TextMeshProUGUI)
│   │   ├── Level (TextMeshProUGUI)
│   │   └── Placeholder (TextMeshProUGUI)
│   ├── Slot_Right (Button + Image)
│   │   ├── Icon (Image)
│   │   ├── Name (TextMeshProUGUI)
│   │   ├── Level (TextMeshProUGUI)
│   │   └── Placeholder (TextMeshProUGUI)
│   ├── LevelIndicator (TextMeshProUGUI)
│   ├── ResultPreview (Image)
│   │   ├── Icon (Image)
│   │   ├── Name (TextMeshProUGUI)
│   │   ├── Desc (TextMeshProUGUI)
│   │   ├── Stats (TextMeshProUGUI)
│   │   └── Placeholder (TextMeshProUGUI)
│   └── CraftBtn (Button + Image)
│       └── Label (TextMeshProUGUI)
│
├── CraftConfirmDialog (Image) ──────────────────── 6.4附属
│   ├── Mat1_Text (TextMeshProUGUI)
│   ├── Mat2_Text (TextMeshProUGUI)
│   ├── Result_Text (TextMeshProUGUI)
│   ├── ConfirmBtn (Button + Image)
│   │   └── Label (TextMeshProUGUI)
│   └── CancelBtn (Button + Image)
│       └── Label (TextMeshProUGUI)
│
└── CraftMatListDialog (Image) ──────────────────── 6.4附属
    ├── ItemContainer (RectTransform)               ─ 动态生成列表容器
    ├── ItemPrefab (Button + Image)                 ─ 模板(初始inactive)
    │   └── Label (TextMeshProUGUI)
    └── CloseBtn (Button + Image)
        └── Label (TextMeshProUGUI)
```

---

## 二、脚本 → 字段 → Hierarchy 映射表

### 2.1 PlayerHUD

**挂载目标**: `Canvas/HUD` (添加 `PlayerHUD` 组件)

| 字段名 | 类型 | Hierarchy 路径 | 节点已有组件 |
|--------|------|----------------|-------------|
| `hpBar` | Slider | `Canvas/HUD/HP_Bar` | Slider, Image(Background), Fill Area > Fill(Image) |
| `mpBar` | Slider | `Canvas/HUD/MP_Bar` | Slider, Image(Background), Fill Area > Fill(Image) |
| `hpText` | TMP_Text | `Canvas/HUD/HP_Text` | TextMeshProUGUI |
| `mpText` | TMP_Text | `Canvas/HUD/MP_Text` | TextMeshProUGUI |

---

### 2.2 PassiveUI

**挂载目标**: `Canvas/PassivePanel` (添加 `PassiveUI` 组件)

#### SO/系统引用

| 字段名 | 类型 | 来源 | 说明 |
|--------|------|------|------|
| `passiveEquipManager` | PassiveEquipManager | 场景中或SO | 非Hierarchy节点，需手动拖入 |

#### 子面板引用

| 字段名 | 类型 | Hierarchy 路径 | 节点已有组件 |
|--------|------|----------------|-------------|
| `lineSelectDialog` | LineSelectDialog | `Canvas/LineSelectDialog` | Image, LineSelectDialog(需添加) |

#### 数组: layerTitles (N=5, 按层索引)

| 索引 | Hierarchy 路径 | 节点已有组件 |
|------|----------------|-------------|
| 0 | `Canvas/PassivePanel/LayerRow_I/Title` | TextMeshProUGUI |
| 1 | `Canvas/PassivePanel/LayerRow_II/Title` | TextMeshProUGUI |
| 2 | `Canvas/PassivePanel/LayerRow_III/Title` | TextMeshProUGUI |
| 3 | `Canvas/PassivePanel/LayerRow_IV/Title` | TextMeshProUGUI |
| 4 | `Canvas/PassivePanel/LayerRow_V/Title` | TextMeshProUGUI |

#### 数组: layerLockIcons (N=5, 按层索引)

| 索引 | Hierarchy 路径 | 节点已有组件 |
|------|----------------|-------------|
| 0 | `Canvas/PassivePanel/LayerRow_I/LockIcon` | Image |
| 1 | `Canvas/PassivePanel/LayerRow_II/LockIcon` | Image |
| 2 | `Canvas/PassivePanel/LayerRow_III/LockIcon` | Image |
| 3 | `Canvas/PassivePanel/LayerRow_IV/LockIcon` | Image |
| 4 | `Canvas/PassivePanel/LayerRow_V/LockIcon` | Image |

#### 数组: slotButtons (N=15, layer*3+slot)

| 索引 | (层,槽) | Hierarchy 路径 | 节点已有组件 |
|------|---------|----------------|-------------|
| 0 | (0,0) | `Canvas/PassivePanel/LayerRow_I/Slot_0` | Button, Image |
| 1 | (0,1) | `Canvas/PassivePanel/LayerRow_I/Slot_1` | Button, Image |
| 2 | (0,2) | `Canvas/PassivePanel/LayerRow_I/Slot_2` | Button, Image |
| 3 | (1,0) | `Canvas/PassivePanel/LayerRow_II/Slot_0` | Button, Image |
| 4 | (1,1) | `Canvas/PassivePanel/LayerRow_II/Slot_1` | Button, Image |
| 5 | (1,2) | `Canvas/PassivePanel/LayerRow_II/Slot_2` | Button, Image |
| 6 | (2,0) | `Canvas/PassivePanel/LayerRow_III/Slot_0` | Button, Image |
| 7 | (2,1) | `Canvas/PassivePanel/LayerRow_III/Slot_1` | Button, Image |
| 8 | (2,2) | `Canvas/PassivePanel/LayerRow_III/Slot_2` | Button, Image |
| 9 | (3,0) | `Canvas/PassivePanel/LayerRow_IV/Slot_0` | Button, Image |
| 10 | (3,1) | `Canvas/PassivePanel/LayerRow_IV/Slot_1` | Button, Image |
| 11 | (3,2) | `Canvas/PassivePanel/LayerRow_IV/Slot_2` | Button, Image |
| 12 | (4,0) | `Canvas/PassivePanel/LayerRow_V/Slot_0` | Button, Image |
| 13 | (4,1) | `Canvas/PassivePanel/LayerRow_V/Slot_1` | Button, Image |
| 14 | (4,2) | `Canvas/PassivePanel/LayerRow_V/Slot_2` | Button, Image |

#### 数组: slotIcons (N=15, 同 layer*3+slot)

| 索引 | Hierarchy 路径 | 节点已有组件 |
|------|----------------|-------------|
| 0 | `Canvas/PassivePanel/LayerRow_I/Slot_0/Icon` | Image |
| 1 | `Canvas/PassivePanel/LayerRow_I/Slot_1/Icon` | Image |
| 2 | `Canvas/PassivePanel/LayerRow_I/Slot_2/Icon` | Image |
| 3 | `Canvas/PassivePanel/LayerRow_II/Slot_0/Icon` | Image |
| ... | ... (余类推, 共15项, 按层 I→V 每层3槽) | Image |

#### 数组: slotLineNames (N=15)

路径模式: `Canvas/PassivePanel/LayerRow_{I..V}/Slot_{0..2}/LineName` — 节点已有 `TextMeshProUGUI`

#### 数组: slotEffects (N=15)

路径模式: `Canvas/PassivePanel/LayerRow_{I..V}/Slot_{0..2}/Effect` — 节点已有 `TextMeshProUGUI`

#### 数组: lockOverlays (N=15)

路径模式: `Canvas/PassivePanel/LayerRow_{I..V}/Slot_{0..2}/LockOverlay` — 节点已有 `Image`

#### 数组: unlockLabels (N=15)

路径模式: `Canvas/PassivePanel/LayerRow_{I..V}/Slot_{0..2}/UnlockLabel` — 节点已有 `TextMeshProUGUI`

#### 数组: lineDialogOptions (N=5)

| 索引 | Hierarchy 路径 | 节点已有组件 |
|------|----------------|-------------|
| 0 | `Canvas/LineSelectDialog/Option_0` | Button, Image |
| 1 | `Canvas/LineSelectDialog/Option_1` | Button, Image |
| 2 | `Canvas/LineSelectDialog/Option_2` | Button, Image |
| 3 | `Canvas/LineSelectDialog/Option_3` | Button, Image |
| 4 | `Canvas/LineSelectDialog/Option_4` | Button, Image |

---

### 2.3 LineSelectDialog

**挂载目标**: `Canvas/LineSelectDialog` (添加 `LineSelectDialog` 组件)

| 字段名 | 类型 | Hierarchy 路径 | 节点已有组件 |
|--------|------|----------------|-------------|
| `optionButtons` [0..4] | Button[] | `Canvas/LineSelectDialog/Option_0` ~ `Option_4` | Button, Image, Label(TMP) |
| `closeBtn` | Button | `Canvas/LineSelectDialog/CloseBtn` | Button, Image, Label(TMP) |
| `title` | TMP_Text | `Canvas/LineSelectDialog/Title` | TextMeshProUGUI |

---

### 2.4 SkillTreeUI

**挂载目标**: `Canvas/SkillTreePanel` (添加 `SkillTreeUI` 组件)

#### SO/系统引用

| 字段名 | 类型 | 来源 | 说明 |
|--------|------|------|------|
| `skillManager` | SkillManager | 场景中或SO | 非Hierarchy，手动拖入 |
| `branchSystem` | BranchUpgradeSystem | 场景中或SO | Awake中会从skillManager自动赋值 |
| `skillPointManager` | SkillPointManager | 场景中或SO | 非Hierarchy，手动拖入 |

#### 单节点字段

| 字段名 | 类型 | Hierarchy 路径 | 节点已有组件 |
|--------|------|----------------|-------------|
| `skillPointLabel` | TMP_Text | `Canvas/SkillTreePanel/SkillPointLabel` | TextMeshProUGUI |

#### 数组: nodeButtons (N=10, skill*5+node)

索引 = skill_slot * 5 + node_index, 其中 skill_slot: 0=Q, 1=E

| 索引 | (技能,节点) | Hierarchy 路径 | 节点已有组件 |
|------|-------------|----------------|-------------|
| 0 | (Q,Lv1) | `Canvas/SkillTreePanel/Skill_Q_View/Node_Lv1` | Image, Button |
| 1 | (Q,Lv2) | `Canvas/SkillTreePanel/Skill_Q_View/Node_Lv2` | Image, Button |
| 2 | (Q,Lv3) | `Canvas/SkillTreePanel/Skill_Q_View/Node_Lv3` | Image, Button |
| 3 | (Q,Lv4) | `Canvas/SkillTreePanel/Skill_Q_View/Node_Lv4` | Image, Button |
| 4 | (Q,Lv5) | `Canvas/SkillTreePanel/Skill_Q_View/Node_Lv5` | Image, Button |
| 5 | (E,Lv1) | `Canvas/SkillTreePanel/Skill_E_View/Node_Lv1` | Image, Button |
| 6 | (E,Lv2) | `Canvas/SkillTreePanel/Skill_E_View/Node_Lv2` | Image, Button |
| 7 | (E,Lv3) | `Canvas/SkillTreePanel/Skill_E_View/Node_Lv3` | Image, Button |
| 8 | (E,Lv4) | `Canvas/SkillTreePanel/Skill_E_View/Node_Lv4` | Image, Button |
| 9 | (E,Lv5) | `Canvas/SkillTreePanel/Skill_E_View/Node_Lv5` | Image, Button |

#### 数组: nodeIcons (N=10, 同上索引)

路径模式: `Canvas/SkillTreePanel/Skill_{Q,E}_View/Node_Lv{1..5}/Icon` — 节点已有 `Image`

#### 数组: nodeNames (N=10, 同上索引)

路径模式: `Canvas/SkillTreePanel/Skill_{Q,E}_View/Node_Lv{1..5}/Name` — 节点已有 `TextMeshProUGUI`

#### 数组: nodeLevels (N=10, 同上索引)

路径模式: `Canvas/SkillTreePanel/Skill_{Q,E}_View/Node_Lv{1..5}/Level` — 节点已有 `TextMeshProUGUI`

#### 数组: nodeCostBadges (N=10, 同上索引)

路径模式: `Canvas/SkillTreePanel/Skill_{Q,E}_View/Node_Lv{1..5}/CostBadge` — 节点已有 `TextMeshProUGUI`

#### 数组: nodeBranchMasks (N=10, 同上索引)

路径模式: `Canvas/SkillTreePanel/Skill_{Q,E}_View/Node_Lv{1..5}/BranchMask` — 节点已有 `Image`

#### 数组: nodeGlows (N=10, 同上索引)

路径模式: `Canvas/SkillTreePanel/Skill_{Q,E}_View/Node_Lv{1..5}/Glow` — 节点已有 `Image`

#### 数组: connectorLines (N=2)

| 索引 | Hierarchy 路径 | 节点已有组件 |
|------|----------------|-------------|
| 0 | `Canvas/SkillTreePanel/Skill_Q_View/ConnectorLines` | Image |
| 1 | `Canvas/SkillTreePanel/Skill_E_View/ConnectorLines` | Image |

#### BranchChoiceDialog 引用字段 (通过SkillTreeUI间接绑定)

| 字段名 | 类型 | Hierarchy 路径 | 节点已有组件 |
|--------|------|----------------|-------------|
| `branchChoiceDialog` | BranchChoiceDialog | `Canvas/BranchChoiceDialog` | Image, BranchChoiceDialog(需添加) |
| `dialog_LeftCard` | GameObject | `Canvas/BranchChoiceDialog/LeftCard` | Button, Image, Lv2Info(TMP), Lv3Info(TMP) |
| `dialog_RightCard` | GameObject | `Canvas/BranchChoiceDialog/RightCard` | Button, Image, Lv2Info(TMP), Lv3Info(TMP) |
| `dialog_Lv2Info` [0] | TMP_Text | `Canvas/BranchChoiceDialog/LeftCard/Lv2Info` | TextMeshProUGUI |
| `dialog_Lv2Info` [1] | TMP_Text | `Canvas/BranchChoiceDialog/RightCard/Lv2Info` | TextMeshProUGUI |
| `dialog_Lv3Info` [0] | TMP_Text | `Canvas/BranchChoiceDialog/LeftCard/Lv3Info` | TextMeshProUGUI |
| `dialog_Lv3Info` [1] | TMP_Text | `Canvas/BranchChoiceDialog/RightCard/Lv3Info` | TextMeshProUGUI |
| `dialog_ConfirmBtn` | Button | `Canvas/BranchChoiceDialog/ConfirmBtn` | Button, Image, Label(TMP) |
| `dialog_CloseBtn` | Button | `Canvas/BranchChoiceDialog/CloseBtn` | Button, Image, Label(TMP) |

> **注意**: BranchChoiceDialog 自身脚本也需绑定以上部分字段(见2.5)，存在字段在两个脚本间共享引用的情况。建议 SkillTreeUI 中的 `dialog_*` 字段仅用于 `SetPreviewText()` 写文本，BranchChoiceDialog 自身绑定 `leftCard/rightCard/confirmBtn/closeBtn` 用于交互回调。

---

### 2.5 BranchChoiceDialog

**挂载目标**: `Canvas/BranchChoiceDialog` (添加 `BranchChoiceDialog` 组件)

| 字段名 | 类型 | Hierarchy 路径 | 节点已有组件 |
|--------|------|----------------|-------------|
| `leftCard` | Button | `Canvas/BranchChoiceDialog/LeftCard` | Button, Image, Lv2Info(TMP), Lv3Info(TMP) |
| `rightCard` | Button | `Canvas/BranchChoiceDialog/RightCard` | Button, Image, Lv2Info(TMP), Lv3Info(TMP) |
| `confirmBtn` | Button | `Canvas/BranchChoiceDialog/ConfirmBtn` | Button, Image, Label(TMP) |
| `closeBtn` | Button | `Canvas/BranchChoiceDialog/CloseBtn` | Button, Image, Label(TMP) |
| `lv2Info` [0] | TMP_Text | `Canvas/BranchChoiceDialog/LeftCard/Lv2Info` | TextMeshProUGUI |
| `lv2Info` [1] | TMP_Text | `Canvas/BranchChoiceDialog/RightCard/Lv2Info` | TextMeshProUGUI |
| `lv3Info` [0] | TMP_Text | `Canvas/BranchChoiceDialog/LeftCard/Lv3Info` | TextMeshProUGUI |
| `lv3Info` [1] | TMP_Text | `Canvas/BranchChoiceDialog/RightCard/Lv3Info` | TextMeshProUGUI |

---

### 2.6 CraftUI

**挂载目标**: `Canvas/CraftPanel` (添加 `CraftUI` 组件)

#### SO引用

| 字段名 | 类型 | 来源 | 说明 |
|--------|------|------|------|
| `craftSystem` | CombinationCraftSystem | 场景中或SO | 非Hierarchy，手动拖入 |

#### 左侧材料槽

| 字段名 | 类型 | Hierarchy 路径 | 节点已有组件 |
|--------|------|----------------|-------------|
| `slotLeft` | Button | `Canvas/CraftPanel/Slot_Left` | Button, Image, Icon(Image), Name(TMP), Level(TMP), Placeholder(TMP) |
| `slotLeftIcon` | Image | `Canvas/CraftPanel/Slot_Left/Icon` | Image |
| `slotLeftName` | TMP_Text | `Canvas/CraftPanel/Slot_Left/Name` | TextMeshProUGUI |
| `slotLeftLevel` | TMP_Text | `Canvas/CraftPanel/Slot_Left/Level` | TextMeshProUGUI |
| `slotLeftPlaceholder` | TMP_Text | `Canvas/CraftPanel/Slot_Left/Placeholder` | TextMeshProUGUI |

#### 右侧材料槽

| 字段名 | 类型 | Hierarchy 路径 | 节点已有组件 |
|--------|------|----------------|-------------|
| `slotRight` | Button | `Canvas/CraftPanel/Slot_Right` | Button, Image, Icon(Image), Name(TMP), Level(TMP), Placeholder(TMP) |
| `slotRightIcon` | Image | `Canvas/CraftPanel/Slot_Right/Icon` | Image |
| `slotRightName` | TMP_Text | `Canvas/CraftPanel/Slot_Right/Name` | TextMeshProUGUI |
| `slotRightLevel` | TMP_Text | `Canvas/CraftPanel/Slot_Right/Level` | TextMeshProUGUI |
| `slotRightPlaceholder` | TMP_Text | `Canvas/CraftPanel/Slot_Right/Placeholder` | TextMeshProUGUI |

#### 等级指示 & 合成按钮

| 字段名 | 类型 | Hierarchy 路径 | 节点已有组件 |
|--------|------|----------------|-------------|
| `levelIndicator` | TMP_Text | `Canvas/CraftPanel/LevelIndicator` | TextMeshProUGUI |
| `craftBtn` | Button | `Canvas/CraftPanel/CraftBtn` | Button, Image, Label(TMP) |

#### 结果预览 (ResultPreview)

| 字段名 | 类型 | Hierarchy 路径 | 节点已有组件 |
|--------|------|----------------|-------------|
| `previewIcon` | Image | `Canvas/CraftPanel/ResultPreview/Icon` | Image |
| `previewName` | TMP_Text | `Canvas/CraftPanel/ResultPreview/Name` | TextMeshProUGUI |
| `previewDesc` | TMP_Text | `Canvas/CraftPanel/ResultPreview/Desc` | TextMeshProUGUI |
| `previewStats` | TMP_Text | `Canvas/CraftPanel/ResultPreview/Stats` | TextMeshProUGUI |
| `previewPlaceholder` | TMP_Text | `Canvas/CraftPanel/ResultPreview/Placeholder` | TextMeshProUGUI |

#### CraftConfirmDialog 桥接字段 (CraftUI 直接写值到子面板)

| 字段名 | 类型 | Hierarchy 路径 | 节点已有组件 |
|--------|------|----------------|-------------|
| `confirmDialog` | CraftConfirmDialog | `Canvas/CraftConfirmDialog` | Image, CraftConfirmDialog(需添加) |
| `confirm_Mat1Text` | TMP_Text | `Canvas/CraftConfirmDialog/Mat1_Text` | TextMeshProUGUI |
| `confirm_Mat2Text` | TMP_Text | `Canvas/CraftConfirmDialog/Mat2_Text` | TextMeshProUGUI |
| `confirm_ResultText` | TMP_Text | `Canvas/CraftConfirmDialog/Result_Text` | TextMeshProUGUI |
| `confirm_ConfirmBtn` | Button | `Canvas/CraftConfirmDialog/ConfirmBtn` | Button, Image, Label(TMP) |
| `confirm_CancelBtn` | Button | `Canvas/CraftConfirmDialog/CancelBtn` | Button, Image, Label(TMP) |

#### CraftMatListDialog 桥接字段

| 字段名 | 类型 | Hierarchy 路径 | 节点已有组件 |
|--------|------|----------------|-------------|
| `matListDialog` | CraftMatListDialog | `Canvas/CraftMatListDialog` | Image, CraftMatListDialog(需添加) |
| `matListItemPrefab` | Button | `Canvas/CraftMatListDialog/ItemPrefab` | Button, Image, Label(TMP) |
| `matListContainer` | Transform | `Canvas/CraftMatListDialog/ItemContainer` | RectTransform |

---

### 2.7 CraftConfirmDialog

**挂载目标**: `Canvas/CraftConfirmDialog` (添加 `CraftConfirmDialog` 组件)

| 字段名 | 类型 | Hierarchy 路径 | 节点已有组件 |
|--------|------|----------------|-------------|
| `mat1Text` | TMP_Text | `Canvas/CraftConfirmDialog/Mat1_Text` | TextMeshProUGUI |
| `mat2Text` | TMP_Text | `Canvas/CraftConfirmDialog/Mat2_Text` | TextMeshProUGUI |
| `resultText` | TMP_Text | `Canvas/CraftConfirmDialog/Result_Text` | TextMeshProUGUI |
| `confirmBtn` | Button | `Canvas/CraftConfirmDialog/ConfirmBtn` | Button, Image, Label(TMP) |
| `cancelBtn` | Button | `Canvas/CraftConfirmDialog/CancelBtn` | Button, Image, Label(TMP) |

---

### 2.8 CraftMatListDialog

**挂载目标**: `Canvas/CraftMatListDialog` (添加 `CraftMatListDialog` 组件)

| 字段名 | 类型 | Hierarchy 路径 | 节点已有组件 |
|--------|------|----------------|-------------|
| `itemContainer` | Transform | `Canvas/CraftMatListDialog/ItemContainer` | RectTransform |
| `itemPrefab` | Button | `Canvas/CraftMatListDialog/ItemPrefab` | Button, Image, Label(TMP) |
| `closeBtn` | Button | `Canvas/CraftMatListDialog/CloseBtn` | Button, Image, Label(TMP) |

---

## 三、按 Hierarchy 路径索引 (快速查找)

### Canvas/HUD

```
Canvas/HUD                             ← 挂 PlayerHUD 组件
Canvas/HUD/HP_Bar                      ← Slider         → PlayerHUD.hpBar
Canvas/HUD/HP_Text                     ← TMP_Text        → PlayerHUD.hpText
Canvas/HUD/MP_Bar                      ← Slider         → PlayerHUD.mpBar
Canvas/HUD/MP_Text                     ← TMP_Text        → PlayerHUD.mpText
```

### Canvas/PassivePanel

```
Canvas/PassivePanel                    ← 挂 PassiveUI 组件
Canvas/PassivePanel/LayerRow_I/Title          ← TMP_Text  → PassiveUI.layerTitles[0]
Canvas/PassivePanel/LayerRow_I/LockIcon       ← Image     → PassiveUI.layerLockIcons[0]
Canvas/PassivePanel/LayerRow_I/Slot_0         ← Button    → PassiveUI.slotButtons[0]
Canvas/PassivePanel/LayerRow_I/Slot_0/Icon    ← Image     → PassiveUI.slotIcons[0]
Canvas/PassivePanel/LayerRow_I/Slot_0/LineName   ← TMP_Text  → PassiveUI.slotLineNames[0]
Canvas/PassivePanel/LayerRow_I/Slot_0/Effect     ← TMP_Text  → PassiveUI.slotEffects[0]
Canvas/PassivePanel/LayerRow_I/Slot_0/LockOverlay    ← Image  → PassiveUI.lockOverlays[0]
Canvas/PassivePanel/LayerRow_I/Slot_0/UnlockLabel    ← TMP_Text → PassiveUI.unlockLabels[0]
Canvas/PassivePanel/LayerRow_I/Slot_1         ← Button    → PassiveUI.slotButtons[1]
Canvas/PassivePanel/LayerRow_I/Slot_2         ← Button    → PassiveUI.slotButtons[2]
... (LayerRow_II~V 类推, 共 5层x3槽=15组)
```

### Canvas/LineSelectDialog

```
Canvas/LineSelectDialog                ← 挂 LineSelectDialog 组件
Canvas/LineSelectDialog/Title             ← TMP_Text  → LineSelectDialog.title
Canvas/LineSelectDialog/Option_0          ← Button    → LineSelectDialog.optionButtons[0]
Canvas/LineSelectDialog/Option_1          ← Button    → LineSelectDialog.optionButtons[1]
Canvas/LineSelectDialog/Option_2          ← Button    → LineSelectDialog.optionButtons[2]
Canvas/LineSelectDialog/Option_3          ← Button    → LineSelectDialog.optionButtons[3]
Canvas/LineSelectDialog/Option_4          ← Button    → LineSelectDialog.optionButtons[4]
Canvas/LineSelectDialog/CloseBtn          ← Button    → LineSelectDialog.closeBtn
```

同时被 PassiveUI 引用:
```
Canvas/LineSelectDialog                ← LineSelectDialog  → PassiveUI.lineSelectDialog
Canvas/LineSelectDialog/Option_0          ← Button          → PassiveUI.lineDialogOptions[0]
Canvas/LineSelectDialog/Option_1          ← Button          → PassiveUI.lineDialogOptions[1]
...
```

### Canvas/SkillTreePanel

```
Canvas/SkillTreePanel                  ← 挂 SkillTreeUI 组件
Canvas/SkillTreePanel/SkillPointLabel     ← TMP_Text  → SkillTreeUI.skillPointLabel
Canvas/SkillTreePanel/Skill_Q_View/Node_Lv1    ← Button,Image → nodeButtons[0], nodeIcons[0], etc.
Canvas/SkillTreePanel/Skill_Q_View/Node_Lv1/Icon      ← Image       → nodeIcons[0]
Canvas/SkillTreePanel/Skill_Q_View/Node_Lv1/Name      ← TMP_Text    → nodeNames[0]
Canvas/SkillTreePanel/Skill_Q_View/Node_Lv1/Level     ← TMP_Text    → nodeLevels[0]
Canvas/SkillTreePanel/Skill_Q_View/Node_Lv1/CostBadge ← TMP_Text    → nodeCostBadges[0]
Canvas/SkillTreePanel/Skill_Q_View/Node_Lv1/BranchMask← Image       → nodeBranchMasks[0]
Canvas/SkillTreePanel/Skill_Q_View/Node_Lv1/Glow      ← Image       → nodeGlows[0]
... (Q Lv2~Lv5, E Lv1~Lv5 共10节点)
Canvas/SkillTreePanel/Skill_Q_View/ConnectorLines     ← Image       → connectorLines[0]
Canvas/SkillTreePanel/Skill_E_View/ConnectorLines     ← Image       → connectorLines[1]
```

### Canvas/BranchChoiceDialog

```
Canvas/BranchChoiceDialog              ← 挂 BranchChoiceDialog 组件
Canvas/BranchChoiceDialog/LeftCard        ← Button    → BranchChoiceDialog.leftCard
Canvas/BranchChoiceDialog/LeftCard/Lv2Info   ← TMP_Text  → BranchChoiceDialog.lv2Info[0]
Canvas/BranchChoiceDialog/LeftCard/Lv3Info   ← TMP_Text  → BranchChoiceDialog.lv3Info[0]
Canvas/BranchChoiceDialog/RightCard       ← Button    → BranchChoiceDialog.rightCard
Canvas/BranchChoiceDialog/RightCard/Lv2Info  ← TMP_Text  → BranchChoiceDialog.lv2Info[1]
Canvas/BranchChoiceDialog/RightCard/Lv3Info  ← TMP_Text  → BranchChoiceDialog.lv3Info[1]
Canvas/BranchChoiceDialog/ConfirmBtn      ← Button    → BranchChoiceDialog.confirmBtn
Canvas/BranchChoiceDialog/CloseBtn        ← Button    → BranchChoiceDialog.closeBtn
```
同时被 SkillTreeUI 引用:
```
Canvas/BranchChoiceDialog              ← BranchChoiceDialog  → SkillTreeUI.branchChoiceDialog
Canvas/BranchChoiceDialog/LeftCard        ← GameObject        → SkillTreeUI.dialog_LeftCard
Canvas/BranchChoiceDialog/RightCard       ← GameObject        → SkillTreeUI.dialog_RightCard
Canvas/BranchChoiceDialog/ConfirmBtn      ← Button            → SkillTreeUI.dialog_ConfirmBtn
Canvas/BranchChoiceDialog/CloseBtn        ← Button            → SkillTreeUI.dialog_CloseBtn
```

### Canvas/CraftPanel

```
Canvas/CraftPanel                      ← 挂 CraftUI 组件
Canvas/CraftPanel/Slot_Left               ← Button    → CraftUI.slotLeft
Canvas/CraftPanel/Slot_Left/Icon          ← Image     → CraftUI.slotLeftIcon
Canvas/CraftPanel/Slot_Left/Name          ← TMP_Text  → CraftUI.slotLeftName
Canvas/CraftPanel/Slot_Left/Level         ← TMP_Text  → CraftUI.slotLeftLevel
Canvas/CraftPanel/Slot_Left/Placeholder   ← TMP_Text  → CraftUI.slotLeftPlaceholder
Canvas/CraftPanel/Slot_Right              ← Button    → CraftUI.slotRight
... (同理 Slot_Right 下4子节点)
Canvas/CraftPanel/LevelIndicator          ← TMP_Text  → CraftUI.levelIndicator
Canvas/CraftPanel/ResultPreview/Icon      ← Image     → CraftUI.previewIcon
Canvas/CraftPanel/ResultPreview/Name      ← TMP_Text  → CraftUI.previewName
Canvas/CraftPanel/ResultPreview/Desc      ← TMP_Text  → CraftUI.previewDesc
Canvas/CraftPanel/ResultPreview/Stats     ← TMP_Text  → CraftUI.previewStats
Canvas/CraftPanel/ResultPreview/Placeholder← TMP_Text → CraftUI.previewPlaceholder
Canvas/CraftPanel/CraftBtn               ← Button    → CraftUI.craftBtn
```

### Canvas/CraftConfirmDialog

```
Canvas/CraftConfirmDialog              ← 挂 CraftConfirmDialog 组件
Canvas/CraftConfirmDialog/Mat1_Text       ← TMP_Text  → CraftConfirmDialog.mat1Text  (也→CraftUI.confirm_Mat1Text)
Canvas/CraftConfirmDialog/Mat2_Text       ← TMP_Text  → CraftConfirmDialog.mat2Text  (也→CraftUI.confirm_Mat2Text)
Canvas/CraftConfirmDialog/Result_Text     ← TMP_Text  → CraftConfirmDialog.resultText (也→CraftUI.confirm_ResultText)
Canvas/CraftConfirmDialog/ConfirmBtn      ← Button    → CraftConfirmDialog.confirmBtn  (也→CraftUI.confirm_ConfirmBtn)
Canvas/CraftConfirmDialog/CancelBtn       ← Button    → CraftConfirmDialog.cancelBtn   (也→CraftUI.confirm_CancelBtn)
```

### Canvas/CraftMatListDialog

```
Canvas/CraftMatListDialog              ← 挂 CraftMatListDialog 组件
Canvas/CraftMatListDialog/ItemContainer   ← Transform → CraftMatListDialog.itemContainer (也→CraftUI.matListContainer)
Canvas/CraftMatListDialog/ItemPrefab      ← Button    → CraftMatListDialog.itemPrefab    (也→CraftUI.matListItemPrefab)
Canvas/CraftMatListDialog/CloseBtn        ← Button    → CraftMatListDialog.closeBtn
```

---

## 四、挂载注意事项

### 4.1 脚本组件需手动添加

以下 GameObject 仅有 Image 等基础组件，**不含**对应脚本，需手动 Add Component:

| GameObject | 需添加的脚本 |
|------------|-------------|
| `Canvas/HUD` | `PlayerHUD` |
| `Canvas/PassivePanel` | `PassiveUI` |
| `Canvas/LineSelectDialog` | `LineSelectDialog` |
| `Canvas/SkillTreePanel` | `SkillTreeUI` |
| `Canvas/BranchChoiceDialog` | `BranchChoiceDialog` |
| `Canvas/CraftPanel` | `CraftUI` |
| `Canvas/CraftConfirmDialog` | `CraftConfirmDialog` |
| `Canvas/CraftMatListDialog` | `CraftMatListDialog` |

### 4.2 跨脚本共享字段

部分 Hierarchy 节点被**两个脚本同时引用**，拖曳时注意:

1. **CraftConfirmDialog 的子节点** — 同时被 `CraftConfirmDialog` 自身和 `CraftUI` 引用。`CraftUI` 在 `OpenConfirmation()` 中直接写值到 `confirm_*` 字段然后调 `confirmDialog.Show()`。
2. **BranchChoiceDialog 的子节点** — 同时被 `BranchChoiceDialog` 自身和 `SkillTreeUI` 引用。`SkillTreeUI.SetPreviewText()` 写值后调 `branchChoiceDialog.Show()`。
3. **LineSelectDialog 的 Option 按钮** — 同时被 `LineSelectDialog.optionButtons[]` 和 `PassiveUI.lineDialogOptions[]` 引用。

### 4.3 非Hierarchy引用 (SO/场景对象)

以下字段不能从 Hierarchy 拖入，需从 Project 窗口或场景中其他 GameObject 拖入:

| 脚本 | 字段 | 类型 |
|------|------|------|
| PassiveUI | `passiveEquipManager` | PassiveEquipManager |
| SkillTreeUI | `skillManager` | SkillManager |
| SkillTreeUI | `branchSystem` | BranchUpgradeSystem (Awake中自动赋值) |
| SkillTreeUI | `skillPointManager` | SkillPointManager |
| CraftUI | `craftSystem` | CombinationCraftSystem |

### 4.4 大数组快速拖入技巧

PassiveUI 有 9 个长度为 15 的数组 (slotButtons/slotIcons/slotLineNames/slotEffects/lockOverlays/unlockLabels)，以及 2 个长度为 5 的数组 (layerTitles/layerLockIcons)。建议:

1. 先展开 PassivePanel 全部子节点
2. 锁定 Inspector (右上角锁图标)
3. 按 Hierarchy 从上到下顺序逐个拖入 (LayerRow_I→V, 每层 Slot_0→Slot_2)

SkillTreeUI 有 8 个长度为 10 的数组 — 按 Q.Lv1→Lv5 再 E.Lv1→Lv5 的顺序拖入。

### 4.5 组件类型对照

| 脚本字段类型 | Hierarchy 节点拖入时需匹配 | 示例 |
|-------------|--------------------------|------|
| `Button` | 挂有 Button 组件的 GameObject | Slot_0, Option_0, CraftBtn |
| `Image` | 挂有 Image 组件的 GameObject | Icon, LockOverlay, Glow |
| `TMP_Text` / `TextMeshProUGUI` | 挂有 TextMeshProUGUI 组件的 GameObject | Title, Name, HP_Text |
| `Slider` | 挂有 Slider 组件的 GameObject | HP_Bar, MP_Bar |
| `Transform` | 任意带 RectTransform 的 GameObject | ItemContainer |
| `GameObject` | 任意 GameObject | dialog_LeftCard |
| `LineSelectDialog` 等 | 挂有对应脚本组件的 GameObject | Canvas/LineSelectDialog |

---

## 五、脚本文件清单

| 文件 | 挂载目标 | 字段数 | 主要数组 |
|------|---------|--------|---------|
| `PlayerHUD.cs` | Canvas/HUD | 4 | - |
| `PassiveUI.cs` | Canvas/PassivePanel | 11 | slotButtons[15], slotIcons[15], slotLineNames[15], slotEffects[15], lockOverlays[15], unlockLabels[15], layerTitles[5], layerLockIcons[5], lineDialogOptions[5] |
| `LineSelectDialog.cs` | Canvas/LineSelectDialog | 3 | optionButtons[5] |
| `SkillTreeUI.cs` | Canvas/SkillTreePanel | 18 | nodeButtons[10], nodeIcons[10], nodeNames[10], nodeLevels[10], nodeCostBadges[10], nodeBranchMasks[10], nodeGlows[10], connectorLines[2] |
| `BranchChoiceDialog.cs` | Canvas/BranchChoiceDialog | 6 | lv2Info[2], lv3Info[2] |
| `CraftUI.cs` | Canvas/CraftPanel | 25 | - |
| `CraftConfirmDialog.cs` | Canvas/CraftConfirmDialog | 5 | - |
| `CraftMatListDialog.cs` | Canvas/CraftMatListDialog | 3 | - |
| `UIConstants.cs` | (无挂载，静态常量类) | - | - |

**总计**: 8 个可挂载脚本 + 1 个常量类, 约 75+ 个 SerializeField 字段需要逐个拖入 Hierarchy。
