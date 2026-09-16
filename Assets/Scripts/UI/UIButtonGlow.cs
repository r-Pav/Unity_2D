using System;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 按钮悬停辉光 · 管理器(2026-09-14,贴图方案)。
/// 挂在任意物体上,每个按钮配一张辉光贴图(用 菜单 Tools/UI/按钮辉光贴图生成器 生成)。
/// 运行时给每个按钮在自身层级下叠一层显示该贴图的 Image:悬停淡入(可选呼吸脉动),离开淡出。
///
/// 为什么用贴图而不是 shader:效果落在图上,可以直接用美术工具精修;运行时零 shader 开销;
/// 也不受 Canvas 缩放和 sprite 图集 UV 的影响。
///
/// 设计约定:
/// - 只运行期创建子物体,不往场景写任何东西,退出播放模式自动消失;
/// - 辉光 Image.raycastTarget = false,不挡点击;SiblingIndex 插到 0,渲染在按钮图形之上、按钮内图标文字之下;
/// - 挂 LayoutGroup 里的按钮也不会被布局拉位置(LayoutElement.ignoreLayout);
/// - 参数悬停时实时读取本组件字段,Play 模式里改数值立刻见效;
/// - 按钮 interactable = false 时悬停不响应(与 UIButtonFeedback 同规则);
/// - 悬停动画受 useUnscaled 控制,面板暂停(timeScale=0)时照播。
/// </summary>
[DisallowMultipleComponent]
public class UIButtonGlow : MonoBehaviour
{
    /// <summary>一个按钮 + 它的辉光贴图</summary>
    [Serializable]
    public class GlowEntry
    {
        [Tooltip("要生效的按钮")]
        public Button button;

        [Tooltip("该按钮的辉光贴图。用 菜单 Tools/UI/按钮辉光贴图生成器 生成后拖进来")]
        public Sprite glowSprite;
    }

    [Header("生效按钮(每个按钮一条)")]
    [Tooltip("把按钮和它对应的辉光贴图配成一对;没配贴图的按钮不会有任何变化")]
    [SerializeField] private GlowEntry[] entries;

    [Header("辉光外观")]
    [Tooltip("辉光染色。贴图本身是白色,颜色靠这里出")]
    [SerializeField] private Color glowColor = new Color(1f, 0.95f, 0.75f, 1f);

    [Tooltip("悬停时辉光最大不透明度(0~1)")]
    [Range(0f, 1f)]
    [SerializeField] private float maxAlpha = 0.9f;

    [Header("时序")]
    [Tooltip("鼠标进入:淡入时长(秒)")]
    [SerializeField] private float fadeInDuration = 0.12f;

    [Tooltip("鼠标离开:淡出时长(秒)")]
    [SerializeField] private float fadeOutDuration = 0.25f;

    [Header("呼吸脉动")]
    [Tooltip("true=悬停期间辉光持续呼吸;false=悬停时保持恒定亮度")]
    [SerializeField] private bool pulse = true;

    [Tooltip("一次呼吸的周期(秒)")]
    [SerializeField] private float pulsePeriod = 1.4f;

    [Tooltip("呼吸幅度:不透明度在最大值的上下按此比例浮动(0.2=上下 20%)")]
    [Range(0f, 1f)]
    [SerializeField] private float pulseAmplitude = 0.2f;

    [Header("其他")]
    [Tooltip("true=timeScale=0(暂停菜单)时辉光动画照播,与 UIPanelMotion / UIButtonFeedback 一致")]
    [SerializeField] private bool useUnscaled = true;

    // ── 供执行体实时读取(避免缓存导致运行时改参不生效) ──
    public Color GlowColor => glowColor;
    public float MaxAlpha => maxAlpha;
    public float FadeInDuration => fadeInDuration;
    public float FadeOutDuration => fadeOutDuration;
    public bool Pulse => pulse;
    public float PulsePeriod => pulsePeriod;
    public float PulseAmplitude => pulseAmplitude;
    public bool UseUnscaled => useUnscaled;

    private void Awake()
    {
        if (entries == null || entries.Length == 0)
            return;

        for (int i = 0; i < entries.Length; i++)
        {
            var e = entries[i];
            if (e == null || e.button == null)
            {
                Debug.LogWarning($"[UIButtonGlow] {name}:entries 第 {i + 1} 项没配按钮,已跳过", this);
                continue;
            }
            Attach(e);
        }
    }

    private void Attach(GlowEntry entry)
    {
        Button btn = entry.button;
        if (btn.GetComponent<UIButtonGlowTarget>() != null)
            return;   // 防重复挂(同一按钮被配两次)

        if (!(btn.transform is RectTransform btnRt))
        {
            Debug.LogWarning($"[UIButtonGlow] {btn.name} 不是 UI 元素(没有 RectTransform),已跳过", this);
            return;
        }

        if (entry.glowSprite == null)
        {
            Debug.LogWarning($"[UIButtonGlow] {btn.name}:没配辉光贴图,这个按钮不会有辉光。" +
                             "用 菜单 Tools/UI/按钮辉光贴图生成器 生成后拖进来", btn);
            return;
        }

        // 按钮自身的 sprite:用来换算辉光贴图该显示多大(两边像素尺寸差 = 描边宽度 ×2)
        Sprite btnSprite = null;
        if (btn.targetGraphic is Image targetImg) btnSprite = targetImg.sprite;
        if (btnSprite == null)
        {
            var ownImg = btn.GetComponent<Image>();
            if (ownImg != null) btnSprite = ownImg.sprite;
        }

        // 描边宽度占按钮原始尺寸的比例(辉光贴图比按钮图大 2 倍描边宽度)
        float padRatioX = 0f, padRatioY = 0f;
        if (btnSprite != null && btnSprite.rect.width > 0f && btnSprite.rect.height > 0f)
        {
            padRatioX = (entry.glowSprite.rect.width - btnSprite.rect.width) * 0.5f / btnSprite.rect.width;
            padRatioY = (entry.glowSprite.rect.height - btnSprite.rect.height) * 0.5f / btnSprite.rect.height;
            padRatioX = Mathf.Max(0f, padRatioX);
            padRatioY = Mathf.Max(0f, padRatioY);
        }
        else
        {
            Debug.LogWarning($"[UIButtonGlow] {btn.name} 上没有 sprite,辉光大小按按钮尺寸估算", btn);
        }

        var go = new GameObject("Glow", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        go.transform.SetParent(btn.transform, false);
        go.transform.SetSiblingIndex(0);   // 按钮图形之上、按钮内图标文字之下

        var rt = (RectTransform)go.transform;
        rt.anchorMin = new Vector2(0.5f, 0.5f);
        rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = Vector2.zero;

        var layout = go.AddComponent<LayoutElement>();
        layout.ignoreLayout = true;   // 按钮在 LayoutGroup 里时不被布局拉位置

        var img = go.GetComponent<Image>();
        img.sprite = entry.glowSprite;
        img.raycastTarget = false;
        img.color = new Color(1f, 1f, 1f, 0f);   // 顶点色 alpha 控制淡入淡出

        var target = btn.gameObject.AddComponent<UIButtonGlowTarget>();
        target.Setup(this, btn, img, rt, btnRt, padRatioX, padRatioY);
    }
}
