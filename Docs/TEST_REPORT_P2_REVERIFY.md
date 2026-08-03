# P2 被动装备系统 — 重验测试报告

**测试时间**: 2026-07-10 11:28
**测试类型**: 回归重验
**前次报告**: t_157c4fc8 (P2 被动装备系统验证)
**验收依据**: Docs/策划案_P2_被动.txt
**范围**: 全部 P2 验收标准，重点校验 25 个 SO 资产 + 修饰器联动

---

## 测试结果总览

| 验收项 | 状态 | 备注 |
|--------|------|------|
| 1) 25 个 SO 资产已创建 | ✅ PASS | Assets/Resources/Skills/Passive/ 下 5 层 × 5 线 = 25 .asset |
| 2) SO 资产数值正确性 | ⚠️ PARTIAL | 24/25 正确，**Bug #1**: L5_L3 TV 减伤+控制 |
| 3) PassiveEquipManager 数据结构 | ✅ PASS | 5 层 × 3 槽位数组，lineId 存储 |
| 4) Equip/Unequip 接口 | ✅ PASS | 参数校验、重复检测、层级解锁判定、战斗锁定 |
| 5) 修饰器联动 (StatModifierManager) | ✅ PASS | AddModifier/RemoveModifier 调用正确 |
| 6) 条件修饰器 (低血加防) | ❌ FAIL | **Bug #1**: source 冲突导致 TV 减伤+控制效果异常 |
| 7) EventBus 事件触发 | ✅ PASS | PassiveSlotsChangedEvent 装备/卸下时触发 |
| 8) PlayerController 集成 | ❌ FAIL | **Bug #2**: PassiveEquipManager 未在 PlayerController 创建/引用 |
| 9) SetCombatState 调用链 | ❌ FAIL | **Bug #3**: 仅有定义，无调用方 |
| 10) 层级解锁规则 | ✅ PASS | TI~TV 解锁等级 = {1, 5, 8, 12, 16}，代码+SO 一致 |

---

## Bug 清单

### **Bug #1** — MEDIUM — TV 减伤+控制 SO 与代码条件修饰器冲突

**严重程度**: MEDIUM
**文件**: 
- Assets/Resources/Skills/Passive/Passive_L5_L3.asset
- Assets/Scripts/Skills/PassiveEquipManager.cs (第 54 行, 第 324-331 行)

**问题描述**:
`Passive_L5_L3.asset` (TV 减伤+控制) 包含两个 effect：
- damageReduction +0.25 (Percent) ✓ 基础 25% 减伤
- damageReduction +0.15 (Percent) ✗ 低血加防值

`PassiveEquipManager.AddModifiersForNode()` 将所有 SO effects 转为 Modifier（source = "Passive_L5_L3"），然后额外添加低血加防条件修饰器（LowHpSource = "Passive_L5_L3"）。

**关键冲突**: `StatModifierManager.AddModifier()` 的**同 source 覆盖机制**导致后添加的 lowHpModifier 覆盖掉了前面 2 个 SO effect 修饰器。结果：
- 25% 基础减伤 **丢失**（被覆盖）
- 仅保留 15% 条件减伤（有条件的 Flat 类型，而非 Percent 类型）

**复现步骤**:
1. 装备 TV 减伤+控制被动 (layer=4, lineId=3)
2. `AddModifiersForNode` 添加 2 个 modifier (source=Passive_L5_L3): damageReduction +0.25, +0.15
3. 代码额外添加 lowHpModifier (source=Passive_L5_L3): damageReduction +0.15 (条件)
4. `AddModifier` 发现同 source "Passive_L5_L3" → 移除前 2 个 → 只剩 lowHpModifier
5. 实际生效: 仅有条件减伤 15%，基础 25% 减伤消失

**预期结果**: 
方案 A: SO 仅保留 0.25 damageReduction + 无条件效果；LowHpSource 改唯一标识（如 "Passive_L5_L3_LowHp"）不与 SO source 冲突。
方案 B: SO 包含无条件 0.25 + 有条件 0.15，代码中移除低血加防的额外添加逻辑。

---

### **Bug #2** — MEDIUM — PlayerController 未集成 PassiveEquipManager

**严重程度**: MEDIUM
**文件**: Assets/Scripts/Player/PlayerController.cs

**问题描述**:
`PlayerController.Awake()` 未创建或引用 `PassiveEquipManager` 组件。其他 P1 子组件（PlayerCombat, PlayerHealth 等）都有 `GetComponent<T>()` 或 `GetOrAddComponent<T>()` 逻辑，但 PassiveEquipManager 完全没有。

**影响**: 
- PassiveEquipManager 不自动挂载到 Player 对象上
- 即使手动挂载，PlayerController 也不持有引用
- PassiveEquipManager 对各系统不可见

**预期**: `PlayerController.Awake()` 应添加:
```csharp
var passiveEquip = GetComponent<PassiveEquipManager>();
if (passiveEquip == null) passiveEquip = gameObject.AddComponent<PassiveEquipManager>();
```

---

### **Bug #3** — MEDIUM — SetCombatState 无人调用

**严重程度**: MEDIUM
**文件**: 
- Assets/Scripts/Skills/PassiveEquipManager.cs (第 228 行)
- (无调用方)

**问题描述**:
`SetCombatState(bool combat)` 方法在 `PassiveEquipManager` 中定义，但全项目无任何代码调用它。战场上无法锁定/解锁被动装备 UI。

**影响**:
- `inCombat` 始终保持 false
- 被动装备在任何时候都可修改（违反策划案 "战斗中锁定装备界面" 要求）
- `RefreshLowHpCondition()` 永远不触发（在 `SetCombatState(true)` 中调用）

**预期**:
- 在进入/退出战斗时调用 `passiveEquipManager.SetCombatState(true/false)`
- 或添加 `PlayerController` 属性/事件，让战斗系统设置此状态

---

## SO 资产数值验证详情

| 层级 | 线 0 (HP恢复) | 线 1 (伤害+攻速) | 线 2 (移速+闪避) | 线 3 (减伤+控制) | 线 4 (法力+CD) |
|------|---------------|-----------------|-----------------|-----------------|---------------|
| TI   | HP+1% ✓ | 伤害+8% ✓ | 移速+6% ✓ | 减伤+5% ✓ | 法力恢复+1% ✓ |
| TII  | HP+2% ✓ | 伤害+15% ✓ | 移速+12% ✓ | 减伤+10% ✓ | 法力恢复+2%, 法力+20 ✓ |
| TIII | HP+3% ✓ | 伤害+22%, 攻速+10% ✓ | 移速+18%, 闪避+15% ✓ | 减伤+15%, 硬直-20% ✓ | 法力恢复+3%, 法力+22, CD-5% ✓ |
| TIV  | HP+4% ✓ | 伤害+28%, 攻速+15% ✓ | 移速+24%, 闪避+20% ✓ | 减伤+20%, 控制-25% ✓ | 法力恢复+4%, 法力+25, CD-8% ✓ |
| TV   | HP+5% ✓ | 伤害+35%, 攻速+20% ✓ | 移速+30%, 闪避+30% ✓ | **Bug #1** | 法力恢复+5%, 法力+30, CD-10%, 法力消耗-3% ✓ |

*解锁等级全部正确: TI=1, TII=5, TIII=8, TIV=12, TV=16 ✓*

---

## 前次 MEDIUM 问题回归检查

| 前次问题 | 当前状态 | 说明 |
|----------|---------|------|
| 缺 SO 资产 | ✅ **已修复** | 25 个 PassiveSkillData 已创建 |
| PassiveEquipManager 未在 PlayerController 集成 | ❌ **未修复** | 见 Bug #2 |
| SetCombatState 无人调用 | ❌ **未修复** | 见 Bug #3 |

---

## 结论

**PASS: 6/10 项** | **PARTIAL: 1/10 项** | **FAIL: 3/10 项**

SO 资产已创建且 24/25 数值正确（计划内修复完成）。但存在 3 个 MEDIUM 级别 Bug：
- 1 个新发现（Bug #1: SO 与代码修饰器冲突）
- 2 个遗留未修复（Bug #2, #3: 集成问题）

**建议**: 修复 3 个 MEDIUM Bug 后进行三次重验。
