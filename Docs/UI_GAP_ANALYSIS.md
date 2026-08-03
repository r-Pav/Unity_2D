# UI 配置 Gap 分析报告

> **分析日期**: 2026-07-13  
> **参照文档**: `Editor_Setup.md` §6.2–§6.5  
> **扫描场景**: `Assets/Scenes/SampleScene.scene` (团结引擎 .scene 格式)  
> **脚本目录**: `Assets/Scripts/UI/` (9 个 .cs 文件)  
> **原则**: 只分析不修改，不触碰任何项目文件

---

## 总体评估

| 面板 | 状态 | 严重度 |
|------|------|--------|
| HUD (6.1) | 存在，需确认绑定 | LOW |
| PassivePanel (6.2) | 存在，需字段绑定 | MEDIUM |
| SkillTreePanel (6.3) | 存在，**脚本未挂载** | HIGH |
| CraftPanel (6.4) | **完全缺失** | CRITICAL |
| ConfirmDialog (6.4) | **完全缺失** | CRITICAL |
| MaterialListDialog (6.4) | **完全缺失** | CRITICAL |

**汇总**: 1 个面板完全缺失 + 3 个弹窗完全缺失 + 1 个面板脚本未挂载 + 2 个现有面板字段绑定待验证。

---

## 6.1 HUD — 已有，确认绑定

**Editor_Setup 要求** (§6.1):

| 字段 | 目标 |
|------|------|
| PlayerHUD → hpBar | HP_Bar Slider |
| PlayerHUD → mpBar | MP_Bar Slider |
| PlayerHUD → hpText | HP_Text TMP |
| PlayerHUD → mpText | MP_Text TMP |

**场景现状**:
- `HUD` GameObject 存在于 Canvas 下，挂有 MonoBehaviour 脚本
- `HealthBar`、`ManaBar` GameObject 存在于场景中
- 场景中存在多个 `Slider` 组件和 `Text (TMP)` 组件

**Gap 项**:

| # | 差距描述 | 分类 | 优先级 | 引用 |
|---|---------|------|--------|------|
| HUD-1 | HUD 上 `hpBar`/`mpBar` 字段是否已绑定到对应 Slider，需在 Inspector 中确认 | 字段绑定 | MEDIUM | §6.1 |
| HUD-2 | HUD 上 `hpText`/`mpText` 字段是否已绑定到对应 TMP_Text，需在 Inspector 中确认 | 字段绑定 | MEDIUM | §6.1 |

---

## 6.2 被动装备面板 — PassivePanel

**Editor_Setup 要求** (§6.2):

```
Canvas
└── PassivePanel (挂 PassiveUI 脚本)
    ├── LayerRow_TI  → Slot_0/1/2 → Button
    ├── LayerRow_TII → Slot_0/1/2 → Button
    ├── LayerRow_TIII→ Slot_0/1/2 → Button
    ├── LayerRow_TIV → Slot_0/1/2 → Button
    └── LayerRow_TV  → Slot_0/1/2 → Button
LineSelectDialog (Canvas顶层) → 弹出式5线选择列表
```

**场景现状**:
- ✅ `PassivePanel` GameObject 存在，挂有脚本
- ✅ `LayerRow_TI`、`_TII`、`_TIII`、`_TIV`、`_TV` 全部存在，每个挂有相同脚本
- ✅ `LineSelectDialog` 存在于 Canvas 顶层
- ⚠️ 槽位命名不一致：场景中用 `Slot_1`、`Slot_1 (1)`、`Slot_1 (2)` 等，Editor_Setup 要求 `Slot_0`~`Slot_2`
- ⚠️ 15 个槽位的子元素（Icon/LineName/Effect/LockOverlay/UnlockLabel）是否存在需逐层检查
- ⚠️ `CloseBtn` 存在于 PassivePanel 内，但前一份分析文档中未提及

**PassiveUI.cs 字段绑定需求**（从源码反推）:

| 字段 | 数量 | 类型 |
|------|------|------|
| `passiveEquipManager` | 1 | 引用 Player 上的 PassiveEquipManager |
| `slotButtons` | 15 (5层×3槽) | Button[] |
| `slotIcons` | 15 | Image[] |
| `slotLineNames` | 15 | TMP_Text[] |
| `slotEffects` | 15 | TMP_Text[] |
| `lockOverlays` | 15 | Image[] |
| `unlockLabels` | 15 | TMP_Text[] |
| `layerTitles` | 5 | TMP_Text[] |
| `layerLockIcons` | 5 | Image[] |
| `lineSelectDialog` | 1 | LineSelectDialog 引用 |
| `lineDialogOptions` | 5 | Button[] |

**Gap 项**:

| # | 差距描述 | 分类 | 优先级 | 引用 |
|---|---------|------|--------|------|
| PAS-1 | `passiveEquipManager` 字段需拖入 Player 上的 PassiveEquipManager 组件 | 字段绑定 | HIGH | §6.2, PassiveUI.cs:16 |
| PAS-2 | `slotButtons[15]` 数组需逐个拖入 15 个槽位 Button（按 layer×3+slotIndex 顺序） | 字段绑定 | HIGH | §6.2, PassiveUI.cs:17 |
| PAS-3 | `slotIcons[15]` 数组需逐个拖入 15 个槽位内的 Image 子对象 | 字段绑定 | MEDIUM | §6.2, PassiveUI.cs:18 |
| PAS-4 | `slotLineNames[15]` 数组需逐个拖入 15 个槽位内的 TMP_Text | 字段绑定 | MEDIUM | §6.2, PassiveUI.cs:19 |
| PAS-5 | `slotEffects[15]` 数组需逐个拖入 15 个槽位内的效果文本 TMP_Text | 字段绑定 | MEDIUM | §6.2, PassiveUI.cs:20 |
| PAS-6 | `lockOverlays[15]` 数组需逐个拖入 15 个槽位内的锁定覆盖 Image | 字段绑定 | MEDIUM | §6.2, PassiveUI.cs:21 |
| PAS-7 | `unlockLabels[15]` 数组需逐个拖入 15 个槽位内的解锁等级 TMP_Text | 字段绑定 | LOW | §6.2, PassiveUI.cs:22 |
| PAS-8 | `layerTitles[5]` 数组需逐个拖入 5 个层级标题 TMP_Text | 字段绑定 | MEDIUM | §6.2, PassiveUI.cs:23 |
| PAS-9 | `layerLockIcons[5]` 数组需逐个拖入 5 个层级锁图标 Image | 字段绑定 | LOW | §6.2, PassiveUI.cs:24 |
| PAS-10 | `lineSelectDialog` 需拖入 LineSelectDialog GameObject 引用 | 字段绑定 | HIGH | §6.2, PassiveUI.cs:25 |
| PAS-11 | `lineDialogOptions[5]` 数组需拖入 LineSelectDialog 内的 5 个 Option Button | 字段绑定 | HIGH | §6.2, PassiveUI.cs:26 |
| PAS-12 | 建议统一槽位命名：`Slot_0`~`Slot_2` 替代当前的 `Slot_1`/`Slot_1 (1)`/`Slot_1 (2)` | 参数设置 | LOW | §6.2 结构树 |

---

## 6.3 技能树面板 — SkillTreePanel

**Editor_Setup 要求** (§6.3):

```
Canvas
├── SkillTreePanel
│   ├── Skill_Q_View
│   │   ├── Node_Lv1 → Button
│   │   ├── Node_Lv2_Left → Button
│   │   ├── Node_Lv2_Right → Button
│   │   ├── Node_Lv3_Left → Button
│   │   ├── Node_Lv3_Right → Button
│   │   └── Connector_Lines → Image
│   └── Skill_E_View (同上)
└── BranchChoiceDialog
    ├── LeftCard / RightCard
    └── ConfirmBtn → Button
```

**场景现状**:
- ✅ `SkillTreePanel` GameObject 存在
- ✅ `Skill_Q_View`、`Skill_E_View` 存在
- ✅ 所有 10 个节点存在：Node_Lv1(×2)、Node_Lv2_Left(×2)、Node_Lv2_Right(×2)、Node_Lv3_Left(×2)、Node_Lv3_Right(×2)
- ✅ `BranchChoiceDialog` 存在（Canvas 顶层），含 LeftCard、RightCard、ConfirmBtn、Card_Border、BranchName、Lv2_Info、Lv3_Info、Option_0~4
- ❌ **SkillTreePanel 上未挂载任何脚本！** — 这意味着 SkillTreeUI.cs 完全未连接

**SkillTreeUI.cs 字段绑定需求**（从源码反推）:

| 字段 | 数量 | 类型 |
|------|------|------|
| `skillManager` | 1 | 引用 Player 上的 SkillManager |
| `branchSystem` | 1 | 引用 BranchUpgradeSystem |
| `skillPointManager` | 1 | 引用 Player 上的 SkillPointManager |
| `skillPointLabel` | 1 | TMP_Text |
| `nodeButtons` | 10 | Button[] (2视图×5节点) |
| `nodeIcons` | 10 | Image[] |
| `nodeNames` | 10 | TMP_Text[] |
| `nodeLevels` | 10 | TMP_Text[] |
| `nodeCostBadges` | 10 | TMP_Text[] |
| `nodeBranchMasks` | 10 | Image[] |
| `nodeGlows` | 10 | Image[] |
| `connectorLines` | 2 | Image[] |
| `branchChoiceDialog` | 1 | BranchChoiceDialog 引用 |
| `dialog_LeftCard` | 1 | GameObject |
| `dialog_RightCard` | 1 | GameObject |
| `dialog_Lv2Info` | 2 | TMP_Text[] |
| `dialog_Lv3Info` | 2 | TMP_Text[] |
| `dialog_ConfirmBtn` | 1 | Button |
| `dialog_CloseBtn` | 1 | Button |

**Gap 项**:

| # | 差距描述 | 分类 | 优先级 | 引用 |
|---|---------|------|--------|------|
| SKL-1 | **SkillTreePanel 上未挂载 SkillTreeUI.cs 脚本** — 这是阻塞性缺失，面板完全不工作 | 挂载脚本 | **CRITICAL** | §6.3, SkillTreeUI.cs |
| SKL-2 | `skillManager` 字段需拖入 Player 上的 SkillManager 组件 | 字段绑定 | HIGH | §6.3, SkillTreeUI.cs:8 |
| SKL-3 | `branchSystem` 字段需拖入 BranchUpgradeSystem 引用 | 字段绑定 | HIGH | §6.3, SkillTreeUI.cs:9 |
| SKL-4 | `skillPointManager` 字段需拖入 Player 上的 SkillPointManager | 字段绑定 | HIGH | §6.3, SkillTreeUI.cs:10 |
| SKL-5 | `skillPointLabel` 需拖入技能点显示 TMP_Text | 字段绑定 | MEDIUM | §6.3, SkillTreeUI.cs:11 |
| SKL-6 | `nodeButtons[10]` 数组需逐个拖入 10 个节点 Button（按 skillView×nodeIndex 顺序） | 字段绑定 | HIGH | §6.3, SkillTreeUI.cs:12 |
| SKL-7 | `nodeIcons[10]` 数组需逐个拖入 10 个节点图标 Image | 字段绑定 | MEDIUM | §6.3, SkillTreeUI.cs:13 |
| SKL-8 | `nodeNames[10]` 数组需逐个拖入 10 个节点名称 TMP_Text | 字段绑定 | MEDIUM | §6.3, SkillTreeUI.cs:14 |
| SKL-9 | `nodeLevels[10]` 数组需逐个拖入 10 个节点等级 TMP_Text | 字段绑定 | MEDIUM | §6.3, SkillTreeUI.cs:15 |
| SKL-10 | `nodeCostBadges[10]` 数组需逐个拖入 10 个技能点消耗徽标 TMP_Text | 字段绑定 | LOW | §6.3, SkillTreeUI.cs:16 |
| SKL-11 | `nodeBranchMasks[10]` 数组需逐个拖入 10 个分支锁定遮罩 Image | 字段绑定 | LOW | §6.3, SkillTreeUI.cs:17 |
| SKL-12 | `nodeGlows[10]` 数组需逐个拖入 10 个状态光效 Image | 字段绑定 | LOW | §6.3, SkillTreeUI.cs:18 |
| SKL-13 | `connectorLines[2]` 数组需拖入 Q/E 的连接线 Image | 字段绑定 | LOW | §6.3, SkillTreeUI.cs:19 |
| SKL-14 | `branchChoiceDialog` 需拖入 BranchChoiceDialog GameObject 引用 | 字段绑定 | HIGH | §6.3, SkillTreeUI.cs:20 |
| SKL-15 | `dialog_LeftCard`/`dialog_RightCard` 需拖入弹窗内左右卡片 GameObject | 字段绑定 | MEDIUM | §6.3, SkillTreeUI.cs:21-22 |
| SKL-16 | `dialog_Lv2Info`/`dialog_Lv3Info` 需拖入弹窗内分支信息 TMP_Text | 字段绑定 | MEDIUM | §6.3, SkillTreeUI.cs:23-24 |
| SKL-17 | `dialog_ConfirmBtn`/`dialog_CloseBtn` 需拖入弹窗内确认/关闭按钮 | 字段绑定 | MEDIUM | §6.3, SkillTreeUI.cs:25-26 |

---

## 6.4 合成面板 — CraftPanel（完全缺失）

**Editor_Setup 要求** (§6.4):

```
Canvas
├── CraftPanel
│   ├── Slot_Left → Button, Image (材料槽1)
│   ├── Slot_Right → Button, Image (材料槽2)
│   ├── ResultPreview → Image, TMP_Text (产出预览)
│   └── CraftBtn → Button (合成)
└── ConfirmDialog (Canvas顶层)
    ├── Mat1_Text → TMP_Text
    ├── Mat2_Text → TMP_Text
    ├── Result_Text → TMP_Text
    ├── ConfirmBtn → Button
    └── CancelBtn → Button
```

**场景现状**:
- ❌ **CraftPanel** — 场景中完全不存在
- ❌ **ConfirmDialog**（合成二次确认）— 不存在
- ❌ **MaterialListDialog**（材料选择列表）— 不存在
- ⚠️ 场景中存在一个 `ConfirmBtn` 但它在 BranchChoiceDialog 下，不是合成确认用的

**三个脚本已存在但无 GameObject 可挂载**:
- `CraftUI.cs` — 需要挂到 CraftPanel 上
- `CraftConfirmDialog.cs` — 需要挂到 ConfirmDialog 上
- `CraftMatListDialog.cs` — 需要挂到 MaterialListDialog 上

### 6.4.1 新建 UI 对象 — CraftPanel

| # | 差距描述 | 分类 | 优先级 | 引用 |
|---|---------|------|--------|------|
| CRF-1 | **需新建 `CraftPanel` GameObject**（Canvas 子级），作为合成面板根容器 | 新建UI对象 | **CRITICAL** | §6.4 |
| CRF-2 | **CraftPanel 上需挂载 `CraftUI.cs` 脚本** | 挂载脚本 | **CRITICAL** | §6.4, CraftUI.cs |
| CRF-3 | 新建 `Slot_Left` Button（含子 Image + TMP_Text×2）— 材料槽1 | 新建UI对象 | **CRITICAL** | §6.4 |
| CRF-4 | 新建 `Slot_Right` Button（含子 Image + TMP_Text×2）— 材料槽2 | 新建UI对象 | **CRITICAL** | §6.4 |
| CRF-5 | 新建 `ResultPreview` 区域（含 PreviewIcon Image + PreviewName/Desc/Stats TMP_Text + PreviewPlaceholder TMP_Text） | 新建UI对象 | HIGH | §6.4 |
| CRF-6 | 新建 `CraftBtn` Button（合成执行按钮） | 新建UI对象 | HIGH | §6.4 |

### 6.4.2 新建 UI 对象 — ConfirmDialog

| # | 差距描述 | 分类 | 优先级 | 引用 |
|---|---------|------|--------|------|
| CRF-7 | **需新建 `ConfirmDialog` GameObject**（Canvas 顶层），挂 `CraftConfirmDialog.cs` | 新建UI对象+挂载脚本 | **CRITICAL** | §6.4, CraftConfirmDialog.cs |
| CRF-8 | 新建 `Mat1_Text` / `Mat2_Text` TMP_Text — 消耗材料提示 | 新建UI对象 | HIGH | §6.4 |
| CRF-9 | 新建 `Result_Text` TMP_Text — 产出提示 | 新建UI对象 | HIGH | §6.4 |
| CRF-10 | 新建 `ConfirmBtn` Button — 确认合成 | 新建UI对象 | HIGH | §6.4 |
| CRF-11 | 新建 `CancelBtn` Button — 取消 | 新建UI对象 | HIGH | §6.4 |

### 6.4.3 新建 UI 对象 — MaterialListDialog

| # | 差距描述 | 分类 | 优先级 | 引用 |
|---|---------|------|--------|------|
| CRF-12 | **需新建 `MaterialListDialog` GameObject**（Canvas 顶层），挂 `CraftMatListDialog.cs` | 新建UI对象+挂载脚本 | HIGH | §6.4 结构树, CraftMatListDialog.cs |
| CRF-13 | 新建 `ItemContainer`（带 VerticalLayoutGroup 的容器） | 新建UI对象 | MEDIUM | §6.4 |
| CRF-14 | 新建 `MatListItem` Button 模板（含 Icon Image + NameText + LevelBadge + TypeTag TMP_Text） | 新建UI对象 | MEDIUM | §6.4 |

### 6.4.4 字段绑定 — CraftUI

| # | 差距描述 | 分类 | 优先级 | 引用 |
|---|---------|------|--------|------|
| CRF-15 | `craftSystem` 需拖入 Player 上的 CombinationCraftSystem | 字段绑定 | **CRITICAL** | CraftUI.cs:8 |
| CRF-16 | `slotLeft`/`slotRight` 需拖入两个材料槽 Button | 字段绑定 | HIGH | CraftUI.cs:9-10 |
| CRF-17 | `slotLeftIcon`/`slotRightIcon` 需拖入两个材料图标 Image | 字段绑定 | MEDIUM | CraftUI.cs:11-12 |
| CRF-18 | `slotLeftName`/`slotRightName` 需拖入两个材料名称 TMP_Text | 字段绑定 | MEDIUM | CraftUI.cs:13-14 |
| CRF-19 | `slotLeftLevel`/`slotRightLevel` 需拖入两个材料等级 TMP_Text | 字段绑定 | MEDIUM | CraftUI.cs:15-16 |
| CRF-20 | `slotLeftPlaceholder`/`slotRightPlaceholder` 需拖入两个空槽占位 TMP_Text | 字段绑定 | LOW | CraftUI.cs:17-18 |
| CRF-21 | `levelIndicator` 需拖入等级判定 TMP_Text | 字段绑定 | LOW | CraftUI.cs:19 |
| CRF-22 | `previewIcon`/`previewName`/`previewDesc`/`previewStats`/`previewPlaceholder` 需拖入预览区域元素 | 字段绑定 | MEDIUM | CraftUI.cs:20-24 |
| CRF-23 | `craftBtn` 需拖入合成按钮 | 字段绑定 | HIGH | CraftUI.cs:25 |
| CRF-24 | `confirmDialog` 需拖入 ConfirmDialog GameObject 引用 | 字段绑定 | HIGH | CraftUI.cs:26 |
| CRF-25 | `confirm_Mat1Text`/`confirm_Mat2Text`/`confirm_ResultText` 需拖入确认弹窗文本 | 字段绑定 | MEDIUM | CraftUI.cs:27-29 |
| CRF-26 | `confirm_ConfirmBtn`/`confirm_CancelBtn` 需拖入确认弹窗按钮 | 字段绑定 | MEDIUM | CraftUI.cs:30-31 |
| CRF-27 | `matListDialog` 需拖入 MaterialListDialog 引用 | 字段绑定 | HIGH | CraftUI.cs:32 |
| CRF-28 | `matListItemPrefab` 需拖入 Button 预制体模板 | 字段绑定 | MEDIUM | CraftUI.cs:33 |
| CRF-29 | `matListContainer` 需拖入列表容器 Transform | 字段绑定 | MEDIUM | CraftUI.cs:34 |

---

## 6.5 全局配色

**Editor_Setup 要求** (§6.5):

| 应用位置 | 颜色 | 十六进制 |
|---------|------|---------|
| 被动技能图标边框 | 灰色 | #999999 |
| 主动技能图标边框 | 金色 | #FFD700 |
| 武器技能图标边框 | 蓝色 | #4488FF |
| 组合技能图标边框 | 紫色 | #AA66FF |
| 已解锁/已装备 | 金色 | #FFD700 |
| 可选 | 白色 | #FFFFFF |
| 锁定/不可用 | 灰色 | #666666 |
| 冲突 | 红色 | #FF4444 |

**场景现状**:
- ✅ `UIConstants.cs` 已定义全部 6 个颜色常量（PassiveIconBorder/ActiveIconGold/WeaponIconBlue/ComboIconPurple/LockedGray/ConflictRed）
- ⚠️ 颜色常量存在但需在 UI 脚本中实际引用才能生效

**Gap 项**:

| # | 差距描述 | 分类 | 优先级 | 引用 |
|---|---------|------|--------|------|
| CLR-1 | 确认各面板的图标边框颜色是否在 Inspector 中引用 UIConstants 的对应颜色 | 参数设置 | LOW | §6.5, UIConstants.cs |

---

## 优先级汇总

### CRITICAL（阻塞性 — 面板完全不工作）

| 编号 | 描述 |
|------|------|
| SKL-1 | SkillTreePanel 挂载 SkillTreeUI.cs |
| CRF-1 | 新建 CraftPanel GameObject |
| CRF-2 | CraftPanel 挂载 CraftUI.cs |
| CRF-3 | 新建 Slot_Left Button |
| CRF-4 | 新建 Slot_Right Button |
| CRF-7 | 新建 ConfirmDialog + 挂载 CraftConfirmDialog.cs |
| CRF-15 | craftSystem 字段绑定 |

### HIGH（核心功能不可用）

| 编号 | 描述 |
|------|------|
| PAS-1 | passiveEquipManager 字段绑定 |
| PAS-2 | slotButtons[15] 字段绑定 |
| PAS-10 | lineSelectDialog 引用绑定 |
| PAS-11 | lineDialogOptions[5] 绑定 |
| SKL-2 | skillManager 字段绑定 |
| SKL-3 | branchSystem 字段绑定 |
| SKL-4 | skillPointManager 字段绑定 |
| SKL-6 | nodeButtons[10] 字段绑定 |
| SKL-14 | branchChoiceDialog 引用绑定 |
| CRF-5 | 新建 ResultPreview 区域 |
| CRF-6 | 新建 CraftBtn Button |
| CRF-8~11 | ConfirmDialog 子元素 |
| CRF-16/23/24/27 | CraftUI 核心字段绑定 |

### MEDIUM（功能降级但可部分工作）

| 编号 | 描述 |
|------|------|
| HUD-1/2 | HUD 字段绑定确认 |
| PAS-3~6/8 | PassiveUI 视觉元素绑定 |
| SKL-5/7/8/9 | SkillTreeUI 视觉元素绑定 |
| SKL-15~17 | SkillTreeUI 弹窗字段绑定 |
| CRF-12~14 | MaterialListDialog 新建 |
| CRF-17~19/22/25/26/28/29 | CraftUI 次要字段绑定 |

### LOW（润色/命名规范）

| 编号 | 描述 |
|------|------|
| PAS-7/9/12 | PassivePanel 命名标准化 + 锁图标 |
| SKL-10~13 | SkillTreeUI 装饰元素绑定 |
| CRF-20/21 | CraftUI 占位符绑定 |
| CLR-1 | 配色确认 |

---

## 实施建议顺序

按 Editor_Setup.md §总结 的推荐顺序，结合本分析：

1. **先补 CRITICAL** — CraftPanel 全家桶（新建+挂脚本+绑定核心字段）
2. **修复 SkillTreePanel** — 挂载 SkillTreeUI.cs + 绑定核心字段
3. **完善 PassivePanel** — 绑定 PassiveUI 的 15 槽位数组
4. **确认 HUD** — 验证已有的 hpBar/mpBar 绑定
5. **配色检查** — 复查各面板颜色引用

---

## 分析范围说明

- 仅检查 `Editor_Setup.md` §6.2~§6.5 明确列出的 UI 面板
- 场景扫描基于 `Assets/Scenes/SampleScene.scene`（项目当前唯一场景）
- 脚本分析基于 `Assets/Scripts/UI/` 下 9 个 .cs 文件
- 未对项目文件做任何修改
- Player GameObject 组件绑定未逐 GUID 解析（.meta 文件 GUID 映射在扫描中未成功解析），建议在 Unity Editor 中配合 Inspector 逐项确认
