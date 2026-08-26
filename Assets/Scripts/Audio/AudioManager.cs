using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 系统设置数据 — JSON 持久化结构（PlayerPrefs key: "GameSettings"）。
/// 与 SettingsPanel / AudioManager 共用：音量三路 + 画面设置。
/// </summary>
[System.Serializable]
public class GameSettingsData
{
    public float master = 1f;
    public float bgm = 1f;
    public float sfx = 1f;
    public bool fullscreen = true;
    public int resolutionIndex = 0;
}

/// <summary>
/// 音频管理器（轻量框架）— 单例，管理主音量/BGM/SFX 三路 AudioSource 组。
/// - masterSources / bgmSources / sfxSources：自动注册制，场景音源通过 RegisterSource 上报，
///   不再手动拖引用（100 场景零拖拽）。空列表/空引用安全。
/// - SetVolumes(master,bgm,sfx)：遍历各组应用音量
/// - Awake 单例防重 + DontDestroyOnLoad 常驻跨场景（TitleScene 常驻、SampleScene 的重复实例自动销毁）
/// - Awake 从 PlayerPrefs("GameSettings") 读初始值应用（跨场景生效）
/// </summary>
public class AudioManager : MonoBehaviour
{
    private static AudioManager _instance;

    public static AudioManager Instance
    {
        get
        {
            if (_instance == null)
                _instance = FindObjectOfType<AudioManager>();
            return _instance;
        }
    }

    /// <summary>音量分组(RegisterSource/UnregisterSource 用)</summary>
    public enum AudioGroup { Master, Bgm, Sfx }

    [Header("音频源组(自动注册,可留空)")]
    [Tooltip("主音量源：全局 master 音量（UI/混音）")]
    [SerializeField] private List<AudioSource> masterSources = new List<AudioSource>();
    [Tooltip("BGM 源：背景音乐，随 bgm 音量")]
    [SerializeField] private List<AudioSource> bgmSources = new List<AudioSource>();
    [Tooltip("SFX 源：音效，随 sfx 音量")]
    [SerializeField] private List<AudioSource> sfxSources = new List<AudioSource>();

    /// <summary>最近一次音量值(注册新源时应用,不重新读档)</summary>
    private float _masterVol = 1f;
    private float _bgmVol = 1f;
    private float _sfxVol = 1f;

    /// <summary>PlayerPrefs 持久化 key（与 SettingsPanel 共用）</summary>
    private const string SettingsKey = "GameSettings";

    private void Awake()
    {
        if (_instance != null)
        {
            Destroy(gameObject);
            return;
        }
        _instance = this;
        DontDestroyOnLoad(gameObject); // 常驻跨场景：双场景各挂一份时，后加载的重复实例在上方已销毁

        GameSettingsData data = LoadSettings();
        SetVolumes(data.master, data.bgm, data.sfx);
    }

    /// <summary>应用三路音量（遍历各组；null 源 / 空列表自动跳过）</summary>
    public void SetVolumes(float master, float bgm, float sfx)
    {
        _masterVol = master;
        _bgmVol = bgm;
        _sfxVol = sfx;
        ApplyVolume(masterSources, master);
        ApplyVolume(bgmSources, bgm);
        ApplyVolume(sfxSources, sfx);
    }

    /// <summary>当前 BGM 音量(切换协程缩放基准,避免覆盖用户设置)</summary>
    public float BgmVolume => _bgmVol;

    /// <summary>音源自动注册(场景播放器 Awake 调用):加入对应组并立即应用当前音量</summary>
    public void RegisterSource(AudioGroup group, AudioSource source)
    {
        if (source == null) return;
        var list = GetList(group);
        if (list == null || list.Contains(source)) return;
        list.Add(source);
        ApplyVolume(list, GetCurrentVolume(group));
    }

    /// <summary>音源注销(场景播放器 OnDestroy 调用):跨场景不残留引用</summary>
    public void UnregisterSource(AudioGroup group, AudioSource source)
    {
        if (source == null) return;
        var list = GetList(group);
        if (list != null) list.Remove(source);
    }

    private List<AudioSource> GetList(AudioGroup group)
    {
        switch (group)
        {
            case AudioGroup.Master: return masterSources;
            case AudioGroup.Bgm: return bgmSources;
            case AudioGroup.Sfx: return sfxSources;
            default: return null;
        }
    }

    private float GetCurrentVolume(AudioGroup group)
    {
        switch (group)
        {
            case AudioGroup.Master: return _masterVol;
            case AudioGroup.Bgm: return _bgmVol;
            case AudioGroup.Sfx: return _sfxVol;
            default: return 1f;
        }
    }

    private void ApplyVolume(List<AudioSource> sources, float volume)
    {
        if (sources == null) return;
        for (int i = 0; i < sources.Count; i++)
        {
            if (sources[i] != null)
                sources[i].volume = volume;
        }
    }

    // ============================================================
    // 设置持久化（与 SettingsPanel 共用同一份 JSON）
    // ============================================================

    public static GameSettingsData LoadSettings()
    {
        GameSettingsData data = new GameSettingsData();
        if (PlayerPrefs.HasKey(SettingsKey))
        {
            string json = PlayerPrefs.GetString(SettingsKey, "");
            if (!string.IsNullOrEmpty(json))
                data = JsonUtility.FromJson<GameSettingsData>(json);
        }
        return data;
    }

    public static void SaveSettings(GameSettingsData data)
    {
        if (data == null) return;
        PlayerPrefs.SetString(SettingsKey, JsonUtility.ToJson(data, prettyPrint: false));
        PlayerPrefs.Save();
    }
}
