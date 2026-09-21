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
    [SerializeField] private int scheduledSfxPoolSize = 20;   // 2026-09-21:预建够(连音 11 刀 + 素材 1.088s → 同刻多声),避免按需扩源时当场 AddComponent 造成一帧抖动

    [Tooltip("排程音源上限:全忙时按需扩新源,扩到这个上限才顶掉最早结束的那一发(连音密集时保证每个音独立走完)")]
    [SerializeField] private int scheduledSfxPoolMax = 20;   // 扩张上限(跟着池大小走;到上限才顶掉最早结束的那一发)

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

    /// <summary>SFX 排程池(PlayScheduled 卡点音用):一个源同一时刻只能排一个音。
    /// 取源靠自记占用截止 `_scheduledBusyUntil`,不问 `AudioSource.isPlaying` —— 排到未来的音 isPlaying 可能还不是 true,
    /// 会被误判空闲而抢用(2026-09-21 saika:连音每个音要有独立生命周期,彼此互不干扰)。</summary>
    private AudioSource[] _scheduledSfxPool;

    /// <summary>与排程池一一对应的占用截止(音频时钟秒,dspTime 口径;<= 当前 dspTime = 空闲)</summary>
    private double[] _scheduledBusyUntil;

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
        _scheduledBusyUntil = new double[schedCount];   // 0 = 空闲
        for (int i = 0; i < schedCount; i++)
            _scheduledSfxPool[i] = CreateScheduledSource(i);
    }

    /// <summary>建一个排程音源(池初始化与满池扩张共用);名字带槽序号,便于诊断日志认源</summary>
    private AudioSource CreateScheduledSource(int index)
    {
        var go = new GameObject($"ScheduledSfxSource_{index + 1}");
        go.transform.SetParent(transform, false);
        var src = go.AddComponent<AudioSource>();
        src.playOnAwake = false;
        src.loop = false;
        src.spatialBlend = 0f;   // 2D:不随距离衰减
        src.volume = _sfxVol;
        RegisterSource(AudioGroup.Sfx, src);   // 音量跟随 sfx 组(含后续 SetVolumes 广播)
        return src;
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
    /// 排程播放的诊断快照(2026-09-21 背刺卡点比对 debug):记录最近一次 PlaySfxScheduled 用了哪个源、
    /// 目标/实际起播时刻、占用截止与池状态。只读,不参与播放逻辑;诊断完连同上下的日志块一起删。
    /// </summary>
    public struct ScheduledSfxInfo
    {
        public AudioSource source;
        public double requestDsp;    // 调用那一刻的 dspTime
        public double targetDsp;     // 要求起播的 dspTime(标点换算来的)
        public double startDsp;      // 实际会开始播的 dspTime(目标在过去 → = 请求时刻)
        public double busyUntil;     // 本槽占用截止
        public float pitch;
        public int slotIndex;        // 槽序号(0 起,源名后缀 = +1)
        public int poolCount;        // 本次排程后的池大小
        public int poolMax;          // 池上限
        public bool expanded;        // 本次为它新扩了一个源
        public bool stole;           // 本次顶掉了还在响的一发
    }

    /// <summary>最近一次 PlaySfxScheduled 的诊断快照(只在同一帧里读有意义)</summary>
    public ScheduledSfxInfo LastScheduledSfx { get; private set; }

    /// <summary>
    /// 排程播放一次性音效(卡点用):把音排在指定的 dspTime 上播,与音乐走同一个音频时钟,不受逻辑帧率影响。
    /// 用于"踩准节拍"的确认音放在拍点上(背刺音效卡点:见 AttackVFXAnchor.PlayBackstab / PlayBackstabSingle 的 scheduleDsp 参数)。
    /// dspTime 落在过去 → Unity 直接立即播(玩家按晚了自然退化成立刻响,不做特判)。
    /// 取源顺序(2026-09-21 改自记占用):① 占用截止已过的空闲槽 → ② 未到上限则扩新槽 → ③ 到上限才顶掉最早结束的那一发。
    /// 不读 `isPlaying`:排到未来的音在那个时刻可能还不是 true,会被误判空闲而抢用(前一发被换 clip 顶掉)。
    /// 每发占住自己的槽直到 `clip.length ÷ pitch` 播完,彼此互不干扰;clip 空 / 池空 = 静默跳过。
    /// 返回值 = 本次排程占用的源(null = 没排出去),供调用方诊断实际发声时刻用;正常播放不需要它。
    /// </summary>
    public AudioSource PlaySfxScheduled(AudioClip clip, float volume, double dspTime, float pitch = 1f)
    {
        if (clip == null) return null;
        if (_scheduledSfxPool == null || _scheduledSfxPool.Length == 0) return null;

        double now = AudioSettings.dspTime;
        int slot = -1;
        bool expanded = false;
        bool stole = false;

        // ① 空闲槽:这一发是「现在还没被占」的源(判据必须是 now,不能用未来目标时刻:
        //    用未来时刻会把正在响的源判成空闲,换 clip 会当场掐掉正在响的那一声)。
        //    整组提前排时靠池子够大(两个场景都是 12),并发数不够才走 ② 扩源。
        for (int i = 0; i < _scheduledSfxPool.Length; i++)
        {
            if (_scheduledSfxPool[i] != null && _scheduledBusyUntil[i] <= now) { slot = i; break; }
        }

        // ② 全忙:还没到上限 → 扩一个新槽(连音密集时每个音都能独立走完,互不顶掉)
        int max = Mathf.Max(1, scheduledSfxPoolMax);
        if (slot < 0 && _scheduledSfxPool.Length < max)
        {
            slot = _scheduledSfxPool.Length;
            System.Array.Resize(ref _scheduledSfxPool, slot + 1);
            System.Array.Resize(ref _scheduledBusyUntil, slot + 1);
            _scheduledSfxPool[slot] = CreateScheduledSource(slot);
            expanded = true;
        }

        // ③ 到上限:顶掉最早结束的那一发(只有这一步会打断已在走的音,且留了诊断开关)
        if (slot < 0)
        {
            int oldest = 0;
            for (int i = 1; i < _scheduledBusyUntil.Length; i++)
                if (_scheduledBusyUntil[i] < _scheduledBusyUntil[oldest]) oldest = i;
            slot = oldest;
            stole = true;
            // [2026-09-21 清理临时调试] 要看有没有吞音,把下面一行打开
            //Debug.Log($"[音效排程诊断] 排程池已到上限 {max}:顶掉最早结束的槽 {slot + 1}");
        }

        AudioSource src = _scheduledSfxPool[slot];
        if (src == null) return null;

        float p = Mathf.Clamp(pitch, 0.01f, 3f);
        src.clip = clip;
        src.volume = Mathf.Clamp01(_sfxVol * Mathf.Clamp01(volume));   // 排程无 volumeScale 参数,相对音量在这里乘进去
        src.pitch = p;                                                 // 连音背刺按刀序升调(do/mi/sol)
        src.PlayScheduled(dspTime);

        // 占用截止 = 真正开始播的时刻 + 实际播放时长(clip 时长 ÷ pitch,pitch 是重采样)+ 一点缓冲
        double start = dspTime > now ? dspTime : now;

        _scheduledBusyUntil[slot] = start + clip.length / p + 0.05;

        // [2026-09-21 背刺卡点比对 debug] 快照给调用方打日志用(同一帧读)
        LastScheduledSfx = new ScheduledSfxInfo
        {
            source = src,
            requestDsp = now,
            targetDsp = dspTime,
            startDsp = start,
            busyUntil = _scheduledBusyUntil[slot],
            pitch = p,
            slotIndex = slot,
            poolCount = _scheduledSfxPool.Length,
            poolMax = max,
            expanded = expanded,
            stole = stole,
        };
        return src;
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
        // 占用表一起复位:全停之后所有槽都是空闲(2026-09-21 自记占用)
        if (_scheduledBusyUntil != null)
        {
            for (int i = 0; i < _scheduledBusyUntil.Length; i++) _scheduledBusyUntil[i] = 0.0;
        }
    }

    /// <summary>
    /// 取消「已经排进去、但还没开始响」的音:连音组是进组时整组排的,中途断链 / 状态退出要把没到的收掉。
    /// 正在响的不动(否则会把刚响的那一声掐掉);同时把这些槽的占用清零,立即可复用。
    /// </summary>
    public void CancelPendingScheduledSfx()
    {
        if (_scheduledSfxPool == null) return;
        double now = AudioSettings.dspTime;
        for (int i = 0; i < _scheduledSfxPool.Length; i++)
        {
            AudioSource s = _scheduledSfxPool[i];
            if (s == null || s.time > 0f) continue;   // 已经开响的保持(armed 的源 isPlaying 不可靠,按 time 判)

            // 马上就开响的也保持:最后一个标点的音常常和「状态退出」落在同一帧,
            // 一刀切掉就会出现「命中那一下偶尔没声」(2026-09-21 saika 报)。
            // 排程起点 = busyUntil − 本段时长 − 余量(PlaySfxScheduled 里就是按这个算的)。
            if (_scheduledBusyUntil != null && s.clip != null)
            {
                double len = s.clip.length / Mathf.Max(0.01f, s.pitch);
                double start = _scheduledBusyUntil[i] - len - 0.05;
                if (now < start + KeepPendingWindowSeconds) continue;
            }

            s.Stop();
            if (_scheduledBusyUntil != null) _scheduledBusyUntil[i] = 0.0;
        }
    }

    /// <summary>取消待播排程音效时,起点还没到但在这个窗口内的照旧播放(秒)</summary>
    private const double KeepPendingWindowSeconds = 0.3;

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
