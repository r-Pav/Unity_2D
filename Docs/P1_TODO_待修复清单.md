# P1 待修复清单（已全部修复）

> ⚠️ **已过时（2026-07-27）**：全部条目已验证修复，保留仅供参考。

记录时间: 2026-07-10 11:10
来源: tester 验证报告 (t_a3bc3225)

## MEDIUM

- [ ] **PlayerStatRecalculatedEvent 定义了但从未触发**
  StatModifierManager 计算完最终值后应触发此事件，目前只触发了 StatModifiersChangedEvent。
  影响: HUD 无法订阅单属性变化通知。

## LOW

- [ ] **3 个新增属性无消费代码**
  attackSpeedMultiplier / controlReduction / manaCostMultiplier 已创建且可被修饰器影响，
  但没有任何组件读取它们。等待 P2/P3 消费。

- [ ] **maxHealth 修饰器变化时 currentHealth 不同步**
  maxHealth 被被动加成后 currentHealth 没有相应调整（可能超过新的上限）。
  需在 StatModifiersChangedEvent 响应中 clamp currentHealth。

- [ ] **maxHealth 变化不触发 HUD 刷新**
  同上，HUD 的 HP 显示未订阅 maxHealth 变化事件。
