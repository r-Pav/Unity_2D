using System;
using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 系统设置面板 — 页签式（音量 / 画面），挂 SettingsPanel 物体。
/// IPanel：FullScreen + Pause + Lock + Cursor；实现 ISlideClose（关闭时向右滑出，不拖入 UIFadeManager.fadePanels）。
/// 场景结构（saika 已搭，勿重建）：
///   SettingsPanel > BTN > Btn_Volume / Btn_Video
///   SettingsPanel > Grp_Volume > Master+Sld_Master / BGM+Sld_BGM / SFX+Sld_SFX
///   SettingsPanel > Grp_Video > Fullscreen+Toggle / Dd_Resolution+Dropdown(TMP_Dropdown)
///   SettingsPanel > Btn_Back
/// 行为：
///   - 页签互斥：Btn_Volume → Grp_Volume 显示 / Grp_Video 隐藏；Btn_Video 反向；OnEnable 默认画面页
///   - 任意改动立即应用：音量 → AudioManager.Instance.SetVolumes()；全屏 → Screen.fullScreen；
///     分辨率 → 解析选项文本 Split('x') → Screen.SetResolution(w, h, fullscreen)
///   - PlayerPrefs 持久化：key "GameSettings"，JSON 存 master/bgm/sfx/fullscreen/resolutionIndex；OnEnable 读回刷新控件
///   - Btn_Back → PanelManager 存在时 CloseTopPanel（检测 ISlideClose 走右滑关闭动效，并行 pauseMenu.ReturnToLevel1()）；
///     无 PanelManager（TitleScene 主菜单）时直接 SetActive(false) 显隐关闭
/// </summary>
public class SettingsPanel : MonoBehaviour, IPanel, ISlideClose
{
    public PanelType PanelType => PanelType.FullScreen;
    public bool PauseGame => true;
    public bool LockInput => true;
    public bool ShowCursor => true;

    [Header("页签")]
    [Tooltip("音量页签按钮")]
    [SerializeField] private Button btnVolume;
    [Tooltip("画面页签按钮")]
    [SerializeField] private Button btnVideo;
    [Tooltip("音量组（Grp_Volume）")]
    [SerializeField] private GameObject grpVolume;
    [Tooltip("画面组（Grp_Video）")]
    [SerializeField] private GameObject grpVideo;

    [Header("音量")]
    [Tooltip("主音量滑条（0~1）")]
    [SerializeField] private Slider masterSlider;
    [Tooltip("BGM 滑条（0~1）")]
    [SerializeField] private Slider bgmSlider;
    [Tooltip("SFX 滑条（0~1）")]
    [SerializeField] private Slider sfxSlider;

    [Header("画面")]
    [Tooltip("全屏开关")]
    [SerializeField] private Toggle fullscreenToggle;
    [Tooltip("分辨率下拉（1920x1080/1600x900/1280x720/1280x800，选项已在场景配好不得改动）")]
    [SerializeField] private TMP_Dropdown resolutionDropdown;

    [Header("返回")]
    [Tooltip("返回按钮 → 关闭当前页（向右滑出，PauseMenu 回一级）")]
    [SerializeField] private Button backButton;
    [Tooltip("PauseMenu 引用（拖 PauseMenu 物体）：本面板关闭后菜单回一级（ReturnToLevel1）")]
    [SerializeField] private PauseMenu pauseMenu;

    private RectTransform _rect;
    private CanvasGroup _canvasGroup;
    private Coroutine _closeRoutine;

    private const float SlideCloseDuration = 0.2f;
    private const float SlideCloseDistance = 300f; // 向右滑出（反方向，与 SlideIn 同向）；SlideIn +300 从右滑入
    private const float SlideInDistance = 300f; // 打开从右侧滑入（与 SaveLoadPanel 一致：+300 = 从右往左滑入）

    private void Awake()
    {
        _rect = GetComponent<RectTransform>();
        _canvasGroup = GetComponent<CanvasGroup>();
        if (_canvasGroup == null)
            _canvasGroup = gameObject.AddComponent<CanvasGroup>();
        // pauseMenu 兜底：Inspector 未拖时自动查找（PauseMenu 打开本面板前必已激活，同 Canvas 内唯一）
        if (pauseMenu == null)
            pauseMenu = FindObjectOfType<PauseMenu>();
    }

    private void OnEnable()
    {
        if (btnVolume != null) btnVolume.onClick.AddListener(OnVolumeTabClicked);
        if (btnVideo != null) btnVideo.onClick.AddListener(OnVideoTabClicked);
        if (masterSlider != null) masterSlider.onValueChanged.AddListener(OnMasterChanged);
        if (bgmSlider != null) bgmSlider.onValueChanged.AddListener(OnBgmChanged);
        if (sfxSlider != null) sfxSlider.onValueChanged.AddListener(OnSfxChanged);
        if (fullscreenToggle != null) fullscreenToggle.onValueChanged.AddListener(OnFullscreenChanged);
        if (resolutionDropdown != null) resolutionDropdown.onValueChanged.AddListener(OnResolutionChanged);
        if (backButton != null) backButton.onClick.AddListener(OnBackClicked);

        // OnEnable 默认画面页（Grp_Volume 隐藏 / Grp_Video 显示）
        ShowVideoTab();

        // 每次打开：从右侧滑入出现（右滑出现 + 渐显 0→1，与 SaveLoadPanel 打开动效一致）
        if (_canvasGroup != null)
            _canvasGroup.alpha = 0f;
        StartSlideIn();

        LoadFromPrefs();
    }

    private void OnDisable()
    {
        if (btnVolume != null) btnVolume.onClick.RemoveListener(OnVolumeTabClicked);
        if (btnVideo != null) btnVideo.onClick.RemoveListener(OnVideoTabClicked);
        if (masterSlider != null) masterSlider.onValueChanged.RemoveListener(OnMasterChanged);
        if (bgmSlider != null) bgmSlider.onValueChanged.RemoveListener(OnBgmChanged);
        if (sfxSlider != null) sfxSlider.onValueChanged.RemoveListener(OnSfxChanged);
        if (fullscreenToggle != null) fullscreenToggle.onValueChanged.RemoveListener(OnFullscreenChanged);
        if (resolutionDropdown != null) resolutionDropdown.onValueChanged.RemoveListener(OnResolutionChanged);
        if (backButton != null) backButton.onClick.RemoveListener(OnBackClicked);
    }

    // ============================================================
    // 页签互斥
    // ============================================================

    private void OnVolumeTabClicked()
    {
        ShowVolumeTab();
    }

    private void OnVideoTabClicked()
    {
        ShowVideoTab();
    }

    private void ShowVolumeTab()
    {
        if (grpVolume != null) grpVolume.SetActive(true);
        if (grpVideo != null) grpVideo.SetActive(false);
    }

    private void ShowVideoTab()
    {
        if (grpVolume != null) grpVolume.SetActive(false);
        if (grpVideo != null) grpVideo.SetActive(true);
    }

    // ============================================================
    // 改动即应用 + 持久化
    // ============================================================

    private void OnMasterChanged(float value)
    {
        ApplyVolumes();
        SaveToPrefs();
    }

    private void OnBgmChanged(float value)
    {
        ApplyVolumes();
        SaveToPrefs();
    }

    private void OnSfxChanged(float value)
    {
        ApplyVolumes();
        SaveToPrefs();
    }

    private void ApplyVolumes()
    {
        if (AudioManager.Instance == null) return;
        AudioManager.Instance.SetVolumes(
            masterSlider != null ? masterSlider.value : 1f,
            bgmSlider != null ? bgmSlider.value : 1f,
            sfxSlider != null ? sfxSlider.value : 1f);
    }

    private void OnFullscreenChanged(bool value)
    {
        Screen.fullScreen = value;
        SaveToPrefs();
    }

    private void OnResolutionChanged(int index)
    {
        ApplyResolution(index);
        SaveToPrefs();
    }

    private void ApplyResolution(int index)
    {
        if (resolutionDropdown == null || index < 0 || index >= resolutionDropdown.options.Count) return;

        // 解析选项文本 "1920x1080" → Split('x') → SetResolution(w, h, fullscreen)
        string[] parts = resolutionDropdown.options[index].text.Split('x');
        if (parts.Length != 2) return;

        int w, h;
        if (!int.TryParse(parts[0].Trim(), out w)) return;
        if (!int.TryParse(parts[1].Trim(), out h)) return;

        Screen.SetResolution(w, h, Screen.fullScreen);
    }

    private void OnBackClicked()
    {
        // 游戏内（SampleScene）：PanelManager 在 → 走栈管理 CloseTopPanel（PanelManager 检测 ISlideClose 走左滑关闭动效）
        // 主菜单（TitleScene）：无 PanelManager → 自己走 SlideClose 动画（播完再 SetActive(false)，与游戏内动效一致）
        if (PanelManager.Instance != null)
        {
            PanelManager.Instance.CloseTopPanel();
        }
        else
        {
            SlideClose(() => gameObject.SetActive(false));
        }
    }

    // ============================================================
    // SlideIn（打开动画）— 与 SaveLoadPanel 同款：右侧滑入 + 渐显 0→1
    // ============================================================

    private void StartSlideIn()
    {
        if (_rect == null) return;
        if (_closeRoutine != null) StopCoroutine(_closeRoutine);
        Vector2 target = _rect.anchoredPosition;
        _closeRoutine = StartCoroutine(SlideInRoutine(target));
    }

    private IEnumerator SlideInRoutine(Vector2 target)
    {
        Vector2 from = target + new Vector2(SlideInDistance, 0f);
        float elapsed = 0f;
        while (elapsed < SlideCloseDuration)
        {
            elapsed += Time.unscaledDeltaTime; // 暂停（timeScale=0）时仍正常播放
            float t = Mathf.Clamp01(elapsed / SlideCloseDuration);
            if (_rect != null) _rect.anchoredPosition = Vector2.Lerp(from, target, t);
            if (_canvasGroup != null) _canvasGroup.alpha = Mathf.Lerp(0f, 1f, t);
            yield return null;
        }
        if (_rect != null) _rect.anchoredPosition = target;
        if (_canvasGroup != null) _canvasGroup.alpha = 1f;
    }

    // ============================================================
    // PlayerPrefs 持久化（key: "GameSettings"）
    // ============================================================

    private void LoadFromPrefs()
    {
        GameSettingsData data = AudioManager.LoadSettings();

        // 滑块/开关赋值会触发 onValueChanged 回调 → 立即应用（幂等）+ 回写持久化，此处无需额外处理
        if (masterSlider != null) masterSlider.value = data.master;
        if (bgmSlider != null) bgmSlider.value = data.bgm;
        if (sfxSlider != null) sfxSlider.value = data.sfx;
        if (fullscreenToggle != null) fullscreenToggle.isOn = data.fullscreen;

        if (resolutionDropdown != null)
        {
            int index = data.resolutionIndex;
            if (index < 0 || index >= resolutionDropdown.options.Count)
                index = 0;
            resolutionDropdown.value = index;
            ApplyResolution(index); // 恢复分辨率（Screen 初始可能与应用内保存值不同）
        }
    }

    private void SaveToPrefs()
    {
        GameSettingsData data = new GameSettingsData
        {
            master = masterSlider != null ? masterSlider.value : 1f,
            bgm = bgmSlider != null ? bgmSlider.value : 1f,
            sfx = sfxSlider != null ? sfxSlider.value : 1f,
            fullscreen = fullscreenToggle != null ? fullscreenToggle.isOn : Screen.fullScreen,
            resolutionIndex = resolutionDropdown != null ? resolutionDropdown.value : 0
        };
        AudioManager.SaveSettings(data);
    }

    // ============================================================
    // ISlideClose — 向右滑出（不拖入 UIFadeManager.fadePanels）
    // ============================================================

    public void SlideClose(Action onComplete)
    {
        if (_closeRoutine != null) StopCoroutine(_closeRoutine);
        // 并行动效：菜单回一级（二级 bg 反方向滑出消失）与 本面板向右滑出 同时进行（一起走）
        if (pauseMenu != null) pauseMenu.ReturnToLevel1();
        _closeRoutine = StartCoroutine(SlideCloseRoutine(onComplete));
    }

    private IEnumerator SlideCloseRoutine(Action onComplete)
    {
        Vector2 startPos = _rect != null ? _rect.anchoredPosition : Vector2.zero;
        Vector2 targetPos = startPos + new Vector2(SlideCloseDistance, 0f);
        float startAlpha = _canvasGroup != null ? _canvasGroup.alpha : 1f;

        float elapsed = 0f;
        while (elapsed < SlideCloseDuration)
        {
            elapsed += Time.unscaledDeltaTime; // 暂停（timeScale=0）时仍正常播放
            float t = Mathf.Clamp01(elapsed / SlideCloseDuration);
            if (_rect != null) _rect.anchoredPosition = Vector2.Lerp(startPos, targetPos, t);
            if (_canvasGroup != null) _canvasGroup.alpha = Mathf.Lerp(startAlpha, 0f, t);
            yield return null;
        }
        if (_rect != null) _rect.anchoredPosition = targetPos;
        if (_canvasGroup != null) _canvasGroup.alpha = 0f;

        // 右滑只是视觉动画：播完恢复初始位置，否则下次打开位置残留偏移
        if (_rect != null) _rect.anchoredPosition = startPos;

        onComplete?.Invoke();
    }
}
