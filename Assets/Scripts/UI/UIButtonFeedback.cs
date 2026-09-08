using System.Collections.Generic;
using DG.Tweening;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// 按钮按压/悬停反馈组件(S6 + 悬停扩展 2026-09-08)— 挂在按钮根物体上(要求同物体有 Button 或任意可交互 UI 元素,
/// 推荐挂 Button 物体;无 Button 也可工作,只要它能收到指针事件)。
/// 反馈内容(全部可选、独立开关):
/// - 按压缩放:按下缩小到 pressScale 再回弹;
/// - 悬停放大:悬停放大到 hoverScale;
/// - 悬停高亮色:悬停时同物体 Graphic 颜色过渡到 hoverColor(保留原 alpha);
/// - 悬停内容上浮:悬停时 contentTargets(按钮内图标/文字)向上平移 floatUpPixels;
/// - 悬停阴影浮起:悬停时 Shadow.effectDistance 拉远 shadowLiftOffset 模拟离地。
///
/// 设计约定:
/// - 用指针接口(IPointerDown/Up/Enter/Exit)而非监听 onClick:Button.onClick 在 PointerClick
///   才触发,按压反馈须在 PointerDown 就开始;本组件与 Button 同物体共存时两者都会收到事件
///   (EventSystem 广播,Selectable 不拦截),本组件只做纯视觉、不改 Selectable 状态,不冲突。
/// - 基准 = 首次指针交互时统一缓存(localScale/Graphic.color/各目标 anchoredPosition/
///   Shadow.effectDistance):面板 pop、入场缩放等根级动画不影响子物体 localScale,基准安全;
///   但若按钮自身在交互前有入场动画(如 UIContentReveal 从透明/位移到摆位),
///   需等它播放完成后再交互,否则基准会被缓存成动画中间值。
/// - interactable=false 时不播反馈(灰按钮不响应);交互中按钮被禁用等场景由 OnDisable 兜底复位。
/// - 中断安全:每次开同维度新动画前 Kill 旧的;OnDisable/OnDestroy Kill 全部并把各维度复位到基准,
///   防止面板关闭时把缩放/颜色/位移/阴影残留带进隐藏态。仅运行期写 transform/graphic,
///   避免退出播放模式时把运行期值写回场景物体。
/// - 状态规则:Enter/Exit 作用于全部维度(悬停集 vs 基准),Down/Up 只作用于缩放维度
///   (按压只改变缩放,颜色/位移/阴影在按压期间保持悬停态)。
/// - 内部 Tween 按 useUnscaled 决定是否 SetUpdate(true):true 时 timeScale=0(暂停菜单)动画照播,
///   与 UIPanelMotion/UIContentReveal 一致。
/// </summary>
public class UIButtonFeedback : MonoBehaviour,
    IPointerDownHandler, IPointerUpHandler, IPointerEnterHandler, IPointerExitHandler
{
    [Header("按压反馈")]
    [Tooltip("true=按下时缩小到 pressScale、松开回弹;false=不做按压缩放")]
    [SerializeField] private bool pressEffect = true;

    [Tooltip("按下缩放倍率(相对初始 localScale 的乘法系数,如 0.92=缩小 8%)")]
    [SerializeField] private float pressScale = 0.92f;

    [Tooltip("按下缩小与松开回弹的时长(秒)")]
    [SerializeField] private float pressDuration = 0.08f;

    [Tooltip("按压/回弹动画缓动曲线")]
    [SerializeField] private Ease pressEase = Ease.OutQuad;

    [Header("悬停反馈")]
    [Tooltip("true=鼠标悬停时放大到 hoverScale;false=悬停无放大")]
    [SerializeField] private bool hoverEffect = false;

    [Tooltip("悬停放大倍率(相对初始 localScale 的乘法系数,如 1.05=放大 5%)")]
    [SerializeField] private float hoverScale = 1.05f;

    [Tooltip("悬停进入/离开动画时长(秒)")]
    [SerializeField] private float hoverDuration = 0.12f;

    [Tooltip("悬停动画缓动曲线")]
    [SerializeField] private Ease hoverEase = Ease.OutCubic;

    [Header("悬停高亮色")]
    [Tooltip("true=悬停时同物体 Graphic(Image/Text)颜色过渡到 hoverColor;false=悬停不变色")]
    [SerializeField] private bool hoverColorEffect = false;

    [Tooltip("悬停目标颜色,建议填比按钮基色更亮的同色系;实际只改 RGB,alpha 保持按钮原值")]
    [SerializeField] private Color hoverColor = new Color(1f, 0.95f, 0.82f);

    [Header("悬停内容上浮")]
    [Tooltip("true=悬停时 contentTargets 各目标向上平移 floatUpPixels;false=不上浮")]
    [SerializeField] private bool contentFloatEffect = false;

    [Tooltip("上浮像素(UI 坐标 +y)。目标不能在 LayoutGroup/ContentSizeFitter 布局控制下,否则位置会被布局拉回")]
    [SerializeField] private float floatUpPixels = 3f;

    [Tooltip("按钮内随悬停上浮的图标/文字 RectTransform,按需拖多个")]
    [SerializeField] private RectTransform[] contentTargets;

    [Header("悬停阴影浮起")]
    [Tooltip("true=悬停时 Shadow.effectDistance 拉远 shadowLiftOffset 模拟浮起;false=不动阴影")]
    [SerializeField] private bool shadowLiftEffect = false;

    [Tooltip("阴影组件;留空=自动取同物体 Shadow,同物体没有则该效果无效并在控制台警告一次")]
    [SerializeField] private Shadow shadowTarget;

    [Tooltip("悬停时 effectDistance 相对基准的增量(如 (0,-3)=阴影向下拉远 3)")]
    [SerializeField] private Vector2 shadowLiftOffset = new Vector2(0f, -3f);

    [Header("时间模式")]
    [Tooltip("true=使用不受 Time.timeScale 影响的时间(timeScale=0 暂停时动画照播);false=跟随 timeScale")]
    [SerializeField] private bool useUnscaled = true;

    private Button _button;
    private Graphic _graphic;
    private Shadow _shadow;
    private bool _stateCached;
    private bool _pressed;
    private bool _inside;

    // 基准缓存(首次指针交互时统一采集)
    private Vector3 _baseScale = Vector3.one;
    private Color _baseColor = Color.white;
    private Vector2[] _basePositions;
    private Vector2 _baseShadowDistance;

    // 各维度活动 tween:同维度重启前互 Kill;OnDisable/OnDestroy 全 Kill
    private Tween _scaleTween;
    private Tween _colorTween;
    private Tween _shadowTween;
    private readonly List<Tween> _floatTweens = new List<Tween>();

    private void OnEnable()
    {
        // 每次启用重新取一次,避免按钮运行时增删导致引用过期
        _button = GetComponent<Button>();
        _graphic = GetComponent<Graphic>();
        _shadow = shadowTarget != null ? shadowTarget : GetComponent<Shadow>();
    }

    private void OnDisable()
    {
        _pressed = false;
        _inside = false;
        KillAllTweens();

        // 面板/按钮被隐藏(含 SetActive(false) 打断动画)时把各维度复位到基准,
        // 防止中途缩放/颜色/位移/阴影残留到下次激活。仅运行期写,避免退出播放模式
        // 时把运行期值写回场景物体。
        if (_stateCached && Application.isPlaying)
            ResetToBaseInstant();
    }

    private void OnDestroy()
    {
        KillAllTweens();
    }

    // ============================================================
    // 指针接口(纯视觉反馈,不改 Selectable 状态、不触发 onClick)
    // ============================================================

    public void OnPointerDown(PointerEventData eventData)
    {
        if (!CanRespond())
            return;
        EnsureBaseState();
        _pressed = true;
        if (pressEffect)
            PlayScale(_baseScale * pressScale, pressDuration, pressEase);
    }

    public void OnPointerUp(PointerEventData eventData)
    {
        if (!_pressed)
            return; // 非本组件承接的按下(如按住拖出后已由 Exit 复位),无需处理
        _pressed = false;
        if (!_stateCached)
            return;

        // 松开只恢复缩放维度:颜色/上浮/阴影在按压期间未变(Enter 已到悬停态),无需处理。
        // 仍悬停在按钮内且开 hoverEffect → 回弹到 hoverScale,否则回基准
        if (hoverEffect && _inside && CanRespond())
            PlayScale(_baseScale * hoverScale, pressDuration, pressEase);
        else
            PlayScale(_baseScale, pressDuration, pressEase);
    }

    public void OnPointerEnter(PointerEventData eventData)
    {
        _inside = true;
        if (!CanRespond())
            return;
        if (_pressed)
            return; // 按住状态下不会触发 Enter,此处仅为状态顺序兜底
        EnsureBaseState();

        // 进入 = 播放全部悬停集,各维度按开关独立生效
        if (hoverEffect)
            PlayScale(_baseScale * hoverScale, hoverDuration, hoverEase);
        if (hoverColorEffect && _graphic != null)
            PlayColor(HoverColorWithBaseAlpha(), hoverDuration, hoverEase);
        if (contentFloatEffect && HasContentTargets())
            PlayContentFloat(floatUpPixels, hoverDuration, hoverEase);
        if (shadowLiftEffect && _shadow != null)
            PlayShadow(_baseShadowDistance + shadowLiftOffset, hoverDuration, hoverEase);
    }

    public void OnPointerExit(PointerEventData eventData)
    {
        _inside = false;
        bool wasPressed = _pressed;
        _pressed = false;
        if (!_stateCached)
            return;

        // 退出一律回基准:普通悬停离开、或按住拖出按钮(防卡在缩小/悬停态,后续
        // OnPointerUp 即使到也在按钮外,统一在此兜底)。无实际变化的维度会被
        // 各 Play 函数内的目标比对短路,不会空播。
        if (hoverEffect)
            PlayScale(_baseScale, wasPressed ? pressDuration : hoverDuration, wasPressed ? pressEase : hoverEase);
        if (hoverColorEffect && _graphic != null)
            PlayColor(_baseColor, wasPressed ? pressDuration : hoverDuration, wasPressed ? pressEase : hoverEase);
        if (contentFloatEffect && HasContentTargets())
            PlayContentFloat(0f, wasPressed ? pressDuration : hoverDuration, wasPressed ? pressEase : hoverEase);
        if (shadowLiftEffect && _shadow != null)
            PlayShadow(_baseShadowDistance, wasPressed ? pressDuration : hoverDuration, wasPressed ? pressEase : hoverEase);
    }

    // ============================================================
    // 内部实现
    // ============================================================

    /// <summary>interactable 判定:有 Button 且不可交互时不播反馈;无 Button 视作可交互。</summary>
    private bool CanRespond()
    {
        if (!isActiveAndEnabled)
            return false;
        if (_button != null && !_button.interactable)
            return false;
        return true;
    }

    /// <summary>
    /// 首次交互时统一缓存各维度基准(localScale/Graphic.color/各目标 anchoredPosition/
    /// Shadow.effectDistance)。之后所有悬停目标都在基准上变化,保证退出后能精确回到最初摆位;
    /// 父级缩放/位移不影响本物体 localScale 与子物体 anchoredPosition 相对关系。
    /// </summary>
    private void EnsureBaseState()
    {
        if (_stateCached)
            return;

        _baseScale = transform.localScale;
        if (_graphic != null)
            _baseColor = _graphic.color;
        if (_shadow != null)
            _baseShadowDistance = _shadow.effectDistance;
        if (contentTargets != null && contentTargets.Length > 0)
        {
            _basePositions = new Vector2[contentTargets.Length];
            for (int i = 0; i < contentTargets.Length; i++)
                _basePositions[i] = contentTargets[i] != null ? contentTargets[i].anchoredPosition : Vector2.zero;
        }

        _stateCached = true;

        if (shadowLiftEffect && _shadow == null && shadowTarget == null)
            Debug.LogWarning("[UIButtonFeedback] 勾了阴影浮起但同物体无 Shadow 组件,请在按钮上添加 Shadow(或拖 shadowTarget)", this);
    }

    /// <summary>悬停高亮色目标:用 hoverColor 的 RGB + 按钮基色原 alpha,避免半透明底图被盖实。</summary>
    private Color HoverColorWithBaseAlpha()
    {
        return new Color(hoverColor.r, hoverColor.g, hoverColor.b, _baseColor.a);
    }

    private bool HasContentTargets()
    {
        return contentTargets != null && contentTargets.Length > 0;
    }

    private void ResetToBaseInstant()
    {
        transform.localScale = _baseScale;
        if (_graphic != null)
            _graphic.color = _baseColor;
        if (_shadow != null)
            _shadow.effectDistance = _baseShadowDistance;
        if (_basePositions != null)
        {
            for (int i = 0; i < contentTargets.Length && i < _basePositions.Length; i++)
            {
                if (contentTargets[i] != null)
                    contentTargets[i].anchoredPosition = _basePositions[i];
            }
        }
    }

    /// <summary>播放一段缩放到 target 的动画:目标已接近时只落位不空转;同维度先 Kill 旧的。</summary>
    private void PlayScale(Vector3 target, float duration, Ease ease)
    {
        KillTween(ref _scaleTween);

        if (duration <= 0f)
        {
            transform.localScale = target;
            return;
        }

        if (CloseTo(transform.localScale, target))
        {
            transform.localScale = target;
            return;
        }

        Tween tween = transform.DOScale(target, duration).SetEase(ease);
        if (useUnscaled)
            tween.SetUpdate(true);
        _scaleTween = tween;
    }

    /// <summary>播放颜色到 target(完整 Color,含 alpha)。</summary>
    private void PlayColor(Color target, float duration, Ease ease)
    {
        KillTween(ref _colorTween);

        if (duration <= 0f)
        {
            _graphic.color = target;
            return;
        }

        if (CloseToColor(_graphic.color, target))
        {
            _graphic.color = target;
            return;
        }

        Tween tween = DOTween.To(() => _graphic.color, c => _graphic.color = c, target, duration).SetEase(ease);
        if (useUnscaled)
            tween.SetUpdate(true);
        _colorTween = tween;
    }

    /// <summary>
    /// 播放内容上浮:yOffset = floatUpPixels(进入)或 0(回基准)。
    /// 目标为 null 的槽跳过。全部目标共用一个 tween 列表,重启前整体 Kill。
    /// </summary>
    private void PlayContentFloat(float yOffset, float duration, Ease ease)
    {
        KillFloatTweens();

        for (int i = 0; i < contentTargets.Length; i++)
        {
            RectTransform target = contentTargets[i];
            if (target == null || _basePositions == null || i >= _basePositions.Length)
                continue;

            Vector2 targetPos = _basePositions[i] + new Vector2(0f, yOffset);
            if (duration <= 0f || CloseToVector2(target.anchoredPosition, targetPos))
            {
                target.anchoredPosition = targetPos;
                continue;
            }

            Vector2 captured = targetPos;
            Tween tween = DOTween.To(() => target.anchoredPosition, v => target.anchoredPosition = v, captured, duration).SetEase(ease);
            if (useUnscaled)
                tween.SetUpdate(true);
            _floatTweens.Add(tween);
        }
    }

    /// <summary>播放阴影 effectDistance 到 target。</summary>
    private void PlayShadow(Vector2 target, float duration, Ease ease)
    {
        KillTween(ref _shadowTween);

        if (duration <= 0f)
        {
            _shadow.effectDistance = target;
            return;
        }

        if (CloseToVector2(_shadow.effectDistance, target))
        {
            _shadow.effectDistance = target;
            return;
        }

        Tween tween = DOTween.To(() => _shadow.effectDistance, v => _shadow.effectDistance = v, target, duration).SetEase(ease);
        if (useUnscaled)
            tween.SetUpdate(true);
        _shadowTween = tween;
    }

    // ============================================================
    // tween 生命周期
    // ============================================================

    private static bool CloseTo(Vector3 a, Vector3 b)
    {
        return (a - b).sqrMagnitude < 0.000001f;
    }

    private static bool CloseToVector2(Vector2 a, Vector2 b)
    {
        return (a - b).sqrMagnitude < 0.000001f;
    }

    private static bool CloseToColor(Color a, Color b)
    {
        return Mathf.Abs(a.r - b.r) < 0.002f && Mathf.Abs(a.g - b.g) < 0.002f
            && Mathf.Abs(a.b - b.b) < 0.002f && Mathf.Abs(a.a - b.a) < 0.002f;
    }

    private void KillTween(ref Tween tween)
    {
        if (tween != null)
        {
            // Kill 不触发 OnComplete:中断旧动画时不让完成回调串台
            if (tween.IsActive())
                tween.Kill();
            tween = null;
        }
    }

    private void KillFloatTweens()
    {
        for (int i = 0; i < _floatTweens.Count; i++)
        {
            if (_floatTweens[i] != null && _floatTweens[i].IsActive())
                _floatTweens[i].Kill();
        }
        _floatTweens.Clear();
    }

    private void KillAllTweens()
    {
        KillTween(ref _scaleTween);
        KillTween(ref _colorTween);
        KillTween(ref _shadowTween);
        KillFloatTweens();
    }
}
