# 全阶段遗留问题清单

> ⚠️ **已过时（2026-07-27）**：全部条目已修复，保留仅供参考。

记录时间: 2026-07-10 12:20
最后更新: P2 MEDIUM 修复 + 坏 .meta/.asset 重建后

---

## P1 待修

### MEDIUM

- [ ] **PlayerStatRecalculatedEvent 定义了但从未触发**
  StatModifierManager 计算完最终值后应触发此事件，目前只触发了 StatModifiersChangedEvent。
  影响: HUD 无法订阅单属性变化通知。

### LOW

- [ ] **3 个新增属性无消费代码**
  attackSpeedMultiplier / controlReduction / manaCostMultiplier 已创建且可被修饰器影响，
  但没有任何组件读取它们。等待 P2/P3 消费。

- [ ] **maxHealth 修饰器变化时 currentHealth 不同步**
  maxHealth 被被动加成后 currentHealth 没有相应调整（可能超过新的上限）。
  需在 StatModifiersChangedEvent 响应中 clamp currentHealth。

- [ ] **maxHealth 变化不触发 HUD 刷新**
  同上，HUD 的 HP 显示未订阅 maxHealth 变化事件。

---

## P2 待修

### LOW

- [ ] **UnequipPassive 参数用 slotIndex 而非 lineId**
  与策划案接口不一致，需改为 lineId。

- [ ] **低血条件只在 combat start 刷新**
  HP 变化后无触发通知，低血加防条件不会动态更新。

- [ ] **卸下操作缺 Debug.Log 入口日志**
  便于运行时排查。

### 延期到 P6

- [ ] **无 UI 面板** — 被动装备/技能 UI
- [ ] **无存档读档**

---

## P3 主动技能（未开始）

- Q 键能量投射（远程）
- E 键灵动身法（位移）
- Lv2 左右分支互斥，永久锁定
- 策划案: Docs/策划案_P3_主动.txt

---

## P4 待修

### MEDIUM

- [ ] **PlayerController 未集成 WeaponSkillLink**
  WeaponSkillLink 组件逻辑正确但未挂载到 PlayerController 上，不会自启动监听装备事件。
  需在 PlayerController 中声明 [SerializeField] WeaponSkillLink + Awake/Start 初始化。
  来源: TEST_REPORT_P4.md

- 5 种武器各一个技能
- 装备即得，0 消耗
- 可作合成材料
- 策划案: Docs/策划案_P4_武器.txt

---

## P5 组合技能（未开始）

- 3 个合成配方（Lv1→双重协同 / Lv2→法则领域 / Lv3→终焉审判）
- 消耗材料合成，不可撤销
- 策划案: Docs/策划案_P5_组合.txt

---

## P6 调优（未开始）

- UI 面板（被动装备界面+技能界面+HUD）
- 存档/读档
- 数值平衡
- 策划案: Docs/策划案_P6_调优.txt
