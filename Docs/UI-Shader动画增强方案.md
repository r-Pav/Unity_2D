# UI Shader 动画增强方案

> 项目: Tuanjie 2D 横板动作 | 渲染管线: Built-in 2D Renderer (非URP)
> 锚点: SampleScene.scene 实际 Hierarchy + `背包装备系统-UI命名规范.md`
> 约束: 不写完整 shader 代码，只给策略 + 伪代码片段 + 实施优先级

---

## 一、现状与约束

### 1.1 渲染管线确认

| 项 | 实际 |
|----|------|
| 引擎 | Tuanjie (yousandi.cn) |
| 渲染管线 | Built-in (com.unity.feature.2d) |
| Shader Graph | 不可用 |
| TMP | 3.0.9，Shaders/ 下自带 TMP 专用 shader |
| 现有自定义 shader | 0 个 |
| 现有 Material 资产 | 0 个 |
| 现有 Glow 元素 | 已存在于 Hierarchy（Image 组件 + Default Material） |

### 1.2 架构影响

- 不能走 URP Shader Graph / URP 2D Renderer 专属特性
- UI shader 必须用 CG/HLSL 手写，走 `UI/Default` 的变体模式
- Material PropertyBlock 完全可用，是主要运行时驱动手段
- Canvas 渲染在 Screen Space - Overlay，走 UI 批次

---

## 二、Shader 文件组织

### 2.1 新建目录

```
Assets/
└── Shaders/
    ├── UI/
    │   ├── UI-RarityGlow.shader          # 稀有度边框发光
    │   ├── UI-Pulse.shader               # 选中脉冲/呼吸
    │   ├── UI-Scanline.shader            # 流光扫描线
    │   ├── UI-Dissolve.shader            # 面板溶解过渡
    │   ├── UI-OutlineGlow.shader         # 悬停高亮描边
    │   └── UI-BlurBackground.shader      # 毛玻璃背景
    └── Materials/
        ├── Mat_RarityCommon.mat
        ├── Mat_RarityRare.mat
        ├── Mat_RarityEpic.mat
        ├── Mat_RarityLegendary.mat
        ├── Mat_Pulse.mat
        ├── Mat_Scanline.mat
        ├── Mat_Dissolve.mat
        ├── Mat_OutlineGlow.mat
        └── Mat_BlurBG.mat
```

### 2.2 Material 共用策略

| Material | 共享范围 | 运行时区分方式 |
|----------|---------|---------------|
| Mat_RarityCommon/Rare/Epic/Legendary | 所有 ItemCell + EquipmentSlot | 同稀有度共用一个 Material，PropertyBlock 调色 |
| Mat_Pulse | 当前选中的 ItemCell (1个) | PropertyBlock 驱动 `_PulsePhase` |
| Mat_Scanline | QuickSlot_0/1 冷却中 | PropertyBlock 驱动 `_CooldownT` |
| Mat_Dissolve | InventoryPanel / WarehousePanel 背景 | PropertyBlock 驱动 `_DissolveAmount` |
| Mat_OutlineGlow | 悬停中的 EquipmentSlot (最多1个) | PropertyBlock 驱动 `_GlowColor` |
| Mat_BlurBG | 面板背景各一个 | 静态配置，不需要运行时改 |

**材质总数: 4 + 1 + 1 + 1 + 1 + 2 = 10 个 Material**。26 个 ItemCell + 4 个 EquipmentSlot 共享 4 个稀有度材质，不产生额外批次打断（同材质合批）。

---

## 三、逐元素 Shader 方案

### 3.1 装备槽位 (Slot_Weapon / Slot_Armor / Slot_Accessory_0 / Slot_Accessory_1)

现有结构: Slot_* (Image) → Icon (Image) + Glow (Image)

#### 3.1.1 装备放入动画

**效果**: 物品图标从拖拽释放点缩放到槽位，带发光一闪

**实现**: 纯 C# 动画，不需要 shader
- DoTween: icon.rectTransform.DOScale(1.2→1.0, 0.3s).SetEase(OutBack)
- 短暂切换 Glow Image 的 Material 到 Mat_Pulse，闪一次后切回 Default

#### 3.1.2 悬停高亮 (Outline Glow)

**Shader**: `UI-OutlineGlow.shader`

**原理**: 扩展 UI/Default，在 fragment 阶段检测像素到边缘距离，在阈值内叠加发光色

```
// 伪代码 fragment 核心
fixed4 frag(v2f IN) : SV_Target
{
    fixed4 col = tex2D(_MainTex, IN.uv) * IN.color;
    
    // 基于 alpha 通道计算边缘距离（signed distance field 近似）
    float edgeDist = fwidth(IN.color.a);  // 简化: 用 alpha 梯度当边缘
    
    // 边缘叠加发光
    float glow = smoothstep(_GlowWidth, 0, abs(IN.color.a - 0.5));
    col.rgb += _GlowColor.rgb * glow * _GlowIntensity;
    
    return col;
}
```

**属性表**:

| 属性 | 类型 | 默认值 | 驱动方式 |
|------|------|--------|---------|
| `_GlowColor` | Color | (1, 0.84, 0, 1) 金色 | PropertyBlock |
| `_GlowIntensity` | Float | 1.5 | PropertyBlock |
| `_GlowWidth` | Float | 0.05 | 静态 |

**挂载方式**: 悬停时 EquipmentSlot.cs 设置 slot Glow Image 的 Material → Mat_OutlineGlow，PropertyBlock 传入稀有度对应颜色；离开时 restore 到 Default。

#### 3.1.3 稀有度常驻光效

**Shader**: `UI-RarityGlow.shader`

**原理**: 在 UI/Default 基础上叠加外层柔光，颜色由稀有度决定

```
// 伪代码
fixed4 frag(v2f IN) : SV_Target
{
    fixed4 col = tex2D(_MainTex, IN.uv) * IN.color;
    
    // 柔光叠加：用 alpha 通道做 mask
    float mask = col.a;
    float glowAlpha = mask * _GlowAlpha * (0.6 + 0.4 * sin(_Time.y * _GlowSpeed));
    
    col.rgb = lerp(col.rgb, _RarityColor.rgb, glowAlpha * 0.3);
    return col;
}
```

**属性表**:

| 属性 | 类型 | 说明 |
|------|------|------|
| `_RarityColor` | Color | 稀有度颜色 |
| `_GlowAlpha` | Float | 发光透明度 (0.3~0.6) |
| `_GlowSpeed` | Float | 呼吸速度 (1.5~3.0) |

**稀有度颜色映射** (来自 `背包仓库装备_设计概览.md`):

| 稀有度 | 颜色 | 材质 |
|--------|------|------|
| Common | #999999 灰 | Mat_RarityCommon |
| Rare | #4488FF 蓝 | Mat_RarityRare |
| Epic | #AA66FF 紫 | Mat_RarityEpic |
| Legendary | #FFD700 金 | Mat_RarityLegendary |

**挂载方式**: 装备放入槽位后，Slot_* 的 Glow Image 切换到对应稀有度的 Mat_Rarity*。空槽位用 Default。

---

### 3.2 物品格子 (ItemCell ×26)

现有结构: ItemCell (Button+Image) → Icon (Image) + Glow (Image)

#### 3.2.1 稀有度边框发光

与 3.1.3 完全相同。`UI-RarityGlow.shader` 共享。

**挂载方式**: ItemCell 的 Glow 子 Image 使用对应 Mat_Rarity*，PropertyBlock 设 `_RarityColor`。同稀有度格子共享同一个 Material 实例。

#### 3.2.2 选中高亮脉冲 (Pulse)

**Shader**: `UI-Pulse.shader`

**原理**: 选中时边框脉动发光，`_PulsePhase` 由代码驱动（非 `_Time`，便于控制相位同步）

```
fixed4 frag(v2f IN) : SV_Target
{
    fixed4 col = tex2D(_MainTex, IN.uv) * IN.color;
    
    float pulse = 0.5 + 0.5 * sin(_PulsePhase * 6.28318);
    
    // 只在 alpha 边缘区域叠加脉冲光
    float edge = smoothstep(0.9, 1.0, col.a);
    col.rgb += _PulseColor.rgb * edge * pulse * _PulseIntensity;
    
    return col;
}
```

**属性表**:

| 属性 | 类型 | 驱动方式 |
|------|------|---------|
| `_PulseColor` | Color | 稀有度对应颜色 |
| `_PulseIntensity` | Float | 1.0 |
| `_PulsePhase` | Float | PropertyBlock，C# 每帧 += Time.deltaTime |

**挂载方式**: 选中 ItemCell 时，Glow Image 切换 Material 到 Mat_Pulse。取消选中时 restore。同时只有一个 ItemCell 是选中态，所以 Mat_Pulse 只有 1 个实例被使用。

---

### 3.3 面板背景 (InventoryPanel / WarehousePanel)

现有结构: InventoryPanel (Image) — 当前用纯色/默认 Material

#### 3.3.1 打开/关闭溶解过渡 (Dissolve)

**Shader**: `UI-Dissolve.shader`

**原理**: 噪声纹理采样 + `_DissolveAmount` 控制从 0→1 的溶解进度

```
fixed4 frag(v2f IN) : SV_Target
{
    fixed4 col = tex2D(_MainTex, IN.uv) * IN.color;
    float noise = tex2D(_NoiseTex, IN.uv * _NoiseScale).r;
    
    float dissolve = noise - _DissolveAmount;
    clip(dissolve - 0.05);
    
    // 溶解边缘发光
    float edge = smoothstep(0, 0.1, dissolve);
    col.rgb += _EdgeColor.rgb * edge * _EdgeIntensity;
    
    return col;
}
```

**驱动方式**: 打开面板时 C# 协程 DoTween: `_DissolveAmount` 从 0→1 (t=0.4s)；关闭时 1→0 (t=0.3s)。每帧 PropertyBlock.SetFloat。

**注意**: 面板 dissolve 只影响面板背景 Image 自身，不影响子节点（ItemCell 等）。子节点的显示由 CanvasGroup alpha 控制，与 dissolve 同步。

#### 3.3.2 毛玻璃背景 (Blur)

**Shader**: `UI-BlurBackground.shader`

**实现路径**: Built-in 管线没有 URP 的 `_CameraOpaqueTexture`，需要额外 RenderTexture。

```
方案A (简单): 用半透明深色 + 模糊噪声纹理模拟毛玻璃
方案B (真模糊): 额外 Camera → RenderTexture → 面板背景 Image 采样
```

**推荐方案A**，理由:
- 不需要额外的 Camera 和 RT
- 性能极低开销
- 视觉上可达到 80% 效果

```
fixed4 frag(v2f IN) : SV_Target
{
    // 采样噪声纹理
    float3 noise = tex2D(_NoiseTex, IN.uv * _NoiseScale + _Time.xy * 0.1).rgb;
    
    // 深色底色 + 噪声柔光
    fixed4 bg = fixed4(0.05, 0.05, 0.08, _BlurAlpha);
    bg.rgb += noise * 0.03;
    
    return bg;
}
```

**属性**: `_BlurAlpha` (0.7~0.85), `_NoiseScale` (5~10)

**挂载方式**: 面板背景 Image 使用 Mat_BlurBG。打开面板时面板背景显示，关闭时隐藏。

---

### 3.4 拖拽中物品

**实现**: 纯 C# 动画，不需要 shader

| 状态 | 效果 | 实现方式 |
|------|------|---------|
| 开始拖拽 | 图标放大 1.1x，alpha 轻微降低到 0.85 | CanvasGroup + DoTween |
| 拖拽中 | 跟随鼠标，保持半透明 | RectTransform.position = Input.mousePosition |
| 悬停在有效目标上 | 目标槽位高亮 | 3.1.2 的悬停光效 |
| 释放到无效区域 | 图标弹回原位 | DoTween DOMove 回原位 |
| 释放到有效槽位 | 图标缩小消失，槽位播放 3.1.1 装入动画 | DoTween Scale 1.0→0 |

**性能注意**: 拖拽中物品本身渲染在顶层 Canvas（SortingOrder 最高），避免被面板遮挡。

---

### 3.5 QuickSlotBar 冷却效果

现有结构: QuickSlotBar → QuickSlot_0/1。技能节点已有 CooldownOverlay (Image)。

#### 3.5.1 冷却暗化 + 扫描线

**Shader**: `UI-Scanline.shader`

**原理**: 冷却中覆盖一层暗色 + 从上到下的扫描线（计时器效果）

```
fixed4 frag(v2f IN) : SV_Target
{
    fixed4 col = tex2D(_MainTex, IN.uv) * IN.color;
    
    // 冷却暗化区域（从顶部覆盖下来）
    float cooldownMask = step(IN.uv.y, _CooldownT);  // _CooldownT: 0=满冷却(全暗), 1=冷却完毕(全亮)
    
    // 冷却区域: 暗化
    col.rgb *= lerp(0.3, 1.0, cooldownMask);
    
    // 扫描线（只在冷却分界线）
    float scanline = smoothstep(0, 0.02, abs(IN.uv.y - _CooldownT));
    col.rgb += _ScanColor.rgb * (1 - scanline) * 0.3;
    
    return col;
}
```

**属性表**:

| 属性 | 驱动方式 |
|------|---------|
| `_CooldownT` | PropertyBlock，C# 每帧设 `1 - currentCooldown / maxCooldown` |
| `_ScanColor` | 静态 (1, 1, 1, 1) |

**挂载方式**: QuickSlot_0/1 上的 CooldownOverlay Image 使用 Mat_Scanline。冷却结束时 restore 到 Default。

#### 3.5.2 冷却完毕提示

冷却结束瞬间: 整个 QuickSlot 短暂脉冲一次（0.2s），用 Mat_Pulse 闪烁一次再切回。纯 C# 协程控制。

---

## 四、技能树面板 (SkillTreePanel) 复用

技能树面板已有 `Glow` Image 子节点（10 个 Node 各一个），可直接复用上述材质：

| Node 状态 | 材质 | 驱动 |
|-----------|------|------|
| 锁定 | Default (灰色调色) | Image.color = #666666 |
| 可解锁 | Mat_Pulse (低强度) | `_PulseIntensity` = 0.5, `_PulseColor` = #FFD700 |
| 已解锁/可升级 | Mat_Pulse | `_PulseIntensity` = 1.0 |
| 已满级 | Mat_RarityLegendary | `_GlowAlpha` = 0.3 |

---

## 五、通用辅助效果

### 5.1 稀有度颜色常量表

在 `UIConstants.cs` 中已有颜色定义，新增静态方法直接返回对应 Material:

```csharp
// UIConstants.cs 新增
public static Material GetRarityMaterial(ItemRarity rarity)
{
    return rarity switch
    {
        ItemRarity.Common    => Resources.Load<Material>("Materials/Mat_RarityCommon"),
        ItemRarity.Rare      => Resources.Load<Material>("Materials/Mat_RarityRare"),
        ItemRarity.Epic      => Resources.Load<Material>("Materials/Mat_RarityEpic"),
        ItemRarity.Legendary => Resources.Load<Material>("Materials/Mat_RarityLegendary"),
        _ => null
    };
}
```

### 5.2 MaterialPropertyBlock 工具方法

建议在 `InventoryPanel.cs` 或新建 `UIShaderHelper.cs` 中统一管理:

```csharp
// 设置 Image 的 Material + PropertyBlock
public static void SetRarityGlow(Image image, ItemRarity rarity)
{
    image.material = UIConstants.GetRarityMaterial(rarity);
    var block = new MaterialPropertyBlock();
    block.SetColor("_RarityColor", UIConstants.GetRarityColor(rarity));
    image.SetPropertyBlock(block);
}

// 驱动脉冲相位
public static void UpdatePulsePhase(Image image, float phase)
{
    var block = new MaterialPropertyBlock();
    image.GetPropertyBlock(block);
    block.SetFloat("_PulsePhase", phase);
    image.SetPropertyBlock(block);
}
```

---

## 六、性能评估

### 6.1 批次影响

| 场景 | Material 实例数 | 潜在额外批次 |
|------|----------------|-------------|
| 背包面板打开 (11 ItemCell + 4 Slot) | 最多 6 (4稀有度+1脉冲+1默认) | +5 DC (相比全 Default) |
| 仓库面板打开 (15 ItemCell) | 最多 5 | +4 DC |
| QuickSlotBar 冷却中 | 2 Mat_Scanline | +2 DC |
| 所有面板关闭 (仅 HUD) | 0 自定义材质 | 0 DC |

**总计最大额外 Draw Call: ~11**。在 Built-in 管线 UI Canvas 下，这个数字不会造成性能瓶颈。

### 6.2 PropertyBlock 开销

- `SetPropertyBlock` 每帧调用 (< 10 次/帧): 几乎零开销（不重建 Material，只更新常量缓冲区）
- 唯一需要每帧驱动的: `_PulsePhase` (1个), `_CooldownT` (最多2个), `_DissolveAmount` (面板开/关动画期间 0.4s)

### 6.3 移动端/低配降级

| 降级级别 | 措施 |
|---------|------|
| Low | 关闭稀有度常驻光效，保留选中脉冲和冷却扫描线 |
| Medium | 稀有度光效 _GlowAlpha 减半，取消面板 dissolve（直接 SetActive） |
| High | 全部开启 |

建议在 `InventoryManager` 中加一个 `ShaderQualityLevel` 枚举，启动时检测 `SystemInfo.graphicsDeviceType` 设置默认值。

---

## 七、实施优先级与阶段

### Phase 1 — 基础设施 (最先)

| # | 任务 | 产出 |
|---|------|------|
| 1 | 创建 Assets/Shaders/UI/ 目录 | 目录结构 |
| 2 | 写 `UI-RarityGlow.shader` | 1 个 shader 文件 |
| 3 | 创建 4 个 Mat_Rarity* Material | 4 个 .mat 文件 |
| 4 | 在 ItemCell 的 Glow Image 上挂载稀有度材质 | Scene 修改 |
| 5 | 在 EquipmentSlot 的 Glow Image 上挂载稀有度材质 | Scene 修改 |

**目标**: 所有有物品的格子和槽位显示对应颜色的呼吸光。这是视觉效果最大、成本最低的第一步。

### Phase 2 — 交互反馈

| # | 任务 | 产出 |
|---|------|------|
| 6 | 写 `UI-Pulse.shader` + 创建 Mat_Pulse | 1 shader + 1 mat |
| 7 | 写 `UI-OutlineGlow.shader` + 创建 Mat_OutlineGlow | 1 shader + 1 mat |
| 8 | ItemCell 选中态脉冲 | C# 逻辑 |
| 9 | EquipmentSlot 悬停描边发光 | C# 逻辑 |

### Phase 3 — 面板过渡

| # | 任务 | 产出 |
|---|------|------|
| 10 | 写 `UI-Dissolve.shader` + 创建 Mat_Dissolve | 1 shader + 1 mat |
| 11 | 写 `UI-BlurBackground.shader` + 创建 Mat_BlurBG | 1 shader + 1 mat |
| 12 | InventoryPanel / WarehousePanel 溶解过渡 | C# 逻辑 |
| 13 | 面板背景毛玻璃效果 | C# 逻辑 |

### Phase 4 — QuickSlot 冷却

| # | 任务 | 产出 |
|---|------|------|
| 14 | 写 `UI-Scanline.shader` + 创建 Mat_Scanline | 1 shader + 1 mat |
| 15 | QuickSlot CooldownOverlay 挂载 + 驱动 | C# 逻辑 |

### Phase 5 — 拖拽 + 降级

| # | 任务 | 产出 |
|---|------|------|
| 16 | 拖拽物品视觉反馈（纯 C# DoTween） | C# 逻辑 |
| 17 | ShaderQualityLevel 降级开关 | C# 逻辑 |
| 18 | 技能树面板 Glow 复用稀有度/脉冲材质 | Unity 配置 |

---

## 八、技术注意事项

### 8.1 Built-in UI Shader 模板

所有自定义 shader 继承 `UI/Default` 的标准结构:

```hlsl
Shader "Custom/UI/UI-RarityGlow"
{
    Properties { ... }
    SubShader
    {
        Tags { "Queue"="Transparent" "RenderType"="Transparent" }
        Blend SrcAlpha OneMinusSrcAlpha
        ZWrite Off
        Cull Off
        
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"
            #include "UnityUI.cginc"   // 关键: UI 支持(裁剪/CanvasGroup)
            
            struct appdata { float4 vertex : POSITION; float4 color : COLOR; float2 uv : TEXCOORD0; };
            struct v2f { float4 vertex : SV_POSITION; float4 color : COLOR; float2 uv : TEXCOORD0; float4 worldPosition : TEXCOORD1; };
            
            sampler2D _MainTex;
            float4 _MainTex_ST;
            // ... 自定义属性
            
            v2f vert(appdata v)
            {
                v2f o;
                o.worldPosition = v.vertex;
                o.vertex = UnityObjectToClipPos(v.vertex);
                o.color = v.color;
                o.uv = TRANSFORM_TEX(v.uv, _MainTex);
                return o;
            }
            
            fixed4 frag(v2f IN) : SV_Target { ... }
            ENDCG
        }
    }
}
```

关键: 必须 `#include "UnityUI.cginc"` 并用 `UnityGet2DClipping` 支持 Mask 裁剪。

### 8.2 TMP Shader 替换风险

TMP 文本使用自己的 shader (TextMeshPro/Mobile/Distance Field)。**不建议**替换 TMP 的 shader，因为 Distance Field 渲染路径特殊。TMP 文本的颜色通过 `vertex color` 驱动，不需要自定义 shader 即可改变颜色。

**如果需要在 TMP 上叠加效果**（如技能名发光），用独立 Image 子节点（Glow Image）而非改 TMP shader。

### 8.3 Canvas Group 与 Material 交互

`CanvasGroup.alpha` 叠加 `material.color` 是乘法关系。如果有 shader 内部用了 `IN.color`（顶点色），CanvasGroup 会正确穿透。所有上述 shader 的 `IN.color` 来自顶点色，兼容 CanvasGroup。

### 8.4 DoTween 依赖

拖拽动画 + 溶解过渡推荐使用 DoTween（项目大概率已安装，Unity 项目标配）。如果没有 DoTween，用 `StartCoroutine` + `Lerp` 替代也可，只是代码稍长。

---

## 九、文件变更清单

### 新建文件

| 文件 | 路径 | 类型 |
|------|------|------|
| UI-RarityGlow.shader | Assets/Shaders/UI/ | Shader |
| UI-Pulse.shader | Assets/Shaders/UI/ | Shader |
| UI-Scanline.shader | Assets/Shaders/UI/ | Shader |
| UI-Dissolve.shader | Assets/Shaders/UI/ | Shader |
| UI-OutlineGlow.shader | Assets/Shaders/UI/ | Shader |
| UI-BlurBackground.shader | Assets/Shaders/UI/ | Shader |
| Mat_RarityCommon.mat ~ Mat_RarityLegendary.mat | Assets/Shaders/Materials/ | Material ×4 |
| Mat_Pulse.mat, Mat_Scanline.mat, Mat_Dissolve.mat, Mat_OutlineGlow.mat, Mat_BlurBG.mat | Assets/Shaders/Materials/ | Material ×5 |
| UIShaderHelper.cs (可选) | Assets/Scripts/UI/ | C# |

### 需修改文件

| 文件 | 改动 |
|------|------|
| UIConstants.cs | 新增 `GetRarityMaterial()` 方法 |
| ItemCell.cs | 稀有度更新时调用 `SetRarityGlow()`；选中/取消选中切换脉冲 |
| EquipmentSlot.cs | 悬停事件 + 装备放入动画 + 稀有度光效 |
| InventoryPanel.cs | 面板打开/关闭时驱动 dissolve 动画 |
| WarehousePanel.cs | 同上 |
| QuickSlotBar.cs | 冷却时驱动 CooldownOverlay 的 shader |

### Scene 修改

| 节点 | 改动 |
|------|------|
| 所有 ItemCell/Glow | Image.material → Mat_Rarity* (按稀有度) |
| Slot_*/Glow | Image.material → Mat_Rarity* (按稀有度) |
| QuickSlot_*/CooldownOverlay | Image.material → Mat_Scanline |
| InventoryPanel | 背景 Image.material → Mat_BlurBG |
| WarehousePanel | 背景 Image.material → Mat_BlurBG |
