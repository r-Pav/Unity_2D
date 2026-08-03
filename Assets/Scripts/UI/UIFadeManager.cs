using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 统一 UI 面板淡入淡出管理器 — 挂 Canvas。
/// Inspector 把需要动画的面板拖入 fadePanels 数组。
/// 打开面板：淡入（alpha 0→1）；关闭面板：淡出（alpha 1→0）后隐藏。
/// 面板自动补 CanvasGroup（代码 AddComponent，无需手动添加）。
/// 被 PanelManager 调用：OpenPanel 后调 FadeIn，ClosePanel 前调 FadeOut。
/// </summary>
public class UIFadeManager : MonoBehaviour
{
    [Header("淡入淡出设置")]
    [Tooltip("淡入时长（秒）")]
    [SerializeField] private float fadeInDuration = 0.15f;
    [Tooltip("淡出时长（秒）")]
    [SerializeField] private float fadeOutDuration = 0.12f;
    [Tooltip("初始透明度（0=完全透明淡入，1=不淡入）")]
    [SerializeField] private float fadeInStartAlpha = 0f;

    [Header("动画面板")]
    [Tooltip("需要淡入淡出效果的面板根物体（拖入 PlayerStatPanel / PassivePanel 等）")]
    [SerializeField] private GameObject[] fadePanels;

    // 面板 → CanvasGroup 缓存（运行时自动补组件）
    private readonly Dictionary<GameObject, CanvasGroup> groupCache = new Dictionary<GameObject, CanvasGroup>();
    // 面板 → 运行中的协程（防止重复播放）
    private readonly Dictionary<GameObject, Coroutine> activeCoroutines = new Dictionary<GameObject, Coroutine>();

    private void Awake()
    {
        if (fadePanels != null)
        {
            foreach (GameObject panel in fadePanels)
            {
                if (panel != null)
                    EnsureCanvasGroup(panel);
            }
        }
    }

    /// <summary>该面板是否在淡入淡出管理列表中</summary>
    public bool IsManaged(GameObject panel)
    {
        if (panel == null || fadePanels == null) return false;
        for (int i = 0; i < fadePanels.Length; i++)
            if (fadePanels[i] == panel) return true;
        return false;
    }

    /// <summary>淡入（打开面板时调用，需面板已 SetActive(true)）</summary>
    public void FadeIn(GameObject panel)
    {
        if (!IsManaged(panel)) return;

        CanvasGroup group = EnsureCanvasGroup(panel);
        StopExisting(panel);

        // 从初始透明度开始，目标 1
        group.alpha = fadeInStartAlpha;
        group.interactable = true;
        group.blocksRaycasts = true;

        if (fadeInDuration <= 0f)
        {
            group.alpha = 1f;
            return;
        }

        activeCoroutines[panel] = StartCoroutine(FadeRoutine(group, 1f, fadeInDuration));
    }

    /// <summary>淡出（关闭面板前调用，播完回调里再 SetActive(false)）</summary>
    public void FadeOut(GameObject panel, System.Action onComplete = null)
    {
        if (!IsManaged(panel)) return;

        CanvasGroup group = EnsureCanvasGroup(panel);
        StopExisting(panel);

        // 淡出期间禁交互，防止动画中误点
        group.interactable = false;
        group.blocksRaycasts = false;

        if (fadeOutDuration <= 0f)
        {
            group.alpha = 0f;
            onComplete?.Invoke();
            return;
        }

        activeCoroutines[panel] = StartCoroutine(FadeRoutine(group, 0f, fadeOutDuration, onComplete));
    }

    /// <summary>立即恢复为不透明（无动画，用于 CloseAllPanels 等强制隐藏场景）</summary>
    public void ResetAlpha(GameObject panel)
    {
        if (!IsManaged(panel)) return;
        StopExisting(panel);
        CanvasGroup group = EnsureCanvasGroup(panel);
        group.alpha = 1f;
        group.interactable = true;
        group.blocksRaycasts = true;
    }

    private CanvasGroup EnsureCanvasGroup(GameObject panel)
    {
        if (groupCache.TryGetValue(panel, out CanvasGroup cached) && cached != null)
            return cached;

        CanvasGroup group = panel.GetComponent<CanvasGroup>();
        if (group == null)
            group = panel.AddComponent<CanvasGroup>();
        groupCache[panel] = group;
        return group;
    }

    private void StopExisting(GameObject panel)
    {
        if (activeCoroutines.TryGetValue(panel, out Coroutine coroutine) && coroutine != null)
            StopCoroutine(coroutine);
        activeCoroutines.Remove(panel);
    }

    private IEnumerator FadeRoutine(CanvasGroup group, float targetAlpha, float duration,
        System.Action onComplete = null)
    {
        float startAlpha = group.alpha;
        float elapsed = 0f;
        while (elapsed < duration)
        {
            elapsed += Time.unscaledDeltaTime; // 面板打开时 timeScale=0，用 unscaled 保证动画正常播
            group.alpha = Mathf.Lerp(startAlpha, targetAlpha, Mathf.Clamp01(elapsed / duration));
            yield return null;
        }
        group.alpha = targetAlpha;
        onComplete?.Invoke();
    }
}
