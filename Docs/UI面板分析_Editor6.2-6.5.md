# UI 面板搭建需求分析 — Editor_Setup.md §6.2–§6.5

> **分析目的**：为后续 Unity 编辑器内 UI 面板搭建提供明确的策划分析基础。
> **参考文档**：Editor_Setup.md · 策划案_P2_被动.txt · 策划案_P3_主动.txt · 策划案_P5_组合.txt
> **源码参考**：PassiveEquipManager.cs · CombinationCraftSystem.cs · BranchUpgradeSystem.cs · SkillManager.cs · SkillPointManager.cs · PlayerHUD.cs · SkillData.cs · ActiveSkillData.cs · PassiveSkillData.cs · CombinationSkillData.cs

---

## 6.2 被动装备面板 — 5×3 网格

### 6.2.1 需创建的 UI 元素类型

| 元素 | 类型 | 数量 | 说明 |
|------|------|------|------|
| PassivePanel | `Panel` (挂 `PassiveUI.cs` 脚本) | 1 | 面板根容器，承载 5 行布局 |
| LayerRow_TI ~ TV | `GameObject` (挂 `HorizontalLayoutGroup`) | 5 | 每行一个标题标签 + 3 个装备槽 |
| Layer_Title | `TMP_Text` (LayerRow 子节点) | 5 | 层级标题文字，如 "TI [Lv1]" |
| Slot_X | `Button` | 15 (5×3) | 装备槽位按钮，按 (layer, slotIndex) 寻址 |
| Slot_Icon | `Image` (Button 子节点) | 15 | 已装备被动技能的图标 |
| Slot_LineName | `TMP_Text` (Button 子节点) | 15 | 线名称文本（如 "HP恢复"） |
| Slot_EffectPreview | `TMP_Text` (Button 子节点) | 15 | 数值效果摘要（如 "+1%"） |
| LockOverlay | `Image` (Slot 锁定覆盖) | 15 | 锁定态时显示的锁图标 + 遮罩 |
| UnlockLevelLabel | `TMP_Text` (Slot 内) | 15 | 锁定态显示解锁等级（如 "Lv5解锁"） |
| LineSelectDialog | `Panel` (Canvas 顶层) | 1 | 5 线选择弹窗，挂 `LineSelectDialog.cs` |

### 6.2.2 完整 UI 结构树

```
Canvas (Screen Space - Overlay)
├── PassivePanel (RectTransform 填充全屏或指定区域, 挂 PassiveUI.cs)
│   ├── Header_Text → TMP_Text ("被动装备")
│   ├── Info_Text → TMP_Text (当前层级/状态信息)
│   ├── LayerRow_TI (layerIndex=0, HorizontalLayoutGroup)
│   │   ├── Label_Area
│   │   │   ├── Layer_Title → TMP_Text ("TI [Lv1]")
│   │   │   └── Layer_LockIcon → Image (锁定态显示)
│   │   ├── Slot_0 → Button (layerIndex=0, slotIndex=0)
│   │   │   ├── Slot_Icon → Image (raycastTarget=false)
│   │   │   ├── Slot_LineName → TMP_Text
│   │   │   ├── Slot_EffectPreview → TMP_Text
│   │   │   ├── LockOverlay → Image (默认隐藏)
│   │   │   └── UnlockLevelLabel → TMP_Text (如 "Lv5解锁")
│   │   ├── Slot_1 → Button (同上结构)
│   │   └── Slot_2 → Button (同上结构)
│   ├── LayerRow_TII (layerIndex=1, 条件: Lv5 解锁)
│   │   └── ... (同上 1 标题 + 3 槽)
│   ├── LayerRow_TIII (layerIndex=2, 需 Lv8) → ...
│   ├── LayerRow_TIV (layerIndex=3, 需 Lv12) → ...
│   └── LayerRow_TV (layerIndex=4, 需 Lv16) → ...
│
└── LineSelectDialog (Canvas 顶层, 初始 SetActive(false))
    ├── BG_Mask → Image (半透明, 点击关闭)
    ├── Title → TMP_Text ("选择 {layer_name} 要装备的线")
    ├── Option_0 → Button ("HP恢复")
    ├── Option_1 → Button ("伤害+攻速")
    ├── Option_2 → Button ("移速+闪避")
    ├── Option_3 → Button ("减伤+控制")
    ├── Option_4 → Button ("法力+CD")
    └── CloseBtn → Button (关闭)
```

### 6.2.3 字段绑定映射

**PassiveUI.cs 绑在 PassivePanel 上**。Editor 中需完成以下绑定：

| PassiveUI 字段（SerializedField） | Inspector 拖入对象 | 说明 |
|----------------------------------|-------------------|------|
| `passiveEquipManager` | Player 上的 `PassiveEquipManager` 组件引用 | 数据源，运行时必不为 null |
| `slotButtons[layer][slotIndex]` | 15 个 `Button` 数组 [5][3] | 每个槽位的 Button 引用 |
| `slotIcons[layer][slotIndex]` | 15 个 `Image` 数组 [5][3] | 槽位图标 Image |
| `slotLineNames[layer][slotIndex]` | 15 个 `TMP_Text` 数组 [5][3] | 线名称 |
| `slotEffects[layer][slotIndex]` | 15 个 `TMP_Text` 数组 [5][3] | 效果摘要 |
| `lockOverlays[layer][slotIndex]` | 15 个 `Image` 数组 [5][3] | 锁定覆盖层 |
| `unlockLabels[layer][slotIndex]` | 15 个 `TMP_Text` 数组 [5][3] | 锁定文字 |
| `layerTitles[layer]` | 5 个 `TMP_Text` 数组 | 层级标题文字 |
| `layerLockIcons[layer]` | 5 个 `Image` 数组 | 层级锁图标 |
| `lineSelectDialog` | `LineSelectDialog` Panel | 弹窗引用 |
| `lineDialogOptions[5]` | 5 个 `Button` 数组 | 弹窗内 5 选项按钮 |

**被动数据与 UI 的绑定逻辑**（PassiveUI 代码负责，Editor 不需配）：

| UI 组件 | 数据源 | 绑定规则 |
|---------|--------|---------|
| `Slot_Icon.sprite` | `PassiveEquipManager.allPassiveData[layer*5+lineId].icon` | 从 dataIndex 查表获取 PassiveSkillData → 取出 icon |
| `Slot_LineName.text` | `LineNames[lineId]` 字典 (0=HP恢复, 1=伤害+攻速...) | 运行时从此 PassiveEquipManager 的内部字典映射 |
| `Slot_EffectPreview.text` | `PassiveSkillData.effects` 摘要拼接 | 取第 1 个 effect 格式化显示 |
| `Layer_Title.text` | `"T{label} [Lv{n}]"` 模板 | n = unlockLevels[layer] |
| `Layer_Title.color` | 锁定 → `#666666`，解锁 → `#FFD700` | 见 §6.5 配色表 |
| `Slot 交互状态` | `IsLayerUnlocked(layer)` | 未解锁 → 禁止点击 + 锁覆盖显示 |
| `Slot 战斗态` | `passiveEquipManager.InCombat` | true → 全部灰显不可交互 |
| `Slot 已装备` | `GetEquippedLineId(layer, slot) >= 0` | 高亮显示图标和文字 |
| `LineSelectDialog 选项` | `LineNames` 字典 | 5 个选项对应 5 条线 |
| `LineSelectDialog 灰色态` | `IsLineEquippedInLayer(layer, lineId)` | 同层已选的线灰显不可再选 |

**事件同步**：

| 事件 | 来源 | PassiveUI 响应 |
|------|------|---------------|
| `PassiveSlotsChangedEvent` | PassiveEquipManager.OnEquip/Unequip | 刷新全部槽位显示状态 |
| `PlayerLevelChangedEvent` | 外部升级系统 | 重新评估各层解锁状态 |
| `PlayerCombatStateChangedEvent` | 战斗系统 | 切换面板锁定 |

---

## 6.3 技能树面板

### 6.3.1 需创建的 UI 元素类型

| 元素 | 类型 | 数量 | 说明 |
|------|------|------|------|
| SkillTreePanel | `Panel` | 1 | 技能树面板根，挂 `SkillTreeUI.cs` |
| Skill_Q_View | `GameObject` | 1 | Q 技能树视图容器（能量球线） |
| Skill_E_View | `GameObject` | 1 | E 技能树视图容器（冲进步线） |
| Node_Lv1 | `Button` | 2 (Q/E 各 1) | Lv1 基础节点（顶部） |
| Node_Lv2_Left | `Button` | 2 | Lv2 左分支节点 |
| Node_Lv2_Right | `Button` | 2 | Lv2 右分支节点 |
| Node_Lv3_Left | `Button` | 2 | Lv3 左分支节点 |
| Node_Lv3_Right | `Button` | 2 | Lv3 右分支节点 |
| Node_Icon | `Image` (Button 子节点) | 10 | 技能节点图标 |
| Node_Name | `TMP_Text` (Button 子节点) | 10 | 节点名称 |
| Node_Level | `TMP_Text` (Button 子节点) | 10 | 当前等级标识（"Lv2"） |
| Node_CostBadge | `TMP_Text` (Button 子节点) | 10 | 升级消耗点数徽标（"1 SP"） |
| Node_BranchMask | `Image` (Button 子节点) | 4 | 分支锁定斜线覆盖（Lv2_L/R, Lv3_L/R） |
| Node_StatusGlow | `Image` (Button 子节点) | 10 | 可选状态的呼吸光效 |
| Connector_Lines | `Image` | 2 (Q/E 各 1) | 树状连接线（从 Lv1 到 Lv2 到 Lv3 的路径线段，可用 Image 线段拼或 `UILineRenderer`） |
| SkillPointLabel | `TMP_Text` | 1 | 面板顶部显示可用技能点 |
| BranchChoiceDialog | `Panel` (Canvas 顶层) | 1 | 分支选择弹窗，挂 `BranchChoiceDialog.cs` |
| LeftCard / RightCard | `Panel` (Dialog 内) | 2 | 左右分支预览卡片（整体可点击） |
| Card_Lv2_Info | `TMP_Text` (Card 内) | 2 | Lv2 分支描述和参数 |
| Card_Lv3_Info | `TMP_Text` (Card 内) | 2 | Lv3 后续路线预览描述 |
| Dialog_ConfirmBtn | `Button` | 1 | 确认选择按钮 |
| Dialog_CloseBtn | `Button` | 1 | 取消（保留 Lv1） |
| Dialog_Warning | `TMP_Text` | 1 | "注意：此选择不可更改" |

### 6.3.2 完整 UI 结构树

```
Canvas
├── SkillTreePanel (挂 SkillTreeUI.cs)
│   ├── Header_Text → TMP_Text ("技能树")
│   ├── SkillPointLabel → TMP_Text ("技能点: {n}")
│   ├── Tab_Q → Button (切换到 Q 技能树) [可选，若 Q/E 同屏显示则不需要]
│   ├── Tab_E → Button (切换到 E 技能树) [同上]
│   │
│   ├── Skill_Q_View
│   │   ├── Node_Lv1 → Button (data: "能量球", slotIndex=0)
│   │   │   ├── Node_Icon → Image (绑 ActiveSkillData.icon)
│   │   │   ├── Node_Name → TMP_Text ("能量球")
│   │   │   ├── Node_Level → TMP_Text ("Lv1")
│   │   │   ├── Node_CostBadge → TMP_Text (可选时显示 "1 SP")
│   │   │   └── Node_StatusGlow → Image (可选时呼吸光效)
│   │   │
│   │   ├── Node_Lv2_Left → Button (分部: "散射弹幕")
│   │   │   └── ... (同上子节点结构)
│   │   │       ├── Node_BranchMask → Image (分支锁定斜线覆盖)
│   │   │
│   │   ├── Node_Lv2_Right → Button (分部: "穿透狙击")
│   │   │   └── ...
│   │   │
│   │   ├── Node_Lv3_Left → Button (分部: "弹幕风暴")
│   │   │   └── ...
│   │   │
│   │   ├── Node_Lv3_Right → Button (分部: "毁灭射线")
│   │   │   └── ...
│   │   │
│   │   └── Connector_Lines → Image (树状连接线, 9条线段:
│   │        Lv1 → Lv2_Left, Lv1 → Lv2_Right,
│   │        Lv2_Left → Lv3_Left, Lv2_Right → Lv3_Right,
│   │        + 分支锁定斜线/灰化)
│   │
│   └── Skill_E_View
│       └── ... (同上 5 节点树结构, 数据不同)
│           ├── Node_Lv1 → "冲进步"
│           ├── Node_Lv2_Left → "突进斩"
│           ├── Node_Lv2_Right → "灵巧闪避"
│           ├── Node_Lv3_Left → "双闪连袭"
│           └── Node_Lv3_Right → "虚空步伐"
│
└── BranchChoiceDialog (Canvas 顶层, 初始 SetActive(false))
    ├── BG → Image (半透明遮罩)
    ├── Title → TMP_Text ("选择分支路线 (不可更改)")
    ├── Warning → TMP_Text ("注意：此选择不可更改")
    │
    ├── LeftCard → Panel (可点击)
    │   ├── Card_Border → Image
    │   ├── BranchName → TMP_Text ("散射弹幕路线")
    │   ├── Lv2_Info → TMP_Text ("伤害: 25×3, CD: 4s")
    │   └── Lv3_Info → TMP_Text ("后续→弹幕风暴: 伤害: 30×5, CD: 3.5s")
    │
    ├── RightCard → Panel (可点击)
    │   └── ... (同上结构, 数据不同)
    │
    ├── ConfirmBtn → Button (选择卡片后亮起 "确认选择")
    └── CloseBtn → Button ("取消")
```

### 6.3.3 字段绑定映射

**SkillTreeUI.cs 绑在 SkillTreePanel 上**。Editor 需完成：

| SkillTreeUI 字段 | Inspector 拖入对象 | 说明 |
|-----------------|-------------------|------|
| `skillManager` | Player 上的 `SkillManager` 组件引用 | 数据源 |
| `branchSystem` | (代码内通过 SkillManager 获取) | `SkillManager.BranchSystem` |
| `skillPointManager` | Player 上的 `SkillPointManager` | 技能点数查询 |
| `skillPointLabel` | 面板顶部的 `TMP_Text` | 显示 "技能点: {n}" |
| `nodeButtons[2][5]` | 10 个 `Button` 数组 [skillViewType][nodeIndex] | Q/E 各 5 节点 |
| `nodeIcons[2][5]` | 10 个 `Image` 数组 | 图标引用 |
| `nodeNames[2][5]` | 10 个 `TMP_Text` 数组 | 节点名称 |
| `nodeLevels[2][5]` | 10 个 `TMP_Text` 数组 | 等级文字 |
| `nodeCostBadges[2][5]` | 10 个 `TMP_Text` 数组 | 消耗点数徽标 |
| `nodeBranchMasks[2][5]` | 10 个 `Image` 数组 | 分支锁定斜线覆盖（Lv2/Lv3 节点用） |
| `nodeGlows[2][5]` | 10 个 `Image` 数组 | 呼吸光效（可选升级时显示） |
| `connectorLines[2]` | 2 个 `Image` (或线段容器) | Q/E 的连接线组 |
| `branchChoiceDialog` | `BranchChoiceDialog` Panel | 弹窗引用 |
| `dialog_LeftCard` | Panel | 左卡片 |
| `dialog_RightCard` | Panel | 右卡片 |
| `dialog_Lv2Info` / `dialog_Lv3Info` | TMP_Text × 4 | 分支预览信息 |
| `dialog_ConfirmBtn` | Button | 确认按钮 |
| `dialog_CloseBtn` | Button | 取消按钮 |

**节点索引约定**（SkillTreeUI 内部）：

| nodeIndex | 节点 | 对应 Q/E 数据 |
|-----------|------|-------------|
| 0 | Lv1 根节点 | `ActiveSkillData.lv1Data` |
| 1 | Lv2_Left | `lv2Left` |
| 2 | Lv2_Right | `lv2Right` |
| 3 | Lv3_Left | `lv3Left` |
| 4 | Lv3_Right | `lv3Right` |

**运行时绑定逻辑**：

| UI 组件 | 数据源 | 绑定规则 |
|---------|--------|---------|
| Node_Name.text | `ActiveBranchData.branchName` 或 `SkillData.skillName` | Lv1 用 skillName，分支用 branchName |
| Node_Icon.sprite | `ActiveSkillData.icon` | 继承自 SkillData.icon |
| Node_Level.text | `"Lv{skillManager.GetSkillLevel(slotIndex)}"` | 当前等级 |
| Node_CostBadge.text | `"{branchSystem.GetUpgradeCost(slotIndex)} SP"` | 仅可升级态显示 |
| skillPointLabel.text | `"技能点: {skillPointManager.CurrentSkillPoints}"` | 跟随事件刷新 |
| Node_StatusGlow 显隐 | `branchSystem.CanUpgrade(slotIndex)` | 可升级时显示呼吸光效 |
| Node_BranchMask 显隐 | `IsBranchLocked(node)` | 未选分支的另一侧显示斜线覆盖 |
| Connector_Lines 颜色/显隐 | 按节点锁定状态级联 | 已选路径亮色，锁定路径灰暗 |
| Node 点击 | `skillManager.LevelUp(slotIndex)` | 点击触发升级流程，Lv1→Lv2 自动弹出分支选择 |

**节点视觉状态表**（引用自 策划案_P3 §5.2，具体颜色见 §6.5）：

| 状态 | 条件 | UI 表现 |
|------|------|---------|
| 已获得 (当前等级 or 已学习) | 节点等级 ≤ `slotLevels[slotIdx]` | 金色 `#FFD700` 边框，图标完整，不显示CostBadge |
| 可选升级 | `CanUpgrade() && 节点等级 == slotLevels[slotIdx]+1` | 白边框 `#FFFFFF` + 呼吸光效 + CostBadge 显示消耗点 |
| 已锁定 (未选分支) | `branchSystem.IsBranchLocked(slotIdx, "Left"/"Right")` | 灰 `#666666` + 斜线覆盖，不可点击 |
| 未解锁 (等级不足) | 节点等级 > slotLevels[slotIdx]+1 | 暗灰 + 锁图标 + 显示解锁等级 |

**事件同步**：

| 事件 | 来源 | SkillTreeUI 响应 |
|------|------|-----------------|
| `SkillLevelChangedEvent` | BranchUpgradeSystem.ApplyLevelUp | 刷新全部节点状态 |
| `BranchChosenEvent` | BranchUpgradeSystem.OnBranchChosen | 关闭分支弹窗，刷新技能树 |
| `PlayerSkillPointsChangedEvent` | SkillPointManager | 刷新技能点标签和节点 CostBadge |

---

## 6.4 合成面板

### 6.4.1 需创建的 UI 元素类型

| 元素 | 类型 | 数量 | 说明 |
|------|------|------|------|
| CraftPanel | `Panel` | 1 | 合成面板根，挂 `CraftUI.cs` |
| Slot_Left | `Button` + `Image` + `TMP_Text×2` | 1 | 材料槽 1——点击弹出材料列表 |
| Slot_Right | `Button` + `Image` + `TMP_Text×2` | 1 | 材料槽 2 |
| Slot_Placeholder | `TMP_Text` (Slot 子节点) | 2 | 空槽时提示 "选择材料" |
| ConnectionArrow | `Image` | 1 | 两槽之间 "+" 箭头指示 |
| LevelIndicator | `TMP_Text` | 1 | "材料等级: Lv{n}" 取两材料较低者 |
| ResultPreview | `Panel` | 1 | 产出预览区域 |
| PreviewIcon | `Image` | 1 | 组合技能图标 |
| PreviewName | `TMP_Text` | 1 | 组合技能名称 |
| PreviewDesc | `TMP_Text` | 1 | 效果描述 |
| PreviewStats | `TMP_Text` | 1 | "CD: {n}s | MP: {n}" |
| PreviewPlaceholder | `TMP_Text` | 1 | "请选择两个材料" 未选时显示 |
| CraftBtn | `Button` | 1 | 合成执行按钮 |
| CraftBtn_Text | `TMP_Text` | 1 | 按钮文字 "合成" |
| ConfirmDialog | `Panel` (Canvas 顶层) | 1 | 二次确认弹窗 |
| Mat1_Text / Mat2_Text | `TMP_Text` | 2 | 消耗材料名称 + 等级 |
| Result_Text | `TMP_Text` | 1 | 产出技能名 |
| Warning_Text | `TMP_Text` | 1 | "合成后将永久消耗以下技能，是否继续？" |
| ConfirmBtn / CancelBtn | `Button` | 2 | 确认/取消 |
| MaterialListDialog | `Panel` (Canvas 顶层) | 1 | 材料选择列表弹窗 |
| MatListItem | `Button` 模板 | N | 每个可用材料一个列表项 |

### 6.4.2 完整 UI 结构树

```
Canvas
├── CraftPanel (挂 CraftUI.cs)
│   ├── Header → TMP_Text ("技能合成")
│   │
│   ├── MaterialSection (横向排列)
│   │   ├── Slot_Left → Button (raycastTarget 保持 true)
│   │   │   ├── Icon → Image (从 SkillData.icon)
│   │   │   ├── NameText → TMP_Text (技能名称)
│   │   │   ├── LevelBadge → TMP_Text ("Lv{n}" 或 "武器")
│   │   │   └── Placeholder → TMP_Text ("选择材料", 空时可见)
│   │   │
│   │   ├── ArrowIcon → Image ("+")
│   │   │
│   │   └── Slot_Right → Button (同上结构)
│   │       └── ...
│   │
│   ├── LevelIndicator → TMP_Text ("材料等级: Lv{n}")
│   │
│   ├── ResultPreview → Panel
│   │   ├── PreviewIcon → Image (绑 result.icon)
│   │   ├── PreviewName → TMP_Text (绑 result.skillName)
│   │   ├── PreviewDesc → TMP_Text (绑 result.description)
│   │   ├── PreviewStats → TMP_Text ("CD: {n}s | MP: {n}")
│   │   └── PreviewPlaceholder → TMP_Text ("请选择两个材料", 合法配方时隐藏)
│   │
│   └── CraftBtn → Button (interactable 由配方校验控制)
│       └── CraftBtn_Text → TMP_Text ("合成")
│
├── ConfirmDialog (Canvas 顶层, 初始 SetActive(false))
│   ├── BG → Image (遮罩, 点击关闭)
│   ├── Title → TMP_Text ("确认合成")
│   ├── Warning → TMP_Text ("合成后将永久消耗以下技能，是否继续？")
│   ├── Mat1_Text → TMP_Text ("[技能1] Lv{n}")
│   ├── Mat2_Text → TMP_Text ("[技能2] Lv{n}")
│   ├── Result_Text → TMP_Text ("产出: [组合技能名称]")
│   ├── ConfirmBtn → Button -> CombinationCraftSystem.Craft()
│   └── CancelBtn → Button (关闭)
│
└── MaterialListDialog (Canvas 顶层, 初始 SetActive(false))
    ├── BG → Image (遮罩, 点击关闭)
    ├── Title → TMP_Text ("选择合成材料")
    ├── ItemContainer → (VerticalLayoutGroup / GridLayout)
    │   ├── MatListItem_0 → Button (模板)
    │   │   ├── Icon → Image
    │   │   ├── NameText → TMP_Text
    │   │   ├── LevelBadge → TMP_Text
    │   │   └── TypeTag → TMP_Text ("主动" / "武器")
    │   ├── MatListItem_1 → Button
    │   └── ...
    └── CloseBtn → Button
```

### 6.4.3 字段绑定映射

**CraftUI.cs 绑在 CraftPanel 上**。Editor 需完成：

| CraftUI 字段 | Inspector 拖入对象 | 说明 |
|-------------|-------------------|------|
| `craftSystem` | Player 上的 `CombinationCraftSystem` | 数据源和执行入口 |
| `slotLeft` / `slotRight` | 两个材料槽 `Button` | 点击弹出材料列表 |
| `slotLeftIcon` / `slotRightIcon` | 2 个 `Image` | 材料图标 |
| `slotLeftName` / `slotRightName` | 2 个 `TMP_Text` | 材料名称 |
| `slotLeftLevel` / `slotRightLevel` | 2 个 `TMP_Text` | 材料等级徽标 |
| `slotLeftPlaceholder` / `slotRightPlaceholder` | 2 个 `TMP_Text` | 空槽占位符 |
| `levelIndicator` | `TMP_Text` | 等级判定文字 |
| `previewIcon` | `Image` | 结果预览图标 |
| `previewName` | `TMP_Text` | 结果预览名称 |
| `previewDesc` | `TMP_Text` | 结果预览描述 |
| `previewStats` | `TMP_Text` | 结果预览数值（CD/MP） |
| `previewPlaceholder` | `TMP_Text` | 未选时占位符 |
| `craftBtn` | `Button` | 合成按钮 |
| `confirmDialog` | `ConfirmDialog` Panel | 二次确认弹窗 |
| `confirm_Mat1Text` / `confirm_Mat2Text` | 2 个 `TMP_Text` | 消耗材料提示 |
| `confirm_ResultText` | `TMP_Text` | 产出提示 |
| `confirm_ConfirmBtn` | `Button` | 确认合成按钮 |
| `confirm_CancelBtn` | `Button` | 取消按钮 |
| `matListDialog` | `MaterialListDialog` Panel | 材料列表弹窗 |
| `matListItemPrefab` | `Button` 预制体模板 | 列表项模板 (供 Instantiate) |
| `matListContainer` | `Transform` / `Content` | 列表项容器 (供 Instantiate 挂载) |

**运行时绑定逻辑**：

| UI 组件 | 数据源 | 绑定规则 |
|---------|--------|---------|
| Slot_Left.Icon | `selectedMaterials[0].skillData.icon` | 选中后更新，空时隐藏 |
| Slot_Left.NameText | `selectedMaterials[0].skillName` | 选中后显示技能名，空时隐藏 Placeholder |
| Slot_Left.LevelBadge | `"Lv{n}"` (武器技能: "武器") | 选中后更新 |
| Slot_Right.* | `selectedMaterials[1]` 同上 | 对称 |
| LevelIndicator.text | `"材料等级: Lv{Mathf.Min(m1.level, m2.level)}"` | 两者都选后更新 |
| PreviewIcon.sprite | `result.icon` (CombinationSkillData) | ValidateRecipe 通过后设置 |
| PreviewName.text | `result.skillName` | 同上 |
| PreviewDesc.text | `result.description` | 同上 |
| PreviewStats.text | `"CD: {result.cooldown}s | MP: {result.manaCost}"` | 同上 |
| PreviewPlaceholder 显隐 | 合法配方时隐藏 | 非法/未选时显示 |
| CraftBtn.interactable | `ValidateRecipe(m1, m2)` | 合法时才可点击 |
| CraftBtn.onClick | → 打开 ConfirmDialog 填充数据 | 二次确认后才 Craft |
| ConfirmDialog.ConfirmBtn.onClick | `craftSystem.Craft(m1, m2)` | 消耗 + 产出 |
| MaterialListDialog 填充 | `craftSystem.GetAvailableMaterials()` | 每次打开时刷新 |

---

## 6.5 全局配色方案

### 6.5.1 颜色常量表

所有面板共享同一套语义配色。建议定义 `UIConstants.cs` 静态类或 `ColorPreset` SO 统一管理。

| 语义名称 | Hex | RGB | 应用场景 |
|---------|-----|-----|---------|
| PassiveIconBorder | `#999999` | (153,153,153) | 被动技能图标边框、被动面板装饰线 |
| ActiveIconGold | `#FFD700` | (255,215,0) | 主动技能节点已获得态、被动已装备高亮、合成主动材料标记 |
| WeaponIconBlue | `#4488FF` | (68,136,255) | 合成材料列表中武器技能边框/标签 |
| ComboIconPurple | `#AA66FF` | (170,102,255) | 合成结果预览卡片边框、组合技能槽 |
| TextWhite | `#FFFFFF` | (255,255,255) | 可交互按钮文字、可选状态文字 |
| LockedGray | `#666666` | (102,102,102) | 锁定层/锁定分支/战斗态/不可交互按钮 |
| ConflictRed | `#FF4444` | (255,68,68) | 非法配方提示、错误状态文字 |
| BgDark | `#222222` | (34,34,34) | 面板背景色（建议） |

### 6.5.2 面板级颜色绑定映射

**被动面板颜色绑定**：

| UI 元素 | 状态条件 | 颜色值 |
|---------|---------|--------|
| Slot 图标边框 | 已装备 (lineId >= 0) | `#FFD700` (金色) |
| Slot 图标边框 | 未装备且层解锁 | `#FFFFFF` (白色, 半透明) |
| Slot 图标边框 | 层未解锁 | `#666666` (灰色) |
| Slot 图标 | 被动类型 | 图标本身自带边框色 `#999999` |
| Slot 全区域 | 战斗态 | 全部 `#666666` + α 半透明遮罩 |
| Layer_Title | 已解锁 | `#FFD700` |
| Layer_Title | 未解锁 | `#666666` |

**技能树面板颜色绑定**：

| UI 元素 | 状态条件 | 颜色值 |
|---------|---------|--------|
| Node_Icon 边框 | 已获得 (当前等级) | `#FFD700` |
| Node_Icon 边框 | 可选升级 | `#FFFFFF` + 呼吸光效动画 |
| Node_Icon 边框 | 锁定 (未选分支) | `#666666` + 斜线覆盖 |
| Node_Icon 边框 | 未解锁 (等级不足) | `#666666` + 锁图标 |
| Node_Name 文字 | 对应状态 | 跟随 Icon 边框色 |
| Connector_Lines | 已激活路径 | `#FFD700` (金色) |
| Connector_Lines | 锁定/未选分支路径 | `#666666` 或隐藏 |
| CostBadge 背景 | 可升级 | `#444444` + 文字 `#FFFFFF` |

**合成面板颜色绑定**：

| UI 元素 | 状态条件 | 颜色值 |
|---------|---------|--------|
| Slot_Left/Right Icon 边框 | 主动技能材料 | `#FFD700` |
| Slot_Left/Right Icon 边框 | 武器技能材料 | `#4488FF` |
| ResultPreview Icon 边框 | 组合技能预览 | `#AA66FF` |
| CraftBtn 按钮 | interactable = true | 正常色 |
| CraftBtn 按钮 | interactable = false | 灰 `#666666` |
| LevelIndicator | 正常 | `#FFFFFF` |
| 非法/冲突提示文字 | 配方非法/同技能自合成 | `#FF4444` |

### 6.5.3 颜色管理建议

1. **创建 `UIConstants.cs` 静态颜色常量类**，所有 UI 脚本统一引用，避免硬编码 Hex
2. **创建 `UIPalette` ScriptableObject**（可选），允许策划在项目设置中调色，无需改代码
3. **定义枚举 `ColorSemantic`**（如 `UnlockedGold`、`LockedGray`、`ConflictRed`），按语义使用颜色

```csharp
// 建议的 UIConstants.cs 结构
public static class UIConstants
{
    // Panel colors
    public static readonly Color PassiveIconBorder = new Color(0.6f, 0.6f, 0.6f);
    public static readonly Color ActiveIconGold   = new Color(1.0f, 0.84f, 0.0f);
    public static readonly Color WeaponIconBlue   = new Color(0.27f, 0.53f, 1.0f);
    public static readonly Color ComboIconPurple  = new Color(0.67f, 0.4f, 1.0f);
    public static readonly Color LockedGray       = new Color(0.4f, 0.4f, 0.4f);
    public static readonly Color ConflictRed      = new Color(1.0f, 0.27f, 0.27f);
}
```

---

## 附录 A：UI 脚本职责与挂载表

| 脚本 | 挂载对象 | 职责 | Editor 需配置的 Inspector 字段 |
|------|---------|------|-------------------------------|
| `PassiveUI.cs` | PassivePanel | 5×3 槽位刷新、点击交互、弹窗管理 | passiveEquipManager / 所有 UI 组件引用（详见 6.2.3） |
| `LineSelectDialog.cs` | LineSelectDialog | 5 线选择弹窗回调 | 5 个选项 Button / CloseBtn / Title |
| `SkillTreeUI.cs` | SkillTreePanel | 节点状态管理、升级交互、分支弹窗 | skillManager / 节点 UI 引用 / dialog 引用（详见 6.3.3） |
| `BranchChoiceDialog.cs` | BranchChoiceDialog | 分支预览 + 确认回调 | LeftCard / RightCard / ConfirmBtn / CloseBtn |
| `CraftUI.cs` | CraftPanel | 材料选择 → 校验 → 预览 → 确认 | craftSystem / 槽位 UI 引用 / dialogs（详见 6.4.3） |
| `CraftConfirmDialog.cs` | ConfirmDialog | 二次确认显示与回调 | Mat1_Text / Mat2_Text / Result_Text / ConfirmBtn / CancelBtn |
| `CraftMatListDialog.cs` | MaterialListDialog | 材料列表弹窗显示与选择 | ItemContainer / ItemPrefab / CloseBtn |
| `PlayerHUD.cs` | HUD GameObject | 血条蓝条（已有，确认绑定） | hpBar / mpBar / hpText / mpText |

## 附录 B：UI 面板切换建议

Editor_Setup.md 未指定面板切换方式，但按项目惯例建议：

1. **面板可见性控制**：各面板初始隐藏，通过快捷键或 UI 按钮切换激活
2. **同时只能打开一个面板**（互斥）：打开 PassivePanel 时关闭 SkillTreePanel 和 CraftPanel
3. **战斗态全局锁定**：战斗中打开任意面板，交互层全部灰显
4. **建议绑定快捷键**：B=被动面板, K=技能树面板, C=合成面板

## 附录 C：参考数据流

```
[编辑器手动搭建]
├──§6.2 PassiveUI: 拖入15个Slot Button/Icon/Text → 代码通过数组索引访问
├──§6.3 SkillTreeUI: 拖入Q/E各5个Node Button → 代码通过slotIndex + nodeIndex定位
└──§6.4 CraftUI: 拖入材料槽/预览/弹窗 → 代码管理selectedMaterials[2]状态

[运行时数据流]
Player GameObject
├── PassiveEquipManager → GetLayoutData() → PassiveUI
│   ├── EquipPassive(layer, lineId, slotIndex) → Event → UI刷新
│   └── InCombat → 面板全局锁定
├── SkillManager.GetSlotData(slotIdx) → SkillTreeUI
│   └── BranchUpgradeSystem.TryUpgrade() → 升级流程
├── CombinationCraftSystem.ValidateRecipe() → CraftUI 预览
│   └── Craft() → 消耗 + 产出
└── SkillPointManager → skillPointLabel / CanUpgrade 判定
```
