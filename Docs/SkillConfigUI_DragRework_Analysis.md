# SkillConfigUI 拖拽改造 — 分析方案

## 日期
2026-07-21

## 一、当前状态

### 交互流程（改造前）
```
左栏技能条目（纯展示）
       ↓
点击右栏"更换"按钮
       ↓
弹出 skillSelectPopup（模态窗口）
       ↓
点击弹窗条目 → EquipToHud → 关闭弹窗
```

### 现有文件
| 文件 | 职责 | 改动 |
|------|------|------|
| `Assets/Scripts/UI/SkillConfigUI.cs` (336行) | 主控：列表刷新、弹窗、选择回调 | **大幅改** |
| `Assets/Scripts/UI/SkillListEntry.cs` (28行) | 左栏条目展示组件 | **加拖拽接口** |
| `Assets/Scripts/UI/SkillPickerItem.cs` (41行) | 弹窗条目（带点击选择） | **删除**（不再需要弹窗） |
| `Assets/Scripts/Skills/SkillPool.cs` (312行) | 数据层：池管理+HUD绑定 | **不改** |

### 场景绑定现状
- 4个 `slot*ChangeBtn`：全部 `{fileID: 0}`（Inspector未绑定，原本就未使用）
- `skillSelectPopup`：已绑定 GameObject
- `popupCloseBtn`：已绑定 Button

---

## 二、改造目标

**拖拽直装**：从左栏技能条目拖到右栏HUD槽位，松手即装备。槽位间互拖交换技能。

删除的交互元素：
- ❌ `skillSelectPopup` 弹窗（整个GameObject）
- ❌ `popupListContainer` / `popupItemPrefab` / `popupCloseBtn`
- ❌ 4个 `slot*ChangeBtn`（已在场景中未绑定，代码中保留监听但无效）
- ❌ `SkillPickerItem.cs` 整个类

---

## 三、改动明细

### 3.1 SkillConfigUI.cs — 删减

**删除字段（15个）：**
```csharp
// Inspector 4个ChangeBtn（Line 39/46/52/59）
slot0ChangeBtn, slot1ChangeBtn, slot2ChangeBtn, slot3ChangeBtn

// 弹窗相关（Line 63-67, 70）
skillSelectPopup, popupListContainer, popupItemPrefab, popupCloseBtn

// 运行时状态（Line 82）
pendingHudSlot
```

**删除方法（5个）：**
- `OpenSkillPicker(int)`（Line 261-297）
- `FillPickerItemFallback(...)`（Line 299-319）
- `CloseSkillPicker()`（Line 321-325）
- `OnSkillSelected(string)`（Line 327-335）

**删除Awake中的绑定：**
- 4行 `slot*ChangeBtn?.onClick.AddListener(...)` 
- `popupCloseBtn?.onClick.AddListener(CloseSkillPicker)`
- `skillSelectPopup.SetActive(false)`

### 3.2 SkillConfigUI.cs — 新增

**新增公共方法（供 SkillListEntry 拖拽回调）：**

```csharp
/// <summary>左栏条目拖到HUD槽位时调用</summary>
public void EquipToSlot(int hudIndex, string skillId)
{
    skillPool?.EquipToHud(hudIndex, skillId);
}
```

### 3.3 SkillListEntry.cs — 改造

**新增接口实现：**
```
IBeginDragHandler, IDragHandler, IEndDragHandler
```

**新增字段：**
```csharp
[SerializeField] private CanvasGroup canvasGroup;   // 拖拽时半透明
private OwnedSkillEntry _entryData;                   // 当前数据
private SkillConfigUI _parentUI;                      // 回调目标
```

**Setup方法签名扩展：**
```csharp
public void Setup(OwnedSkillEntry entry, SkillConfigUI parentUI)
```

**拖拽行为规格：**

| 阶段 | 行为 |
|------|------|
| BeginDrag | 记录数据；canvasGroup.alpha=0.6；blocksRaycasts=false |
| Drag | 跟随鼠标（RectTransform.position = Input.mousePosition，考虑Canvas缩放） |
| EndDrag | 射线检测落点；若命中HUD槽位的DropZone → 调用 `_parentUI.EquipToSlot(slotIndex, _entryData.id)`；否则恢复原位；alpha=1；blocksRaycasts=true |

**拖拽视觉效果：**
- 拖拽中：条目半透明+跟随鼠标
- 离开原位：原位置留空（或被其他条目上移填补——取决于LayoutGroup行为）
- 松手无效区域：条目回到原位

### 3.4 SkillPickerItem.cs — 删除

整个文件不再需要。弹窗交互被拖拽完全替代。

---

## 四、场景改动

### 4.1 Canvas下的SkillConfigPanel结构（改造后）

```
SkillConfigPanel (SkillConfigUI)
├── 左栏 Scroll View
│   └── skillListContainer
│       └── SkillListEntry (Prefab) ← 加 CanvasGroup + DragHandler
├── 右栏 HUD槽区域
│   ├── SlotArea_Q (Image + DropZone脚本)
│   ├── SlotArea_E
│   ├── SlotArea_R
│   └── SlotArea_F
└── 底部跳转按钮（toCraftBtn / toSkillTreeBtn）
```

### 4.2 需要新增/修改的GameObject

**每个HUD槽位需要添加一个 DropZone 子对象：**
- 放在槽位图标的同级或父级
- RectTransform 覆盖整个槽位可视区域
- 挂一个简单的 `DropZone` 脚本（见下）

### 4.3 新增脚本：DropZone.cs

```csharp
// 职责：挂在每个 HUD 槽位上，接收拖拽落点
public class DropZone : MonoBehaviour, IDropHandler, IPointerEnterHandler, IPointerExitHandler
{
    [SerializeField] private int hudIndex;                   // 0/1/2/3
    [SerializeField] private Image highlightImage;           // 高亮边框（可选）
    private SkillConfigUI _parentUI;

    void Awake() { _parentUI = GetComponentInParent<SkillConfigUI>(); }

    public void OnDrop(PointerEventData eventData)
    {
        // 获取被拖拽的 SkillListEntry
        var draggedEntry = eventData.pointerDrag?.GetComponent<SkillListEntry>();
        if (draggedEntry == null) return;

        string skillId = draggedEntry.GetSkillId();
        if (string.IsNullOrEmpty(skillId)) return;

        _parentUI.EquipToSlot(hudIndex, skillId);
    }

    public void OnPointerEnter(PointerEventData eventData)
    {
        if (highlightImage != null) highlightImage.enabled = true;
    }

    public void OnPointerExit(PointerEventData eventData)
    {
        if (highlightImage != null) highlightImage.enabled = false;
    }
}
```

### 4.4 删除的GameObject
- `skillSelectPopup` 及其子物体（整个弹窗GameObject）
- 4个 ChangeBtn GameObject（如果场景中存在且未绑定则清理残留）

---

## 五、拖拽交互完整规格

### 5.1 左栏条目 → HUD槽位

```
[BeginDrag] 条目记录skillId → 半透明 → 跟随鼠标
[Drag]     条目图标跟随鼠标移动
[EndDrag]
  ├─ 落在 DropZone(hudIndex) → EquipToHud(hudIndex, skillId)
  └─ 落在其他位置 → 条目回到原位
```

SkillPool.EquipToHud 自动处理：
- 同一技能已在其他槽位 → 从旧槽位移除
- 目标槽位已有技能 → 被替换（旧技能仍在池中，只是从HUD解绑）
- 刷新UI：OnHudSlotChanged → RefreshHudSlot

### 5.2 槽位间互拖（后续迭代）

当前方案不包含槽位间互拖。理由：
- 槽位目前显示的是静态信息（icon+name+level），不是可拖拽对象
- 实现槽位互拖需要给每个槽位加 IBeginDragHandler + 拖拽视觉反馈
- 左栏→右槽的拖拽已经覆盖了核心用例（更换装备）

如果后续需要槽位间互拖，需要：
- 右栏槽位的图标区域挂 IBeginDragHandler
- DropZone 同时接受来自左栏条目和右栏条目的拖拽
- 区分来源类型（PoolItem vs HudItem）

### 5.3 空槽位的处理

- 拖拽到已有技能的槽位：直接替换（SkillPool.EquipToHud 自动处理旧槽位清除）
- 拖拽到空槽位：正常装备
- 拖拽已在目标槽位的技能：无操作（EquipToHud 会检测并跳过重复绑定）

---

## 六、不变的部分

| 组件 | 说明 |
|------|------|
| SkillPool.cs | 完全不改。EquipToHud/ClearHudSlot/GetOwnedSkills 接口已就绪 |
| SkillBarHUD.cs | 不改。OnHudSlotChanged事件驱动刷新 |
| SkillManager.cs | 不改 |
| RefreshRightSlots/RefreshLeftList/RefreshHudSlot | 不改。拖拽完成后 SkillPool 触发事件 → UI 自动刷新 |
| OwnedSkillEntry | 不改 |
| 页面跳转按钮（toCraftBtn/toSkillTreeBtn） | 不改 |

---

## 七、实施步骤

| 步骤 | 内容 | 方式 |
|------|------|------|
| 1 | SkillListEntry.cs 加拖拽接口+字段+Setup签名扩展 | programer |
| 2 | SkillConfigUI.cs 删弹窗代码+4个ChangeBtn+AddListener | programer |
| 3 | 新建 DropZone.cs | programer |
| 4 | 删除 SkillPickerItem.cs | programer |
| 5 | Scene中删 skillSelectPopup GameObject | saika (编辑器) |
| 6 | Scene中每个HUD槽位添加 DropZone 子对象+挂脚本+设hudIndex | saika (编辑器) |
| 7 | SkillListEntry Prefab 加 CanvasGroup 组件 | saika (编辑器) |
| 8 | 验证：左栏拖拽到4个槽位，装备+刷新 | tester |
