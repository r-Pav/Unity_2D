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
/// - masterSources / bgmSources / sfxSources：Inspector 可拖可留空（空数组不报错，遍历 null 安全）
/// - SetVolumes(master,bgm,sfx)：遍历各数组应用 volume
/// - Awake 从 PlayerPrefs("GameSettings") 读初始值应用（跨场景生效）
/// 挂载点由场景侧决定（推荐常驻物体）；场景中已有 AudioSource 拖入对应数组即可生效。
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

    [Header("音频源组（可留空）")]
    [Tooltip("主音量源：全局 master 音量（UI/混音）")]
    [SerializeField] private AudioSource[] masterSources;
    [Tooltip("BGM 源：背景音乐，随 bgm 音量")]
    [SerializeField] private AudioSource[] bgmSources;
    [Tooltip("SFX 源：音效，随 sfx 音量")]
    [SerializeField] private AudioSource[] sfxSources;

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

        GameSettingsData data = LoadSettings();
        SetVolumes(data.master, data.bgm, data.sfx);
    }

    /// <summary>应用三路音量（遍历各数组；null 源 / 空数组自动跳过）</summary>
    public void SetVolumes(float master, float bgm, float sfx)
    {
        ApplyVolume(masterSources, master);
        ApplyVolume(bgmSources, bgm);
        ApplyVolume(sfxSources, sfx);
    }

    private void ApplyVolume(AudioSource[] sources, float volume)
    {
        if (sources == null) return;
        for (int i = 0; i < sources.Length; i++)
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
