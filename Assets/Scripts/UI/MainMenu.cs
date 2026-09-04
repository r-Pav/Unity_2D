using System.Collections;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// TitleScene 主菜单控制 — 挂 TitleScene Canvas 根。
/// 按钮 OnEnable/OnDisable 成对绑定（抄 PauseMenu/DeathPanel 模式）。
/// 行为：
///   - 开始游戏 → 新游戏标记（PendingLoadFlag.slot = -1）→ SceneTransition.ToGame()
///   - 读档 → 按钮组左滑 + 打开 LoadPanel（SaveLoadPanel mode=Load）
///   - 设置 → 按钮组左滑 + 打开 SettingsPanel（TitleScene 无 PanelManager，不走栈管理；游戏内 ESC 设置仍走 PanelManager 栈，SampleScene 路径不变）
/// 打开子面板：SetActive(true) 后若面板挂 UIPanelMotion → 调 PlayOpen 播打开动效（替代直接显示）；未挂则原样直接显示。
/// 关闭子面板：走面板自身 ISlideClose.SlideClose（S3 起内部转调 UIPanelMotion.PlayClose，未挂则直接回调），播完 SetActive(false)。
///   - 退出 → Application.Quit()
/// 按钮组滑动（抄 PauseMenu 模式）：点读档/设置 → buttonGroup 左滑到 leftPosition，面板在右侧显示；
/// 面板关闭（active→inactive）→ 自动右滑回默认位置。Update 轮询检测面板状态变化，不依赖回调。
/// 读档回调：Awake 里 saveLoadPanel.onLoadRequested 绑定 OnLoadRequested（UnityEvent<int> → UnityAction<int>），
/// 回调收到槽位 → 写 PendingLoadFlag.slot = N → SceneTransition.ToGame()；
/// 实际读档由 SceneBootstrap 在 SampleScene 启动时恢复（TitleScene 里直接 LoadGame 会空引用）。
/// 防御：SceneTransition.Instance 为 null 时 LogWarning（场景里没挂 SceneTransition，无法切场景）。
/// </summary>
public class MainMenu : MonoBehaviour
{
    [Header("按钮")]
    [Tooltip("开始游戏/新游戏 → 新游戏标记 + 切 SampleScene")]
    [SerializeField] private Button btnStart;
    [Tooltip("读档 → 按钮组左滑 + 打开读档面板（LoadPanel）")]
    [SerializeField] private Button btnLoad;
    [Tooltip("退出游戏 → Application.Quit()")]
    [SerializeField] private Button btnQuit;
    [Tooltip("设置 → 按钮组左滑 + 打开设置面板（SettingsPanel）")]
    [SerializeField] private Button btnSettings;

    [Header("按钮组滑动（抄 ESC 菜单 PauseMenu 动效）")]
    [Tooltip("按钮组物体（Btn_Menu，CanvasGroup）：点读档/设置后左滑，面板关闭后滑回")]
    [SerializeField] private RectTransform buttonGroup;
    [Tooltip("按钮组上的 CanvasGroup（滑动时 alpha 保持不变）")]
    [SerializeField] private CanvasGroup buttonCanvasGroup;
    [Tooltip("左滑目标位置（绝对 anchoredPosition）：默认居中；点读档/设置后按钮组左移到此处，与右侧面板同屏显示")]
    [SerializeField] private Vector2 leftPosition = new Vector2(-322f, 0f);

    [Header("设置面板")]
    [Tooltip("SettingsPanel 物体（默认 inactive，点设置后 SetActive(true)）")]
    [SerializeField] private GameObject settingsPanel;

    [Header("读档面板")]
    [Tooltip("SaveLoadPanel mode=Load 的面板（默认 inactive，点读档后 SetActive(true)）")]
    [SerializeField] private GameObject loadPanel;
    [Tooltip("loadPanel 上的 SaveLoadPanel 组件，用于绑定 onLoadRequested 回调")]
    [SerializeField] private SaveLoadPanel saveLoadPanel;

    /// <summary>按钮组默认（居中）anchoredPosition，Awake 记录</summary>
    private Vector2 _groupCenterPos;
    /// <summary>按钮组是否已左滑（面板打开状态；面板关闭后据此滑回）</summary>
    private bool _groupPushedLeft;
    /// <summary>上一帧是否有面板打开（Update 检测 active 变化用）</summary>
    private bool _wasPanelOpen;

    private const float SlideDuration = 0.2f;

    private void Awake()
    {
        // UnityEvent<int> 用 UnityAction<int>：方法组转换即可；OnDisable 成对 RemoveListener
        if (saveLoadPanel != null)
            saveLoadPanel.onLoadRequested.AddListener(OnLoadRequested);

        if (buttonGroup != null)
            _groupCenterPos = buttonGroup.anchoredPosition;
    }

    private void OnEnable()
    {
        if (btnStart != null) btnStart.onClick.AddListener(OnStartClicked);
        if (btnLoad != null) btnLoad.onClick.AddListener(OnLoadClicked);
        if (btnQuit != null) btnQuit.onClick.AddListener(OnQuitClicked);
        if (btnSettings != null) btnSettings.onClick.AddListener(OnSettingsClicked);

        // 主菜单/非战斗界面：强制显示鼠标（战斗态 PanelManager 会隐藏，切到本场景后需重置）
        Cursor.visible = true;
        Cursor.lockState = CursorLockMode.None;
    }

    private void OnDisable()
    {
        if (btnStart != null) btnStart.onClick.RemoveListener(OnStartClicked);
        if (btnLoad != null) btnLoad.onClick.RemoveListener(OnLoadClicked);
        if (btnQuit != null) btnQuit.onClick.RemoveListener(OnQuitClicked);
        if (btnSettings != null) btnSettings.onClick.RemoveListener(OnSettingsClicked);
        if (saveLoadPanel != null) saveLoadPanel.onLoadRequested.RemoveListener(OnLoadRequested);
    }

    private void Update()
    {
        // 面板关闭（active→inactive）→ 按钮组滑回默认位置（不依赖面板回调，轮询状态变化）
        bool panelOpen = (loadPanel != null && loadPanel.activeInHierarchy)
            || (settingsPanel != null && settingsPanel.activeInHierarchy);

        if (_wasPanelOpen && !panelOpen && _groupPushedLeft)
        {
            _groupPushedLeft = false;
            SlideToCenter();
        }
        _wasPanelOpen = panelOpen;

        // ESC：主菜单无 PanelManager，面板打开时 ESC 直接关闭（走 SlideClose 动画）
        if (Input.GetKeyDown(KeyCode.Escape))
        {
            if (loadPanel != null && loadPanel.activeInHierarchy)
                CloseSubPanel(loadPanel);
            else if (settingsPanel != null && settingsPanel.activeInHierarchy)
                CloseSubPanel(settingsPanel);
        }

        // 点击面板区域外关闭：面板打开时，点击位置不在面板 RectTransform 内 → 关闭
        // （排除区域，与技能拖拽卸载的 RectangleContainsScreenPoint 同款判定）
        if (panelOpen && Input.GetMouseButtonDown(0))
        {
            GameObject activePanel = loadPanel != null && loadPanel.activeInHierarchy
                ? loadPanel
                : (settingsPanel != null && settingsPanel.activeInHierarchy ? settingsPanel : null);
            if (activePanel != null)
            {
                RectTransform panelRect = activePanel.GetComponent<RectTransform>();
                if (panelRect != null
                    && !RectTransformUtility.RectangleContainsScreenPoint(panelRect, Input.mousePosition))
                {
                    CloseSubPanel(activePanel);
                }
            }
        }
    }

    /// <summary>打开子面板：TitleScene 无 PanelManager，需手动播打开动效（SetActive(true) 后若挂 UIPanelMotion → PlayOpen；未挂则原样直接显示）</summary>
    private void OpenSubPanel(GameObject panel)
    {
        if (panel == null) return;
        panel.SetActive(true);
        UIPanelMotion motion = panel.GetComponent<UIPanelMotion>();
        if (motion != null) motion.PlayOpen();
    }

    /// <summary>关闭子面板（ISlideClose.SlideClose → S3 起内部转调 UIPanelMotion.PlayClose 或直接回调；播完 SetActive(false)；按钮组随之滑回）</summary>
    private void CloseSubPanel(GameObject panel)
    {
        if (panel == null) return;
        var slideClose = panel.GetComponent<ISlideClose>();
        if (slideClose != null)
        {
            slideClose.SlideClose(() => panel.SetActive(false));
        }
        else
        {
            panel.SetActive(false);
        }
    }

    /// <summary>开始游戏/新游戏：标记新游戏 → 切 SampleScene</summary>
    private void OnStartClicked()
    {
        PendingLoadFlag.slot = -1; // 新游戏标记（读档由 SceneBootstrap 判定 slot<0 走出生点传送）
        GoToGame();
    }

    /// <summary>读档：按钮组左滑 + 打开读档面板（SaveLoadPanel mode=Load，面板内确认读档走 onLoadRequested 回调；打开动效由 OpenSubPanel 触发 UIPanelMotion）</summary>
    private void OnLoadClicked()
    {
        if (settingsPanel != null && settingsPanel.activeInHierarchy)
            settingsPanel.SetActive(false); // 切面板前先关另一个（Load/Set 互斥）
        SlideToLeft();
        OpenSubPanel(loadPanel);
    }

    /// <summary>设置：按钮组左滑 + 打开 SettingsPanel（TitleScene 无 PanelManager 不走栈管理；打开动效由 OpenSubPanel 触发 UIPanelMotion；关闭由 SettingsPanel.OnBackClicked/SlideClose 兜底）</summary>
    private void OnSettingsClicked()
    {
        if (loadPanel != null && loadPanel.activeInHierarchy)
            loadPanel.SetActive(false); // 切面板前先关另一个（Load/Set 互斥）
        SlideToLeft();
        OpenSubPanel(settingsPanel);
    }

    /// <summary>退出游戏</summary>
    private void OnQuitClicked()
    {
        Application.Quit();
    }

    /// <summary>外部读档回调：写槽位标记 → 切 SampleScene（实际读档由 SceneBootstrap 在 SampleScene 恢复）</summary>
    private void OnLoadRequested(int slot)
    {
        PendingLoadFlag.slot = slot;
        GoToGame();
    }

    /// <summary>切游戏场景：SceneTransition 缺失时 LogWarning 提示（场景里没挂 SceneTransition）</summary>
    private void GoToGame()
    {
        if (SceneTransition.Instance == null)
        {
            Debug.LogWarning("[MainMenu] 场景里没挂 SceneTransition，无法切换场景");
            return;
        }
        SceneTransition.Instance.ToGame();
    }

    // ============================================================
    // 按钮组滑动（抄 PauseMenu.SlideToLeft/SlideToCenter/SlideRoutine）
    // ============================================================

    /// <summary>按钮组左滑 + 渐隐：居中 → leftPosition，alpha 1 → 0（与右侧面板同屏时淡出）</summary>
    private void SlideToLeft()
    {
        if (buttonGroup == null) return;
        _groupPushedLeft = true;
        float alpha = buttonCanvasGroup != null ? buttonCanvasGroup.alpha : 1f;
        StopAllCoroutines();
        StartCoroutine(SlideRoutine(buttonGroup.anchoredPosition, leftPosition, alpha, 0f));
    }

    /// <summary>按钮组右滑 + 渐显：leftPosition → 居中，alpha 0 → 1</summary>
    private void SlideToCenter()
    {
        if (buttonGroup == null) return;
        StopAllCoroutines();
        float alpha = buttonCanvasGroup != null ? buttonCanvasGroup.alpha : 1f;
        StartCoroutine(SlideRoutine(buttonGroup.anchoredPosition, _groupCenterPos, alpha, 1f));
    }

    private IEnumerator SlideRoutine(Vector2 fromPos, Vector2 toPos, float fromAlpha, float toAlpha)
    {
        float elapsed = 0f;
        while (elapsed < SlideDuration)
        {
            elapsed += Time.unscaledDeltaTime; // 暂停（timeScale=0）时仍正常播放
            float t = Mathf.Clamp01(elapsed / SlideDuration);
            if (buttonGroup != null) buttonGroup.anchoredPosition = Vector2.Lerp(fromPos, toPos, t);
            if (buttonCanvasGroup != null) buttonCanvasGroup.alpha = Mathf.Lerp(fromAlpha, toAlpha, t);
            yield return null;
        }
        if (buttonGroup != null) buttonGroup.anchoredPosition = toPos;
        if (buttonCanvasGroup != null) buttonCanvasGroup.alpha = toAlpha;
    }
}

