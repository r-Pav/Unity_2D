# SkillConfigUI 拖拽改造方案

## 一、改造总览

| 改造项 | 说明 |
|--------|------|
| 删除弹窗交互 | 移除 skillSelectPopup / popupCloseBtn / SkillPickerItem.cs |
| 删除 4 个 ChangeBtn | 移除 slot0~3ChangeBtn 字段及绑定逻辑 |
| 左栏拖拽 → HUD 槽 | 装备技能 |
| HUD 槽间拖拽 | 交换技能 |
| HUD 槽 → 空白区 | 卸载技能 |
| SkillPool | **不改** |

---

## 二、文件级别改动

| 文件 | 操作 |
|------|------|
| `Assets/Scripts/UI/SkillConfigUI.cs` | 大幅修改 |
| `Assets/Scripts/UI/SkillListEntry.cs` | 修改：增加拖拽源 |
| `Assets/Scripts/UI/SkillPickerItem.cs` | **删除** |
| `Assets/Scripts/UI/SkillHudSlot.cs` | **新增** |
| `Assets/Scripts/Skills/SkillPool.cs` | **不改** |

---

## 三、SkillConfigUI.cs 改动明细

### 3.1 删除的 SerializeField 字段（共9个）

```csharp
// --- 以下全部删除 ---
[Header("HUD Slot 0 (Q)")]
[SerializeField] private Button slot0ChangeBtn;   // 删除

[Header("HUD Slot 1 (E)")]
[SerializeField] private Button slot1ChangeBtn;   // 删除

[Header("HUD Slot 2 (R)")]
[SerializeField] private Button slot2ChangeBtn;   // 删除

[Header("HUD Slot 3 (F)")]
[SerializeField] private Button slot3ChangeBtn;   // 删除

[Header("技能选择弹窗")]
[SerializeField] private GameObject skillSelectPopup;      // 删除
[SerializeField] private Transform popupListContainer;     // 删除
[SerializeField] private GameObject popupItemPrefab;       // 删除
[SerializeField] private Button popupCloseBtn;             // 删除
```

### 3.2 保留的 SerializeField 字段（20个 slot 显示字段 + 3个左栏字段 + 5个页面跳转字段）

Slot 显示字段全部保留，用于 SkillHudSlot 组件的 Inspector 绑定：
- `slot0~3KeyLabel` (TMP_Text) ×4
- `slot0~3Icon` (Image) ×4
- `slot0~3Name` (TMP_Text) ×4
- `slot0~3Level` (TMP_Text) ×4

左栏字段保留：`skillListContainer`, `skillListItemPrefab`, `emptyHint`

页面跳转字段保留：`toCraftBtn`, `toSkillTreeBtn`, `panelManager`, `craftPanel`, `skillTreePanel`

### 3.3 新增 SerializeField 字段

```csharp
[Header("HUD 槽位组件")]
[Tooltip("4 个 HUD 槽位对象，每个需挂 SkillHudSlot 组件。按 Q/E/R/F 顺序拖入")]
[SerializeField] private SkillHudSlot[] hudSlots = new SkillHudSlot[4];

[Header("拖拽设置")]
[Tooltip("拖拽幽灵的父容器（通常是 Canvas 根节点或本面板根节点）")]
[SerializeField] private RectTransform dragGhostParent;

[Header("卸载区域")]
[Tooltip("拖拽 HUD 技能到此区域 = 卸载。若为空则使用面板背景作为卸载区")]
[SerializeField] private RectTransform unequipDropZone;
```

### 3.4 删除的私有字段

```csharp
private int pendingHudSlot = -1;  // 删除
```

### 3.5 删除的方法

```
- OpenSkillPicker(int hudSlotIndex)     // 整方法删除
- CloseSkillPicker()                     // 整方法删除
- OnSkillSelected(string skillId)        // 整方法删除
- FillPickerItemFallback(...)            // 整方法删除
- SetSlotDisplay(int, OwnedSkillEntry)   // 整方法删除（改由 SkillHudSlot 负责）
```

### 3.6 修改的方法

**Awake()**：
```csharp
// 删除：4 个 ChangeBtn 绑定
// 删除：popupCloseBtn 绑定
// 删除：skillSelectPopup.SetActive(false)
// 新增：初始化 hudSlots 的 hudIndex 和 SkillConfigUI 引用
for (int i = 0; i < hudSlots.Length; i++)
{
    if (hudSlots[i] != null)
    {
        hudSlots[i].Initialize(i, this);
    }
}
// 保留：页面跳转按钮绑定（不变）
```

**RefreshRightSlots()**：
```csharp
// 原来：for (int i = 0; i < 4; i++) RefreshHudSlot(i);
// 改为：
if (hudSlots == null) return;
for (int i = 0; i < hudSlots.Length; i++)
{
    if (hudSlots[i] != null)
        hudSlots[i].RefreshFromPool(skillPool);
}
```

**RefreshHudSlot(int index)**：
```csharp
// 原来：SetSlotDisplay(index, ownedSkill);
// 改为：
if (hudSlots != null && index >= 0 && index < hudSlots.Length && hudSlots[index] != null)
    hudSlots[index].RefreshFromPool(skillPool);
```

**SetSlotDisplay(int, OwnedSkillEntry)**：整方法删除。显示逻辑移入 SkillHudSlot。

### 3.7 新增方法

```csharp
/// <summary>
/// 由 SkillHudSlot 在接收到 drop 时回调。
/// 左栏技能拖入 → 装备；HUD 槽间拖入 → 交换。
/// </summary>
public void HandleSkillDrop(int targetSlotIndex, string skillId, SkillHudSlot sourceSlot)
{
    if (skillPool == null) return;

    if (sourceSlot != null)
    {
        // 来源是另一个 HUD 槽位 → 交换
        int sourceIndex = sourceSlot.HudIndex;
        if (sourceIndex == targetSlotIndex) return;

        string targetSkillId = skillPool.GetHudAssignments()[targetSlotIndex];
        string sourceSkillId = skillPool.GetHudAssignments()[sourceIndex];

        // 清空两个槽位，再分别装备（避免 EquipToHud 的自动移除逻辑干扰）
        skillPool.ClearHudSlot(sourceIndex);
        skillPool.ClearHudSlot(targetSlotIndex);

        if (!string.IsNullOrEmpty(sourceSkillId))
            skillPool.EquipToHud(targetSlotIndex, sourceSkillId);
        if (!string.IsNullOrEmpty(targetSkillId))
            skillPool.EquipToHud(sourceIndex, targetSkillId);
    }
    else
    {
        // 来源是左栏 → 装备
        skillPool.EquipToHud(targetSlotIndex, skillId);
    }
}

/// <summary>
/// 由 SkillHudSlot 在被拖到空白区时回调。
/// </summary>
public void HandleSkillUnequip(int hudSlotIndex)
{
    skillPool?.ClearHudSlot(hudSlotIndex);
}
```

### 3.8 不再需要 using

```csharp
// 删除 using System.Linq（如果只为弹窗使用）
```

---

## 四、SkillListEntry.cs 改动明细

### 4.1 新增 using

```csharp
using UnityEngine.EventSystems;
```

### 4.2 新增接口实现

```csharp
public class SkillListEntry : MonoBehaviour, IBeginDragHandler, IDragHandler, IEndDragHandler
```

### 4.3 新增 SerializeField

```csharp
[Header("拖拽")]
[SerializeField] private CanvasGroup canvasGroup;
[SerializeField] private float dragAlpha = 0.6f;
```

### 4.4 新增私有字段

```csharp
private OwnedSkillEntry _entry;                     // 当前显示的技能条目
private GameObject _dragGhost;                       // 拖拽时跟随鼠标的幽灵对象
private RectTransform _dragGhostRect;
```

### 4.5 修改 Setup()

```csharp
public void Setup(OwnedSkillEntry entry)
{
    _entry = entry;  // ← 新增：保存引用供拖拽使用
    
    // ... 原有逻辑不变（icon/nameText/levelText 填充）...
}
```

### 4.6 新增方法（拖拽接口实现）

```csharp
public void OnBeginDrag(PointerEventData eventData)
{
    if (_entry == null || _entry.skillData == null) return;

    // 禁用自身射线，确保能 hit 到下方的 drop 目标
    if (canvasGroup != null)
    {
        canvasGroup.alpha = dragAlpha;
        canvasGroup.blocksRaycasts = false;
    }

    // 创建拖拽幽灵（简化版：仅图标+名字）
    _dragGhost = CreateDragGhost();
    if (_dragGhost != null)
    {
        _dragGhost.transform.SetParent(GetCanvasRoot(), false);
        _dragGhost.transform.SetAsLastSibling();
    }
}

public void OnDrag(PointerEventData eventData)
{
    if (_dragGhostRect != null)
        _dragGhostRect.position = eventData.position;
}

public void OnEndDrag(PointerEventData eventData)
{
    // 恢复射线
    if (canvasGroup != null)
    {
        canvasGroup.alpha = 1f;
        canvasGroup.blocksRaycasts = true;
    }

    // 销毁幽灵
    if (_dragGhost != null)
    {
        Destroy(_dragGhost);
        _dragGhost = null;
        _dragGhostRect = null;
    }
}
```

### 4.7 新增辅助方法

```csharp
/// <summary>创建一个跟随鼠标的半透明技能图标</summary>
private GameObject CreateDragGhost()
{
    var ghost = new GameObject("DragGhost", typeof(RectTransform), typeof(CanvasGroup), typeof(Image));
    _dragGhostRect = ghost.GetComponent<RectTransform>();
    _dragGhostRect.sizeDelta = new Vector2(64, 64);

    var img = ghost.GetComponent<Image>();
    var active = _entry.skillData as ActiveSkillData;
    img.sprite = active != null ? active.GetIconForLevel(_entry.level) : _entry.skillData.icon;
    img.raycastTarget = false;

    var cg = ghost.GetComponent<CanvasGroup>();
    cg.alpha = 0.7f;
    cg.blocksRaycasts = false;

    return ghost;
}

/// <summary>向上查找 Canvas 根节点</summary>
private Transform GetCanvasRoot()
{
    var canvas = GetComponentInParent<Canvas>();
    return canvas != null ? canvas.transform : transform.root;
}
```

---

## 五、SkillPickerItem.cs — 整文件删除

连同对应的 `.meta` 文件一起删除。

---

## 六、新增 SkillHudSlot.cs 完整规格

### 6.1 文件路径

`Assets/Scripts/UI/SkillHudSlot.cs`

### 6.2 职责

1. 显示一个 HUD 槽位的技能信息（图标/名字/等级/快捷键标签）
2. 作为拖拽源：可把已装备技能拖出到其他槽位或卸载区
3. 作为拖拽目标：接受来自左栏技能条目或其他 HUD 槽位的拖入

### 6.3 完整代码

```csharp
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;

/// <summary>
/// [P7.1] HUD 槽位组件 — 挂在 SkillConfigPanel 右栏每个 Slot 根节点上。
/// 负责：显示当前装备技能 + 拖拽源（拖出） + 拖拽目标（接受拖入）。
/// </summary>
public class SkillHudSlot : MonoBehaviour, IBeginDragHandler, IDragHandler, IEndDragHandler, IDropHandler
{
    // ============================================================
    // Inspector 绑定
    // ============================================================

    [Header("显示元素")]
    [SerializeField] private TMP_Text keyLabel;
    [SerializeField] private Image icon;
    [SerializeField] private TMP_Text nameText;
    [SerializeField] private TMP_Text levelText;

    [Header("空槽视觉")]
    [SerializeField] private Image emptySlotBackground;  // 可选，空槽时高亮/显示+号
    [SerializeField] private Color emptyColor = new Color(0.3f, 0.3f, 0.3f, 0.5f);
    [SerializeField] private Color filledColor = new Color(0.15f, 0.15f, 0.25f, 0.8f);
    [SerializeField] private Color dropHighlightColor = new Color(0.3f, 0.5f, 0.3f, 0.6f);

    [Header("拖拽")]
    [SerializeField] private CanvasGroup canvasGroup;
    [SerializeField] private float dragAlpha = 0.6f;

    [Header("快捷键标签")]
    [SerializeField] private string keyLabelString = "Q";  // 在 Inspector 中设为 Q/E/R/F

    // ============================================================
    // 运行时状态
    // ============================================================

    public int HudIndex { get; private set; } = -1;
    public OwnedSkillEntry CurrentEntry { get; private set; }

    private SkillConfigUI _configUI;
    private GameObject _dragGhost;
    private RectTransform _dragGhostRect;
    private static SkillHudSlot _currentDragSource;  // 跨槽位拖拽的源（静态共享）

    // ============================================================
    // 初始化
    // ============================================================

    /// <summary>由 SkillConfigUI.Awake 调用，绑定索引和父面板引用</summary>
    public void Initialize(int hudIndex, SkillConfigUI configUI)
    {
        HudIndex = hudIndex;
        _configUI = configUI;
        if (keyLabel != null) keyLabel.text = keyLabelString;
    }

    // ============================================================
    // 显示刷新（由 SkillConfigUI 驱动）
    // ============================================================

    /// <summary>从 SkillPool 拉取当前槽位数据并刷新显示</summary>
    public void RefreshFromPool(SkillPool pool)
    {
        if (pool == null) return;
        CurrentEntry = pool.GetHudSkill(HudIndex);
        UpdateDisplay();
    }

    private void UpdateDisplay()
    {
        bool hasSkill = CurrentEntry != null && CurrentEntry.skillData != null;

        if (icon != null)
        {
            if (hasSkill)
            {
                var active = CurrentEntry.skillData as ActiveSkillData;
                icon.sprite = active != null ? active.GetIconForLevel(CurrentEntry.level) : CurrentEntry.skillData.icon;
                icon.enabled = true;
            }
            else
            {
                icon.sprite = null;
                icon.enabled = false;
            }
        }

        if (nameText != null)
            nameText.text = hasSkill ? CurrentEntry.skillData.skillName : "空";

        if (levelText != null)
            levelText.text = hasSkill ? $"Lv{CurrentEntry.level}" : "";

        if (emptySlotBackground != null)
            emptySlotBackground.color = hasSkill ? filledColor : emptyColor;
    }

    /// <summary>拖入悬停时的高亮效果</summary>
    public void SetDropHighlight(bool active)
    {
        if (emptySlotBackground != null)
            emptySlotBackground.color = active ? dropHighlightColor
                : (CurrentEntry != null ? filledColor : emptyColor);
    }

    // ============================================================
    // 拖拽源（拖出）— IBeginDragHandler / IDragHandler / IEndDragHandler
    // ============================================================

    public void OnBeginDrag(PointerEventData eventData)
    {
        if (CurrentEntry == null || CurrentEntry.skillData == null) return;

        _currentDragSource = this;

        // 禁用自身射线
        if (canvasGroup != null)
        {
            canvasGroup.alpha = dragAlpha;
            canvasGroup.blocksRaycasts = false;
        }

        // 创建拖拽幽灵
        _dragGhost = CreateDragGhost();
        if (_dragGhost != null)
        {
            _dragGhost.transform.SetParent(GetCanvasRoot(), false);
            _dragGhost.transform.SetAsLastSibling();
        }
    }

    public void OnDrag(PointerEventData eventData)
    {
        if (_dragGhostRect != null)
            _dragGhostRect.position = eventData.position;
    }

    public void OnEndDrag(PointerEventData eventData)
    {
        // 恢复射线
        if (canvasGroup != null)
        {
            canvasGroup.alpha = 1f;
            canvasGroup.blocksRaycasts = true;
        }

        // 销毁幽灵
        if (_dragGhost != null)
        {
            Destroy(_dragGhost);
            _dragGhost = null;
            _dragGhostRect = null;
        }

        // 检查是否拖到了有效目标
        // 如果没有有效的 drop 目标 → 视为卸载
        if (_currentDragSource == this)
        {
            var targetSlot = GetDropTarget(eventData);
            if (targetSlot == null)
            {
                // 没有命中任何 HUD 槽 → 卸载
                _configUI?.HandleSkillUnequip(HudIndex);
            }
        }

        _currentDragSource = null;
    }

    // ============================================================
    // 拖拽目标（接受拖入）— IDropHandler
    // ============================================================

    public void OnDrop(PointerEventData eventData)
    {
        SetDropHighlight(false);

        // 检查拖入来源类型
        var listEntry = eventData.pointerDrag?.GetComponent<SkillListEntry>();
        if (listEntry != null)
        {
            // 来源：左栏技能列表条目
            var entry = GetSkillEntryFromListEntry(listEntry);
            if (entry != null)
                _configUI?.HandleSkillDrop(HudIndex, entry.id, null);
            return;
        }

        var sourceSlot = eventData.pointerDrag?.GetComponent<SkillHudSlot>();
        if (sourceSlot != null && sourceSlot != this)
        {
            // 来源：另一个 HUD 槽位 → 交换
            if (sourceSlot.CurrentEntry != null)
                _configUI?.HandleSkillDrop(HudIndex, sourceSlot.CurrentEntry.id, sourceSlot);
            return;
        }
    }

    // ============================================================
    // 高亮反馈（悬停检测）
    // ============================================================

    // 注意：Unity 不直接支持 IDropHandler 的 hover 检测。
    // 需要在 SkillConfigUI 中额外实现 IPointerEnterHandler/IPointerExitHandler，
    // 或者在 Update 中检测 pointerEnter。这里通过静态源配合实现：

    private void Update()
    {
        // 如果当前有 SkillHudSlot 正在拖拽，检测鼠标是否在本槽上
        if (_currentDragSource != null && _currentDragSource != this)
        {
            bool hovering = RectTransformUtility.RectangleContainsScreenPoint(
                (RectTransform)transform, Input.mousePosition, null);
            SetDropHighlight(hovering);
        }
    }

    // ============================================================
    // 辅助方法
    // ============================================================

    private GameObject CreateDragGhost()
    {
        var ghost = new GameObject("DragGhost", typeof(RectTransform), typeof(CanvasGroup), typeof(Image));
        _dragGhostRect = ghost.GetComponent<RectTransform>();
        _dragGhostRect.sizeDelta = new Vector2(64, 64);

        var img = ghost.GetComponent<Image>();
        if (CurrentEntry?.skillData != null)
        {
            var active = CurrentEntry.skillData as ActiveSkillData;
            img.sprite = active != null ? active.GetIconForLevel(CurrentEntry.level) : CurrentEntry.skillData.icon;
        }
        img.raycastTarget = false;

        var cg = ghost.GetComponent<CanvasGroup>();
        cg.alpha = 0.7f;
        cg.blocksRaycasts = false;

        return ghost;
    }

    private Transform GetCanvasRoot()
    {
        var canvas = GetComponentInParent<Canvas>();
        return canvas != null ? canvas.transform : transform.root;
    }

    /// <summary>检查拖拽结束时鼠标下方是否有有效的 HUD 槽位</summary>
    private SkillHudSlot GetDropTarget(PointerEventData eventData)
    {
        if (eventData.pointerEnter == null) return null;
        return eventData.pointerEnter.GetComponentInParent<SkillHudSlot>();
    }

    /// <summary>从 SkillListEntry 获取其绑定的 OwnedSkillEntry</summary>
    private OwnedSkillEntry GetSkillEntryFromListEntry(SkillListEntry entry)
    {
        // 使用反射或公开属性。建议在 SkillListEntry 中添加 public 属性。
        var prop = typeof(SkillListEntry).GetProperty("CurrentEntry",
            System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
        return prop?.GetValue(entry) as OwnedSkillEntry;
    }
}
```

### 6.4 关键设计说明

| 设计点 | 说明 |
|--------|------|
| `_currentDragSource` (static) | 跨槽位共享拖拽源，用于 Update 中检测 hover 高亮 |
| `GetDropTarget()` | 在 OnEndDrag 中检查 pointerEnter 是否有 SkillHudSlot 组件，没有 = 卸载 |
| `GetSkillEntryFromListEntry()` | 通过反射读 SkillListEntry 的公开属性；更好的方案是在 SkillListEntry 上加 `public OwnedSkillEntry CurrentEntry` |
| `Update()` 高亮 | 利用静态源，在拖拽过程中检测鼠标是否在本槽位范围内 |

---

## 七、SkillListEntry.cs 补充 — 暴露公开属性

在 SkillListEntry 中新增一行：

```csharp
/// <summary>当前显示的技能条目（供 SkillHudSlot 读取）</summary>
public OwnedSkillEntry CurrentEntry => _entry;
```

这样 SkillHudSlot 的 `GetSkillEntryFromListEntry()` 可以简化，不再需要反射。

---

## 八、场景 (Scene/Hierarchy) 改动

### 8.1 删除节点

```
Canvas/SkillConfigPanel/SkillSelectPopup  ← 整节点删除（含 PopupListContainer, PopupItemPrefab, PopupCloseBtn）
```

### 8.2 删除现有组件/子节点

| 路径 | 操作 |
|------|------|
| `.../Slot0_Q/ChangeBtn` | 删除子节点 |
| `.../Slot1_E/ChangeBtn` | 删除子节点 |
| `.../Slot2_R/ChangeBtn` | 删除子节点 |
| `.../Slot3_F/ChangeBtn` | 删除子节点 |

### 8.3 为 4 个 Slot 根节点添加/修改组件

对每个 Slot（`Slot0_Q` / `Slot1_E` / `Slot2_R` / `Slot3_F`）：

| 操作 | 说明 |
|------|------|
| 添加 `SkillHudSlot` 组件 | 新增脚本 |
| 添加 `CanvasGroup` 组件 | 拖拽时控制透明度+射线阻断 |
| 确保有 `Image` 组件 | 作为空槽背景（若已有空槽背景 Image 则复用） |

逐个挂载 SkillHudSlot 的 Inspector 字段：

```
Slot0_Q (挂 SkillHudSlot)
├── KeyLabel (TMP_Text)       → keyLabel
├── Icon (Image)              → icon
├── Name (TMP_Text)           → nameText
├── Level (TMP_Text)          → levelText
├── SlotBackground (Image)    → emptySlotBackground (可选)
└── keyLabelString = "Q"
```

Slot1_E / Slot2_R / Slot3_F 同理，keyLabelString 分别设为 "E"/"R"/"F"。

### 8.4 为 SkillListItemPrefab 添加组件

| 操作 | 说明 |
|------|------|
| 添加 `CanvasGroup` 组件 | 拖拽时控制透明度+射线阻断 |

在 Inspector 中将 `CanvasGroup` 拖入 `SkillListEntry` 的 `canvasGroup` 字段。

### 8.5 更新 SkillConfigUI 组件（Canvas/SkillConfigPanel）

**删除字段绑定**（清空以下 Inspector 引用）：
- `Slot0~3 ChangeBtn` ×4
- `Skill Select Popup`
- `Popup List Container`
- `Popup Item Prefab`
- `Popup Close Btn`

**新增字段绑定**：
| 字段 | 拖入对象 |
|------|---------|
| `Hud Slots` → Size=4 | |
| Element 0 | `Canvas/.../Slot0_Q` |
| Element 1 | `Canvas/.../Slot1_E` |
| Element 2 | `Canvas/.../Slot2_R` |
| Element 3 | `Canvas/.../Slot3_F` |
| `Drag Ghost Parent` | `Canvas/SkillConfigPanel`（或 Canvas 根节点） |
| `Unequip Drop Zone` | 可新建一个底部空白区域 Image，或留空使用面板自身 |

### 8.6 （可选）新建卸载提示区

在 `Canvas/SkillConfigPanel` 底部新建：

```
UnequipZone (Image + 文字"拖到此处卸载")
```

将其 `RectTransform` 拖入 SkillConfigUI 的 `Unequip Drop Zone` 字段。

> 若不留这个字段，SkillHudSlot 的 OnEndDrag 逻辑会自动将「未命中任何 HUD 槽」的 drop 视为卸载。

---

## 九、拖拽交互规则总结

| 操作 | 触发条件 | 结果 | 实现位置 |
|------|---------|------|---------|
| 装备 | 左栏条目拖到 HUD 槽 | `SkillPool.EquipToHud(targetSlot, skillId)` | `SkillConfigUI.HandleSkillDrop` |
| 交换 | HUD 槽 A 拖到 HUD 槽 B | 清空 A+B → 互装对方技能 | `SkillConfigUI.HandleSkillDrop` |
| 卸载 | HUD 槽拖到空白区（无目标） | `SkillPool.ClearHudSlot(hudIndex)` | `SkillConfigUI.HandleSkillUnequip` |
| 无效 | 左栏条目拖到空白区 | 无操作（幽灵消失） | `SkillListEntry.OnEndDrag` |
| 无效 | 左栏条目拖到左栏 | 无操作（左栏无 IDropHandler） | — |
| 无效 | 空槽 HUD 拖拽 | OnBeginDrag 直接 return | `SkillHudSlot.OnBeginDrag` |
| 冲突 | 装备已在其他槽的技能 | EquipToHud 自动从旧槽移除 | `SkillPool.EquipToHud`（已有逻辑） |

---

## 十、开发实施顺序建议

| 步骤 | 内容 | 预估工作量 |
|------|------|-----------|
| 1 | 新建 `SkillHudSlot.cs`，完成所有逻辑 | 主力 |
| 2 | 修改 `SkillListEntry.cs`（加拖拽接口 + CurrentEntry 属性） | 小 |
| 3 | 修改 `SkillConfigUI.cs`（删弹窗/ChangeBtn，改 Refresh，加 HandleSkillDrop/Unequip） | 中 |
| 4 | 场景操作：删弹窗、删 ChangeBtn、挂 SkillHudSlot+CanvasGroup、连线 Inspector | 中 |
| 5 | 删除 `SkillPickerItem.cs` + .meta | 微小 |
| 6 | 测试：装备/交换/卸载三种拖拽，边界情况（拖到自身、空拖、ScrollRect 干扰） | 验证 |

---

## 十一、潜在风险与注意

1. **ScrollRect 冲突**：左栏 `skillListContainer` 在 ScrollView 内。当 SkillListEntry 实现 IBeginDragHandler 后，Unity EventSystem 会优先将拖拽事件分发给条目而非 ScrollRect，自动解决冲突。无需额外处理。

2. **EventSystem 存在性**：确保场景中有 `EventSystem` + `StandaloneInputModule`。Unity 默认创建 Canvas 时自动添加，通常无需操作。

3. **CanvasGroup 丢失**：若 SkillListItemPrefab 未挂 CanvasGroup，拖拽时无法阻断射线，导致幽灵下方仍然能触发 ScrollRect。必须在 Prefab 上添加。

4. **拖拽跨 Canvas**：若 SkillSelectPopup 原本在另一个 Canvas 下，删除后不存在此问题。当前所有拖拽元素在同一 Canvas（SkillConfigPanel）下，无跨 Canvas 问题。

5. **SkillHudSlot 静态引用**：`_currentDragSource` 是 static，同时只能有一个拖拽源。这符合使用场景（单指触摸/单鼠标），不需要支持多点同时拖拽。

6. **空槽不可拖出**：`OnBeginDrag` 检查 `CurrentEntry == null` 时直接 return，符合设计。

7. **刷新时机**：SkillPool 的 `OnPoolChanged` / `OnHudSlotChanged` 事件触发后 → SkillConfigUI.OnEnable 中订阅 → 自动调用 RefreshAll → 驱动所有 SkillHudSlot.RefreshFromPool。改造后此链路不变。
