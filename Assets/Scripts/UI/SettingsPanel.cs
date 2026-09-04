using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 系统设置面板 — 页签式（音量 / 画面），挂 SettingsPanel 物体。
/// IPanel：FullScreen + Pause + Lock + Cursor；实现 ISlideClose（兼容 MainMenu / PanelManager 旧分支的调用入口）。
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
///   - Btn_Back → PanelManager 存在时 CloseTopPanel（面板挂 UIPanelMotion → PlayClose；未挂 → ISlideClose 兼容分支）；
///     无 PanelManager（TitleScene 主菜单）时自己走 SlideClose（内部转调 UIPanelMotion.PlayClose，未挂则直接回调）
/// 动效（S3 起）：不再自带滑入/滑出手写协程，统一交给 UIPanelMotion（见 SaveLoadPanel 说明）。
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
    [Tooltip("返回按钮 → 关闭当前页（关闭动效由 UIPanelMotion 承担，PauseMenu 回一级）")]
    [SerializeField] private Button backButton;
    [Tooltip("PauseMenu 引用（拖 PauseMenu 物体）：本面板关闭后菜单回一级（ReturnToLevel1）")]
    [SerializeField] private PauseMenu pauseMenu;

    private void Awake()
    {
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

        // 打开动效由 PanelManager.OpenPanel / MainMenu.OpenSubPanel 调 UIPanelMotion.PlayOpen 承担（S3 起），此处不再自播滑入

        LoadFromPrefs();

        // 打开设置:背景音乐淡出(静音渐变,不暂停)
        MusicPointManager.Instance?.SetBgmMuted(true);
    }

    private void OnDisable()
    {
        // 关闭设置:背景音乐淡入恢复
        MusicPointManager.Instance?.SetBgmMuted(false);

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
        // 游戏内（SampleScene）：PanelManager 在 → 走栈管理 CloseTopPanel
        // （面板挂 UIPanelMotion → PlayClose；未挂 → ISlideClose 兼容分支 → SlideClose → 本面板直接回调隐藏）
        // 主菜单（TitleScene）：无 PanelManager → 自己走 SlideClose（内部转调 UIPanelMotion.PlayClose，
        // 播完 SetActive(false)；未挂 UIPanelMotion 则直接回调隐藏）
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
    // ISlideClose — 兼容关闭入口（MainMenu/TitleScene 直接调用；PanelManager 旧分支）
    // ============================================================

    /// <summary>
    /// 关闭动效统一转调 UIPanelMotion.PlayClose（关闭方向/距离由组件 slideDistance 等配置）；
    /// 未挂 UIPanelMotion（saika 场景配置前）直接回调 onComplete，不写代码兜底动画。
    /// pauseMenu.ReturnToLevel1() 保留：SampleScene 暂停菜单二级 bg 回一级动画仍由菜单自身承担（本面板不写动画）。
    /// </summary>
    public void SlideClose(Action onComplete)
    {
        // 并行动效：暂停菜单回一级（二级 bg 反方向滑出消失）与本面板关闭同时进行（SampleScene 兼容路径；TitleScene 无 PauseMenu 自动跳过）
        if (pauseMenu != null) pauseMenu.ReturnToLevel1();

        UIPanelMotion motion = GetComponent<UIPanelMotion>();
        if (motion != null)
            motion.PlayClose(onComplete);
        else
            onComplete?.Invoke();
    }
}
