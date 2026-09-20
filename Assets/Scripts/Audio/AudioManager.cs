using System.Collections;
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
/// - 执行顺序 -10000：保证本组件在任何业务脚本 Awake 之前完成接管，
///   业务脚本(如 MusicPointManager.Awake → RegisterAudioSources)访问 Instance 时拿到的一定是已初始化实例
/// - masterSources / bgmSources / sfxSources：自动注册制，场景音源通过 RegisterSource 上报，
///   不再手动拖引用（100 场景零拖拽）。空列表/空引用安全。
/// - SetVolumes(master,bgm,sfx)：遍历各组应用音量
/// - Awake 单例防重(判 _instance != this) + DontDestroyOnLoad 常驻跨场景(后加载场景的重复实例自毁)
/// - OnDestroy 自清单例引用(Instance 无 Find 兜底,靠这一对维护)
/// - Awake 从 PlayerPrefs("GameSettings") 读初始值应用（跨场景生效）
/// - library：全局音效配置 SO(AudioLibrary)，UI 音效 clip 与相对音量从它取；为空时 Awake 警告一次并全静默
/// </summary>
[DefaultExecutionOrder(-10000)]
public class AudioManager : MonoBehaviour
{
    private static AudioManager _instance;

    /// <summary>
    /// 当前实例。无 Find 兜底：靠 Awake 接管(判 _instance != this) + OnDestroy 自清维护，
    /// 避免兜底把"还没 Awake 的自己"提前写进静态字段导致自身被当重复实例销毁。
    /// </summary>
    public static AudioManager Instance => _instance;

    /// <summary>音量分组(RegisterSource/UnregisterSource 用)</summary>
    public enum AudioGroup { Master, Bgm, Sfx }

    /// <summary>UI 音效类型(全局共用;None = 不播)</summary>
    public enum UiSfxKind { None, Hover, Click, Close, Open, Start }   // Start 加在末尾:枚举是序列化值,插中间会让已有配置错位

    [Header("音频源组(自动注册,可留空)")]
    [Tooltip("主音量源：全局 master 音量（UI/混音）")]
    [SerializeField] private List<AudioSource> masterSources = new List<AudioSource>();
    [Tooltip("BGM 源：背景音乐，随 bgm 音量")]
    [SerializeField] private List<AudioSource> bgmSources = new List<AudioSource>();
    [Tooltip("SFX 源：音效，随 sfx 音量")]
    [SerializeField] private List<AudioSource> sfxSources = new List<AudioSource>();

    [Header("SFX 播放池(运行时自建,无需拖引用)")]
    [Tooltip("一次性音效并发音源数量(轮转取源 + PlayOneShot,连击多声可重叠)")]
    [SerializeField] private int sfxPoolSize = 6;

    [Header("SFX 排程池(卡点音排程用,运行时自建)")]
    [Tooltip("排程音效并发音源数量:PlayScheduled 把音排到指定 dspTime(拍点)播,一个源同一时刻只能占一个排程,连音密集时需要多个")]
    [SerializeField] private int scheduledSfxPoolSize = 3;

    [Header("音频库")]
    [Tooltip("全局音效配置资产(AudioLibrary);为空则音效静默")]
    [SerializeField] private AudioLibrary library;

    /// <summary>最近一次音量值(注册新源时应用,不重新读档)</summary>
    private float _masterVol = 1f;
    private float _bgmVol = 1f;

    /// <summary>过渡期 BGM 淡变系数(0=静音 1=用户设置音量)。叠加在用户设置之上,不改用户设置本身;
    /// 新注册的 BGM 源(过渡后新场景的播放器)注册即拿当前系数 → 加载期间保持静音,到点再渐入。</summary>
    private float _bgmFadeMul = 1f;
    private Coroutine _bgmFadeRoutine;
    private float _sfxVol = 1f;

    /// <summary>SFX 播放池(轮转取源,PlayOneShot 支持重叠)</summary>
    private AudioSource[] _sfxPool;
    private int _sfxNext;

    /// <summary>SFX 排程池(PlayScheduled 卡点音用):一个源同一时刻只能排一个音,取空闲源,全忙时轮转顶掉最早的</summary>
    private AudioSource[] _scheduledSfxPool;
    private int _scheduledNext;

    /// <summary>PlayerPrefs 持久化 key（与 SettingsPanel 共用）</summary>
    private const string SettingsKey = "GameSettings";

    private void Awake()
    {
        if (_instance != null && _instance != this)   // 判 != this：别的脚本先摸 Instance 时不会把自己算成重复实例
        {
            Destroy(gameObject);
            return;
        }
        _instance = this;
        DontDestroyOnLoad(gameObject); // 常驻跨场景：双场景各挂一份时，后加载的重复实例在上方已销毁

        if (library == null)
            Debug.LogWarning("[AudioManager] 未挂音频库(AudioLibrary),音效将全部静默"); // 排查配置遗漏:两份 AudioManager 都必须引同一个资产

        GameSettingsData data = LoadSettings();
        SetVolumes(data.master, data.bgm, data.sfx);
        CreateSfxPool();
    }

    /// <summary>自清单例引用：Instance 不再需要 Find 兜底(销毁后静态字段不留脏引用；重复实例自毁时 _instance != this 不会误清)</summary>
    private void OnDestroy()
    {
        if (_instance == this) _instance = null;
    }

    /// <summary>应用三路音量（遍历各组；null 源 / 空列表自动跳过）</summary>
    public void SetVolumes(float master, float bgm, float sfx)
    {
        _masterVol = master;
        _bgmVol = bgm;
        _sfxVol = sfx;
        ApplyVolume(masterSources, master);
        ApplyBgmVolume();   // = bgm × 过渡系数(见 FadeBgmMultiplier)
        ApplyVolume(sfxSources, sfx);
    }

    /// <summary>
    /// 过渡期 BGM 淡变(场景切换时用):把 BGM 组整体响度乘一个系数过渡到 targetMul(0 静音 / 1 恢复)。
    /// 系数叠加在用户设置音量之上,不动用户设置;时长用 unscaledDeltaTime(过渡可能发生在 timeScale=0)。
    /// 再次调用会打断上一次淡变。duration ≤ 0 时立即到位。
    /// </summary>
    public void FadeBgmMultiplier(float targetMul, float duration)
    {
        if (_bgmFadeRoutine != null)
        {
            StopCoroutine(_bgmFadeRoutine);
            _bgmFadeRoutine = null;
        }

        if (duration <= 0f)
        {
            _bgmFadeMul = targetMul;
            ApplyBgmVolume();
            return;
        }

        _bgmFadeRoutine = StartCoroutine(BgmFadeRoutine(targetMul, duration));
    }

    private IEnumerator BgmFadeRoutine(float targetMul, float duration)
    {
        float start = _bgmFadeMul;
        float elapsed = 0f;
        while (elapsed < duration)
        {
            // 夹大帧:场景加载后首帧 unscaledDeltaTime 含整段加载耗时,不夹会一帧跳完整段淡变
            elapsed += Mathf.Min(Time.unscaledDeltaTime, MaxFrameStep);
            _bgmFadeMul = Mathf.Lerp(start, targetMul, Mathf.Clamp01(elapsed / duration));
            ApplyBgmVolume();
            yield return null;
        }

        _bgmFadeMul = targetMul;
        ApplyBgmVolume();
        _bgmFadeRoutine = null;
    }

    /// <summary>BGM 组音量应用点(唯一):用户设置音量 × 过渡淡变系数</summary>
    private void ApplyBgmVolume()
    {
        ApplyVolume(bgmSources, _bgmVol * _bgmFadeMul);
    }

    /// <summary>单帧最大步进(秒):场景同步加载后首帧 deltaTime 含加载耗时,不夹住会把整段渐变一帧跳完</summary>
    private const float MaxFrameStep = 0.1f;

    /// <summary>当前 BGM 音量(切换协程缩放基准,避免覆盖用户设置)</summary>
    public float BgmVolume => _bgmVol;

    /// <summary>当前 SFX 音量</summary>
    public float SfxVolume => _sfxVol;

    // ============================================================
    // SFX 播放池(运行时自建,零拖拽)
    // ============================================================

    /// <summary>
    /// 运行时创建 SFX 音源池(2D)。每个源注册进 sfxSources → 音量自动跟随设置面板 SFX 滑条;
    /// 挂在 AudioManager 自身子节点下,随单例常驻跨场景。
    /// </summary>
    private void CreateSfxPool()
    {
        int count = Mathf.Max(1, sfxPoolSize);
        _sfxPool = new AudioSource[count];
        for (int i = 0; i < count; i++)
        {
            var go = new GameObject($"SfxSource_{i + 1}");
            go.transform.SetParent(transform, false);
            var src = go.AddComponent<AudioSource>();
            src.playOnAwake = false;
            src.loop = false;
            src.spatialBlend = 0f;   // 2D:不随距离衰减
            src.volume = _sfxVol;
            _sfxPool[i] = src;
            RegisterSource(AudioGroup.Sfx, src);   // 音量跟随 sfx 组(含后续 SetVolumes 广播)
        }

        // 排程池:同池规则,仅供 PlaySfxScheduled 使用(与一次性池分开,排程音不会被打断)
        int schedCount = Mathf.Max(1, scheduledSfxPoolSize);
        _scheduledSfxPool = new AudioSource[schedCount];
        for (int i = 0; i < schedCount; i++)
        {
            var go = new GameObject($"ScheduledSfxSource_{i + 1}");
            go.transform.SetParent(transform, false);
            var src = go.AddComponent<AudioSource>();
            src.playOnAwake = false;
            src.loop = false;
            src.spatialBlend = 0f;
            src.volume = _sfxVol;
            _scheduledSfxPool[i] = src;
            RegisterSource(AudioGroup.Sfx, src);
        }
    }

    /// <summary>
    /// 播放一次性音效(clip 空 = 静默跳过,不警告)。轮转取源 + PlayOneShot,快速连击时多声可重叠不互相打断。
    /// volume 为 0~1 相对缩放,最终响度 = SFX 音量 × volume。
    /// </summary>
    public void PlaySfx(AudioClip clip, float volume = 1f, float pitch = 1f)
    {
        if (clip == null) return;
        if (_sfxPool == null || _sfxPool.Length == 0) return;

        // 取源优先空闲源:pitch 是 AudioSource 级属性,复用正在发声的源会把上一发一起改调(连击变调会串音)。
        // 全忙时退回轮转取源(与改前的轮转行为一致)。
        AudioSource src = null;
        for (int i = 0; i < _sfxPool.Length; i++)
        {
            int idx = (_sfxNext + i) % _sfxPool.Length;
            if (_sfxPool[idx] != null && !_sfxPool[idx].isPlaying)
            {
                src = _sfxPool[idx];
                _sfxNext = (idx + 1) % _sfxPool.Length;
                break;
            }
        }
        if (src == null)
        {
            src = _sfxPool[_sfxNext];
            _sfxNext = (_sfxNext + 1) % _sfxPool.Length;
        }
        if (src == null) return;

        src.pitch = Mathf.Clamp(pitch, 0.01f, 3f);
        src.PlayOneShot(clip, Mathf.Clamp01(volume));
    }

    /// <summary>
    /// 半音偏移 → AudioSource.pitch 倍率(2^(n/12)):0 = 原调,4 = 大三度(1.2599),7 = 纯五度(1.4983),12 = 八度(2.0)。
    /// pitch 是重采样,音高升高的同时音效时长按 1/倍率缩短(0.3s 的短打击音听不出问题)。
    /// </summary>
    public static float PitchFromSemitone(int semitone) => Mathf.Pow(2f, semitone / 12f);

    /// <summary>
    /// 排程播放一次性音效(卡点用):把音排在指定的 dspTime 上播,与音乐走同一个音频时钟,不受逻辑帧率影响。
    /// 用于"踩准节拍"的确认音放在拍点上(见 PlayerCombat.PlayBackstabSfxScheduled)。
    /// dspTime 落在过去 → Unity 直接立即播(玩家按晚了自然退化成立刻响,不做特判)。
    /// 取源顺序:空闲源 → 全忙时轮转顶掉最早的排程;clip 空 / 池空 = 静默跳过。
    /// 注意:排程音占住源直到播完,与 PlaySfx 的一次性池分开(互不打断)。
    /// </summary>
    public void PlaySfxScheduled(AudioClip clip, float volume, double dspTime, float pitch = 1f)
    {
        if (clip == null) return;
        if (_scheduledSfxPool == null || _scheduledSfxPool.Length == 0) return;

        AudioSource src = null;
        for (int i = 0; i < _scheduledSfxPool.Length; i++)
        {
            var s = _scheduledSfxPool[i];
            if (s != null && !s.isPlaying) { src = s; break; }
        }
        if (src == null)
        {
            // [2026-09-20 清理临时调试] 池满时在这里顶掉最早一发;要看有没有吞音,把下面一行打开
            //Debug.Log($"[音效排程诊断] 排程池 {_scheduledSfxPool.Length} 源全忙:顶掉槽 {_scheduledNext + 1}");
            src = _scheduledSfxPool[_scheduledNext];
            _scheduledNext = (_scheduledNext + 1) % _scheduledSfxPool.Length;
        }
        if (src == null) return;

        src.clip = clip;
        src.volume = Mathf.Clamp01(_sfxVol * Mathf.Clamp01(volume));   // 排程无 volumeScale 参数,相对音量在这里乘进去
        src.pitch = Mathf.Clamp(pitch, 0.01f, 3f);                      // 连音背刺按刀序升调(do/mi/sol)
        src.PlayScheduled(dspTime);
    }

    /// <summary>
    /// 取消所有未播完的排程音(切曲/暂停/退出时调):排程按旧曲的时间基准算出来的,基准一变就不该再响。
    /// 未排程的音源调用 Stop 无副作用。
    /// </summary>
    public void CancelScheduledSfx()
    {
        if (_scheduledSfxPool == null) return;
        foreach (var s in _scheduledSfxPool)
        {
            if (s != null) s.Stop();
        }
    }

    /// <summary>
    /// 播放 UI 音效(全局 4 个音效位:悬停/点击/关闭/打开),clip 从 library(AudioLibrary)取。
    /// 复用 SFX 轮转池与 sfx 音量组,多声可重叠;库为空 / clip 未拖 / kind=None = 该类静默跳过,不警告、不打日志。
    /// 不做节流与互斥:各挂点自己触发自己的音,快速划过一排按钮连响属预期。
    /// </summary>
    public void PlayUiSfx(UiSfxKind kind)
    {
        if (library == null) return;

        AudioClip clip = kind switch
        {
            UiSfxKind.Hover => library.uiHover,
            UiSfxKind.Click => library.uiClick,
            UiSfxKind.Close => library.uiClose,
            UiSfxKind.Open => library.uiOpen,
            UiSfxKind.Start => library.uiStart,
            _ => null
        };
        PlaySfx(clip, library.uiVolume);
    }

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
            case AudioGroup.Bgm: return _bgmVol * _bgmFadeMul;   // 新注册的 BGM 源同样带过渡系数
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
