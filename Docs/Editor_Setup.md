# Editor 操作手册 — 今日代码更新后

> 基于今日全部 skill 模块代码 + Assets/Resources/Skills/ 现有资产
> 只列还需要你手动做的部分，已完成的(25被动SO/5武器SO/3组合SO)不再重复

---

## 1. Player 组件检查 — 挂载确认

选中 Player GameObject，确认以下所有组件已挂载：

| 组件 | 脚本 | 状态 |
|------|------|------|
| PlayerController | Assets/Scripts/Player/PlayerController.cs | ✅ 应有 |
| PlayerCombat | Assets/Scripts/Player/PlayerCombat.cs | ✅ 应有 |
| PlayerGroundPound | Assets/Scripts/Player/PlayerGroundPound.cs | ✅ 应有 |
| PlayerStomp | Assets/Scripts/Player/PlayerStomp.cs | ✅ 应有 |
| PlayerAimLine | Assets/Scripts/Player/PlayerAimLine.cs | ✅ 应有 |
| PlayerHitFeedback | Assets/Scripts/Player/PlayerHitFeedback.cs | ✅ 应有 |
| SkillManager | Assets/Scripts/Skills/SkillManager.cs | ✅ 应有 |
| StatModifierManager | Assets/Scripts/Skills/StatModifierManager.cs | ✅ 应有 |
| SkillPointManager | Assets/Scripts/Skills/SkillPointManager.cs | ✅ 应有 |
| PassiveEquipManager | Assets/Scripts/Skills/PassiveEquipManager.cs | ✅ 应有 |
| **WeaponSkillLink** | Assets/Scripts/Skills/WeaponSkillLink.cs | ⚠️ **确认已挂** |
| **CombinationCraftSystem** | Assets/Scripts/Skills/CombinationCraftSystem.cs | ⚠️ **确认已挂** |

> PlayerJump / PlayerDash / PlayerHealth 由 PlayerController.Awake() 自动创建，不用手动挂

---

## 2. SkillManager Inspector 字段配置

| 字段路径 | 操作 |
|---------|------|
| SkillManager → skillSlots[0] | **拖入** `Skill_Active_Q.asset`（先做第3步重建Q/E） |
| SkillManager → skillSlots[1] | **拖入** `Skill_Active_E.asset` |
| SkillManager → skillSlots[2~3] | 留空（供组合技能产出分配） |
| SkillManager → P3 分支升级 → pointCostToLv2 | 1 |
| SkillManager → P3 分支升级 → pointCostToLv3 | 2 |

---

## 3. 【必须】重建 Q/E ActiveSkillData SO 资产

之前 Q/E 的 .asset 文件因为 GUID 损坏已删除，需在编辑器中重建：

**操作：** 菜单栏 → `Tools → Create ActiveSkillData Assets (Q/E)`

验证 `Assets/Resources/Skills/Active/` 下生成 2 个文件：
- `Skill_Active_Q.asset`
- `Skill_Active_E.asset`

然后拖入第 2 步的 SkillManager 槽位。

---

## 4. PassiveEquipManager 数据填充

| 字段 | 操作 |
|------|------|
| PassiveEquipManager → **All Passive Data** 数组(25) | 把 `Assets/Resources/Skills/Passive/` 下 25 个 `.asset` 全部拖入，确保无空位 |
| PassiveEquipManager → **Unlock Levels** 数组 | [1, 5, 8, 12, 16] |

---

## 5. CombinationCraftSystem 配方配置

| 字段 | 拖入对象 |
|------|---------|
| recipeLv1 | `Assets/Resources/Skills/Combo/Skill_Combo_DualSynergy.asset` |
| recipeLv2 | `Assets/Resources/Skills/Combo/Skill_Combo_LawDomain.asset` |
| recipeLv3 | `Assets/Resources/Skills/Combo/Skill_Combo_FinalJudgment.asset` |

---

## 6. UI 面板搭建（这部分要你手动搭布局）

以下所有面板挂 Canvas 下，Panel 不用写代码（数据接口已暴露），只需拖 UI 布局：

### 6.1 HUD（已有，确认绑定）

| 字段 | 拖入 |
|------|------|
| PlayerHUD → hpBar | HP_Bar Slider |
| PlayerHUD → mpBar | MP_Bar Slider |
| PlayerHUD → hpText | HP_Text TMP |
| PlayerHUD → mpText | MP_Text TMP |

### 6.2 被动装备面板 — 5×3 网格

```
Canvas
└── PassivePanel (挂 PassiveUI 脚本)
    ├── LayerRow_TI
    │   ├── Slot_0 → Button (layerIndex=0, slotIndex=0)
    │   ├── Slot_1 → Button (layerIndex=0, slotIndex=1)
    │   └── Slot_2 → Button (layerIndex=0, slotIndex=2)
    ├── LayerRow_TII (layerIndex=1)
    │   └── Slot_0~2 → Button
    ├── LayerRow_TIII (layerIndex=2) ...
    ├── LayerRow_TIV (layerIndex=3) ...
    └── LayerRow_TV (layerIndex=4) ...
LineSelectDialog (Canvas顶层) → 弹出式5线选择列表
```

### 6.3 技能树面板

```
Canvas
├── SkillTreePanel
│   ├── Skill_Q_View
│   │   ├── Node_Lv1 → Button (能量球)
│   │   ├── Node_Lv2_Left → Button (散射弹幕)
│   │   ├── Node_Lv2_Right → Button (穿透狙击)
│   │   ├── Node_Lv3_Left → Button (弹幕风暴)
│   │   ├── Node_Lv3_Right → Button (毁灭射线)
│   │   └── Connector_Lines → Image (连接线)
│   └── Skill_E_View (同上结构: 冲进步→分支→Lv3)
└── BranchChoiceDialog (Canvas顶层, Lv1→Lv2时弹出)
    ├── LeftCard → 预览Lv2左+Lv3左
    ├── RightCard → 预览Lv2右+Lv3右
    └── ConfirmBtn → Button
```

### 6.4 合成面板

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

### 6.5 全局配色

| 应用位置 | 颜色 |
|---------|------|
| 被动技能图标边框 | 灰色 #999999 |
| 主动技能图标边框 | 金色 #FFD700 |
| 武器技能图标边框 | 蓝色 #4488FF |
| 组合技能图标边框 | 紫色 #AA66FF |
| 已解锁/已装备 | 金色 #FFD700 |
| 可选 | 白色 #FFFFFF |
| 锁定/不可用 | 灰色 #666666 |
| 冲突 | 红色 #FF4444 |

---

## 总结：你需要做的顺序

1. ✅ 打开 Unity → 确认 Player 上 WeaponSkillLink + CombinationCraftSystem 已挂
2. ✅ 菜单 Tools → Create ActiveSkillData Assets (Q/E)
3. ✅ 把 Q/E SO 拖入 SkillManager 槽位
4. ✅ PassiveEquipManager 拖入 25 个被动 SO
5. ✅ CombinationCraftSystem 拖入 3 个组合 SO 配方
6. ✅ 按 6.2~6.4 搭 UI 面板（无代码，纯拖控件配颜色）
7. ▶ Play 测试
