using System.Collections;
using System.Collections.Generic;
using DG.Tweening;
using UnityEngine;
using UnityEngine.EventSystems;
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
///   - [2026-09-19] 点击任意处 → 与 Start 同一入口(新游戏 + 切场景):标题出场(UIContentReveal)播完才允许点击(出现 = 可点);
///     可交互控件(按钮/滑条)与子面板内容上的点击不触发;面板打开时不触发(那时点击是关面板)
///   - [2026-09-19 saika] 主界面动效统一到 UI 动画组件:页面开关走 UIPanelMotion(+UIActivateMotion)、标题出场走 UIContentReveal、
///     按钮组滑出/滑回走 UIPanelMotion;本类里旧的自绘 DOTween 实现全部注释留档
/// 按钮组（统一 UIPanelMotion，2026-09-19）：点读档/设置 → Btn_Menu 上的 UIPanelMotion.PlayClose 滑出屏幕；面板关闭 → PlayOpen 滑回摆放位。
/// 面板关闭（active→inactive）检测走 Update 轮询，不依赖回调。
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

    [Header("标题出场（统一 UIContentReveal，2026-09-19）")]
    [Tooltip("Title 上的 UIContentReveal：出场播完 = 允许点击进游戏（未接线时不拦点击，保持旧行为）")]
    [SerializeField] private UIContentReveal titleReveal;

    // [2026-09-19 saika：主界面动效统一改用 UIPanelMotion / UIContentReveal] 旧的标题自绘出场字段注释留档
    //[SerializeField] private RectTransform titleRoot;
    //[SerializeField] private float titleAppearDuration = 0.6f;
    //[SerializeField] private Vector2 titleAppearOffset = new Vector2(0f, -40f);
    //[SerializeField] private float titleAppearScaleFrom = 0.92f;

    [Header("点击任意位置进入游戏（2026-09-19）")]
    [Tooltip("场景里的 EventSystem（留空则用 EventSystem.current）；用来判断点击是否落在可交互控件上")]
    [SerializeField] private EventSystem eventSystem;
    //[SerializeField] private float clickToStartDelay = 0.5f;   // 已被标题出场取代,注释留档

    [Header("按钮组（统一 UIPanelMotion，2026-09-19）")]
    [Tooltip("Btn_Menu 上的 UIPanelMotion：点读档/设置 → PlayClose 滑出屏幕；面板关闭 → PlayOpen 滑回摆放位")]
    [SerializeField] private UIPanelMotion buttonGroupMotion;

    // [2026-09-19 saika：主界面动效统一改用 UIPanelMotion] 旧的按钮组滑动字段注释留档
    //[SerializeField] private RectTransform buttonGroup;
    //[SerializeField] private CanvasGroup buttonCanvasGroup;
    //[SerializeField] private Vector2 leftPosition = new Vector2(-322f, 0f);

    [Header("设置面板")]
    [Tooltip("SettingsPanel 物体（默认 inactive，点设置后 SetActive(true)）")]
    [SerializeField] private GameObject settingsPanel;

    [Header("读档面板")]
    [Tooltip("SaveLoadPanel mode=Load 的面板（默认 inactive，点读档后 SetActive(true)）")]
    [SerializeField] private GameObject loadPanel;
    [Tooltip("loadPanel 上的 SaveLoadPanel 组件，用于绑定 onLoadRequested 回调")]
    [SerializeField] private SaveLoadPanel saveLoadPanel;

    /// <summary>上一帧是否有面板打开（Update 检测 active 变化用）</summary>
    private bool _wasPanelOpen;

    /// <summary>标题出场是否已完成（UIContentReveal 播完 = 允许点击进游戏）</summary>
    private bool _titleAppeared;

    /// <summary>是否已订阅开场黑场揭开事件（OnDisable 成对退订）</summary>
    private bool _introFadeSubscribed;

    // [2026-09-19 saika：主界面动效统一改用 UIPanelMotion / UIContentReveal] 旧自绘实现的运行时状态注释留档
    //private Vector2 _groupCenterPos;
    //private bool _groupPushedLeft;
    //private CanvasGroup _titleGroup;
    //private Vector2 _titleTargetPos;
    //private Vector3 _titleTargetScale;
    //private Tween _titleTween;
    //private Tween _slideTween;

    private const float SlideDuration = 0.2f;

    private void Awake()
    {
        // UnityEvent<int> 用 UnityAction<int>：方法组转换即可；OnDisable 成对 RemoveListener
        if (saveLoadPanel != null)
            saveLoadPanel.onLoadRequested.AddListener(OnLoadRequested);

        // [2026-09-19 统一 UIPanelMotion] 旧按钮组滑动记录居中位置，已停用
        //if (buttonGroup != null)
        //    _groupCenterPos = buttonGroup.anchoredPosition;
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

        // 标题出场等开场黑场揭开后再播（黑场期间播会被遮住）；没有开场黑场就直接播
        if (SceneTransition.Instance != null && SceneTransition.Instance.IsIntroFading)
        {
            _introFadeSubscribed = true;
            SceneTransition.Instance.OnIntroFadeFinished += HandleIntroFadeFinished;
        }
        else
        {
            PlayTitleReveal();   // 标题出场（UIContentReveal 播完 = 可以点击进游戏）
        }
    }

    private void OnDisable()
    {
        if (btnStart != null) btnStart.onClick.RemoveListener(OnStartClicked);
        if (btnLoad != null) btnLoad.onClick.RemoveListener(OnLoadClicked);
        if (btnQuit != null) btnQuit.onClick.RemoveListener(OnQuitClicked);
        if (btnSettings != null) btnSettings.onClick.RemoveListener(OnSettingsClicked);
        if (saveLoadPanel != null) saveLoadPanel.onLoadRequested.RemoveListener(OnLoadRequested);

        // [2026-09-19 统一 UIContentReveal] 出场动画由组件自己在 OnDisable 里 Kill（见 UIContentReveal.OnDisable），这里不再持有 Tween

        if (_introFadeSubscribed && SceneTransition.Instance != null)
        {
            SceneTransition.Instance.OnIntroFadeFinished -= HandleIntroFadeFinished;
            _introFadeSubscribed = false;
        }
    }

    private void Update()
    {
        // 面板关闭（active→inactive）→ 按钮组滑回默认位置（不依赖面板回调，轮询状态变化）
        bool panelOpen = (loadPanel != null && loadPanel.activeInHierarchy)
            || (settingsPanel != null && settingsPanel.activeInHierarchy);

        // 面板关闭 → 按钮组滑回 Inspector 摆放位（UIPanelMotion.PlayOpen：从滑出方向滑入）
        if (_wasPanelOpen && !panelOpen)
            buttonGroupMotion?.PlayOpen();
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

        // [2026-09-19 saika] 点击任意处 → 进入游戏（与 Start 按钮同一入口）：
        //   条件 = 标题出场已完成（出现 = 可点）+ 无子面板打开（面板打开时点击是上面那条「点面板外关闭」）
        //          + 点击没落在可交互控件（按钮/滑条/开关）或子面板内容上。
        //   背景图/标题文字这类不可交互的 UI 也算「任意处」，点它们照样进游戏。
        if (_titleAppeared && !panelOpen && Input.GetMouseButtonDown(0) && !IsClickOnInteractiveUi())
        {
            OnStartClicked();
        }
    }

    // ============================================================
    // 标题出场（出场播完 = 可以点击进游戏）
    // ============================================================

    /// <summary>开场黑场揭开完成 → 退订并开始标题出场</summary>
    private void HandleIntroFadeFinished()
    {
        if (_introFadeSubscribed && SceneTransition.Instance != null)
        {
            SceneTransition.Instance.OnIntroFadeFinished -= HandleIntroFadeFinished;
            _introFadeSubscribed = false;
        }
        PlayTitleReveal();
    }

    /// <summary>
    /// 标题出场（统一走 Title 上的 UIContentReveal）：Play 的完成回调里把 _titleAppeared 置真 = 允许点击进游戏。
    /// 未接线时不拦点击（保持旧行为）。
    /// </summary>
    private void PlayTitleReveal()
    {
        _titleAppeared = false;

        if (titleReveal == null)
        {
            _titleAppeared = true;   // 没接标题出场组件:不拦点击
            return;
        }

        titleReveal.Play(() => _titleAppeared = true);
    }

    /* [2026-09-19 saika：主界面动效统一改用 UIContentReveal] 旧的标题自绘出场实现（DOTween 淡入+上移+缩放）注释留档
    private void PlayTitleAppear()
    {
        _titleAppeared = false;
        if (_titleTween != null && _titleTween.IsActive()) _titleTween.Kill();

        if (titleRoot == null)
        {
            _titleAppeared = true;   // 没接标题:不拦点击
            return;
        }

        _titleTargetPos = titleRoot.anchoredPosition;   // 场景摆放位置 = 出场终点
        _titleTargetScale = titleRoot.localScale;
        _titleGroup = EnsureCanvasGroup(titleRoot.gameObject);

        titleRoot.anchoredPosition = _titleTargetPos + titleAppearOffset;
        titleRoot.localScale = _titleTargetScale * Mathf.Max(0.01f, titleAppearScaleFrom);
        if (_titleGroup != null) _titleGroup.alpha = 0f;

        float dur = Mathf.Max(0.01f, titleAppearDuration);
        Sequence seq = DOTween.Sequence();
        seq.SetUpdate(true);
        if (_titleGroup != null) seq.Join(_titleGroup.DOFade(1f, dur));
        seq.Join(titleRoot.DOAnchorPos(_titleTargetPos, dur).SetEase(Ease.OutCubic));
        seq.Join(titleRoot.DOScale(_titleTargetScale, dur).SetEase(Ease.OutCubic));
        seq.OnComplete(() =>
        {
            if (titleRoot != null)
            {
                titleRoot.anchoredPosition = _titleTargetPos;
                titleRoot.localScale = _titleTargetScale;
            }
            if (_titleGroup != null) _titleGroup.alpha = 1f;
            _titleAppeared = true;   // 标题出现 = 可以点击进游戏
        });
        _titleTween = seq;
    }
    */

    /// <summary>点击是否落在「可交互」的 UI 上（按钮/滑条/开关等 Selectable，或子面板内容）——是则不当作「点击任意处」</summary>
    private bool IsClickOnInteractiveUi()
    {
        EventSystem es = eventSystem != null ? eventSystem : EventSystem.current;
        if (es == null) return false;

        PointerEventData data = new PointerEventData(es) { position = Input.mousePosition };
        List<RaycastResult> hits = new List<RaycastResult>();
        es.RaycastAll(data, hits);

        foreach (RaycastResult hit in hits)
        {
            if (hit.gameObject == null) continue;
            if (hit.gameObject.GetComponentInParent<Selectable>() != null) return true;   // 可交互控件
            if (IsUnderSubPanel(hit.gameObject)) return true;                             // 子面板内容
        }
        return false;
    }

    /// <summary>该物体是否属于子面板（LoadPanel / SettingsPanel）</summary>
    private bool IsUnderSubPanel(GameObject go)
    {
        if (loadPanel != null && go.transform.IsChildOf(loadPanel.transform)) return true;
        if (settingsPanel != null && go.transform.IsChildOf(settingsPanel.transform)) return true;
        return false;
    }

    /* [2026-09-19 统一 UIContentReveal] 旧的标题淡入辅助（运行时补 CanvasGroup）注释留档；现在由 UIContentReveal 自己补
    private static CanvasGroup EnsureCanvasGroup(GameObject go)
    {
        if (go == null) return null;
        CanvasGroup group = go.GetComponent<CanvasGroup>();
        if (group == null) group = go.AddComponent<CanvasGroup>();
        return group;
    }
    */

    /// <summary>
    /// 打开子面板（统一走面板上的 UIActivateMotion：SetActive(true) + OnEnable 自动播 UIPanelMotion.PlayOpen）。
    /// 面板没挂 UIActivateMotion 时回退旧行为（SetActive + 手动 PlayOpen）。
    /// </summary>
    private void OpenSubPanel(GameObject panel)
    {
        if (panel == null) return;

        UIActivateMotion activate = panel.GetComponent<UIActivateMotion>();
        if (activate != null)
        {
            activate.Open();
            return;
        }

        panel.SetActive(true);
        UIPanelMotion motion = panel.GetComponent<UIPanelMotion>();
        if (motion != null) motion.PlayOpen();
    }

    /// <summary>
    /// 关闭子面板（统一走面板上的 UIActivateMotion.Close：播 UIPanelMotion.PlayClose，播完 SetActive(false)）。
    /// 没挂 UIActivateMotion 时回退 ISlideClose / 直接隐藏。
    /// </summary>
    private void CloseSubPanel(GameObject panel)
    {
        if (panel == null) return;

        UIActivateMotion activate = panel.GetComponent<UIActivateMotion>();
        if (activate != null)
        {
            activate.Close();
            return;
        }

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
        AudioManager.Instance?.PlayUiSfx(AudioManager.UiSfxKind.Start);   // 进入游戏独立音效（AudioLibrary.uiStart，留空 = 静默）
        PendingLoadFlag.slot = -1; // 新游戏标记（读档由 SceneBootstrap 判定 slot<0 走出生点传送）
        GoToGame();
    }

    /// <summary>读档：按钮组左滑 + 打开读档面板（SaveLoadPanel mode=Load，面板内确认读档走 onLoadRequested 回调；打开动效由 OpenSubPanel 触发 UIPanelMotion）</summary>
    private void OnLoadClicked()
    {
        if (settingsPanel != null && settingsPanel.activeInHierarchy)
            settingsPanel.SetActive(false); // 切面板前先关另一个（Load/Set 互斥）
        buttonGroupMotion?.PlayClose();   // 按钮组滑出（方向由组件「关闭动效」决定）
        OpenSubPanel(loadPanel);
    }

    /// <summary>设置：按钮组左滑 + 打开 SettingsPanel（TitleScene 无 PanelManager 不走栈管理；打开动效由 OpenSubPanel 触发 UIPanelMotion；关闭由 SettingsPanel.OnBackClicked/SlideClose 兜底）</summary>
    private void OnSettingsClicked()
    {
        if (loadPanel != null && loadPanel.activeInHierarchy)
            loadPanel.SetActive(false); // 切面板前先关另一个（Load/Set 互斥）
        buttonGroupMotion?.PlayClose();   // 按钮组滑出（方向由组件「关闭动效」决定）
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
    // 按钮组滑动（统一 DOTween，与 PauseMenu.SlideToLeft/SlideToCenter 同款；原手写协程 SlideRoutine 已删）
    // ============================================================

    /* [2026-09-19 saika：主界面动效统一改用 UIPanelMotion] 旧的按钮组自绘滑动（DOTween 位置+透明度）注释留档
    private void SlideToLeft()
    {
        if (buttonGroup == null) return;
        _groupPushedLeft = true;
        float alpha = buttonCanvasGroup != null ? buttonCanvasGroup.alpha : 1f;
        PlaySlide(buttonGroup.anchoredPosition, leftPosition, alpha, 0f);
    }

    private void SlideToCenter()
    {
        if (buttonGroup == null) return;
        float alpha = buttonCanvasGroup != null ? buttonCanvasGroup.alpha : 1f;
        PlaySlide(buttonGroup.anchoredPosition, _groupCenterPos, alpha, 1f);
    }
    */

    /// <summary>
    /// 按钮组滑动（统一 DOTween：DOAnchorPos + DOFade，SetUpdate(true) 让暂停 timeScale=0 时也能播）。
    /// 每次新动画前 Kill 旧序列防重入；播完兜底复位到目标值（防中断残留半途位置）。
    /// </summary>
    /* 旧实现（PlaySlide，DOTween 自绘）注释留档
    private void PlaySlide(Vector2 fromPos, Vector2 toPos, float fromAlpha, float toAlpha)
    {
        if (buttonGroup == null) return;
        if (_slideTween != null && _slideTween.IsActive()) _slideTween.Kill();

        buttonGroup.anchoredPosition = fromPos;
        if (buttonCanvasGroup != null) buttonCanvasGroup.alpha = fromAlpha;

        Sequence seq = DOTween.Sequence();
        seq.SetUpdate(true);
        seq.Join(buttonGroup.DOAnchorPos(toPos, SlideDuration).SetEase(Ease.OutCubic));
        if (buttonCanvasGroup != null)
            seq.Join(buttonCanvasGroup.DOFade(toAlpha, SlideDuration));
        seq.OnComplete(() =>
        {
            if (buttonGroup != null) buttonGroup.anchoredPosition = toPos;
            if (buttonCanvasGroup != null) buttonCanvasGroup.alpha = toAlpha;
            _slideTween = null;
        });
        _slideTween = seq;
    }
    */
}

