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

    [Tooltip("单段淡出/淡入时长(秒);全流程 = 淡出 + 全黑做事 + 淡入")]
    [SerializeField] private float fadeDuration = 0.25f;

    /// <summary>是否正在黑场流程(淡出→全黑→淡入);true 期间拒绝新 Run(防重入)</summary>
    public bool IsBusy { get; private set; }

    private void Awake()
    {
        // 初始状态:全透明不遮挡游戏 + 射线检测关闭(淡出开始时打开,淡入结束后关闭)。
        // 场景内幕布每场景加载时复位,防止上一场残留 alpha=1 黑屏。
        if (canvasGroup != null) canvasGroup.alpha = 0f;
        if (blackImage != null) blackImage.raycastTarget = false;
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
        // 淡出期即阻断输入:黑图中途开始挡点击,防淡出半透明时误触背后 UI
        blackImage.raycastTarget = true;

        yield return StartCoroutine(FadeRoutine(1f));   // 淡出:alpha 0 → 1(全黑)

        onFullyBlack?.Invoke();                          // 全黑内做传送(位置突变画面不可见,无穿帮)

        yield return StartCoroutine(FadeRoutine(0f));   // 淡入:alpha 1 → 0(揭幕回游戏)

        blackImage.raycastTarget = false;
        IsBusy = false;
        onDone?.Invoke();
    }

    /// <summary>alpha 线性渐变到 targetAlpha(Time.unscaledDeltaTime:暂停/跨场景均不受 timeScale 影响,抄 SceneTransition.FadeRoutine)</summary>
    private IEnumerator FadeRoutine(float targetAlpha)
    {
        float startAlpha = canvasGroup.alpha;
        float elapsed = 0f;
        while (elapsed < fadeDuration)
        {
            elapsed += Time.unscaledDeltaTime;
            float t = Mathf.Clamp01(elapsed / fadeDuration);
            canvasGroup.alpha = Mathf.Lerp(startAlpha, targetAlpha, t);
            yield return null;
        }
        canvasGroup.alpha = targetAlpha;
    }
}
