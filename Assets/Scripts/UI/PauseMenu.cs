using System;
using DG.Tweening;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// ESC 暂停菜单 — 全屏面板，暂停游戏、锁输入、显示光标。
/// 按钮 OnEnable/OnDisable 成对绑定（抄 DeathPanel 模式）。
/// 状态机 MenuStage（Closed → Level1 → Level2）：
///   - ESC 打开（Level1）：一级菜单栏从左外滑入到场景拖好的位置 + 一级上下 bg 上下渐显滑入
///   - 保存/读取/设置（→Level2）：二级面板右侧滑入 + 二级上下 bg 上下渐显滑入（一级 bg 保持不动）
///   - 关二级（ESC/返回 → Level1）：二级 bg 反方向滑出消失 + 二级面板自身反方向滑出（一级 bg 不动不重播）
///   - 关菜单（继续/ESC → Closed）：实现 ISlideClose，走一级逆动画（menuBar 向左滑出 + 一级 bg 上下滑出消失）
///   - 返回主菜单 → SceneTransition.ToTitle()（淡出 → 切 TitleScene → 淡入）
/// 动效：DOTween（DOAnchorPos/DOFade + Sequence + SetUpdate(true)，暂停 timeScale=0 时也能播；
///       每次新动画前 Kill 旧主序列防重入，OnDisable 也 Kill 防 Tween 残留回调串台）
/// 说明：本面板不挂 UIPanelMotion —— menuBar+4bg 的分组协同比单一 open/closeEffect 复杂，
///       open/close 由本类自绘 DOTween；外部入口 ISlideClose.SlideClose / ReturnToLevel1 签名保持不变。
/// </summary>
public class PauseMenu : MonoBehaviour, IPanel, ISlideClose
{
    /// <summary>菜单阶段：Closed 关闭 / Level1 一级菜单 / Level2 二级面板打开</summary>
    private enum MenuStage
    {
        Closed,
        Level1,
        Level2
    }

    // Dialog：不被 FullScreen 互斥替换关掉 —— 打开二级面板（Save/Load）时菜单保持显示，
    // 二级面板关闭后由面板回调 ReturnToLevel1() 回一级
    public PanelType PanelType => PanelType.Dialog;
    public bool PauseGame => true;
    public bool LockInput => true;
    public bool ShowCursor => true;

    [Header("按钮")]
    [Tooltip("继续游戏 → 关闭本菜单")]
    [SerializeField] private Button btnContinue;
    [Tooltip("保存 → 开二级：打开保存面板 + 二级 bg 渐显滑入")]
    [SerializeField] private Button btnSave;
    [Tooltip("读取 → 开二级：打开读取面板 + 二级 bg 渐显滑入")]
    [SerializeField] private Button btnLoad;
    [Tooltip("设置 → 开二级：打开设置面板 + 二级 bg 渐显滑入")]
    [SerializeField] private Button btnSettings;
    [Tooltip("返回主菜单 → SceneTransition.ToTitle() 回主菜单场景")]
    [SerializeField] private Button btnQuit;

    [Header("二级面板")]
    [Tooltip("保存面板（SavePanel）— 打开用")]
    [SerializeField] private GameObject savePanel;
    [Tooltip("读取面板（LoadPanel）— 打开用")]
    [SerializeField] private GameObject loadPanel;
    [Tooltip("设置面板（SettingsPanel）— 打开用")]
    [SerializeField] private GameObject settingsPanel;

    [Header("背景")]
    [Tooltip("菜单全屏背景（Panels 下独立对象，Image 半透明，Raycast Target 取消勾选）：开菜单显示、关菜单隐藏")]
    [SerializeField] private GameObject background;

    [Header("一级背景(全屏共用,开菜单时上下渐显滑入)")]
    [Tooltip("一级上 bg（全屏共用背景，初始 inactive）：开菜单时从上方滑入 + 渐显，关菜单向上滑出 + 淡出")]
    [SerializeField] private GameObject level1BgTop;
    [Tooltip("一级下 bg（全屏共用背景，初始 inactive）：开菜单时从下方滑入 + 渐显，关菜单向下滑出 + 淡出")]
    [SerializeField] private GameObject level1BgBottom;

    [Header("二级背景(二级专属,开二级时渐显滑入,关二级反方向滑出)")]
    [Tooltip("二级上 bg（二级专属，初始 inactive）：开二级时从上方滑入 + 渐显，关二级向上滑出 + 淡出")]
    [SerializeField] private GameObject level2BgTop;
    [Tooltip("二级下 bg（二级专属，初始 inactive）：开二级时从下方滑入 + 渐显，关二级向下滑出 + 淡出")]
    [SerializeField] private GameObject level2BgBottom;

    [Header("一级菜单栏")]
    [Tooltip("一级菜单栏（RectTransform，saika 在场景拖好最终显示位置，初始 inactive）：打开时从屏幕左外滑入到此位置")]
    [SerializeField] private RectTransform menuBar;

    [Header("动效")]
    [Tooltip("屏幕外偏移量（默认 1600x900）：菜单栏/背景从屏幕外滑入滑出的距离")]
    [SerializeField] private Vector2 slideOffset = new Vector2(1600f, 900f);

    /// <summary>当前菜单阶段（OnDisable 复位 Closed；OnEnable 首次打开播一级滑入）</summary>
    private MenuStage _stage = MenuStage.Closed;

    /// <summary>一级菜单栏最终显示位置（OnEnable 首次打开激活后记录，即场景拖好的位置）</summary>
    private Vector2 _menuTargetPos;

    // 四个 bg 的最终显示位置（各自激活后记录；动画播完复位用，防反复开关累积漂移）
    private Vector2 _level1BgTopTarget;
    private Vector2 _level1BgBottomTarget;
    private Vector2 _level2BgTopTarget;
    private Vector2 _level2BgBottomTarget;

    private const float Level1SlideInDuration = 0.25f; // 一级滑入（菜单栏 + 一级 bg）
    private const float Level2SlideInDuration = 0.25f; // 二级 bg 滑入
    private const float CloseReverseDuration = 0.2f;   // 关闭逆动画（关菜单/关二级 bg 滑出）

    /// <summary>当前播放中的主动画序列（每次新动画前 Kill 防重入；OnDisable 也 Kill 防残留回调串台）</summary>
    private Sequence _activeSequence;

    private void OnEnable()
    {
        if (btnContinue != null) btnContinue.onClick.AddListener(OnContinueClicked);
        if (btnSave != null) btnSave.onClick.AddListener(OnSaveClicked);
        if (btnLoad != null) btnLoad.onClick.AddListener(OnLoadClicked);
        if (btnSettings != null) btnSettings.onClick.AddListener(OnSettingsClicked);
        if (btnQuit != null) btnQuit.onClick.AddListener(OnQuitClicked);

        // 菜单打开 → 显示全屏背景（关闭时 OnDisable 隐藏）
        if (background != null) background.SetActive(true);

        // 首次打开（Closed）：播一级滑入 + 一级上下 bg 渐显滑入；
        // 从二级返回时菜单未关闭（不触发 OnEnable），此处不重播
        if (_stage == MenuStage.Closed)
        {
            PlayLevel1Open();
            _stage = MenuStage.Level1;
        }
    }

    private void OnDisable()
    {
        if (btnContinue != null) btnContinue.onClick.RemoveListener(OnContinueClicked);
        if (btnSave != null) btnSave.onClick.RemoveListener(OnSaveClicked);
        if (btnLoad != null) btnLoad.onClick.RemoveListener(OnLoadClicked);
        if (btnSettings != null) btnSettings.onClick.RemoveListener(OnSettingsClicked);
        if (btnQuit != null) btnQuit.onClick.RemoveListener(OnQuitClicked);

        // 菜单关闭 → 隐藏全屏背景
        if (background != null) background.SetActive(false);

        // 复位阶段：下次 ESC 打开重新走一级滑入（CloseAllPanels 直接隐藏也走到这里）
        _stage = MenuStage.Closed;

        // 面板隐藏 → 打断播放中的动画（Kill 不触发 OnComplete，防 Tween 残留回调串台）
        KillActiveAnimation();
    }

    private void OnContinueClicked()
    {
        PanelManager.Instance?.CloseTopPanel();
    }

    private void OnSaveClicked()
    {
        // 切换二级面板前先关掉另一个（Load）：避免 OpenPanel 的 FullScreen 互斥替换把它塞进 history，
        // 导致关闭当前面板时误恢复旧的（切走 = 放弃旧的，只回菜单）
        if (loadPanel != null && loadPanel.activeInHierarchy)
            PanelManager.Instance?.ClosePanel(loadPanel);
        PlayLevel2(savePanel);
    }

    private void OnLoadClicked()
    {
        // 同上：先关掉另一个（Save）再切换
        if (savePanel != null && savePanel.activeInHierarchy)
            PanelManager.Instance?.ClosePanel(savePanel);
        PlayLevel2(loadPanel);
    }

    /// <summary>设置按钮 — 先关另一二级面板（save/load）→ 打开设置面板</summary>
    private void OnSettingsClicked()
    {
        // 切换二级面板前先关掉另一个（Save/Load）：避免 OpenPanel 的 FullScreen 互斥替换把它塞进 history，
        // 导致关闭当前面板时误恢复旧的（切走 = 放弃旧的，只回菜单）
        if (savePanel != null && savePanel.activeInHierarchy)
            PanelManager.Instance?.ClosePanel(savePanel);
        if (loadPanel != null && loadPanel.activeInHierarchy)
            PanelManager.Instance?.ClosePanel(loadPanel);
        PlayLevel2(settingsPanel);
    }

    /// <summary>返回主菜单按钮 — 淡出 → 切 TitleScene → 淡入；切场景前 PanelManager 面板栈/暂停态随场景销毁自动清，无需手动 CloseAllPanels</summary>
    private void OnQuitClicked()
    {
        if (SceneTransition.Instance == null)
        {
            Debug.LogWarning("[PauseMenu] SceneTransition.Instance 为 null，无法返回主菜单（场景中缺少 TransitionCanvas）");
            return;
        }
        SceneTransition.Instance.ToTitle();
    }

    // ============================================================
    // 打开动画（一级滑入 / 二级滑入）
    // ============================================================

    /// <summary>首次打开菜单（Closed → Level1）：一级菜单栏从左外滑入 + 一级上下 bg 上下渐显滑入（DOTween 同帧并行）</summary>
    private void PlayLevel1Open()
    {
        KillActiveAnimation();

        Sequence seq = DOTween.Sequence();
        seq.SetUpdate(true); // timeScale=0（暂停中开菜单）动画照播，等价原 Time.unscaledDeltaTime
        bool hasTween = false;

        // 一级菜单栏：激活后记录场景拖好的最终位置 → 设初始左外位置 → 从左往右滑入 + 渐显（0→1）
        if (menuBar != null)
        {
            menuBar.gameObject.SetActive(true);
            _menuTargetPos = menuBar.anchoredPosition; // 激活后读，即场景拖好的最终显示位置
            CanvasGroup barGroup = EnsureCanvasGroup(menuBar.gameObject);
            menuBar.anchoredPosition = _menuTargetPos + new Vector2(-slideOffset.x, 0f);
            if (barGroup != null) barGroup.alpha = 0f;
            AddMoveFade(seq, ref hasTween, menuBar, barGroup, _menuTargetPos, 1f, Level1SlideInDuration, true);
        }

        // 一级上 bg：从上方滑入 + 渐显（0→1）
        if (level1BgTop != null)
        {
            RectTransform rt = level1BgTop.GetComponent<RectTransform>();
            if (rt != null)
            {
                level1BgTop.SetActive(true);
                _level1BgTopTarget = rt.anchoredPosition;
                CanvasGroup group = EnsureCanvasGroup(level1BgTop);
                rt.anchoredPosition = _level1BgTopTarget + new Vector2(0f, slideOffset.y);
                if (group != null) group.alpha = 0f;
                AddMoveFade(seq, ref hasTween, rt, group, _level1BgTopTarget, 1f, Level1SlideInDuration, true);
            }
        }

        // 一级下 bg：从下方滑入 + 渐显（0→1）
        if (level1BgBottom != null)
        {
            RectTransform rt = level1BgBottom.GetComponent<RectTransform>();
            if (rt != null)
            {
                level1BgBottom.SetActive(true);
                _level1BgBottomTarget = rt.anchoredPosition;
                CanvasGroup group = EnsureCanvasGroup(level1BgBottom);
                rt.anchoredPosition = _level1BgBottomTarget - new Vector2(0f, slideOffset.y);
                if (group != null) group.alpha = 0f;
                AddMoveFade(seq, ref hasTween, rt, group, _level1BgBottomTarget, 1f, Level1SlideInDuration, true);
            }
        }

        if (!hasTween)
        {
            seq.Kill();
            return;
        }

        _activeSequence = seq;
        seq.OnComplete(() =>
        {
            _activeSequence = null;
            // 播完兜底复位到最终显示位/alpha=1（Tween 已推到位，防极端中断后残留半途状态累积漂移）
            if (menuBar != null)
            {
                menuBar.anchoredPosition = _menuTargetPos;
                CanvasGroup g = EnsureCanvasGroup(menuBar.gameObject);
                if (g != null) g.alpha = 1f;
            }
            if (level1BgTop != null)
            {
                RectTransform rt = level1BgTop.GetComponent<RectTransform>();
                if (rt != null) rt.anchoredPosition = _level1BgTopTarget;
                CanvasGroup g = EnsureCanvasGroup(level1BgTop);
                if (g != null) g.alpha = 1f;
            }
            if (level1BgBottom != null)
            {
                RectTransform rt = level1BgBottom.GetComponent<RectTransform>();
                if (rt != null) rt.anchoredPosition = _level1BgBottomTarget;
                CanvasGroup g = EnsureCanvasGroup(level1BgBottom);
                if (g != null) g.alpha = 1f;
            }
        });
    }

    /// <summary>进入二级（Level1 → Level2）：打开二级面板 + 二级上下 bg 渐显滑入（一级 bg 保持）；已在二级（面板切换）只切面板、不重播二级 bg</summary>
    private void PlayLevel2(GameObject panel)
    {
        if (panel != null)
            PanelManager.Instance?.OpenPanel(panel);

        bool firstLevel2 = _stage == MenuStage.Level1;
        _stage = MenuStage.Level2;
        if (!firstLevel2) return; // 保存/读取/设置之间切换：二级 bg 已显示，不重播

        KillActiveAnimation();

        // 一级复位到最终显示态（防一级滑入动画被中断时残留半途状态），一级 bg 保持不动
        if (menuBar != null)
        {
            menuBar.anchoredPosition = _menuTargetPos;
            CanvasGroup barGroup = EnsureCanvasGroup(menuBar.gameObject);
            if (barGroup != null) barGroup.alpha = 1f;
        }
        if (level1BgTop != null)
        {
            RectTransform rt = level1BgTop.GetComponent<RectTransform>();
            if (rt != null) rt.anchoredPosition = _level1BgTopTarget;
            CanvasGroup group = EnsureCanvasGroup(level1BgTop);
            if (group != null) group.alpha = 1f;
        }
        if (level1BgBottom != null)
        {
            RectTransform rt = level1BgBottom.GetComponent<RectTransform>();
            if (rt != null) rt.anchoredPosition = _level1BgBottomTarget;
            CanvasGroup group = EnsureCanvasGroup(level1BgBottom);
            if (group != null) group.alpha = 1f;
        }

        Sequence seq = DOTween.Sequence();
        seq.SetUpdate(true); // timeScale=0（暂停中开二级）动画照播
        bool hasTween = false;

        // 二级上 bg：从上方滑入 + 渐显（0→1）
        if (level2BgTop != null)
        {
            RectTransform rt = level2BgTop.GetComponent<RectTransform>();
            if (rt != null)
            {
                level2BgTop.SetActive(true);
                _level2BgTopTarget = rt.anchoredPosition;
                CanvasGroup group = EnsureCanvasGroup(level2BgTop);
                rt.anchoredPosition = _level2BgTopTarget + new Vector2(0f, slideOffset.y);
                if (group != null) group.alpha = 0f;
                AddMoveFade(seq, ref hasTween, rt, group, _level2BgTopTarget, 1f, Level2SlideInDuration, true);
            }
        }

        // 二级下 bg：从下方滑入 + 渐显（0→1）
        if (level2BgBottom != null)
        {
            RectTransform rt = level2BgBottom.GetComponent<RectTransform>();
            if (rt != null)
            {
                level2BgBottom.SetActive(true);
                _level2BgBottomTarget = rt.anchoredPosition;
                CanvasGroup group = EnsureCanvasGroup(level2BgBottom);
                rt.anchoredPosition = _level2BgBottomTarget - new Vector2(0f, slideOffset.y);
                if (group != null) group.alpha = 0f;
                AddMoveFade(seq, ref hasTween, rt, group, _level2BgBottomTarget, 1f, Level2SlideInDuration, true);
            }
        }

        if (!hasTween)
        {
            seq.Kill();
            return;
        }

        _activeSequence = seq;
        seq.OnComplete(() =>
        {
            _activeSequence = null;
            // 播完兜底复位到最终显示位/alpha=1（防极端中断后残留半途状态累积漂移）
            if (level2BgTop != null)
            {
                RectTransform rt = level2BgTop.GetComponent<RectTransform>();
                if (rt != null) rt.anchoredPosition = _level2BgTopTarget;
                CanvasGroup g = EnsureCanvasGroup(level2BgTop);
                if (g != null) g.alpha = 1f;
            }
            if (level2BgBottom != null)
            {
                RectTransform rt = level2BgBottom.GetComponent<RectTransform>();
                if (rt != null) rt.anchoredPosition = _level2BgBottomTarget;
                CanvasGroup g = EnsureCanvasGroup(level2BgBottom);
                if (g != null) g.alpha = 1f;
            }
        });
    }

    // ============================================================
    // 关闭动画（逆动画）
    // ============================================================

    /// <summary>公开入口：二级面板（Save/Load/Settings）关闭时由面板自身 SlideClose 并行调用 —— 二级上下 bg 反方向滑出消失；一级 bg 不动不重播</summary>
    public void ReturnToLevel1()
    {
        if (_stage != MenuStage.Level2) return;
        _stage = MenuStage.Level1;

        KillActiveAnimation();

        Sequence seq = DOTween.Sequence();
        seq.SetUpdate(true); // timeScale=0（暂停中关二级）动画照播
        bool hasTween = false;

        // 二级上 bg：先复位到最终显示位（防开二级动画被中断残留半途）→ 向上滑出 + 淡出（1→0）
        if (level2BgTop != null)
        {
            RectTransform rt = level2BgTop.GetComponent<RectTransform>();
            if (rt != null)
            {
                CanvasGroup group = EnsureCanvasGroup(level2BgTop);
                rt.anchoredPosition = _level2BgTopTarget;
                if (group != null) group.alpha = 1f;
                AddMoveFade(seq, ref hasTween, rt, group,
                    _level2BgTopTarget + new Vector2(0f, slideOffset.y), 0f, CloseReverseDuration, false);
            }
        }

        // 二级下 bg：先复位到最终显示位 → 向下滑出 + 淡出（1→0）
        if (level2BgBottom != null)
        {
            RectTransform rt = level2BgBottom.GetComponent<RectTransform>();
            if (rt != null)
            {
                CanvasGroup group = EnsureCanvasGroup(level2BgBottom);
                rt.anchoredPosition = _level2BgBottomTarget;
                if (group != null) group.alpha = 1f;
                AddMoveFade(seq, ref hasTween, rt, group,
                    _level2BgBottomTarget - new Vector2(0f, slideOffset.y), 0f, CloseReverseDuration, false);
            }
        }

        if (!hasTween)
        {
            seq.Kill();
            return;
        }

        _activeSequence = seq;
        seq.OnComplete(() =>
        {
            _activeSequence = null;
            // 播完复位初始摆位/alpha 并隐藏（防反复开关累积漂移，原协程收尾同款）
            if (level2BgTop != null)
            {
                RectTransform rt = level2BgTop.GetComponent<RectTransform>();
                if (rt != null) rt.anchoredPosition = _level2BgTopTarget;
                CanvasGroup g = EnsureCanvasGroup(level2BgTop);
                if (g != null) g.alpha = 1f;
                level2BgTop.SetActive(false);
            }
            if (level2BgBottom != null)
            {
                RectTransform rt = level2BgBottom.GetComponent<RectTransform>();
                if (rt != null) rt.anchoredPosition = _level2BgBottomTarget;
                CanvasGroup g = EnsureCanvasGroup(level2BgBottom);
                if (g != null) g.alpha = 1f;
                level2BgBottom.SetActive(false);
            }
        });
    }

    /// <summary>ISlideClose：关闭菜单走一级逆动画（menuBar 向左滑出 + 一级上下 bg 滑出消失），播完回调后由 PanelManager SetActive(false)</summary>
    public void SlideClose(Action onComplete)
    {
        _stage = MenuStage.Closed;
        KillActiveAnimation();

        Sequence seq = DOTween.Sequence();
        seq.SetUpdate(true); // timeScale=0（暂停中关菜单）动画照播
        bool hasTween = false;

        // 一级菜单栏：先复位到最终显示位（防开菜单动画被中断残留半途）→ 向左滑出 + 淡出（1→0）
        if (menuBar != null)
        {
            CanvasGroup barGroup = EnsureCanvasGroup(menuBar.gameObject);
            menuBar.anchoredPosition = _menuTargetPos;
            if (barGroup != null) barGroup.alpha = 1f;
            AddMoveFade(seq, ref hasTween, menuBar, barGroup,
                _menuTargetPos + new Vector2(-slideOffset.x, 0f), 0f, CloseReverseDuration, false);
        }

        // 一级上 bg：先复位到最终显示位 → 向上滑出 + 淡出（1→0）
        if (level1BgTop != null)
        {
            RectTransform rt = level1BgTop.GetComponent<RectTransform>();
            if (rt != null)
            {
                CanvasGroup group = EnsureCanvasGroup(level1BgTop);
                rt.anchoredPosition = _level1BgTopTarget;
                if (group != null) group.alpha = 1f;
                AddMoveFade(seq, ref hasTween, rt, group,
                    _level1BgTopTarget + new Vector2(0f, slideOffset.y), 0f, CloseReverseDuration, false);
            }
        }

        // 一级下 bg：先复位到最终显示位 → 向下滑出 + 淡出（1→0）
        if (level1BgBottom != null)
        {
            RectTransform rt = level1BgBottom.GetComponent<RectTransform>();
            if (rt != null)
            {
                CanvasGroup group = EnsureCanvasGroup(level1BgBottom);
                rt.anchoredPosition = _level1BgBottomTarget;
                if (group != null) group.alpha = 1f;
                AddMoveFade(seq, ref hasTween, rt, group,
                    _level1BgBottomTarget - new Vector2(0f, slideOffset.y), 0f, CloseReverseDuration, false);
            }
        }

        if (!hasTween)
        {
            seq.Kill();
            FinishLevel1Close(onComplete);
            return;
        }

        _activeSequence = seq;
        seq.OnComplete(() =>
        {
            _activeSequence = null;
            FinishLevel1Close(onComplete);
        });
    }

    /// <summary>一级逆动画收尾：复位摆位/alpha + 全部隐藏 + 回调（原 Level1CloseRoutine 结尾同款，防反复开关累积漂移）</summary>
    private void FinishLevel1Close(Action onComplete)
    {
        if (menuBar != null)
        {
            CanvasGroup g = EnsureCanvasGroup(menuBar.gameObject);
            menuBar.anchoredPosition = _menuTargetPos;
            if (g != null) g.alpha = 1f;
            menuBar.gameObject.SetActive(false);
        }
        if (level1BgTop != null)
        {
            RectTransform rt = level1BgTop.GetComponent<RectTransform>();
            CanvasGroup g = EnsureCanvasGroup(level1BgTop);
            if (rt != null) rt.anchoredPosition = _level1BgTopTarget;
            if (g != null) g.alpha = 1f;
            level1BgTop.SetActive(false);
        }
        if (level1BgBottom != null)
        {
            RectTransform rt = level1BgBottom.GetComponent<RectTransform>();
            CanvasGroup g = EnsureCanvasGroup(level1BgBottom);
            if (rt != null) rt.anchoredPosition = _level1BgBottomTarget;
            if (g != null) g.alpha = 1f;
            level1BgBottom.SetActive(false);
        }
        // 关闭菜单时兜底隐藏二级 bg（若某路径残留打开态）
        if (level2BgTop != null)
        {
            RectTransform rt = level2BgTop.GetComponent<RectTransform>();
            CanvasGroup g = EnsureCanvasGroup(level2BgTop);
            if (rt != null) rt.anchoredPosition = _level2BgTopTarget;
            if (g != null) g.alpha = 1f;
            level2BgTop.SetActive(false);
        }
        if (level2BgBottom != null)
        {
            RectTransform rt = level2BgBottom.GetComponent<RectTransform>();
            CanvasGroup g = EnsureCanvasGroup(level2BgBottom);
            if (rt != null) rt.anchoredPosition = _level2BgBottomTarget;
            if (g != null) g.alpha = 1f;
            level2BgBottom.SetActive(false);
        }

        onComplete?.Invoke();
    }

    // ============================================================
    // 动效工具
    // ============================================================

    /// <summary>
    /// 把“位移 + 透明度”一对并行 tween 追加进主序列：首对 Append、后续 Join（同帧同起点并行）。
    /// 起点初态由调用方先摆好（位置=目标+偏移、alpha=0 或复位 target/alpha=1），这里只负责“动到 toPos/toAlpha”。
    /// </summary>
    private static void AddMoveFade(Sequence seq, ref bool hasTween, RectTransform target, CanvasGroup group,
        Vector2 toPos, float toAlpha, float duration, bool easeOutCubic)
    {
        if (target == null) return;

        Tween move = target.DOAnchorPos(toPos, duration);
        move.SetUpdate(true); // 逐 tween 显式 unscaled：子 tween 不随 Sequence 的 SetUpdate 传播时也保证 timeScale=0 照播
        if (easeOutCubic) move.SetEase(Ease.OutCubic);

        if (!hasTween)
            seq.Append(move);
        else
            seq.Join(move);

        if (group != null)
        {
            Tween fade = group.DOFade(toAlpha, duration);
            fade.SetUpdate(true);
            seq.Join(fade);
        }

        hasTween = true;
    }

    /// <summary>打断当前动画：Kill 主序列（打断语义与旧协程停止一致）。Kill 不触发 OnComplete，防旧回调在错误时机串台</summary>
    private void KillActiveAnimation()
    {
        if (_activeSequence == null) return;
        if (_activeSequence.IsActive())
            _activeSequence.Kill();
        _activeSequence = null;
    }

    /// <summary>自动补 CanvasGroup（参考 UIFadeManager.EnsureCanvasGroup 模式）</summary>
    private CanvasGroup EnsureCanvasGroup(GameObject go)
    {
        if (go == null) return null;
        CanvasGroup group = go.GetComponent<CanvasGroup>();
        if (group == null)
            group = go.AddComponent<CanvasGroup>();
        return group;
    }
}
