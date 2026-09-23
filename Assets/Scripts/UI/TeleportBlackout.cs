using System;
using System.Collections;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 传送黑场幕布(石碑系统 T5)— 挂在场景内 BlackoutCanvas(Canvas 最上层、常驻)上的组件。
///
/// 职责:淡出到全黑 → 全黑内执行 onFullyBlack(传送/区显隐/存档都在全黑里做)→ 淡入回游戏。
/// 模式抄 SceneTransition.FadeRoutine:
/// - Time.unscaledDeltaTime:传送页 PauseGame=true → timeScale=0(世界冻结防敌人偷袭),
///   幕布淡入淡出不受暂停影响,保证黑场流程走完;
/// - blackImage.raycastTarget 在淡出期置 true 挡输入(点不到背后 UI),淡入完置 false。
///
/// 与 SceneTransition 的区别(风险 R8):
/// - 本组件挂**场景内** BlackoutCanvas,不 DontDestroyOnLoad(场景卸载即销毁,不跨场景残留);
/// - 独立 Canvas,不与切场景幕布(TransitionCanvas)共用 Image/Canvas → alpha 不打架。
///
/// 防重入:IsBusy=true 期间拒绝新 Run(淡入淡出过程不允许二次黑场)。
/// 黑图资源:代码不造图——blackImage.sprite 由 saika 编辑器拖(Assets/Graphics 下自备黑图)。
/// </summary>
public class TeleportBlackout : MonoBehaviour
{
    [Header("幕布")]
    [Tooltip("全屏黑 Image(拉伸覆盖全屏;sprite 由 saika 在 Inspector 拖,代码不创建资源)")]
    [SerializeField] private Image blackImage;

    [Tooltip("幕布 CanvasGroup(控制整体 alpha:0 透明不遮挡,1 全黑)")]
    [SerializeField] private CanvasGroup canvasGroup;

    [Header("三段式黑场时长(秒)")]
    [Tooltip("进:透明 → 全黑(管道 = 进管道起跑;石碑 = 传送开始)")]
    [SerializeField] private float blackInDuration = 0.4f;

    [Tooltip("全黑保持:传送/瞬移在这段里发生(管道 = 瞬移那一刻起计时;石碑 = 全黑后立即计时)")]
    [SerializeField] private float blackHoldDuration = 0.2f;

    [Tooltip("出:全黑 → 透明")]
    [SerializeField] private float blackOutDuration = 0.4f;

    /// <summary>是否正在黑场流程(淡出→全黑→淡入);true 期间拒绝新 Run(防重入)</summary>
    public bool IsBusy { get; private set; }

    /// <summary>
    /// 场景内唯一幕布。给「管道瞬移」这类非 Run 流程用(先全黑 → 传送 → 再淡出):免拖引用免 Find。
    /// Awake 接管 + OnDestroy 自清(同 AudioManager/SceneTransition 单例口径);每场景一个实例,场景卸载即失效。
    /// </summary>
    public static TeleportBlackout Instance { get; private set; }

    private void Awake()
    {
        Instance = this;

        // 初始状态:全透明不遮挡游戏 + 射线检测关闭(淡出开始时打开,淡入结束后关闭)。
        // 场景内幕布每场景加载时复位,防止上一场残留 alpha=1 黑屏。
        if (canvasGroup != null) canvasGroup.alpha = 0f;
        if (blackImage != null) blackImage.raycastTarget = false;
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;   // 自清:场景卸载后不留悬挂引用
    }

    /// <summary>
    /// 执行一次黑场流程:淡出到全黑 → onFullyBlack(全黑内做传送等不可见操作)→ 淡入回游戏 → onDone。
    /// 防重入:IsBusy 期间调用直接返回(打印告警,不打断进行中的黑场)。
    /// 引用缺失(blackImage/canvasGroup 未拖,saika T7 前):LogError 并直接收尾(不执行 onFullyBlack,
    /// 避免无遮罩下瞬移穿帮;走 onDone 兜底复位,不锁输入/不卡 IsTeleporting)。
    /// </summary>
    public void Run(Action onFullyBlack, Action onDone = null)
    {
        if (IsBusy)
        {
            Debug.LogWarning("[TeleportBlackout] 正在黑场流程中(IsBusy),忽略新的 Run 请求", this);
            return;
        }

        if (blackImage == null || canvasGroup == null)
        {
            Debug.LogError("[TeleportBlackout] blackImage/canvasGroup 未拖引用(检查 Inspector 接线),无法执行黑场;本次流程直接收尾", this);
            onDone?.Invoke();
            return;
        }

        IsBusy = true;
        StartCoroutine(BlackoutRoutine(onFullyBlack, onDone));
    }

    private IEnumerator BlackoutRoutine(Action onFullyBlack, Action onDone)
    {
        // 进段即阻断输入:黑图中途开始挡点击,防半透明时误触背后 UI
        blackImage.raycastTarget = true;

        yield return StartCoroutine(FadeTo(1f, blackInDuration));   // 进:0 → 1(全黑)

        onFullyBlack?.Invoke();                                      // 全黑内做传送/切区(突变不可见)

        if (blackHoldDuration > 0f)
            yield return new WaitForSecondsRealtime(blackHoldDuration);   // 全黑保持

        yield return StartCoroutine(FadeTo(0f, blackOutDuration));   // 出:1 → 0(揭幕回游戏)

        FinishBlackout();
        onDone?.Invoke();
    }

    /// <summary>
    /// 统一入口·分段用法之一(管道):进管道时调 —— 走「进」段变到全黑并停住,之后一直保持全黑,
    /// 直到 EndBlackout 被调(传送完成)。这样黑场覆盖整条管道行程,不靠时间猜位置。
    /// </summary>
    public void BeginBlackout()
    {
        if (blackImage == null || canvasGroup == null) return;
        if (IsBusy) return;                       // 已有流程在跑(如石碑传送)不打断
        IsBusy = true;
        blackImage.raycastTarget = true;
        StartCoroutine(FadeTo(1f, blackInDuration));
    }

    /// <summary>
    /// 统一入口·分段用法之二(管道):传送完成时调 —— 先保持全黑 blackHoldDuration,再走「出」段变回透明。
    /// </summary>
    public void EndBlackout(Action onDone = null)
    {
        if (blackImage == null || canvasGroup == null)
        {
            onDone?.Invoke();
            return;
        }
        StartCoroutine(EndBlackoutRoutine(onDone));
    }

    private IEnumerator EndBlackoutRoutine(Action onDone)
    {
        if (blackHoldDuration > 0f)
            yield return new WaitForSecondsRealtime(blackHoldDuration);

        yield return StartCoroutine(FadeTo(0f, blackOutDuration));

        FinishBlackout();
        onDone?.Invoke();
    }

    /// <summary>立即给幕布一个 alpha(传送那一帧的兜底:管道极短、「进」段没跑完也要保证传送不可见)</summary>
    public void SetAlpha(float alpha)
    {
        if (canvasGroup == null) return;
        canvasGroup.alpha = Mathf.Clamp01(alpha);
        if (blackImage != null) blackImage.raycastTarget = canvasGroup.alpha > 0.001f;
    }

    /// <summary>收尾:恢复输入可点 + 清忙碌标记(进/出两条路径共用)</summary>
    private void FinishBlackout()
    {
        if (blackImage != null) blackImage.raycastTarget = false;
        IsBusy = false;
    }

    /// <summary>alpha 线性渐变到 targetAlpha(Time.unscaledDeltaTime:暂停/跨场景均不受 timeScale 影响,抄 SceneTransition.FadeRoutine)</summary>
    private IEnumerator FadeTo(float targetAlpha, float duration)
    {
        if (canvasGroup == null) yield break;
        float startAlpha = canvasGroup.alpha;
        if (duration <= 0f)
        {
            canvasGroup.alpha = targetAlpha;
            yield break;
        }
        float elapsed = 0f;
        while (elapsed < duration)
        {
            elapsed += Time.unscaledDeltaTime;
            float t = Mathf.Clamp01(elapsed / duration);
            canvasGroup.alpha = Mathf.Lerp(startAlpha, targetAlpha, t);
            yield return null;
        }
        canvasGroup.alpha = targetAlpha;
    }
}
