using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// 黑屏过渡组件 — 全屏黑 Image 淡出(alpha 0→1) → SceneManager.LoadScene → 淡入(1→0)。
/// 公开方法：
///   - ToGame()：淡出 → LoadScene("SampleScene") → 淡入（进游戏）
///   - ToTitle()：淡出 → LoadScene("TitleScene") → 淡入（回主菜单，PauseMenu 返回用）
/// 单例 + DontDestroyOnLoad：首个场景加载的实例常驻跨场景成为唯一幕布；
/// 后续场景重复挂载的 TransitionCanvas 在 Awake 自动销毁（防重，同 AudioManager 模式）。
/// 必须用 Time.unscaledDeltaTime：跨场景 timeScale 无碍；淡出期间 timeScale 可能为 0
/// （PauseMenu 里触发回主菜单时游戏是暂停的）。
/// 初始状态：CanvasGroup.alpha = 0（不遮挡游戏）；淡出起 blackImage.raycastTarget = true 阻断输入，淡入完设 false。
/// 挂载点：场景 Canvas 下独立 TransitionCanvas 物体（saika 手动搭场景时挂，脚本侧保证组件自身逻辑完整）。
/// </summary>
public class SceneTransition : MonoBehaviour
{
    private static SceneTransition _instance;

    /// <summary>单例：FindObjectOfType 查找（参照 AudioManager）</summary>
    public static SceneTransition Instance
    {
        get
        {
            if (_instance == null)
                _instance = FindObjectOfType<SceneTransition>();
            return _instance;
        }
    }

    [Header("过渡幕布")]
    [Tooltip("全屏黑 Image（拉伸覆盖全屏），用于 raycast 阻断输入")]
    [SerializeField] private Image blackImage = null;
    [Tooltip("单段淡出/淡入时长（秒）")]
    [SerializeField] private float fadeDuration = 0.5f;
    [Tooltip("幕布 CanvasGroup：控制整体 alpha（0 透明不遮挡，1 全黑）")]
    [SerializeField] private CanvasGroup canvasGroup = null;

    private const string GameSceneName = "SampleScene";
    private const string TitleSceneName = "TitleScene";

    /// <summary>是否正在过渡（防重入：过渡中忽略新的 ToGame/ToTitle）</summary>
    private bool _isTransitioning;

    private void Awake()
    {
        // 单例防重 + 常驻跨场景：首个实例成为唯一过渡幕布，后续场景的重复实例自动销毁
        if (_instance != null)
        {
            Destroy(gameObject);
            return;
        }
        _instance = this;
        DontDestroyOnLoad(gameObject);

        // 初始状态：全透明不遮挡游戏；射线检测关闭（过渡流程开始时打开，结束后关闭）
        if (canvasGroup != null) canvasGroup.alpha = 0f;
        if (blackImage != null) blackImage.raycastTarget = false;
    }

    /// <summary>进游戏：淡出 → LoadScene("SampleScene") → 淡入</summary>
    public void ToGame()
    {
        StartTransition(GameSceneName);
    }

    /// <summary>回主菜单：淡出 → LoadScene("TitleScene") → 淡入（PauseMenu 返回用）</summary>
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

        yield return StartCoroutine(FadeRoutine(1f));   // 淡出：0 → 1（全黑）
        SceneManager.LoadScene(sceneName);              // 切场景（本组件 DontDestroyOnLoad，跨场景存活继续淡入）
        yield return StartCoroutine(FadeRoutine(0f));   // 淡入：1 → 0（揭幕）

        if (blackImage != null) blackImage.raycastTarget = false;
        _isTransitioning = false;
    }

    /// <summary>alpha 线性渐变到 targetAlpha（Time.unscaledDeltaTime：暂停/跨场景均不受 timeScale 影响）</summary>
    private IEnumerator FadeRoutine(float targetAlpha)
    {
        if (canvasGroup == null) yield break;

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
