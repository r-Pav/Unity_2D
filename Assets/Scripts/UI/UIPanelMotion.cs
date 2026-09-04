using System;
using DG.Tweening;
using UnityEngine;

/// <summary>
/// UI 面板开/关动效组件 — 挂在面板根物体上(须带 RectTransform)。
/// 约定:面板在场景里保持 inactive,Inspector 摆放好的位置与 alpha=1 即目标态;
/// 本组件不持久化目标值,每次播放前读当前 RectTransform.anchoredPosition 作为目标位,
/// 动画播完自动把面板复位到起始摆位/alpha=1,保证反复开关不累积偏移。
///
/// PlayOpen 前调用方(PanelManager)需先 SetActive(true);
/// PlayClose 播完回调 onDone,由调用方负责 SetActive(false) —— 本组件不碰 active 状态,
/// 避免与 PanelManager 栈逻辑打架。
/// 内部 Tween 按 useUnscaled 决定是否 SetUpdate(true):true 时 timeScale=0(暂停菜单)动画照播。
/// </summary>
public class UIPanelMotion : MonoBehaviour
{
    /// <summary>
    /// 面板开/关动效类型。
    /// 打开语义:面板从该方向进入(如 SlideLeft = 从左侧滑入到 Inspector 摆放位置);
    /// 关闭语义由 closeEffect 同名字段表示反向(closeEffect=SlideLeft = 从当前位置向左滑出)。
    /// </summary>
    public enum PanelMotionEffect
    {
        /// <summary>无动画:打开立即显示(alpha=1),关闭立即隐藏(alpha=0)</summary>
        None,

        /// <summary>淡入(alpha 0→1)/淡出(alpha 1→0)</summary>
        Fade,

        /// <summary>打开:从左侧滑入;关闭:向左滑出</summary>
        SlideLeft,

        /// <summary>打开:从右侧滑入;关闭:向右滑出</summary>
        SlideRight,

        /// <summary>打开:从上方滑入;关闭:向上滑出</summary>
        SlideTop,

        /// <summary>打开:从下方滑入;关闭:向下滑出</summary>
        SlideBottom
    }

    [Header("开关动效")]
    [Tooltip("打开动效:None=无动画,Fade=淡入,SlideX=面板从该方向滑入到 Inspector 摆放位置")]
    [SerializeField] private PanelMotionEffect openEffect = PanelMotionEffect.Fade;

    [Tooltip("关闭动效:None=无动画,Fade=淡出,SlideX=面板从当前位置向该方向滑出屏幕")]
    [SerializeField] private PanelMotionEffect closeEffect = PanelMotionEffect.Fade;

    [Tooltip("打开动画时长(秒)")]
    [SerializeField] private float openDuration = 0.2f;

    [Tooltip("关闭动画时长(秒)")]
    [SerializeField] private float closeDuration = 0.2f;

    [Tooltip("屏幕外滑入/滑出偏移量:横向=(slideDistance,0),纵向=(0,slideDistance);与 PauseMenu.slideOffset 思路一致")]
    [SerializeField] private float slideDistance = 1600f;

    [Tooltip("打开动画缓动曲线")]
    [SerializeField] private Ease openEase = Ease.OutCubic;

    [Tooltip("关闭动画缓动曲线")]
    [SerializeField] private Ease closeEase = Ease.InCubic;

    [Tooltip("true=使用不受 Time.timeScale 影响的时间(暂停时动画照播);false=跟随 timeScale")]
    [SerializeField] private bool useUnscaled = true;

    private RectTransform _rect;
    private CanvasGroup _group;
    private Tween _activeTween;
    private bool _playing;
    private bool _warnedNoRect;

    /// <summary>当前是否有开关动画在播放(供 PanelManager 做防连点/栈保护参考)</summary>
    public bool IsPlaying
    {
        get { return _playing; }
    }

    private void OnDisable()
    {
        // 面板被隐藏(含外部直接 SetActive(false) 打断动画)时终止残留 Tween,
        // 防止动画在隐藏物体上继续跑完并回调 onDone 与 PanelManager 栈状态串台
        KillActiveTween();
    }

    private void OnDestroy()
    {
        KillActiveTween();
    }

    /// <summary>
    /// 播放打开动效。
    /// 前置条件:gameObject 已 SetActive(true)(调用方负责),anchoredPosition 处于 Inspector 摆位。
    /// 播完回调 onDone。重复调用会先 Kill 旧动画再播。
    /// </summary>
    public void PlayOpen(Action onDone = null)
    {
        KillActiveTween();

        RectTransform rect = EnsureRect();
        if (rect == null)
        {
            onDone?.Invoke();
            return;
        }
        CanvasGroup group = EnsureCanvasGroup();

        _playing = true;

        // None / 非正时长:不做动画,直接落到打开目标态(alpha=1,位置保持当前)
        if (openDuration <= 0f || openEffect == PanelMotionEffect.None)
        {
            group.alpha = 1f;
            _playing = false;
            onDone?.Invoke();
            return;
        }

        if (openEffect == PanelMotionEffect.Fade)
        {
            group.alpha = 0f;
            Tween fade = group.DOFade(1f, openDuration);
            ApplyTime(fade);
            _activeTween = fade;
            fade.OnComplete(() => FinishOpen(group, onDone));
            return;
        }

        // Slide:当前摆位即目标位,起点 = 目标位 + 来向偏移(该方向屏幕外);
        // 先跳起点 + alpha 置 0,再并行滑回摆位与淡入
        Vector2 to = rect.anchoredPosition;
        Vector2 from = to + SlideOffset(openEffect);
        group.alpha = 0f;
        rect.anchoredPosition = from;

        Sequence seq = DOTween.Sequence();
        seq.Join(rect.DOAnchorPos(to, openDuration).SetEase(openEase));
        seq.Join(group.DOFade(1f, openDuration));
        ApplyTime(seq);
        _activeTween = seq;
        seq.OnComplete(() => FinishOpen(group, onDone));
    }

    /// <summary>
    /// 播放关闭动效。
    /// 播完回调 onDone,由调用方负责 SetActive(false)(本组件不碰 active 状态)。
    /// 动画终点在屏幕外;播完同一帧内复位摆位与 alpha,视觉无跳变,
    /// 且下次 PlayOpen 读到的 anchoredPosition 仍是 Inspector 摆位,不会累积偏移。
    /// </summary>
    public void PlayClose(Action onDone = null)
    {
        KillActiveTween();

        RectTransform rect = EnsureRect();
        if (rect == null)
        {
            onDone?.Invoke();
            return;
        }
        CanvasGroup group = EnsureCanvasGroup();

        _playing = true;

        // None / 非正时长:不做动画,直接落到关闭目标态(alpha=0,位置保持当前)
        if (closeDuration <= 0f || closeEffect == PanelMotionEffect.None)
        {
            group.alpha = 0f;
            _playing = false;
            onDone?.Invoke();
            return;
        }

        // 起始摆位快照:无论淡出还是滑出,播完都复位到这里(面板关闭前处于全开摆位)
        Vector2 homePos = rect.anchoredPosition;

        if (closeEffect == PanelMotionEffect.Fade)
        {
            Tween fade = group.DOFade(0f, closeDuration);
            ApplyTime(fade);
            _activeTween = fade;
            fade.OnComplete(() => FinishClose(rect, group, homePos, onDone));
            return;
        }

        // Slide:从当前摆位滑向"该方向屏幕外",alpha 同步 1→0
        Vector2 off = homePos + SlideOffset(closeEffect);
        Sequence seq = DOTween.Sequence();
        seq.Join(rect.DOAnchorPos(off, closeDuration).SetEase(closeEase));
        seq.Join(group.DOFade(0f, closeDuration));
        ApplyTime(seq);
        _activeTween = seq;
        seq.OnComplete(() => FinishClose(rect, group, homePos, onDone));
    }

    // ============================================================
    // 内部实现
    // ============================================================

    private void FinishOpen(CanvasGroup group, Action onDone)
    {
        group.alpha = 1f; // Tween 已把位置/alpha 推到终点,兜底保证落在目标态
        _activeTween = null;
        _playing = false;
        onDone?.Invoke();
    }

    private void FinishClose(RectTransform rect, CanvasGroup group, Vector2 homePos, Action onDone)
    {
        // 动画终点在屏幕外;SetActive(false) 前复位摆位与 alpha(同一帧无视觉跳变),
        // 保证下次 PlayOpen 读到的 anchoredPosition 仍是 Inspector 摆位,不累积偏移
        rect.anchoredPosition = homePos;
        group.alpha = 1f;
        _activeTween = null;
        _playing = false;
        onDone?.Invoke();
    }

    private void KillActiveTween()
    {
        if (_activeTween != null)
        {
            // Kill 不触发 OnComplete:中断旧动画时,旧 onDone 不会在错误时机串台
            if (_activeTween.IsActive())
                _activeTween.Kill();
            _activeTween = null;
        }
        _playing = false;
    }

    private void ApplyTime(Tween tween)
    {
        if (useUnscaled)
            tween.SetUpdate(true);
    }

    /// <summary>
    /// 滑入/滑出方向偏移(anchoredPosition 坐标系,与 PauseMenu.slideOffset 思路一致):
    /// 左 = 负 X,右 = 正 X,上 = 正 Y,下 = 负 Y。
    /// </summary>
    private Vector2 SlideOffset(PanelMotionEffect effect)
    {
        switch (effect)
        {
            case PanelMotionEffect.SlideLeft: return new Vector2(-slideDistance, 0f);
            case PanelMotionEffect.SlideRight: return new Vector2(slideDistance, 0f);
            case PanelMotionEffect.SlideTop: return new Vector2(0f, slideDistance);
            case PanelMotionEffect.SlideBottom: return new Vector2(0f, -slideDistance);
            default: return Vector2.zero;
        }
    }

    private RectTransform EnsureRect()
    {
        if (_rect == null)
        {
            _rect = GetComponent<RectTransform>();
            if (_rect == null && !_warnedNoRect)
            {
                _warnedNoRect = true;
                Debug.LogWarning("[UIPanelMotion] " + name + ":未找到 RectTransform(组件应挂在 UI 面板根物体),已跳过", this);
            }
        }
        return _rect;
    }

    private CanvasGroup EnsureCanvasGroup()
    {
        if (_group == null)
        {
            _group = GetComponent<CanvasGroup>();
            if (_group == null)
                _group = gameObject.AddComponent<CanvasGroup>();
        }
        return _group;
    }
}
