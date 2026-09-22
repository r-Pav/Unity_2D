using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// 黑屏过渡组件 — 全屏黑 Image 淡出(alpha 0→1) → SceneManager.LoadScene → 渐显(1→0)。
/// 公开方法：
///   - ToGame()：淡出 → LoadScene("SampleScene") → 渐显（进游戏）
///   - ToTitle()：淡出 → LoadScene("TitleScene") → 渐显（回主菜单，PauseMenu 返回用）
/// 单例 + DontDestroyOnLoad：首个场景加载的实例常驻跨场景成为唯一幕布；
/// 后续场景重复挂载的 TransitionCanvas 在 Awake 自动销毁（防重，同 AudioManager 模式）。
/// 必须用 Time.unscaledDeltaTime：跨场景 timeScale 无碍；淡出期间 timeScale 可能为 0
/// （PauseMenu 里触发回主菜单时游戏是暂停的）。
/// 初始状态：CanvasGroup.alpha = 0（不遮挡游戏）；淡出起 blackImage.raycastTarget = true 阻断输入，渐显完设 false。
/// 开场渐显：首个场景（启动进主菜单）时幕布初始全黑 → 淡出到透明（黑幕淡出、主界面渐渐露出）
///   → 发 OnIntroFadeFinished，订阅方（MainMenu 的标题出场）在回调里才开始播；只首个实例播一次。
/// 幕布 Canvas：不论场景里有没有 Canvas，运行时都强制 ScreenSpaceOverlay + sortingOrder 32767
///   （实测场景里挂的 Canvas 是 WorldSpace 时不铺屏 = 黑场看不见）。
/// 时长三段各管一处：introFadeDuration 开场渐显 / fadeOutDuration 切场景淡出 / fadeInDuration 切场景渐显；
///   生效的是启动场景那份（常驻实例）。
/// 单帧步进夹上限（MaxFrameStep）：同步 LoadScene 后第一帧的 unscaledDeltaTime 含整段加载耗时
///   （实测 0.84 秒），不夹住渐变会被一帧跳完、新场景看起来"直接亮"。
/// </summary>
[DefaultExecutionOrder(-10000)]
public class SceneTransition : MonoBehaviour
{
    private static SceneTransition _instance;

    /// <summary>
    /// 当前实例。无 Find 兜底：靠 Awake 接管(判 _instance != this) + OnDestroy 自清维护，
    /// 避免兜底把"还没 Awake 的自己"提前写进静态字段导致自身被当重复实例销毁。
    /// </summary>
    public static SceneTransition Instance => _instance;

    [Header("过渡幕布")]
    [Tooltip("全屏黑 Image（拉伸覆盖全屏），用于 raycast 阻断输入")]
    [SerializeField] private Image blackImage = null;

    //[Tooltip("单段淡出/淡入时长（秒）")]   // 已拆成下面两个,注释留档
    //[SerializeField] private float fadeDuration = 0.5f;

    [Tooltip("切场景淡出时长（秒）：旧场景渐渐变黑那一段")]
    [SerializeField] private float fadeOutDuration = 0.5f;

    [Tooltip("切场景渐显时长（秒）：新场景从全黑渐渐亮起那一段")]
    [SerializeField] private float fadeInDuration = 0.5f;

    [Tooltip("幕布 CanvasGroup：控制整体 alpha（0 透明不遮挡，1 全黑）")]
    [SerializeField] private CanvasGroup canvasGroup = null;

    [Header("开场渐显（启动进主界面）")]
    [Tooltip("true = 启动进首个场景时幕布初始全黑并淡出到透明（主界面渐渐露出）；false = 直接显示")]
    [SerializeField] private bool playIntroFadeOnStart = true;
    [Tooltip("开场渐显时长（秒）；≤0 时用切场景渐显时长 fadeInDuration")]
    [SerializeField] private float introFadeDuration = 0.6f;

    [Header("过渡期 BGM")]
    [Tooltip("过渡时把 BGM 渐隐,进入新场景后再渐显(进游戏 / 回主菜单都生效)")]
    [SerializeField] private bool fadeBgmOnTransition = true;
    [Tooltip("BGM 渐入时长(秒)")]
    [SerializeField] private float bgmFadeInDuration = 1f;
    [Tooltip("额外静默(秒):BGM 静默总时长 = 黑场淡出 + 黑场渐显 + 该值,从过渡开始计时")]
    [SerializeField] private float bgmExtraSilence = 1f;

    private const string GameSceneName = "SampleScene";
    private const string TitleSceneName = "TitleScene";

    /// <summary>常驻幕布自带 Canvas 的排序值，压在所有 UI 之上（拉满 short 上限,防任何 UI 盖住幕布）</summary>
    private const int PersistSortingOrder = 32767;

    /// <summary>
    /// 单帧最大步进（秒）。场景切换是同步 LoadScene，加载后第一帧的 unscaledDeltaTime 会包含整段加载耗时
    /// （实测 0.84 秒）；不夹住的话渐变循环第一帧就越过总时长 → 渐显被一帧跳完、新场景看起来"直接亮"。
    /// </summary>
    private const float MaxFrameStep = 0.1f;

    /// <summary>是否正在过渡（防重入：过渡中忽略新的 ToGame/ToTitle）</summary>
    private bool _isTransitioning;

    /// <summary>开场渐显是否正在播放（订阅方据此延后自己的出场动画）</summary>
    private bool _isIntroFading;

    /// <summary>开场渐显完成（MainMenu 的标题出场等在此回调里启动）</summary>
    public event System.Action OnIntroFadeFinished;

    /// <summary>开场渐显是否正在播放</summary>
    public bool IsIntroFading => _isIntroFading;

    private void Awake()
    {
        // 单例防重 + 常驻跨场景：首个实例成为唯一过渡幕布，后续场景的重复实例自动销毁
        // 判 != this：别的脚本先摸 Instance 时不会把自己算成重复实例（同 AudioManager）
        if (_instance != null && _instance != this)
        {
            Destroy(gameObject);
            return;
        }
        _instance = this;

        MakePersistable();

        // 开场渐显：首个场景先全黑 + 挡输入,Start 里淡出到透明；否则维持透明的原行为
        if (playIntroFadeOnStart)
        {
            _isIntroFading = true;
            if (canvasGroup != null) canvasGroup.alpha = 1f;          // 先全黑
            if (blackImage != null) blackImage.raycastTarget = true;   // 开场渐显期间不响应点击
        }
        else
        {
            if (canvasGroup != null) canvasGroup.alpha = 0f;
            if (blackImage != null) blackImage.raycastTarget = false;
        }
    }

    private void Start()
    {
        if (_isIntroFading)
            StartCoroutine(IntroFadeRoutine());
    }

    /// <summary>开场：全黑 → 透明（黑幕淡出，主界面渐渐露出），播完发包并放开输入</summary>
    private IEnumerator IntroFadeRoutine()
    {
        float dur = introFadeDuration > 0f ? introFadeDuration : fadeInDuration;
        yield return StartCoroutine(FadeRoutine(0f, dur));

        if (blackImage != null) blackImage.raycastTarget = false;
        _isIntroFading = false;
        OnIntroFadeFinished?.Invoke();
    }

    /// <summary>
    /// 让本组件真正能跨场景常驻。
    /// DontDestroyOnLoad 只对根对象生效，而本对象通常挂在场景 Canvas 下（非根），直接调用会报
    /// "DontDestroyOnLoad only works for root GameObjects"，幕布跨场景直接丢失；因此先提到根，
    /// 再保证自带 Canvas + GraphicRaycaster（否则黑幕画不出来、也挡不住输入）。
    /// 不再只在「没有 Canvas」时才配置：场景里若已挂 Canvas（实测 WorldSpace / overrideSorting=False），
    /// 旧写法整段跳过 → 幕布按世界空间渲染，位置随场景相机漂移，屏幕上根本看不到黑场。
    /// </summary>
    private void MakePersistable()
    {
        if (transform.parent != null)
            transform.SetParent(null, true);   // worldPositionStays = true：视觉位置与尺寸不变

        Canvas canvas = GetComponent<Canvas>();
        if (canvas == null) canvas = gameObject.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;   // 幕布必须屏幕空间叠加,否则不铺屏
        canvas.overrideSorting = true;
        canvas.sortingOrder = PersistSortingOrder;           // 幕布压在所有 UI 之上
        canvas.sortingLayerID = 0;
        if (GetComponent<GraphicRaycaster>() == null)
            gameObject.AddComponent<GraphicRaycaster>();      // 黑幕靠 raycastTarget 阻断输入，需要它才能被射线命中

        DontDestroyOnLoad(gameObject);
    }

    /// <summary>自清单例引用：Instance 不再需要 Find 兜底(销毁后静态字段不留脏引用；重复实例自毁时 _instance != this 不会误清)</summary>
    private void OnDestroy()
    {
        if (_instance == this) _instance = null;
    }

    /// <summary>进游戏：淡出 → LoadScene("SampleScene") → 渐显</summary>
    public void ToGame()
    {
        StartTransition(GameSceneName);
    }

    /// <summary>回主菜单：淡出 → LoadScene("TitleScene") → 渐显（PauseMenu 返回用）</summary>
    public void ToTitle()
    {
        StartTransition(TitleSceneName);
    }

    private void StartTransition(string sceneName)
    {
        if (_isTransitioning) return;
        StartCoroutine(TransitionRoutine(sceneName));
    }

    private IEnumerator TransitionRoutine(string sceneName)
    {
        _isTransitioning = true;

        // 淡出期阻断输入，避免过渡中误触按钮
        if (blackImage != null) blackImage.raycastTarget = true;

        // BGM 随黑场一起渐隐(系数叠加在用户设置音量上,不动设置本身);进游戏 / 回主菜单都生效
        bool fadeBgm = fadeBgmOnTransition;
        float transitionStartTime = Time.realtimeSinceStartup;
        if (fadeBgm) AudioManager.Instance?.FadeBgmMultiplier(0f, fadeOutDuration);

        yield return StartCoroutine(FadeRoutine(1f, fadeOutDuration));   // 淡出：0 → 1（全黑）

        yield return null;   // 先把"全黑"这一帧提交出去再加载,否则同步 LoadScene 在同一帧完成卸载+加载,
                             // 加载卡顿期间屏幕停留的是加载前那一帧(还没全黑)→ 看起来"卡一下能看到页面 UI"

        // 切场景前先取消管道接管:AreaChannelTrigger 的移动/缩放协程是挂在 VCam 上的(场景根对象,
        // 不在任何区域下),LoadScene 会把 VCam 连同协程宿主一起销毁 → 协程在 yield 后无法继续,
        // Unity 打印 "Coroutine continue failure"(2026-09-22 saika 报)。先停掉,
        // 顺带恢复玩家输入/速度,不把管道接管状态留到下一个场景。
        AreaChannelTrigger.CancelMove();

        SceneManager.LoadScene(sceneName);   // 切场景（本组件 DontDestroyOnLoad，跨场景存活继续渐显）

        yield return null;   // 等新场景第一帧（幕布此时仍全黑）

        yield return StartCoroutine(FadeRoutine(0f, fadeInDuration));    // 渐显：1 → 0（新场景从黑亮起）

        if (blackImage != null) blackImage.raycastTarget = false;

        // BGM 渐显:静默总时长 = 黑场淡出 + 黑场渐显 + 额外静默(从过渡开始计时,扣掉已经过去的时间)
        if (fadeBgm && AudioManager.Instance != null)
        {
            float silenceTotal = fadeOutDuration + fadeInDuration + bgmExtraSilence;
            float waited = Mathf.Max(0f, silenceTotal - (Time.realtimeSinceStartup - transitionStartTime));
            if (waited > 0f) yield return new WaitForSecondsRealtime(waited);
            AudioManager.Instance.FadeBgmMultiplier(1f, bgmFadeInDuration);
        }

        _isTransitioning = false;
    }

    /// <summary>
    /// alpha 线性渐变到 targetAlpha（Time.unscaledDeltaTime：暂停/跨场景均不受 timeScale 影响）。
    /// 单帧步进夹 MaxFrameStep 上限：场景切换后第一帧的 deltaTime 含加载耗时,不夹会把整段动画一帧跳完。
    /// duration ≤ 0 时用切场景渐显时长 fadeInDuration 兜底。
    /// </summary>
    private IEnumerator FadeRoutine(float targetAlpha, float duration = -1f)
    {
        if (canvasGroup == null) yield break;

        float dur = duration > 0f ? duration : fadeInDuration;
        float startAlpha = canvasGroup.alpha;
        float elapsed = 0f;
        while (elapsed < dur)
        {
            elapsed += Mathf.Min(Time.unscaledDeltaTime, MaxFrameStep);
            float t = Mathf.Clamp01(elapsed / dur);
            canvasGroup.alpha = Mathf.Lerp(startAlpha, targetAlpha, t);
            yield return null;
        }
        canvasGroup.alpha = targetAlpha;
    }
}
