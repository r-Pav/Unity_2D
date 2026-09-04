using System;
using DG.Tweening;
using UnityEngine;

/// <summary>
/// 面板内容错峰入场组件(S5)— 挂在面板"内容容器"上,负责面板打开后
/// "标题→列表项→按钮组"逐项延迟浮入(错峰级联:第 i 项在第 i*stagger 秒开始,
/// 项与项动画互相重叠,不是串行等待)。
///
/// 设计约定:
/// - 每个元素的"摆放位"= 面板激活后、元素处于 Inspector 摆位时的 anchoredPosition。
///   首次调用 ResetToHidden/Play 时若尚未缓存,则以当时位置记录为摆放位
///   (调用方需保证该时刻元素处于摆位、alpha=1;RectTransform 在隐藏下也能读,
///   但保险起见 Play/ResetToHidden 须在面板 SetActive(true) 后调用)。
/// - 与 UIPanelMotion 共用:CanvasGroup 相乘(根面板淡入 + 元素错峰淡入视觉叠加可接受),
///   典型链路:面板 SetActive(true) → UIPanelMotion.PlayOpen(整板淡入) → 其 OnComplete 里调
///   reveal.Play(onDone),即"先整板淡入、再内容错峰"。
/// - 若不想整板开启动画期间内容裸露(如滑入式面板),可先调 ResetToHidden() 预置隐藏,
///   面板开启动画完成后再 Play()。
/// - 不自动触发:不在 Update 轮询,由调用方决定何时调 Play。
/// - 内部 Tween 按 useUnscaled 决定是否 SetUpdate(true),与 UIPanelMotion 一致。
/// </summary>
public class UIContentReveal : MonoBehaviour
{
    [Header("内容元素")]
    [Tooltip("按入场顺序排列的面板内容元素(标题→列表项→按钮组);fadeIn=true 时元素自动补 CanvasGroup 控制透明度(容器会连同子物体一起淡入,纯 Image/Text 亦可)")]
    [SerializeField] private RectTransform[] elements = null;

    [Header("错峰参数")]
    [Tooltip("每项之间错峰延迟(秒):第 i 项起点时间 = i*stagger;第 0 项立即开始")]
    [SerializeField] private float stagger = 0.05f;

    [Tooltip("单项动画时长(秒)")]
    [SerializeField] private float duration = 0.25f;

    [Tooltip("true=透明度 0→1 淡入(每元素自动补 CanvasGroup);false=不动透明度")]
    [SerializeField] private bool fadeIn = true;

    [Tooltip("true=带小位移入场:从下方 slideRevealOffset 处滑回摆放位;false=纯原地淡入")]
    [SerializeField] private bool slideIn = false;

    [Tooltip("滑入起点偏移:起点 anchoredPosition = 摆放位 + (0, -slideRevealOffset),仅 slideIn=true 时用")]
    [SerializeField] private float slideRevealOffset = 30f;

    [Header("曲线与时间")]
    [Tooltip("单项动画缓动曲线")]
    [SerializeField] private Ease ease = Ease.OutCubic;

    [Tooltip("true=使用不受 Time.timeScale 影响的时间(暂停时动画照播);false=跟随 timeScale")]
    [SerializeField] private bool useUnscaled = true;

    // 按 elements 索引对齐的缓存:摆放位 / 是否已缓存 / 懒补的 CanvasGroup
    private Vector2[] _homePositions;
    private bool[] _homeCached;
    private CanvasGroup[] _groups;
    private Tween _activeTween;
    private bool _playing;
    private bool _warnedEmpty;
    private bool _warnedNullSlot;

    /// <summary>当前是否有错峰动画在播放(供调用方防连点参考)</summary>
    public bool IsPlaying
    {
        get { return _playing; }
    }

    private void OnDisable()
    {
        // 内容容器被隐藏(含外部 SetActive(false) 打断动画)时终止残留 Tween,
        // 防止 onDone 在错误时机串台;下次打开前 ResetToHidden 会重新归位
        KillActiveTween();
    }

    private void OnDestroy()
    {
        KillActiveTween();
    }

    /// <summary>
    /// 播放内容错峰入场。前置条件:所在面板已 SetActive(true)、元素处于摆位(alpha=1)。
    /// 首个 Play 会顺带缓存每个元素的摆放位;重复调用先 Kill 旧动画再播。
    /// 播完:全部元素复位(alpha=1、位置=摆放位),回调 onDone。
    /// </summary>
    public void Play(Action onDone = null)
    {
        KillActiveTween();

        if (!ValidateElements())
        {
            onDone?.Invoke();
            return;
        }

        // 摆放位缓存:首个 Play 时元素应处于摆位,读当前 anchoredPosition 即摆放位
        // (若之前调过 ResetToHidden 则已缓存,这里 no-op;摆放位不受隐藏态影响)
        CacheAllHomes();

        // 无实际动画(非正时长 / fadeIn 与 slideIn 全关):直接落到完成态,立即回调
        if (duration <= 0f || (!fadeIn && !slideIn))
        {
            ForceRest();
            onDone?.Invoke();
            return;
        }

        _playing = true;

        Sequence seq = DOTween.Sequence();
        int built = 0;
        for (int i = 0; i < elements.Length; i++)
        {
            RectTransform rt = elements[i];
            if (rt == null)
            {
                WarnNullSlotOnce();
                continue;
            }
            Vector2 home = _homePositions[i];
            CanvasGroup group = fadeIn ? EnsureGroup(i) : null;

            // 第 i 项在 0 时刻先跳隐藏起点(alpha=0、滑入起点),到 i*stagger 时间点再浮出;
            // 这样整条 Sequence 里所有元素起点一致,由各自 Insert 时间错峰触发
            if (fadeIn && group != null)
                group.alpha = 0f;
            if (slideIn)
                rt.anchoredPosition = home - new Vector2(0f, slideRevealOffset);

            Tween item = BuildItemTween(rt, group, home);
            if (item != null)
            {
                seq.Insert(Mathf.Max(0f, stagger * i), item);
                built++;
            }
        }

        if (built == 0)
        {
            ForceRest();
            _playing = false;
            onDone?.Invoke();
            return;
        }

        seq.OnComplete(() => Finish(onDone));
        ApplyTime(seq);
        _activeTween = seq;
    }

    /// <summary>
    /// 预置隐藏态:全部元素 alpha=0、slideIn=true 时位置移到滑入起点,
    /// 供面板"打开动画前"先置隐藏、开启动画完成后 Play 再做错峰入场。
    /// 重复调用幂等;会先 Kill 进行中的动画。
    /// </summary>
    public void ResetToHidden()
    {
        KillActiveTween();

        if (!ValidateElements())
            return;

        // ResetToHidden 于面板刚激活、元素在摆位时调用 → 此处缓存即摆放位
        CacheAllHomes();

        for (int i = 0; i < elements.Length; i++)
        {
            RectTransform rt = elements[i];
            if (rt == null)
            {
                WarnNullSlotOnce();
                continue;
            }
            if (slideIn)
                rt.anchoredPosition = _homePositions[i] - new Vector2(0f, slideRevealOffset);
            if (fadeIn)
                EnsureGroup(i).alpha = 0f;
        }
    }

    // ============================================================
    // 内部实现
    // ============================================================

    /// <summary>
    /// 校验 elements 配置并重建索引缓存。空数组 / 未配置时 Warning 一次并返回 false。
    /// </summary>
    private bool ValidateElements()
    {
        if (elements == null || elements.Length == 0)
        {
            if (!_warnedEmpty)
            {
                _warnedEmpty = true;
                Debug.LogWarning("[UIContentReveal] " + name + ":elements 未配置任何内容元素,错峰入场已跳过", this);
            }
            return false;
        }

        if (_homePositions == null || _homePositions.Length != elements.Length)
        {
            _homePositions = new Vector2[elements.Length];
            _homeCached = new bool[elements.Length];
            _groups = new CanvasGroup[elements.Length];
        }
        return true;
    }

    /// <summary>逐元素缓存摆放位:仅首次记录当前位置(elements 数组 resize 后按新索引重新缓存)。</summary>
    private void CacheAllHomes()
    {
        for (int i = 0; i < elements.Length; i++)
        {
            RectTransform rt = elements[i];
            if (rt == null)
            {
                WarnNullSlotOnce();
                continue;
            }
            if (!_homeCached[i])
            {
                _homePositions[i] = rt.anchoredPosition;
                _homeCached[i] = true;
            }
        }
    }

    /// <summary>确保元素带 CanvasGroup(fadeIn 时才需要;没有则自动补)。</summary>
    private CanvasGroup EnsureGroup(int i)
    {
        if (_groups[i] == null)
        {
            _groups[i] = elements[i].GetComponent<CanvasGroup>();
            if (_groups[i] == null)
                _groups[i] = elements[i].gameObject.AddComponent<CanvasGroup>();
        }
        return _groups[i];
    }

    /// <summary>单项浮出动画:fade 与滑回摆放位并行(视开关组合),共用 duration/ease。</summary>
    private Tween BuildItemTween(RectTransform rt, CanvasGroup group, Vector2 home)
    {
        if (fadeIn && slideIn)
        {
            Sequence item = DOTween.Sequence();
            item.Join(group.DOFade(1f, duration).SetEase(ease));
            item.Join(rt.DOAnchorPos(home, duration).SetEase(ease));
            return item;
        }
        if (fadeIn)
            return group.DOFade(1f, duration).SetEase(ease);
        if (slideIn)
            return rt.DOAnchorPos(home, duration).SetEase(ease);
        return null;
    }

    private void Finish(Action onDone)
    {
        ForceRest();
        _activeTween = null;
        _playing = false;
        onDone?.Invoke();
    }

    /// <summary>
    /// 复位全部元素到完成态:alpha=1、位置=摆放位(视开关组合)。
    /// 保证动画结束/中断后无残留、反复开关不累积偏移。
    /// </summary>
    private void ForceRest()
    {
        for (int i = 0; i < elements.Length; i++)
        {
            RectTransform rt = elements[i];
            if (rt == null)
                continue;
            if (_homeCached[i])
            {
                if (slideIn)
                    rt.anchoredPosition = _homePositions[i];
            }
            if (fadeIn)
                EnsureGroup(i).alpha = 1f;
        }
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

    private void WarnNullSlotOnce()
    {
        if (!_warnedNullSlot)
        {
            _warnedNullSlot = true;
            Debug.LogWarning("[UIContentReveal] " + name + ":elements 含空槽(未拖入物体的槽位),已跳过该元素", this);
        }
    }
}
