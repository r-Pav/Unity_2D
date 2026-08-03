# 背包装备系统 UI 命名规范

> 以 SampleScene.scene 实际 Hierarchy 为准，最后更新：2026-07-22

## Hierarchy 结构

```
InventorySystem                    根节点（挂 InventoryManager.cs）
├── EnemyEquipmentIcon             enemy装备图标
├── QuickSlotBar                   主界面快捷消耗品栏（挂 QuickSlotBar.cs）
│   ├── QuickSlot_0
│   └── QuickSlot_1
├── WarehousePanel                 仓库面板（挂 WarehousePanel.cs）
│   ├── CategoryTabs               分类标签
│   │   ├── Button                 全部
│   │   ├── Button (1)             消耗品
│   │   ├── Button (2)             装备
│   │   └── Button (3)             材料
│   └── WarehouseGrid              仓库网格（ScrollRect）
│       └── ItemCell x15           物品格子（挂 ItemCell.cs）
└── InventoryPanel                 背包+装备主面板（挂 InventoryPanel.cs）
    ├── CategoryTabs               分类标签
    │   ├── Button                 全部
    │   ├── Button (1)             消耗品
    │   ├── Button (2)             装备
    │   └── Button (3)             材料
    ├── EquipmentSlots             装备槽位区
    │   ├── Slot_Weapon            武器槽（挂 EquipmentSlot.cs）
    │   ├── Slot_Armor             护甲槽（挂 EquipmentSlot.cs）
    │   ├── Slot_Accessory_0       饰品槽1（挂 EquipmentSlot.cs）
    │   └── Slot_Accessory_1       饰品槽2（挂 EquipmentSlot.cs）
    └── ItemGrid                   物品网格（ScrollRect）
        └── ItemCell x11           物品格子（挂 ItemCell.cs）
```

## 脚本清单

| 脚本 | 挂载位置 | 职责 |
|------|----------|------|
| InventoryManager.cs | InventorySystem | 总管：数据层、面板开关、仓库交互 |
| InventoryPanel.cs | InventoryPanel | 背包面板：拖拽、装备槽联动 |
| EquipmentSlot.cs | Slot_* | 单个装备槽：穿戴/卸下/接受拖放 |
| ItemCell.cs | 所有 ItemCell | 物品格子：拖拽源/目标、显示图标数量 |
| WarehousePanel.cs | WarehousePanel | 仓库面板：存入/取出 |
| QuickSlotBar.cs | QuickSlotBar | 快捷栏：点击使用消耗品 |
| EnemyEquipmentIcon.cs | EnemyEquipmentIcon | enemy装备：无装备隐藏，有装备50%透明度 |
| ItemDragHandler.cs | ItemCell | 拖拽逻辑（BeginDrag/Drag/EndDrag） |

## 分类标签同步规则

- 背包 CategoryTabs 点击切换分类时，仓库 CategoryTabs 同步切换到相同分类
- 仓库 CategoryTabs 点击切换分类时，背包 CategoryTabs 同步切换到相同分类
- 由 InventoryManager 统一管理分类状态，两个面板订阅状态变化

## 拖拽规则

- ItemCell 实现 IBeginDragHandler + IDragHandler + IEndDragHandler
- EquipmentSlot 实现 IDropHandler（只接受装备类型物品）
- ItemCell 同时是 IDropHandler（背包网格↔仓库网格互拖）
- 仓库格子和背包格子互为拖拽目标

## Enemy 装备视觉

- 无装备：EnemyEquipmentIcon 隐藏
- 有装备：EnemyEquipmentIcon 显示装备图标，CanvasGroup alpha = 0.5
