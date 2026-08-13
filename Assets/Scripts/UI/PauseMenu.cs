using System.Collections;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// ESC 暂停菜单 — 全屏面板，暂停游戏、锁输入、显示光标。
/// 按钮 OnEnable/OnDisable 成对绑定（抄 DeathPanel 模式）。
/// 行为：
///   - 继续游戏 → PanelManager.CloseTopPanel()
///   - 保存/读取/设置 → 菜单靠左滑出（SlideToLeft）+ 打开对应二级面板（二级面板在右侧）
///   - 返回主菜单 → 置灰（interactable=false，功能后续）
/// 动效：
///   - SlideToLeft()：anchoredPosition 居中→leftPosition（绝对坐标，菜单左移后同屏可见，alpha 不变）
///   - SlideToCenter()：anchoredPosition leftPosition→居中（返回默认位置，alpha 不变）
///   原生协程 + Time.unscaledDeltaTime（暂停时也能播）
/// </summary>
public class PauseMenu : MonoBehaviour, IPanel
{
    // Dialog：不被 FullScreen 互斥替换关掉 —— 打开二级面板（Save/Load）时菜单保持显示（左移同屏），
    // 二级面板关闭后由 SaveLoadPanel 回调 ReturnToCenter() 右移回默认位置
    public PanelType PanelType => PanelType.Dialog;
    public bool PauseGame => true;
    public bool LockInput => true;
    public bool ShowCursor => true;

    [Header("按钮")]
    [Tooltip("继续游戏 → 关闭本菜单")]
    [SerializeField] private Button btnContinue;
    [Tooltip("保存 → 靠左滑出 + 打开保存面板")]
    [SerializeField] private Button btnSave;
    [Tooltip("读取 → 靠左滑出 + 打开读取面板")]
    [SerializeField] private Button btnLoad;
    [Tooltip("设置 → 靠左滑出 + 打开设置面板")]
    [SerializeField] private Button btnSettings;
    [Tooltip("返回主菜单 — 置灰占位，待办2 完成后启用")]
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

    [Header("动效")]
    [Tooltip("靠左目标位置（绝对 anchoredPosition）：默认菜单居中在场景初始位置；点击保存/读取后菜单左移到此处，与右侧二级面板同屏显示")]
    [SerializeField] private Vector2 leftPosition = new Vector2(-322.21f, 0f);

    /// <summary>居中 anchoredPosition（Awake 记录初始值）</summary>
    private Vector2 _centerPos;

    /// <summary>是否已被推到左侧（二级面板打开状态；返回时据此向右渐显回居中）</summary>
    private bool _pushedLeft;

    private RectTransform _rect;
    private CanvasGroup _canvasGroup;

    private const float SlideDuration = 0.2f;

    private void Awake()
    {
        _rect = GetComponent<RectTransform>();
        _centerPos = _rect != null ? _rect.anchoredPosition : Vector2.zero;

        _canvasGroup = GetComponent<CanvasGroup>();
        if (_canvasGroup == null)
            _canvasGroup = gameObject.AddComponent<CanvasGroup>();
    }

    private void OnEnable()
    {
        if (btnContinue != null) btnContinue.onClick.AddListener(OnContinueClicked);
        if (btnSave != null) btnSave.onClick.AddListener(OnSaveClicked);
        if (btnLoad != null) btnLoad.onClick.AddListener(OnLoadClicked);
        if (btnSettings != null) btnSettings.onClick.AddListener(OnSettingsClicked);
        if (btnQuit != null) btnQuit.onClick.AddListener(OnQuitClicked);

        // 菜单打开 → 显示全屏背景（关闭时 OnDisable 隐藏）
        if (background != null) background.SetActive(true);

        // 返回主菜单：功能未实现，置灰（设置已实现，保持可用）
        if (btnQuit != null) btnQuit.interactable = false;

        if (_pushedLeft)
        {
            // 从二级面板返回 → 向右渐显回居中
            _pushedLeft = false;
            SlideToCenter();
        }
        else
        {
            // 普通打开（首次 ESC）：确保回正中且不透明（防止上次滑动中途关闭残留左偏/半透明状态）
            if (_rect != null) _rect.anchoredPosition = _centerPos;
            if (_canvasGroup != null) _canvasGroup.alpha = 1f;
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
        SlideToLeft();
        if (savePanel != null)
            PanelManager.Instance?.OpenPanel(savePanel);
    }

    private void OnLoadClicked()
    {
        // 同上：先关掉另一个（Save）再切换
        if (savePanel != null && savePanel.activeInHierarchy)
            PanelManager.Instance?.ClosePanel(savePanel);
        SlideToLeft();
        if (loadPanel != null)
            PanelManager.Instance?.OpenPanel(loadPanel);
    }

    /// <summary>设置按钮 — 先关另一二级面板（save/load）→ 菜单靠左滑出 → 打开设置面板</summary>
    private void OnSettingsClicked()
    {
        // 切换二级面板前先关掉另一个（Save/Load）：避免 OpenPanel 的 FullScreen 互斥替换把它塞进 history，
        // 导致关闭当前面板时误恢复旧的（切走 = 放弃旧的，只回菜单）
        if (savePanel != null && savePanel.activeInHierarchy)
            PanelManager.Instance?.ClosePanel(savePanel);
        if (loadPanel != null && loadPanel.activeInHierarchy)
            PanelManager.Instance?.ClosePanel(loadPanel);
        SlideToLeft();
        if (settingsPanel != null)
            PanelManager.Instance?.OpenPanel(settingsPanel);
    }

    /// <summary>返回主菜单按钮 — 置灰，依赖待办2</summary>
    private void OnQuitClicked()
    {
    }

    /// <summary>菜单靠左滑出：居中 → leftPosition（alpha 保持不变，菜单同屏可见）</summary>
    private void SlideToLeft()
    {
        if (_rect == null) return;
        _pushedLeft = true;
        float alpha = _canvasGroup != null ? _canvasGroup.alpha : 1f;
        StopAllCoroutines();
        StartCoroutine(SlideRoutine(_rect.anchoredPosition, leftPosition, alpha, alpha));
    }

    /// <summary>菜单右移回默认位置：leftPosition → 居中（alpha 保持不变，同屏可见）</summary>
    private void SlideToCenter()
    {
        if (_rect == null) return;
        StopAllCoroutines();
        float alpha = _canvasGroup != null ? _canvasGroup.alpha : 1f;
        StartCoroutine(SlideRoutine(_rect.anchoredPosition, _centerPos, alpha, alpha));
    }

    /// <summary>公开入口：二级面板（Save/Load）关闭后由 SaveLoadPanel 调用，菜单右移回默认位置</summary>
    public void ReturnToCenter()
    {
        _pushedLeft = false; // 复位靠左状态：下次 ESC 打开菜单直接居中显示
        SlideToCenter();
    }

    private IEnumerator SlideRoutine(Vector2 fromPos, Vector2 toPos, float fromAlpha, float toAlpha)
    {
        float elapsed = 0f;
        while (elapsed < SlideDuration)
        {
            elapsed += Time.unscaledDeltaTime; // 暂停（timeScale=0）时仍正常播放
            float t = Mathf.Clamp01(elapsed / SlideDuration);
            if (_rect != null) _rect.anchoredPosition = Vector2.Lerp(fromPos, toPos, t);
            if (_canvasGroup != null) _canvasGroup.alpha = Mathf.Lerp(fromAlpha, toAlpha, t);
            yield return null;
        }
        if (_rect != null) _rect.anchoredPosition = toPos;
        if (_canvasGroup != null) _canvasGroup.alpha = toAlpha;
    }
}
