# PanelManager + P键技能面板打开方案

> 文档类型：策划案（方案 → saika 确认 → 推 programmer 实现）
> 产出日期：2026-07-15
> 目标：建立 Unity UI 面板管理系统 + P/Esc 快捷键

---

## 1. 现状分析

### 1.1 当前 UI 架构

项目**没有**统一的 UI 管理框架。UI 面板各自独立管理：

| 脚本 | 面板 GameObject | 显示/隐藏方式 |
|------|----------------|--------------|
| `SkillTreeUI` | SkillTreePanel | OnEnable / OnDisable（订阅事件） |
| `PassiveUI` | PassivePanel | OnEnable / OnDisable |
| `PlayerHUD` | HUD | 始终显示，订阅事件驱动更新 |
| `BranchChoiceDialog` | BranchChoiceDialog | Hide() / Show() 自管理 |
| `LineSelectDialog` | LineSelectDialog | Hide() / Show() 自管理 |
| `CraftUI` | CraftPanel | 自管理 |
| `CraftConfirmDialog` | CraftConfirmDialog | 自管理 |

**痛点：**
- 没有面板栈，不知道谁在最上层
- 没有统一的打开/关闭逻辑，Esc 无法实现「关闭当前最上层面板」
- 面板间可能有重叠冲突（两个面板同时开），但没有规则约束
- 没有游戏暂停/输入屏蔽机制

### 1.2 当前按键使用情况

| 按键 | 位置 | 用途 |
|------|------|------|
| `Q` | `SkillData.hotkey` → `SkillManager.CheckHotkeys()` | 技能槽0 快捷键（能量球） |
| `E` | `SkillData.hotkey` → `SkillManager.CheckHotkeys()` | 技能槽1 快捷键（冲进步） |
| `Space` | `PlayerController.TryEnterWallState()` | 墙跳 |
| 方向键/WASD | `Input.GetAxisRaw` / `Input.GetKey` | 移动/爬墙 |
| `P` | **未使用** | — |
| `Esc` | **未使用** | — |

`SkillManager.CheckHotkeys()` 遍历 `skillSlots[]` 按 `slot.data.hotkey` 激活技能——面板打开时若不去检查，Q/E 会在技能面板打开时误触技能。

### 1.3 目标场景 Hierarchy（参考 Editor_Setup.md §6）

```
Canvas
├── HUD                       ← 始终显示
├── SkillTreePanel             ← P键打开，Esc关闭
├── PassivePanel               ← 后续扩展，独立面板
├── CraftPanel                 ← 后续扩展，独立面板
├── BranchChoiceDialog         ← 弹出层（由技能树内部触发）
├── LineSelectDialog           ← 弹出层（由被动面板内部触发）
└── CraftConfirmDialog / ...   ← 其他弹出层
```

---

## 2. PanelManager 系统设计

### 2.1 核心概念

```
PanelManager（单例 MonoBehaviour，挂 Canvas 顶层）
  ├── 面板栈（Stack<PanelEntry>）  ← 记录打开顺序
  │     ├── 弹出层（Dialog = 不推栈、独立管理）
  │     └── 面板（Panel = 推栈、Esc逐层关闭）
  ├── 按键监听（P / Esc）
  └── 游戏暂停/输入屏蔽控制
```

**两层分类：**

| 类型 | 说明 | 例子 | 行为 |
|------|------|------|------|
| **Panel** | 全屏/大面积覆盖面板 | SkillTreePanel, PassivePanel, CraftPanel | 推入面板栈，Esc 逐层关闭。同时只能有一个 Panel 打开。 |
| **Dialog** | 小弹窗/确认框 | BranchChoiceDialog, LineSelectDialog | 不推栈。由所属 Panel 内部管理生命周期。Panel 关闭时联动关闭其 Dialog。 |

### 2.2 PanelManager 接口设计

```csharp
public class PanelManager : MonoBehaviour
{
    // === 面板管理 ===

    /// <summary>打开一个 Panel。传入面板 GameObject。自动关闭当前已打开的 Panel（如有）。</summary>
    /// <returns>是否成功打开（面板切换：旧 Panel 被关闭，新 Panel 打开）</returns>
    public bool OpenPanel(GameObject panel);

    /// <summary>关闭当前最上层 Panel。弹出层（Dialog）优先关闭。</summary>
    /// <returns>是否有关闭操作（false = 已无面板可关）</returns>
    public bool CloseTopPanel();

    /// <summary>强制关闭所有面板和弹出层（场景切换/存档加载时用）</summary>
    public void CloseAll();

    /// <summary>注册一个 Dialog，P键/面板切换时不冲突</summary>
    public void RegisterDialog(GameObject dialog);
    public void UnregisterDialog(GameObject dialog);

    // === 输入控制 ===

    /// <summary>是否有面板打开（影响游戏暂停和输入屏蔽决策）</summary>
    public bool IsAnyPanelOpen { get; }

    /// <summary>设置全局输入屏蔽（禁止移动/技能/跳跃等）</summary>
    public bool InputBlocked { get; set; }

    // === 快捷键 ===

    [SerializeField] private KeyCode skillTreeKey = KeyCode.P;    // 打开技能面板
    [SerializeField] private KeyCode closePanelKey = KeyCode.Escape; // 关闭面板

    [SerializeField] private GameObject skillTreePanel;    // Inspector 拖入 SkillTreePanel
}
```

### 2.3 按键处理流程

#### P 键按下

```
Update() → Input.GetKeyDown(KeyCode.P)
  │
  ├─ SkillTreePanel 已打开？
  │   └─ YES → 关闭 SkillTreePanel（toggle 行为）
  │
  └─ SkillTreePanel 未打开？
      ├─ 已有其他 Panel 打开？
      │   └─ YES → 不处理（面板互斥，不允许覆盖）
      │          或：先关闭当前面板，再打开 SkillTreePanel
      │          **→ 建议：互斥策略，P键有面板时无效（防止覆盖关键操作面板）**
      │
      └─ NO → OpenPanel(skillTreePanel)
          ├─ 设置 InputBlocked = true（暂停移动/技能/跳跃）
          └─ Time.timeScale = 0？（可选：暂停游戏）
```

#### Esc 键按下

```
Update() → Input.GetKeyDown(KeyCode.Escape)
  │
  ├─ Dialog 打开？
  │   └─ YES → 关闭当前 Dialog
  │
  ├─ Panel 打开？
  │   └─ YES → CloseTopPanel()
  │       ├─ 恢复 InputBlocked = false
  │       └─ Time.timeScale = 1（如果之前暂停了）
  │
  └─ 都无 → 不做任何事
       （如果后续需要 Esc 打开主菜单/暂停菜单，可以在这里扩展）
```

### 2.4 面板切换规则

```
规则1（互斥）：同一时间只能有一个 Panel 处于打开状态。
  打开 Panel B 时 → 自动关闭 Panel A（OnDisable → OnEnable 切换）

规则2（Esc 逐层）：Esc 先关 Dialog，再关 Panel。
  面板栈为空时 Esc 不做任何事。

规则3（P键 toggle）：P 键对 SkillTreePanel 是 toggle 行为。
  已打开 → 关闭；未打开 → 打开。

规则4（输入屏蔽）：Panel 打开时，InputBlocked = true。
  PlayerController / SkillManager / PlayerJump 等检测 InputBlocked 跳过逻辑。

规则5（Dialog 跟随）：Panel 关闭时，其所属 Dialog 联动关闭。
  由 Panel 的 OnDisable 中调用 Dialog.Hide() 实现。
```

### 2.5 游戏暂停策略

**建议：不暂停游戏（Time.timeScale = 1）**

理由：
- 技能树是 RPG 升级界面，不是系统暂停菜单
- 玩家可能在战斗中想快速加点——暂停破坏战斗节奏
- 只需屏蔽玩家操作输入（`InputBlocked = true`），不暂停物理/动画

**InputBlocked 实现方式：**

在 `PlayerController.OnUpdate()` 开头加一行：
```csharp
protected override void OnUpdate()
{
    if (PanelManager.Instance != null && PanelManager.Instance.InputBlocked)
    {
        // 面板打开时：禁止所有玩家行动
        Move(0f);    // 停止移动
        return;      // 跳过跳跃/墙跳/技能等
    }
    // ... 原有逻辑
}
```

在 `SkillManager.CheckHotkeys()` 开头：
```csharp
private void CheckHotkeys()
{
    if (PanelManager.Instance != null && PanelManager.Instance.InputBlocked)
        return;  // 面板打开时禁止技能快捷键
    // ... 原有逻辑
}
```

### 2.6 SkillTreeUI 增强

现有 `SkillTreeUI` 需要增加 Panel 生命周期接口：

```csharp
// 现有 OnEnable / OnDisable 已处理事件订阅，不需改动
// 需要增加：
public void OnPanelOpened()  // 由 PanelManager 调用
{
    gameObject.SetActive(true);  // 已由 OnEnable 处理
    Refresh();                   // 面板打开时立即刷新
}

public void OnPanelClosed()  // 由 PanelManager 调用
{
    gameObject.SetActive(false);  // 已由 OnDisable 处理
    // 关闭关联的 BranchChoiceDialog
    if (branchChoiceDialog != null)
        branchChoiceDialog.Hide();
}
```

---

## 3. 实现步骤

### Step 1: 创建 PanelManager.cs

| 文件 | 位置 |
|------|------|
| `PanelManager.cs` | `Assets/Scripts/UI/PanelManager.cs` |

### Step 2: 挂载 PanelManager

- 在 Canvas 顶层创建 `PanelManager` GameObject
- 挂 `PanelManager` 脚本
- Inspector 拖入 `SkillTreePanel` GameObject

### Step 3: 修改 PlayerController

- `OnUpdate()` 开头加 `InputBlocked` 检测
- 面板打开时停止移动/跳跃/墙跳

### Step 4: 修改 SkillManager

- `CheckHotkeys()` 开头加 `InputBlocked` 检测

### Step 5: 修改 PlayerJump / PlayerDash

- 各 `OnPlayerUpdate()` 函数开头加 `InputBlocked` 返回

### Step 6: Editor Setup — 场景 Hierarchy

- 确保 `SkillTreePanel` 存在（按 Editor_Setup.md §6.3 结构）
- 初始状态设为 `SetActive(false)`
- 挂好 SkillTreeUI 的所有 SerializeField

### Step 7: 测试验证

- [ ] 按 P → SkillTreePanel 打开，玩家停止移动
- [ ] 按 Esc → SkillTreePanel 关闭，玩家恢复控制
- [ ] 面板打开时，Q/E 不触发技能
- [ ] 再按 P → 面板关闭（toggle）
- [ ] 有 Dialog 弹出时，Esc 先关 Dialog 再关 Panel

---

## 4. 后续扩展点

当前只实现 SkillTreePanel。PanelManager 框架已预留扩展位：

| 面板 | 快捷键 | 备注 |
|------|--------|------|
| PassivePanel | 待定（Tab? B?） | 被动装备面板 |
| CraftPanel | 待定（C?） | 合成面板 |
| 主菜单/暂停菜单 | Esc（栈空时） | 后续扩展 |

只需创建面板 GameObject → 在 PanelManager Inspector 绑定 → 添加按键监听即可。

---

## 5. 风险与注意事项

| 风险 | 缓解 |
|------|------|
| `InputBlocked` 忘记在某些组件中检查 | 用 `PanelManager.Instance.InputBlocked` 统一检查点，代码 review 确保覆盖 |
| SkillTreeUI.OnDisable 中 Unsubscribe 事件 + Panel 关闭时别处还在用同一 GameObject | `OnDisable` 只在 `SetActive(false)` 时触发，正常 |
| Panel 切换时闪烁 | `OpenPanel` 先关旧再开新，毫秒级，无感知 |
| 多个脚本同时按 P 打开不同面板 | PanelManager 单例 + 互斥规则，不会冲突 |
