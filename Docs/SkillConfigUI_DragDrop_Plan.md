# SkillConfigUI 拖拽改造方案

## 一、现状分析

### 1.1 代码已实施完成

当前 `SkillConfigUI.cs` (271行) 及其关联组件已完整实现拖拽交互。旧版弹窗选技能 + ChangeBtn 的交互已全部移除，替换为：

| 旧交互（已删除） | 新交互（已实施） |
|---|---|
| 点击槽位 ChangeBtn → 弹出 skillSelectPopup | 左栏长按拖拽到槽位 → 装备 |
| popupListContainer 中点击 popupItemPrefab | SkillListEntry 长按 0.5s 激活拖拽幽灵 |
| popupCloseBtn 关闭弹窗 | 不需要弹窗，左栏始终可见 |
| slot0~3ChangeBtn 按钮 | SkillHudSlot 本身即是交互目标 |

### 1.2 涉及文件

```
Assets/Scripts/UI/
  SkillConfigUI.cs    — 主面板，挂 SkillConfigPanel，管理左栏列表 + 右栏槽位 + 拖拽回调
  SkillListEntry.cs   — 左栏条目，挂 IBeginDragHandler/IDragHandler/IEndDragHandler + 长按激活
  SkillHudSlot.cs     — 右栏槽位，挂 IDropHandler + 自身也是拖拽源（槽位间互换）
  SkillBarHUD.cs      — 不改：游戏内 HUD 技能栏，通过 SkillPool 事件刷新
Assets/Scripts/Skills/
  SkillPool.cs        — 不改：数据层，EquipToHud/ClearHudSlot/GetHudAssignments
  OwnedSkillEntry.cs  — 不改：数据结构
```

---

## 二、架构设计

### 2.1 组件关系图

```
SkillConfigUI (主面板)
    |
    +--- 左栏：ScrollRect
    |        └── skillListContainer
    |             └── SkillListEntry × N  ← 长按 0.5s → 生成 DragGhost → OnDrop
    |                    implements: IBeginDragHandler, IDragHandler, IEndDragHandler
    |                    property: CurrentEntry (OwnedSkillEntry)
    |
    +--- 右栏：4 × SkillHudSlot[]
    |        implements: IDropHandler, IBeginDragHandler/IDragHandler/IEndDragHandler
    |        property: HudIndex (0~3), CurrentEntry
    |        回调父面板: HandleSkillDrop / HandleSkillUnequip
    |
    +--- dragGhostParent   (RectTransform, Canvas 根)
    +--- rightColumnArea   (RectTransform, 用于判断卸载区)
    |
    +--- 页面跳转: toCraftBtn / toSkillTreeBtn → PanelManager
```

### 2.2 数据流

```
左栏拖拽到 HUD 槽:
  SkillListEntry.OnEndDrag → EventSystem 路由到 SkillHudSlot.OnDrop
    → SkillHudSlot 识别 pointerDrag 为 SkillListEntry
    → _configUI.HandleSkillDrop(targetSlotIndex, skillId, sourceSlot: null)
      → skillPool.EquipToHud(targetSlotIndex, skillId)   // 装备
      → RefreshLeftList()  // 已装备技能从左栏过滤掉

槽位间拖拽:
  SkillHudSlot_A.OnEndDrag → EventSystem 路由到 SkillHudSlot_B.OnDrop
    → SkillHudSlot_B 识别 pointerDrag 为 SkillHudSlot
    → _configUI.HandleSkillDrop(targetIndex, skillId, sourceSlot)
      → skillPool.ClearHudSlot(sourceIndex)
      → skillPool.ClearHudSlot(targetIndex)
      → skillPool.EquipToHud(targetIndex, sourceSkillId)   // 交换 A→B
      → skillPool.EquipToHud(sourceIndex, targetSkillId)   // 交换 B→A
      → RefreshLeftList()  // 左栏不变（装备的技能都没回池子）

槽位拖出到右栏外:
  SkillHudSlot.OnEndDrag → GetDropTarget() == null
    → _configUI.IsOverUnequipZone(eventData.position) == true
    → _configUI.HandleSkillUnequip(HudIndex)
      → skillPool.ClearHudSlot(hudSlotIndex)  // 卸载
      → RefreshLeftList()  // 技能回到左栏显示

长按失败（松开过早）:
  SkillListEntry.OnPointerUp → holdTimer < 0.5s → 不激活拖拽 → 无操作
```

### 2.3 SkillPool API（不改，仅列关键接口）

```csharp
// 装备：指定技能到指定槽位（自动处理重复装备检测）
public bool EquipToHud(int hudIndex, string skillId);

// 清空槽位
public void ClearHudSlot(int hudIndex);

// 获取所有槽位的 skillId 数组（4个元素，空串=空槽）
public string[] GetHudAssignments();

// 获取已拥有技能列表（不含已装备）
public List<OwnedSkillEntry> GetOwnedSkills();

// 获取指定槽位的 OwnedSkillEntry
public OwnedSkillEntry GetHudSkill(int hudIndex);

// 事件
public event Action OnPoolChanged;         // 池子内容变化
public event Action<int> OnHudSlotChanged; // 指定槽位变化
```

---

## 三、组件详细设计

### 3.1 SkillListEntry — 左栏条目（拖拽源）

**职责**: 显示技能图标/名称/等级，长按 0.5s 后激活拖拽。

**拖拽激活流程:**

```
OnPointerDown → holdTimer=0, _isHolding=true, 显示圆形指示器
    ↓
Update() → holdTimer += deltaTime, 填充指示器 fillAmount
    ↓ holdTimer >= 0.5s
StartRealDrag()
    ├── canvasGroup.alpha=0.6, blocksRaycasts=false   // 本体半透明，不挡射线
    ├── CreateDragGhost() → 64×64 半透明图标，挂到 Canvas Root
    └── _isDragging=true
    ↓
OnDrag() → _dragGhostRect.position = eventData.position   // 幽灵跟随鼠标
    ↓
OnEndDrag()
    ├── 恢复 canvasGroup.alpha=1, blocksRaycasts=true
    ├── Destroy(dragGhost)
    └── _isDragging=false
```

**关键设计决策**:
- 长按 0.5s 激活而非直接拖拽：避免与 ScrollRect 滚动冲突。短滑=滚动，长按=拖拽。
- `IPointerDownHandler/IPointerUpHandler` 单独处理按压计时，不依赖 `OnBeginDrag`（EventSystem 的 BeginDrag 阈值太短）。
- 拖拽期间 `blocksRaycasts=false` 确保射线穿透到下方的 `SkillHudSlot` 作为 drop 目标。

### 3.2 SkillHudSlot — 右栏槽位（拖拽目标 + 拖拽源）

**双重身份**:
1. **作为 Drop 目标** (`IDropHandler.OnDrop`): 接受左栏条目或另一个槽位的拖入。
2. **作为 Drag 源** (`IBeginDragHandler/IDragHandler/IEndDragHandler`): 拖出已装备技能到另一个槽位或卸载区。

**拖入处理流程（OnDrop）**:

```
OnDrop(PointerEventData)
    ├── pointerDrag.GetComponent<SkillListEntry>() != null?
    │     → 左栏拖入: HandleSkillDrop(hudIndex, entry.id, sourceSlot:null)
    │     → skillPool.EquipToHud(hudIndex, skillId)
    │
    └── pointerDrag.GetComponent<SkillHudSlot>() != null?
          → 槽位拖入: HandleSkillDrop(hudIndex, entry.id, sourceSlot)
          → 交换逻辑（两个 ClearHudSlot + 两个 EquipToHud）
```

**拖出处理流程（OnEndDrag）**:

```
OnEndDrag(PointerEventData)
    ├── 恢复 canvasGroup, 销毁 ghost
    ├── GetDropTarget(eventData) → 检查鼠标是否对着另一个 HUD 槽
    │     → 如果 OnDrop 已被调用（事件在 OnEndDrag 之前触发）→ 已处理，不做额外操作
    │     → 如果未命中任何 HUD 槽:
    │         └── IsOverUnequipZone(eventData.position)?
    │               → HandleSkillUnequip(hudIndex)  // 卸载
    │               → skillPool.ClearHudSlot(hudIndex)
    │
    └── _currentDragSource = null
```

**悬停高亮（Update）**:

```
Update()
    └── if (_currentDragSource != null && _currentDragSource != this)
          └── 检测鼠标是否在本槽 Rect 内
                → SetDropHighlight(true/false)  // 绿色高亮/恢复
```

**关键设计决策**:
- `_currentDragSource` (static): 跨槽位共享拖拽源引用，让每个槽都能在 Update 中检测是否被悬停并高亮。
- 卸载判定用 `rightColumnArea` RectTransform: 只有拖出右栏区域才算卸载，拖到左栏或其他 UI 区域不触发卸载（防止误操作）。
- `OnDrop` 在 `OnEndDrag` 之前触发（Unity EventSystem 执行顺序），所以在 OnEndDrag 中检查 `GetDropTarget()==null` 即可判断是否被有效处理。

### 3.3 SkillConfigUI — 主面板

**HandleSkillDrop** — 统一拖入回调:

```csharp
public void HandleSkillDrop(int targetSlotIndex, string skillId, SkillHudSlot sourceSlot)
{
    if (sourceSlot != null)
    {
        // 槽位间交换
        int sourceIndex = sourceSlot.HudIndex;
        string targetSkillId = skillPool.GetHudAssignments()[targetSlotIndex];
        string sourceSkillId = skillPool.GetHudAssignments()[sourceIndex];

        skillPool.ClearHudSlot(sourceIndex);
        skillPool.ClearHudSlot(targetSlotIndex);

        if (!string.IsNullOrEmpty(sourceSkillId))
            skillPool.EquipToHud(targetSlotIndex, sourceSkillId);
        if (!string.IsNullOrEmpty(targetSkillId))
            skillPool.EquipToHud(sourceIndex, targetSkillId);
    }
    else
    {
        // 左栏装备到槽位
        skillPool.EquipToHud(targetSlotIndex, skillId);
    }

    RefreshLeftList();  // 更新左栏：已装备技能不显示
}
```

**HandleSkillUnequip** — 卸载回调:

```csharp
public void HandleSkillUnequip(int hudSlotIndex)
{
    skillPool?.ClearHudSlot(hudSlotIndex);
    RefreshLeftList();  // 技能回到左栏列表
}
```

---

## 四、Inspector 绑定清单

### SkillConfigPanel 上需绑定

| 分组 | 字段 | 类型 | 说明 |
|---|---|---|---|
| 左栏 | `skillListContainer` | Transform | ScrollRect 的 Content |
| 左栏 | `skillListItemPrefab` | GameObject | 条目模板，挂 `SkillListEntry` 组件，设为 inactive |
| 左栏 | `emptyHint` | TMP_Text | 空列表提示文字 |
| HUD 槽位 | `hudSlots[0]` | SkillHudSlot | Q 槽，挂 `SkillHudSlot` |
| HUD 槽位 | `hudSlots[1]` | SkillHudSlot | E 槽 |
| HUD 槽位 | `hudSlots[2]` | SkillHudSlot | R 槽 |
| HUD 槽位 | `hudSlots[3]` | SkillHudSlot | F 槽 |
| 拖拽设置 | `dragGhostParent` | RectTransform | Canvas 根节点（幽灵挂载点） |
| 卸载判定 | `rightColumnArea` | RectTransform | 右栏背景 Rect，拖出此区域=卸载 |
| 页面跳转 | `toCraftBtn` | Button | 跳转合成页 |
| 页面跳转 | `toSkillTreeBtn` | Button | 跳转技能树 |
| 页面跳转 | `panelManager` | PanelManager | PanelManager 引用 |
| 页面跳转 | `craftPanel` | GameObject | 合成面板 |
| 页面跳转 | `skillTreePanel` | GameObject | 技能树面板 |

### SkillListEntry Prefab 上需绑定

| 字段 | 类型 | 说明 |
|---|---|---|
| `icon` | Image | 技能图标 |
| `nameText` | TMP_Text | 技能名称 |
| `levelText` | TMP_Text | 等级文字 |
| `canvasGroup` | CanvasGroup | 拖拽时控制透明度/射线 |
| `holdIndicator` | Image | 长按圆形指示器（Fill Method=Radial360） |

### SkillHudSlot Prefab 上需绑定

| 字段 | 类型 | 说明 |
|---|---|---|
| `keyLabel` | TMP_Text | 快捷键标签 Q/E/R/F |
| `icon` | Image | 技能图标 |
| `nameText` | TMP_Text | 技能名称 |
| `levelText` | TMP_Text | 等级文字 |
| `emptySlotBackground` | Image | 空槽背景（颜色区分空/满/高亮） |
| `canvasGroup` | CanvasGroup | 拖拽时控制透明度/射线 |
| `keyLabelString` | string | 快捷键字符 "Q"/"E"/"R"/"F" |

---

## 五、删除项汇总

以下字段/组件/GameObject 已从 SkillConfigPanel 的场景层级和 SkillConfigUI.cs 中移除：

| 删除项 | 类型 | 原因 |
|---|---|---|
| `skillSelectPopup` | GameObject (SerializeField) | 弹窗选技能改为左栏直接拖拽 |
| `popupListContainer` | Transform (SerializeField) | 弹窗内的滚动列表容器 |
| `popupItemPrefab` | GameObject (SerializeField) | 弹窗内条目模板 |
| `popupCloseBtn` | Button (SerializeField) | 弹窗关闭按钮 |
| `slot0ChangeBtn` | Button (SerializeField) | Q 槽更换按钮 |
| `slot1ChangeBtn` | Button (SerializeField) | E 槽更换按钮 |
| `slot2ChangeBtn` | Button (SerializeField) | R 槽更换按钮 |
| `slot3ChangeBtn` | Button (SerializeField) | F 槽更换按钮 |
| `SkillPickerItem.cs` | 脚本文件 | 弹窗条目组件（已不再需要） |

**场景清理**: 需在 Hierarchy 中手动删除 `skillSelectPopup` GameObject 及其子树，以及各槽位下的 ChangeBtn 子对象。

---

## 六、不改动项

| 组件 | 原因 |
|---|---|
| `SkillPool.cs` | 数据层 API (`EquipToHud`/`ClearHudSlot`/`GetHudAssignments`) 已经满足需求 |
| `SkillBarHUD.cs` | 游戏内 HUD 展示，通过 `OnHudSlotChanged` 事件自动同步，与配置页解耦 |
| `SkillManager.cs` | 技能激活/冷却逻辑，与配置 UI 无关 |
| `PanelManager.cs` | 面板栈管理，SkillConfigUI 通过 IPanel 接口注册 |
| `OwnedSkillEntry.cs` | 纯数据结构，无需改动 |
| `ActiveSkillData.cs` / `SkillData.cs` | SO 配置数据，无需改动 |

---

## 七、交互流程对比

### 旧流程（已废弃）

```
玩家想换 Q 槽技能:
  1. 点击 Q 槽 ChangeBtn
  2. 弹出 skillSelectPopup（覆盖整个面板）
  3. 在 popupListContainer 中滚动找到目标技能
  4. 点击 popupItemPrefab 条目
  5. 弹窗关闭，技能装备到槽位
  6. 点 popupCloseBtn 或点空白区关闭弹窗（放弃选择）
```

### 新流程（当前）

```
玩家想换 Q 槽技能:
  1. 在左栏找到目标技能条目
  2. 长按 0.5s（圆形指示器填满）
  3. 出现半透明幽灵图标跟随鼠标
  4. 拖拽到 Q 槽 → 松开
  5. 装备完成

槽位交换:
  1. 按住 Q 槽技能图标，拖出
  2. 幽灵跟随鼠标
  3. 拖到 E 槽 → E 槽高亮
  4. 松开 → Q 和 E 技能互换

卸载:
  1. 按住槽位技能图标
  2. 拖出到右栏区域之外
  3. 松开 → 技能回到左栏
```

---

## 八、边界情况处理

| 场景 | 处理方式 |
|---|---|
| 左栏条目为空 | `emptyHint` 显示提示文字，无条目可拖 |
| 所有技能已装备到 HUD | 左栏为空，`emptyHint` 显示 |
| 空槽拖拽 | `OnBeginDrag` 中检查 `CurrentEntry==null`，return 不激活拖拽 |
| 拖到同一槽位 | `HandleSkillDrop` 中检查 `sourceIndex==targetSlotIndex`，return |
| 左栏条目拖到非槽位区域 | `OnDrop` 不触发，`OnEndDrag` 恢复原位（无操作） |
| 槽位拖到左栏/其他 UI | `GetDropTarget()==null` 且不在卸载区 → 恢复原位 |
| 技能不在池中 | `SkillPool.EquipToHud` 检查 `FindEntryById`，不存在则返回 false + LogWarning |
| 已装备技能拖入另一个槽位 | `SkillPool.EquipToHud` 自动从旧槽位移除 |
| 长按不足 0.5s 松开 | holdIndicator 消失，不激活拖拽，可正常滚动 |
| 拖拽期间 ScrollRect 行为 | `OnBeginDrag` 和 `OnDrag` 中检测 `_isDragging`，未激活时转发给 ScrollRect |

---

## 九、性能与兼容性

- **无 GC 分配热点**: DragGhost 在拖拽结束时 `Destroy`，OnEnable 全量重建列表（合理，技能池通常 <50 个条目）
- **ScrollRect 兼容**: 长按 0.5s 机制彻底避免了拖拽与滚动的冲突
- **事件订阅**: OnEnable/OnDisable 中正确订阅/取消订阅 SkillPool 事件，不会泄漏
- **EventSystem 依赖**: 场景必须有 `EventSystem` + `StandaloneInputModule` + `GraphicRaycaster`（Canvas 上）

---

## 十、验证清单

- [ ] SkillConfigPanel 的 Inspector 字段全部绑定（14 个字段）
- [ ] SkillListEntry Prefab 挂 `SkillListEntry` 组件，5 个字段绑定
- [ ] 4 个 HUD 槽位 GameObject 分别挂 `SkillHudSlot`，Inspector 字段绑定
- [ ] `skillSelectPopup` GameObject 已从 Hierarchy 删除
- [ ] 4 个 `slotChangeBtn` 已从各槽位下删除
- [ ] Scene 中有 `EventSystem` + `GraphicRaycaster`（Canvas 上）
- [ ] 左栏 ScrollRect 正常工作（滚动时不会误触拖拽）
- [ ] 长按 0.5s 激活拖拽，幽灵跟随鼠标
- [ ] 左栏条目拖入 HUD 槽位 → 装备成功，左栏刷新
- [ ] HUD 槽位间拖拽 → 交换成功
- [ ] HUD 槽位拖出到右栏外 → 卸载成功，技能回左栏
- [ ] `SkillBarHUD` 游戏内技能栏自动同步刷新
- [ ] 面板关闭再打开，状态保持（通过 SkillPool 事件驱动）
