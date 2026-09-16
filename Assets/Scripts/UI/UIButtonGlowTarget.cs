using DG.Tweening;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// 单个按钮的悬停辉光执行体。由 UIButtonGlow 在运行时自动添加,不需要手动挂,也不要手动删。
/// 进入:按按钮实际尺寸重算辉光范围后淡入到 maxAlpha,完成后按需呼吸;
/// 离开:淡出到 0;按钮/面板被隐藏时(OnDisable)复位为全透明,避免下次显示残留亮着。
/// 参数每次都从管理组件实时读,所以 Play 模式里改 Inspector 数值立刻见效。
/// </summary>
[DisallowMultipleComponent]
public class UIButtonGlowTarget : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
{
    private UIButtonGlow _owner;
    private Button _button;
    private Image _glow;
    private RectTransform _glowRect;
    private RectTransform _buttonRect;

    /// <summary>描边宽度占按钮尺寸的比例(辉光贴图比按钮图大 2 倍描边宽度)</summary>
    private float _padRatioX = 0f;
    private float _padRatioY = 0f;

    /// <summary>由 UIButtonGlow 注入(挂在按钮上,无需在 Inspector 配置)</summary>
    public void Setup(UIButtonGlow owner, Button button, Image glow, RectTransform glowRect,
                      RectTransform buttonRect, float padRatioX, float padRatioY)
    {
        _owner = owner;
        _button = button;
        _glow = glow;
        _glowRect = glowRect;
        _buttonRect = buttonRect;
        _padRatioX = padRatioX;
        _padRatioY = padRatioY;

        RefreshSize();
        ApplyColor();
    }

    /// <summary>辉光显示尺寸 = 按钮实际尺寸 + 四周描边宽度(按钮被布局或分辨率改动后依然贴合)</summary>
    private void RefreshSize()
    {
        if (_glowRect == null || _buttonRect == null) return;

        float w = _buttonRect.rect.width * (1f + 2f * _padRatioX);
        float h = _buttonRect.rect.height * (1f + 2f * _padRatioY);
        _glowRect.sizeDelta = new Vector2(w, h);
    }

    private void ApplyColor()
    {
        if (_glow == null) return;

        Color c = _owner != null ? _owner.GlowColor : Color.white;
        c.a = _glow.color.a;   // alpha 保留当前值,交给淡入淡出控制
        _glow.color = c;
    }

    public void OnPointerEnter(PointerEventData eventData)
    {
        if (_glow == null) return;
        if (_button != null && !_button.interactable) return;   // 灰按钮不响应

        RefreshSize();
        ApplyColor();

        _glow.DOKill();
        _glow.DOFade(_owner != null ? _owner.MaxAlpha : 0.9f, _owner != null ? _owner.FadeInDuration : 0.12f)
             .SetUpdate(_owner == null || _owner.UseUnscaled)
             .OnComplete(StartPulse);
    }

    public void OnPointerExit(PointerEventData eventData)
    {
        if (_glow == null) return;

        _glow.DOKill();
        _glow.DOFade(0f, _owner != null ? _owner.FadeOutDuration : 0.25f)
             .SetUpdate(_owner == null || _owner.UseUnscaled);
    }

    private void StartPulse()
    {
        if (_glow == null || _owner == null || !_owner.Pulse) return;

        float max = Mathf.Clamp01(_owner.MaxAlpha);
        if (max <= 0f) return;

        float low = Mathf.Clamp01(max * (1f - _owner.PulseAmplitude));
        _glow.DOFade(low, Mathf.Max(0.05f, _owner.PulsePeriod * 0.5f))
             .SetUpdate(_owner.UseUnscaled)
             .SetEase(Ease.InOutSine)
             .SetLoops(-1, LoopType.Yoyo);
    }

    private void OnDisable()
    {
        if (_glow == null) return;

        _glow.DOKill();
        Color c = _glow.color;
        c.a = 0f;
        _glow.color = c;
    }
}
