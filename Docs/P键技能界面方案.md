# P键技能主界面方案 — PanelManager + 快捷键设计

> 策划案 | 只读分析 | 不改代码 | 输出给 saika 确认后再推 programmer

---

## 一、现状分析

### 1.1 已有资产

| 组件 | 位置 | 状态 |
|------|------|------|
| SkillTreeUI.cs | `Assets/Scripts/UI/SkillTreeUI.cs` | 已实现逻辑，挂在 SkillTreePanel 上 |
| SkillTreePanel | Canvas 下（Hierarchy 6.3 规划） | GameObject 存在但从未被 SetActive 切换 |
| BranchChoiceDialog | Canvas 顶层弹窗 | 有 Show/Hide 接口 |
| 事件系统 | EventBus (static) | SkillTreeUI 通过 OnEnable/OnDisable 订阅事件 |
| 输入检测 | PlayerController / SkillManager | 无 P 键检测，无 ESC 检测 |

### 1.2 当前缺失

1. **无 PanelManager** — 没有面板打开/关闭/层叠的统一管理
2. **SkillTreePanel 从未显示** — SkillTreeUI.Awake()/OnEnable() 会执行，但 panel 本身 inactive
3. **无暂停机制** — Time.timeScale 管理缺失
4. **输入未禁用** — 打开面板时玩家仍可移动/跳跃/攻击
5. **ESC 无通用关闭** — 需要统一的"关闭顶层面板"逻辑

### 1.3 按键使用现状

| 按键 | 当前用途 | 侦测位置 |
|------|---------|---------|
| Q | 技能槽 0（能量球） | SkillManager.CheckHotkeys |
| E | 技能槽 1（冲进步） | SkillManager.CheckHotkeys |
| Space | 跳跃/墙跳 | PlayerJump / PlayerController |
| Esc | 未使用 | — |
| P | 未使用 | — |

**P 键可用，ESC 可用，无冲突。**

---

## 二、设计目标

1. P 键 → 打开/关闭 SkillTreePanel（技能主界面）
2. ESC 键 → 关闭当前最上层面板（通用，后续主菜单等共用）
3. 打开 SkillTreePanel 时：
   - 暂停游戏（Time.timeScale = 0）
   - 禁用玩家输入（移动/跳跃/攻击/技能快捷键）
   - 鼠标光标显示
4. 关闭所有面板时恢复游戏

---

## 三、PanelManager 框架设计

### 3.1 组件定位

```
Canvas (已有)
└── PanelManager (新增脚本，挂 Canvas 上)
    负责：面板栈管理 + 按键监听 + 暂停/恢复
```

### 3.2 核心数据结构

```csharp
// 面板类型
public enum PanelType
{
    FullScreen,  // 全屏独占（SkillTree、主菜单）— 打开时关闭其他 FullScreen
    Dialog       // 弹窗叠加（BranchChoice、LineSelect）— 可盖在 FullScreen 上
}

// 面板注册信息（Inspector 配置）
[System.Serializable]
public class PanelEntry
{
    public GameObject panel;       // 面板 GameObject 引用
    public PanelType type;         // 面板类型
    public bool pauseGame;         // 打开时是否暂停游戏
    public KeyCode toggleKey;      // 切换快捷键（0=None，仅用于 FullScreen 面板）
}
```

### 3.3 运行时面板栈

```csharp
// 用 Stack<GameObject> 记录打开顺序
// 栈顶 = 当前最上层面板
private Stack<GameObject> panelStack;
```

### 3.4 公开接口

| 方法 | 用途 |
|------|------|
| OpenPanel(GameObject panel) | 打开面板：压栈 + SetActive(true) + 暂停判断 |
| CloseTopPanel() | 关闭栈顶面板：出栈 + SetActive(false) + 恢复判断 |
| CloseAllPanels() | 一键关闭所有面板 |
| TogglePanel(GameObject panel) | 切换：已在栈顶则关闭，否则打开 |
| IsPanelOpen(GameObject panel) | 查询某面板是否打开 |
| IsAnyPanelOpen | 是否有任何面板打开 |

### 3.5 暂停/恢复逻辑

```
OpenPanel:
  1. 若该 panel.pauseGame = true → Time.timeScale = 0
  2. 禁用 PlayerController 输入（设置 player.InputEnabled = false）
  3. Cursor.visible = true; Cursor.lockState = CursorLockMode.None

CloseTopPanel:
  1. 出栈后若栈为空 → Time.timeScale = 1; player.InputEnabled = true
  2. 若栈非空但栈顶 panel.pauseGame = false → 同上恢复
  3. 若栈非空且栈顶 panel.pauseGame = true → 保持暂停
  4. Cursor.visible = (栈非空); Cursor.lockState = 栈非空 ? None : Locked
```

### 3.6 PanelType 层叠规则

| 操作 | FullScreen | Dialog |
|------|-----------|--------|
| 打开时 | 关闭所有已打开的 FullScreen 面板 | 直接压栈 |
| 被覆盖时 | 保持 inactive（不响应输入） | 同上 |
| ESC 关闭 | 从栈中移除自身 | 从栈中移除自身 |

具体流程：
```
OpenPanel(X):
  if X.type == FullScreen:
    // 先关闭所有已打开的 FullScreen（出栈 + SetActive false）
    while 栈中有 FullScreen 面板:
      close(栈中面板)
    // 再压入 X
  // 无论什么类型都压栈
  panelStack.Push(X)
  X.SetActive(true)
  处理暂停
```

---

## 四、快捷键方案

### 4.1 P 键 — 切换技能面板

```
Update() 中检测:
  if Input.GetKeyDown(KeyCode.P):
    if IsAnyPanelOpen && 栈顶 == skillTreePanel:
      CloseTopPanel()   // 已打开 → 关闭
    else:
      OpenPanel(skillTreePanel)  // 未打开或不在栈顶 → 打开（FullScreen 规则会关闭其他面板）
```

### 4.2 ESC 键 — 关闭顶层面板

```
Update() 中检测:
  if Input.GetKeyDown(KeyCode.Escape):
    if IsAnyPanelOpen:
      CloseTopPanel()
    else:
      // 预留：栈为空时打开主菜单
      // OpenPanel(mainMenuPanel)
```

### 4.3 输入优先级

```
PlayerController.OnUpdate / OnFixedUpdate:
  开头插入:
    if (!InputEnabled) return;  // 面板打开时跳过所有输入

SkillManager.CheckHotkeys:
  开头插入:
    if (!player.InputEnabled) return;  // Q/E 等技能快捷键失效
```

**PlayerController 需要新增字段：**
```csharp
public bool InputEnabled { get; set; } = true;
```

### 4.4 按键不会被 Unity Input System 吞掉

P 键和 ESC 当前无绑定，不会与已有输入冲突。PanelManager 的 Update 在 MonoBehaviour 生命周期中先于 PlayerController 执行（因为挂在 Canvas 上），但为了保证顺序，建议用 `Input.GetKeyDown` 而非 `Input.GetKey`，单帧内谁先检测到谁处理。

---

## 五、PlayerController 改动摘要

仅新增一个开关字段，不改输入逻辑：

```csharp
// 新增字段
public bool InputEnabled { get; set; } = true;

// OnUpdate() 开头加:
protected override void OnUpdate()
{
    if (!InputEnabled) return;  // ← 新增
    // ... 原有逻辑不变
}

// OnFixedUpdate() 开头加:
protected override void OnFixedUpdate()
{
    if (!InputEnabled) return;  // ← 新增
    // ... 原有逻辑不变
}
```

PlayerJump / PlayerDash / PlayerHealth 等子组件也需检查，但因为它们的 OnPlayerUpdate 由 PlayerController.OnUpdate 内部调用，PlayerController 提前 return 后子组件也不会执行，所以只需改 PlayerController 即可。

---

## 六、Scene 层级规划

```
Canvas
├── PanelManager (新增脚本)
│   Inspector 中拖入:
│   ├── panels[0] = SkillTreePanel (type=FullScreen, pause=true, key=P)
│   └── panels[n] = 后续面板...
│
├── HUD (已有)
├── SkillTreePanel (已有, 初始 inactive)
│   └── SkillTreeUI (已有脚本)
├── PassivePanel (各面板初始均为 inactive)
├── CraftPanel
├── BranchChoiceDialog (Dialog 型, 由 SkillTreeUI 内部调用 Show/Hide)
├── LineSelectDialog
└── ConfirmDialog
```

**注意：** BranchChoiceDialog 和 LineSelectDialog 的 Show/Hide 由 SkillTreeUI/PassiveUI 内部管理，不需要经过 PanelManager。但将来也可以把它们注册为 Dialog 类型，统一走 PanelManager，好处是 ESC 能关闭它们。

---

## 七、实现步骤（给 programmer 的执行顺序）

| 步骤 | 内容 | 涉及文件 |
|------|------|---------|
| 1 | 新建 PanelManager.cs | `Assets/Scripts/UI/PanelManager.cs` |
| 2 | PlayerController 加 InputEnabled 开关 | `PlayerController.cs` (加字段 + OnUpdate/OnFixedUpdate 开头 return) |
| 3 | Canvas 挂 PanelManager，Inspector 配置 panels 数组 | SampleScene.scene |
| 4 | SkillTreePanel 设初始 inactive | SampleScene.scene |
| 5 | 测试：P 键打开 → 暂停 + 光标 → ESC 关闭 → 恢复 | Play mode |

---

## 八、扩展预留

PanelManager 设计为通用框架，后续可直接复用：

| 后续面板 | 快捷键 | 类型 |
|---------|--------|------|
| 主菜单 | ESC（栈空时） | FullScreen |
| 被动装备面板 | I 键（示例） | FullScreen |
| 合成面板 | C 键（示例） | FullScreen |
| 设置面板 | 主菜单内打开 | Dialog |

只需在 Inspector 中往 PanelManager.panels 数组加一项即可，不改代码。

---

## 九、设计理由

1. **为什么用 Stack 而非单一 currentPanel？** — 弹窗（BranchChoiceDialog）需要盖在全屏面板上，Stack 天然支持层叠关闭（ESC 先关弹窗再关底层面板）
2. **为什么 PanelManager 挂 Canvas 而非独立 GameObject？** — Canvas 是所有 UI 的根，PanelManager 管理 Canvas 下的子面板最自然，且能保证 Update 在 UI 帧中执行
3. **为什么 InputEnabled 设在 PlayerController 而非 PanelManager 中统一拦截？** — PanelManager 不知道有哪些输入组件，设在 PlayerController 是最小改动、最直接有效的方式
4. **为什么 FullScreen 打开时关闭其他 FullScreen？** — 避免面板叠加堆满屏幕，用户按 ESC 要按很多次才回到游戏。独占模式体验更好
