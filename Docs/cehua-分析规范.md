# cehua 分析规范

> 后续 cehua 分析时遵守，programer 实现时参考。

## 一、类型选择原则

### 枚举（enum）

**适合用枚举的场景：**
- 数量固定、不会频繁增删的分类（如 EquipmentSlotType: Weapon/Armor/Accessory）
- 需要在 Inspector 里下拉选择的字段
- switch 分支不超过 10 个

**不适合用枚举的场景：**
- 需要在运行时动态增加的类型（如技能 ID、Buff ID）
- 需要携带额外数据的分类（如 ItemCategory 需要关联默认槽位）
- 跨版本热更可能新增的项

**枚举的热更风险：**
- 加新枚举值后，已有 prefab/scene 的序列化值不变，不会自动更新
- 删/重排枚举值会导致序列化数据错位
- 对策：新值只加在末尾，永远不删旧值（标记 `[Obsolete]`）

### 字符串 ID

**适合用字符串 ID 的场景：**
- 运行时动态注册的类型（属性 StatId、技能 SkillId）
- 需要 ScriptableObject 查找对应的配置数据
- 跨系统解耦（字符串 ID 比枚举引用更松耦合）

你项目里 StatId 用字符串是对的。

### ScriptableObject

**适合用 SO 的场景：**
- 需要携带配置数据（数值、图标、描述）
- 需要在不同 prefab/场景间共享引用
- 需要通过 AssetDatabase 查找

### 决定流程

```
这个分类 → 数量固定且 <10？ → 未来可能热更加项？
    ↓是              ↓否              ↓是
   枚举          字符串ID/SO        字符串ID/SO
```

## 二、cehua 分析时的类型检查清单

- [ ] 新定义的分类类型是什么？是否适合用枚举？
- [ ] 如果用了枚举，热更新增项会不会有序列化断裂？
- [ ] 如果用了字符串 ID，是否有对应的查找机制（SO 索引/字典/反射）？
- [ ] ScriptableObject 的字段是否所有都需要序列化（public 非 static 字段自动序列化到 .asset）？
