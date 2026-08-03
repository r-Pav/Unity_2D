# P2 待修复清单（已全部修复）

> ⚠️ **已过时（2026-07-27）**：全部条目已验证修复，保留仅供参考。

记录时间: 2026-07-10 11:18
来源: tester 验证报告 (t_157c4fc8)
最后更新: 2026-07-10 12:01 (t_ae60f72c 编译错误+2MEDIUM修复验证)

## 已修复（2026-07-10）

- [x] **Bug #1: TV 减伤+控制 SO 与代码条件修饰器冲突**
  低血加防 source 改为独立标识 "Passive_LowHpDefense"，不再与 SO 被动层 source 冲突。

- [x] **Bug #2: PlayerController 未集成 PassiveEquipManager**
  PlayerController 已加 GetComponent + 公开属性。

- [x] **Bug #3: SetCombatState 玩家侧无人调用**
  PlayerController 统一管理战斗态：OnAttack/OnDamaged → SetCombatState(true)，combatTimer 归零 → SetCombatState(false)。

- [x] **编译错误 CS0108: PlayerController statModManager 隐藏继承成员**
  移除重复字段，使用继承自 CharacterBase 的 protected statModManager。

- [x] **SO 数值翻倍: 编辑器脚本往 L5_L3 写入 +0.15**
  CreatePassiveSkillDataAssets.cs 已移除无条件 +0.15，低血加防由 PassiveEquipManager 条件处理。

## LOW

- [ ] **UnequipPassive 参数用 slotIndex 而非 lineId**
  与策划案接口不一致，需改为 lineId。

- [ ] **低血条件只在 combat start 刷新**
  HP 变化后无触发通知，低血加防条件不会动态更新。

- [ ] **卸下操作缺 Debug.Log 入口日志**
  便于运行时排查。

## 补充（待确认是否延期）

- [ ] 无 UI 面板（P6 再做）
- [ ] 无存档读档（P6 再做）
