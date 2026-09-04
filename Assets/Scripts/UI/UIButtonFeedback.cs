using DG.Tweening;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// 按钮按压/悬停反馈组件(S6)— 挂在按钮根物体上(要求同物体有 Button 或任意可交互 UI 元素,
/// 推荐挂 Button 物体;无 Button 也可工作,只要它能收到指针事件)。
/// 反馈内容:按下缩小到 pressScale 再回弹,可选悬停放大到 hoverScale。
///
/// 设计约定:
/// - 用指针接口(IPointerDown/Up/Enter/Exit)而非监听 onClick:Button.onClick 在 PointerClick
///   才触发,按压反馈须在 PointerDown 就开始;本组件与 Button 同物体共存时两者都会收到事件
///   (EventSystem 广播,Selectable 不拦截),本组件只做纯视觉、不改 Selectable 状态,不冲突。
/// - 缩放基准 = 首次交互时缓存的 localScale:面板 pop 等根级缩放不影响子物体 localScale,
///   基准不受父级影响安全;但若按钮自身在交互前有入场缩放动画(如从 0 放大到 1),
///   需等它播放完成后再交互,否则基准会被缓存成动画中间值。
/// - interactable=false 时不播反馈(灰按钮不响应);交互中按钮被禁用等场景由 OnDisable 兜底复位。
/// - 中断安全:每次开新动画前 Kill 旧的;OnDisable/OnDestroy Kill 并把 localScale 复位到基准,
///   防止面板关闭时把缩放残留带进隐藏态。
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
    [Tooltip("true=鼠标悬停时放大到 hoverScale;false=悬停无反馈")]
    [SerializeField] private bool hoverEffect = false;

    [Tooltip("悬停放大倍率(相对初始 localScale 的乘法系数,如 1.05=放大 5%)")]
    [SerializeField] private float hoverScale = 1.05f;

    [Tooltip("悬停进入/离开动画时长(秒)")]
    [SerializeField] private float hoverDuration = 0.12f;

    [Tooltip("悬停动画缓动曲线")]
    [SerializeField] private Ease hoverEase = Ease.OutCubic;

    [Header("时间模式")]
    [Tooltip("true=使用不受 Time.timeScale 影响的时间(timeScale=0 暂停时动画照播);false=跟随 timeScale")]
    [SerializeField] private bool useUnscaled = true;

    private Button _button;
    private Vector3 _baseScale = Vector3.one;
    private bool _scaleCached;
    private bool _pressed;
    private bool _inside;
    private Tween _activeTween;

    private void OnEnable()
    {
        // 每次启用重新取一次,避免按钮运行时增删导致引用过期
        _button = GetComponent<Button>();
    }

    private void OnDisable()
    {
        _pressed = false;
        _inside = false;
        KillActiveTween();

        // 面板/按钮被隐藏(含 SetActive(false) 打断动画)时把 localScale 复位到基准,
        // 防止中途缩放残留到下次激活。仅运行期写 transform,避免退出播放模式时把
        // 运行期缩放写回场景物体。
        if (_scaleCached && Application.isPlaying)
            transform.localScale = _baseScale;
    }

    private void OnDestroy()
    {
        KillActiveTween();
    }

    // ============================================================
    // 指针接口(纯视觉反馈,不改 Selectable 状态、不触发 onClick)
    // ============================================================

    public void OnPointerDown(PointerEventData eventData)
    {
        if (!CanRespond())
            return;
        EnsureBaseScale();
        _pressed = true;
        if (pressEffect)
            PlayScale(_baseScale * pressScale, pressDuration, pressEase);
    }

    public void OnPointerUp(PointerEventData eventData)
    {
        if (!_pressed)
            return; // 非本组件承接的按下(如按住拖出后已由 Exit 复位),无需处理
        _pressed = false;
        if (!_scaleCached)
            return;

        // 松开回弹:仍悬停在按钮内且开 hoverEffect → 回弹到 hoverScale,否则回基准
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
        if (!hoverEffect || _pressed)
            return; // 按住状态下不会触发 Enter,此处仅为状态顺序兜底
        EnsureBaseScale();
        PlayScale(_baseScale * hoverScale, hoverDuration, hoverEase);
    }

    public void OnPointerExit(PointerEventData eventData)
    {
        _inside = false;
        bool wasPressed = _pressed;
        _pressed = false;
        if (!_scaleCached)
            return;

        if (wasPressed)
        {
            // 按住拖出按钮:立即回基准,防卡在缩小态(后续 OnPointerUp 也会到,但可能在按钮外)
            PlayScale(_baseScale, pressDuration, pressEase);
        }
        else if (hoverEffect)
        {
            // 普通悬停离开:回基准
            PlayScale(_baseScale, hoverDuration, hoverEase);
        }
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
    /// 缩放基准(首次交互时缓存当前 localScale)。之后所有目标都在基准上乘系数,
    /// 保证按下/悬停后能精确回到最初摆位;父级缩放不影响本物体 localScale。
    /// </summary>
    private void EnsureBaseScale()
    {
        if (!_scaleCached)
        {
            _baseScale = transform.localScale;
            _scaleCached = true;
        }
    }

    /// <summary>播放一段缩放到 target 的动画:先 Kill 旧的(防重入叠加),按 useUnscaled 决定时间模式。</summary>
    private void PlayScale(Vector3 target, float duration, Ease ease)
    {
        // 已在目标值附近(如开关组合下无实际变化):只清残留动画并落位,不空转一段 tween
        if ((target - transform.localScale).sqrMagnitude < 0.000001f)
        {
            KillActiveTween();
            transform.localScale = target;
            return;
        }

        KillActiveTween();

        if (duration <= 0f)
        {
            transform.localScale = target;
            return;
        }

        Tween tween = transform.DOScale(target, duration).SetEase(ease);
        if (useUnscaled)
            tween.SetUpdate(true);
        _activeTween = tween;
    }

    private void KillActiveTween()
    {
        if (_activeTween != null)
        {
            // Kill 不触发 OnComplete:中断旧动画时不让完成回调串台
            if (_activeTween.IsActive())
                _activeTween.Kill();
            _activeTween = null;
        }
    }
}
