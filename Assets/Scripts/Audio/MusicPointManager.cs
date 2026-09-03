using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 音乐管理器(攻击-音乐 v2)— 场景 BGM 播放 + 音乐点窗口。
/// P2:音乐点排程(协程按点表等点,事件驱动,无每帧业务轮询)+ 查询接口。
/// P1:场景模式单源播放(loop=true 播完重复);P4 管道 CrossFadeTo;P5 Boss 双源交叠循环。
/// 时钟唯一参照 = 当前主源 AudioSource.time,不做系统计时累加。
/// 排程:点表升序,逐个等窗口开(点-lead-半宽)→开窗 → 等窗口关(点-lead+半宽)→关窗;最后一圈等 loop 回绕后从头再排。
/// </summary>
public class MusicPointManager : MonoBehaviour
{
    private static MusicPointManager _instance;

    public static MusicPointManager Instance
    {
        get
        {
            if (_instance == null)
                _instance = FindObjectOfType<MusicPointManager>();
            return _instance;
        }
    }

    [Header("曲目")]
    [Tooltip("场景初始曲(场景加载自动播)")]
    [SerializeField] private MusicTrackData initialTrack;

    [Header("音频源")]
    [Tooltip("BGM 源 A(场景模式主源;两源都拖进 AudioManager.bgmSources 走音量)")]
    [SerializeField] private AudioSource audioSourceA;

    [Tooltip("BGM 源 B(CrossFade/Boss 交叠副源)")]
    [SerializeField] private AudioSource audioSourceB;

    [Header("音乐点(全局)")]
    [Tooltip("窗口半宽(秒):点±半宽为可触发区间")]
    [SerializeField] private float windowHalfWidth = 0.15f;

    [Tooltip("触发提前量(秒):从音乐点反推,动画提前启动,伤害落点更贴点")]
    [SerializeField] private float triggerLead = 0.033f;

    [Tooltip("预告提前量(秒):距下一点 ≤ 此值时激活预告")]
    [SerializeField] private float previewLead = 1f;

    [Tooltip("缓入缓出时长(秒):管道/Boss 切换")]
    [SerializeField] private float crossFadeDuration = 1f;

    [Header("调试")]
    [Tooltip("屏幕显示当前音频时间/距下一点(标点验证用)")]
    [SerializeField] private bool debugDisplay;

    private MusicTrackData _currentTrack;
    private AudioSource _activeSource;   // 当前主源(场景模式 = A)

    private Coroutine _scheduleRoutine;  // 点表排程协程
    private Coroutine _crossFadeRoutine; // 缓入缓出协程
    private Coroutine _bossLoopRoutine;  // Boss 双源交叠循环协程
    private Coroutine _introRoutine;     // 两段式前奏协程(前奏→切主体)
    private Coroutine _fadeRoutine;      // 界面静音淡入淡出协程
    private Coroutine _autoBarRoutine;   // 自动重音调度协程(barIntervalSeconds>0 的场景曲)
    private bool _inWindow;              // 当前是否在触发窗口内
    private bool _autoBarActive;         // 当前窗口是否由自动重音开启(IsAutoBarWindow 区分背刺窗口)
    private bool _autoBarConsumed;       // 当前自动重音窗口是否已被消费(F 背刺用:每 bar 限一次,防窗口内连按 F 连触发)
    private float _activePointTime;      // 当前窗口对应的点时刻
    private bool _bossMode;              // Boss 战模式(双源交叠)
    private bool _inIntroPhase;          // 两段式:当前是否处于前奏段(恢复/仲裁用)
    private MusicTrackData _sceneTrack;  // 进 Boss 前保存的场景曲(退 Boss 时切回)
    private float _savedTrackTime;       // 应用失焦/切后台时保存的音频位置(恢复时重定位)

    /// <summary>窗口开启(参数=点时刻)</summary>
    public event Action<float> OnWindowEnter;

    /// <summary>窗口关闭/点已过(参数=点时刻)</summary>
    public event Action<float> OnWindowPassed;

    /// <summary>两段式:前奏结束切到主体循环(转阶段点,音乐与阶段同步;Boss 订阅后 ForcePhaseTransition)</summary>
    public event Action OnBossMainLoopStarted;

    /// <summary>当前曲目(空 = 未配置)</summary>
    public MusicTrackData CurrentTrack => _currentTrack;

    /// <summary>缓入缓出时长(切换用)</summary>
    public float CrossFadeDuration => crossFadeDuration;

    /// <summary>预告提前量(预告圆环激活判定用)</summary>
    public float PreviewLead => previewLead;

    /// <summary>当前主源音频时间(唯一时钟;P5 Boss 模式跟随当前主源)</summary>
    public float TrackTime => _activeSource != null ? _activeSource.time : 0f;

    /// <summary>当前是否在触发窗口内(特殊攻击按键事件查询,不做每帧轮询)</summary>
    public bool IsInWindow() => _inWindow;

    /// <summary>当前在窗口内时,返回对应点时刻</summary>
    public bool IsInWindow(out float pointTime)
    {
        pointTime = _activePointTime;
        return _inWindow;
    }

    /// <summary>当前是否在「自动重音窗口」内(背刺判定用;Boss 标点窗口不满足,不干扰 PlayerBeatJudge)</summary>
    public bool IsAutoBarWindow => _inWindow && _autoBarActive && !_autoBarConsumed;

    /// <summary>消费当前自动重音窗口:背刺成功进入状态后调用,本窗口内不再响应 F(每 bar 一次);
    /// 下一窗口开窗时自动重置</summary>
    public void ConsumeAutoBarWindow()
    {
        if (_inWindow && _autoBarActive)
            _autoBarConsumed = true;
    }

    /// <summary>下一个音乐点时刻(-1 = 无点)</summary>
    public float NextPointTime
    {
        get
        {
            if (_currentTrack == null || _currentTrack.points == null || _currentTrack.points.Length == 0)
                return -1f;
            // 当前窗口内 → 该点;否则找下一个未过的点
            if (_inWindow) return _activePointTime;
            float t = TrackTime;
            var points = _currentTrack.points;
            for (int i = 0; i < points.Length; i++)
            {
                if (points[i] > t + 0.001f) return points[i];
            }
            return points[points.Length - 1];   // 最后一圈,等 loop 回绕
        }
    }

    /// <summary>距下一个音乐点秒数(-1 = 无点)</summary>
    public float TimeToNextPoint
    {
        get
        {
            float next = NextPointTime;
            return next < 0f ? -1f : next - TrackTime;
        }
    }

    /// <summary>按组名查下一个标点时刻(-1 = 该组无点/未配置)。命名组:BossHeavy/BossOrb1~5/PlayerCombo/BossHeavySound</summary>
    public float NextPointInGroup(string groupName)
    {
        var group = _currentTrack != null ? _currentTrack.GetGroup(groupName) : null;
        if (group == null || group.points == null || group.points.Length == 0) return -1f;
        float t = TrackTime;
        for (int i = 0; i < group.points.Length; i++)
        {
            if (group.points[i] > t + 0.001f) return group.points[i];
        }
        return group.points[group.points.Length - 1];   // 最后一圈,等 loop 回绕
    }

    /// <summary>按组名查距下一个标点秒数(-1 = 无点)</summary>
    public float TimeToNextPointInGroup(string groupName)
    {
        float next = NextPointInGroup(groupName);
        return next < 0f ? -1f : next - TrackTime;
    }

    /// <summary>当前是否处于指定组某标点的窗口内(事件驱动查询,不做每帧轮询)</summary>
    public bool IsInGroupWindow(string groupName)
    {
        if (!_inWindow) return false;
        var group = _currentTrack != null ? _currentTrack.GetGroup(groupName) : null;
        if (group == null || group.points == null) return false;
        foreach (float p in group.points)
        {
            if (Mathf.Abs(p - _activePointTime) < 0.001f) return true;
        }
        return false;
    }

    /// <summary>当前窗口所属组名(遍历曲目标点组匹配;不在窗口/未匹配返回 null)</summary>
    public string CurrentWindowGroup
    {
        get
        {
            if (!_inWindow || _currentTrack == null || _currentTrack.pointGroups == null) return null;
            foreach (var g in _currentTrack.pointGroups)
            {
                if (g == null || g.points == null) continue;
                foreach (float p in g.points)
                {
                    if (Mathf.Abs(p - _activePointTime) < 0.001f) return g.groupName;
                }
            }
            return null;
        }
    }

    private void Awake()
    {
        if (initialTrack != null)
            PlayTrack(initialTrack);
        // 音量自动注册(替代手动拖 AudioManager.bgmSources):挂上即生效,场景销毁自动注销
        RegisterAudioSources();
    }

    /// <summary>把本播放器的两个音源注册进 AudioManager 的 BGM 组(音量面板统一控制)</summary>
    private void RegisterAudioSources()
    {
        var am = EnsureAudioManager();
        if (am == null) return;
        if (audioSourceA != null) am.RegisterSource(AudioManager.AudioGroup.Bgm, audioSourceA);
        if (audioSourceB != null) am.RegisterSource(AudioManager.AudioGroup.Bgm, audioSourceB);
    }

    /// <summary>确保 AudioManager 存在:任意场景直接测试(未经过 TitleScene)时自动补一个常驻实例,音量系统不失效</summary>
    private static AudioManager EnsureAudioManager()
    {
        var am = AudioManager.Instance;
        if (am != null) return am;
        var go = new GameObject("AudioManager");
        return go.AddComponent<AudioManager>();
    }

    private void OnDestroy()
    {
        var am = AudioManager.Instance;
        if (am == null) return;
        if (audioSourceA != null) am.UnregisterSource(AudioManager.AudioGroup.Bgm, audioSourceA);
        if (audioSourceB != null) am.UnregisterSource(AudioManager.AudioGroup.Bgm, audioSourceB);
    }

    // ============================================================
    // 应用失焦/切后台:保存播放位置;恢复:重定位 + 重启编排
    // (Boss 曲 AudioSource.loop=false,切后台期间播放状态/时钟被系统打断,
    //  恢复时 time 跳变或源已停 → 交叠协程误判立即切圈 = "从循环处开始"。)
    // ============================================================

    private void OnApplicationPause(bool pause)
    {
        if (pause) _savedTrackTime = _activeSource != null ? _activeSource.time : 0f;
        else RestoreAfterAppResume();
    }

    private void OnApplicationFocus(bool focus)
    {
        if (!focus) _savedTrackTime = _activeSource != null ? _activeSource.time : 0f;
        else RestoreAfterAppResume();
    }

    /// <summary>恢复前台:主源重定位到保存位置(若已停则续播),副源清空,重启当前段编排</summary>
    private void RestoreAfterAppResume()
    {
        if (_activeSource == null || _activeSource.clip == null) return;

        _activeSource.time = Mathf.Clamp(_savedTrackTime, 0f, _activeSource.clip.length);
        if (!_activeSource.isPlaying)
            _activeSource.Play();

        // 副源清空(交叠尾巴可能残留/停摆,交给重启后的编排重新管理)
        AudioSource other = _activeSource == audioSourceA ? audioSourceB : audioSourceA;
        if (other != null && other.isPlaying)
        {
            other.Stop();
            other.clip = null;
        }

        if (_bossMode && _currentTrack != null && _currentTrack.introClip != null && _inIntroPhase)
        {
            // 前奏段:强制恢复前奏源(后台期间可能被误切/暂停,一律拉回 introClip 重定位)
            var introSource = audioSourceA;
            introSource.Stop();
            introSource.clip = _currentTrack.introClip;
            introSource.loop = false;
            introSource.time = Mathf.Clamp(_savedTrackTime, 0f, introSource.clip.length);
            introSource.Play();
            _activeSource = introSource;
            if (audioSourceB != null && audioSourceB.isPlaying)
            {
                audioSourceB.Stop();
                audioSourceB.clip = null;
            }
            StartScheduleWith(_currentTrack.introPoints);
            if (_introRoutine != null) StopCoroutine(_introRoutine);
            _introRoutine = StartCoroutine(IntroRoutine(_currentTrack));
        }
        else
        {
            RestartSchedule();
            RestartAutoBar();   // 恢复前台:按恢复后的 TrackTime 重新对齐自动重音窗口
            if (_bossMode)
            {
                if (_bossLoopRoutine != null) StopCoroutine(_bossLoopRoutine);
                _bossLoopRoutine = null;
                if (_currentTrack != null && _currentTrack.loopPoint > 0f)
                    _bossLoopRoutine = StartCoroutine(BossLoopRoutine());
            }
        }
    }

    /// <summary>切曲重播:换 clip 从头播,主源 = A(场景模式,普通循环),点表重新排程</summary>
    public void PlayTrack(MusicTrackData track)
    {
        if (track == null || track.clip == null || audioSourceA == null) return;

        _currentTrack = track;
        _activeSource = audioSourceA;

        audioSourceA.clip = track.clip;
        audioSourceA.loop = true;          // 场景模式:播完重复
        audioSourceA.time = 0f;
        audioSourceA.Play();

        StopSource(audioSourceB);          // 副源清空,防残留
        StopAutoBar();                     // 切曲:停旧自动重音协程
        _inWindow = false;                 // 旧窗口残留清掉,新排程重新管理
        RestartSchedule();
        RestartAutoBar();                  // 新曲 barIntervalSeconds>0 时启动自动重音
    }

    /// <summary>重启点表排程(切曲/切圈时调用):排当前曲主体 points,合并所有组标点</summary>
    private void RestartSchedule()
    {
        if (_scheduleRoutine != null)
            StopCoroutine(_scheduleRoutine);
        _scheduleRoutine = StartCoroutine(ScheduleRoutine(_currentTrack != null ? _currentTrack.points : null, true));
    }

    /// <summary>用指定点表启动排程(两段式前奏段 introPoints 用;合并组点,保证前奏段组标点也有窗口)</summary>
    private void StartScheduleWith(float[] points)
    {
        if (_scheduleRoutine != null)
            StopCoroutine(_scheduleRoutine);
        _scheduleRoutine = StartCoroutine(ScheduleRoutine(points, true));
    }

    /// <summary>
    /// 点表排程:逐个点等窗口开/关。事件驱动:协程内部只等待时间,不在 Update 轮询业务。
    /// mergeGroups=true 时合并主 points + 所有 Point Groups 标点(升序去重),保证
    /// BossHeavy/BossHeavySound/PlayerCombo/BossOrb 等组标点也有窗口事件。
    /// 场景模式 loop 回绕:处理完最后一个点后,等 time 回落(loop 归 0)再从头排。
    /// </summary>
    private IEnumerator ScheduleRoutine(float[] basePoints, bool mergeGroups)
    {
        List<float> points;
        if (mergeGroups && _currentTrack != null && _currentTrack.pointGroups != null)
        {
            var all = new List<float>();
            if (basePoints != null) all.AddRange(basePoints);
            foreach (var g in _currentTrack.pointGroups)
            {
                if (g != null && g.points != null) all.AddRange(g.points);
            }
            all.Sort();
            points = new List<float>();
            foreach (float p in all)
            {
                if (points.Count == 0 || Mathf.Abs(p - points[points.Count - 1]) > 0.001f)
                    points.Add(p);   // 去重(同一时刻多个组共用标点只开一次窗)
            }
        }
        else
        {
            points = new List<float>();
            if (basePoints != null) points.AddRange(basePoints);
        }

        if (points.Count == 0) yield break;

        int i = 0;
        while (true)
        {
            if (i >= points.Count)
            {
                // 本圈结束:等 loop 回绕(time 从末点之后回落到开头)再排下一圈
                float last = points[points.Count - 1];
                while (TrackTime >= last) yield return null;
                i = 0;
                continue;
            }

            float point = points[i];
            float openAt = point - triggerLead - windowHalfWidth;
            float closeAt = point - triggerLead + windowHalfWidth;

            while (TrackTime < openAt) yield return null;   // 等开窗
            _inWindow = true;
            _activePointTime = point;
            OnWindowEnter?.Invoke(point);

            while (TrackTime < closeAt) yield return null;  // 等关窗
            _inWindow = false;
            OnWindowPassed?.Invoke(point);

            i++;
        }
    }

    // ============================================================
    // 自动重音(普通场景曲 barIntervalSeconds>0):按小节对齐开窗,复用 OnWindowEnter/Passed 事件。
    // 与标点排程互斥设计(场景曲配置自动重音时 points 一般留空);loop 回绕天然安全:
    // next 每轮按 TrackTime 重新对齐(Floor 取整),TrackTime 倒退后 next 仍指向未来时刻,不会卡死。
    // 曲目切换(PlayTrack/CrossFadeTo/EnterBossMusic)时 StopAutoBar 停掉旧协程,防新曲时间内误开窗。
    // ============================================================

    /// <summary>停止自动重音协程并复位标志(不碰 _inWindow — 由各切曲点显式复位,防误关刚由排程打开的手工窗口)</summary>
    private void StopAutoBar()
    {
        if (_autoBarRoutine != null)
            StopCoroutine(_autoBarRoutine);
        _autoBarRoutine = null;
        _autoBarActive = false;
        _autoBarConsumed = false;
    }

    /// <summary>按当前曲重启自动重音(barIntervalSeconds>0 才启动;曲目切换/恢复前台后调用)</summary>
    private void RestartAutoBar()
    {
        StopAutoBar();
        if (_currentTrack != null && _currentTrack.barIntervalSeconds > 0f)
            _autoBarRoutine = StartCoroutine(AutoBarRoutine());
    }

    /// <summary>自动重音调度:每隔 barIntervalSeconds 开一个窗口(对齐小节,窗口时长与标点窗口一致 = 2×半宽)</summary>
    private IEnumerator AutoBarRoutine()
    {
        float interval = _currentTrack != null ? _currentTrack.barIntervalSeconds : 0f;
        if (interval <= 0f) yield break;
        float windowDuration = windowHalfWidth * 2f;

        _autoBarActive = true;
        while (_currentTrack != null && _currentTrack.barIntervalSeconds > 0f)
        {
            float next = Mathf.Floor(TrackTime / interval) * interval + interval;   // 下一个窗口时刻(对齐小节)
            while (_currentTrack != null && TrackTime < next) yield return null;     // 等窗口(TrackTime 倒退也安全)
            if (_currentTrack == null) break;

            _inWindow = true;
            _autoBarActive = true;
            _autoBarConsumed = false;   // 新窗口重置消费标记(每 bar 可触发一次背刺)
            OnWindowEnter?.Invoke(next);

            while (_currentTrack != null && TrackTime < next + windowDuration) yield return null;
            if (_currentTrack == null) break;

            _inWindow = false;
            _autoBarActive = false;
            OnWindowPassed?.Invoke(next);
        }
        _autoBarActive = false;
        _autoBarRoutine = null;
    }

    /// <summary>缓入缓出切曲(管道/场景切换用):当前主源淡出,副源淡入新曲,完成后主源切换</summary>
    public void CrossFadeTo(MusicTrackData track)
    {
        if (track == null || track.clip == null) return;
        if (_activeSource == null || crossFadeDuration <= 0f)
        {
            PlayTrack(track);
            return;
        }
        if (_crossFadeRoutine != null) StopCoroutine(_crossFadeRoutine);
        StopAutoBar();                     // 切曲开始:停旧自动重音协程(新曲协程在 fade 结束按新曲重启)
        _inWindow = false;                 // 过渡期无窗口
        _crossFadeRoutine = StartCoroutine(CrossFadeRoutine(track));
    }

    private IEnumerator CrossFadeRoutine(MusicTrackData track)
    {
        AudioSource fadeOut = _activeSource;
        AudioSource fadeIn = fadeOut == audioSourceA ? audioSourceB : audioSourceA;
        if (fadeIn == null)
        {
            PlayTrack(track);
            yield break;
        }

        // 音量基准 = AudioManager 当前 BGM 音量,淡入目标跟随用户设置,不覆盖
        float targetVol = AudioManager.Instance != null ? AudioManager.Instance.BgmVolume : 1f;

        fadeIn.clip = track.clip;
        fadeIn.loop = true;              // 场景模式:播完重复
        fadeIn.time = 0f;
        fadeIn.volume = 0f;
        fadeIn.Play();

        _activeSource = fadeIn;          // 时钟立即切到新曲
        _currentTrack = track;

        float elapsed = 0f;
        while (elapsed < crossFadeDuration)
        {
            elapsed += Time.unscaledDeltaTime;
            float k = Mathf.Clamp01(elapsed / crossFadeDuration);
            fadeOut.volume = Mathf.Lerp(targetVol, 0f, k);
            fadeIn.volume = Mathf.Lerp(0f, targetVol, k);
            yield return null;
        }

        fadeOut.Stop();
        fadeOut.clip = null;
        fadeOut.volume = targetVol;      // 恢复默认,下次作 fadeIn 时强制 0
        fadeIn.volume = targetVol;
        _crossFadeRoutine = null;
        RestartSchedule();
        RestartAutoBar();                // 新曲 barIntervalSeconds>0 时启动自动重音
    }

    /// <summary>进入 Boss 战:场景曲缓出,指定曲目双源交叠循环缓入(进 Boss 房调用,曲目由触发处传入)</summary>
    public void EnterBossMusic(MusicTrackData bossTrack)
    {
        if (_bossMode || bossTrack == null || bossTrack.clip == null) return;
        _sceneTrack = _currentTrack;   // 保存场景曲(可能为 null,退 Boss 时直接停)
        _bossMode = true;
        StopAutoBar();                 // Boss 曲无自动重音:停场景曲的自动重音协程
        _inWindow = false;
        if (_crossFadeRoutine != null) StopCoroutine(_crossFadeRoutine);
        _crossFadeRoutine = StartCoroutine(EnterBossRoutine(bossTrack));
    }

    private IEnumerator EnterBossRoutine(MusicTrackData bossTrack)
    {
        AudioSource fadeOut = _activeSource;   // 场景曲主源(可能 null)
        AudioSource fadeIn = audioSourceA;
        float targetVol = AudioManager.Instance != null ? AudioManager.Instance.BgmVolume : 1f;

        if (bossTrack.introClip != null && fadeIn != null)
        {
            // 两段式:先播前奏,IntroRoutine 到 introSwitchTime 交叠切主体
            fadeIn.clip = bossTrack.introClip;
            fadeIn.loop = false;
            fadeIn.time = 0f;
            fadeIn.volume = 0f;
            fadeIn.Play();
            _activeSource = fadeIn;
            _currentTrack = bossTrack;
            _inIntroPhase = true;
            StartScheduleWith(bossTrack.introPoints);   // 前奏段点表
            _introRoutine = StartCoroutine(IntroRoutine(bossTrack));
        }
        else
        {
            // 单曲:Boss 曲直接播,交叠循环由 BossLoopRoutine 控制
            _inIntroPhase = false;
            fadeIn.clip = bossTrack.clip;
            fadeIn.loop = false;
            fadeIn.time = 0f;
            fadeIn.volume = 0f;
            fadeIn.Play();
            _activeSource = fadeIn;
            _currentTrack = bossTrack;
            RestartSchedule();
            _bossLoopRoutine = StartCoroutine(BossLoopRoutine());
        }

        if (fadeOut != null && fadeOut != fadeIn)
        {
            float elapsed = 0f;
            while (elapsed < crossFadeDuration)
            {
                elapsed += Time.unscaledDeltaTime;
                float k = Mathf.Clamp01(elapsed / crossFadeDuration);
                fadeOut.volume = Mathf.Lerp(targetVol, 0f, k);
                fadeIn.volume = Mathf.Lerp(0f, targetVol, k);
                yield return null;
            }
            fadeOut.Stop();
            fadeOut.clip = null;
            fadeOut.volume = targetVol;
        }
        fadeIn.volume = targetVol;
        _crossFadeRoutine = null;
    }

    /// <summary>
    /// 两段式前奏:等前奏播到 introSwitchTime → 副源从 0 播主体曲,主源切到主体(时钟切),
    /// 重启主体点表 + 启动 Boss 交叠循环;前奏尾巴(交叠)播到自然结束停用。
    /// </summary>
    private IEnumerator IntroRoutine(MusicTrackData track)
    {
        AudioSource introSource = audioSourceA;

        // 等前奏播到切换点。注意:不查 isPlaying — 切后台时团结引擎会暂停源(playing=False 但 time 保留),
        // 查 isPlaying 会误判"前奏结束"直接切主体(从 0 播) = 切回时从循环处开始。只等 time 到达。
        while (_bossMode && introSource != null && introSource.time < track.introSwitchTime)
            yield return null;
        if (!_bossMode) yield break;

        AudioSource mainSource = audioSourceB;
        if (mainSource == null) yield break;

        mainSource.clip = track.clip;
        mainSource.loop = false;
        mainSource.time = 0f;
        mainSource.Play();
        _activeSource = mainSource;      // 时钟切到主体
        _inIntroPhase = false;
        RestartSchedule();               // 排主体 points
        _bossLoopRoutine = StartCoroutine(BossLoopRoutine());
        OnBossMainLoopStarted?.Invoke(); // 转阶段点:音乐切到循环段

        // 前奏尾巴(交叠)播到自然结束停用
        while (_bossMode && introSource != null && introSource.isPlaying
               && introSource.time < introSource.clip.length - 0.01f)
            yield return null;
        if (introSource != null)
        {
            introSource.Stop();
            introSource.clip = null;
        }
    }

    /// <summary>退出 Boss 战:停交叠循环,Boss 曲缓出、场景曲缓入(击杀后调用)</summary>
    public void ExitBossMusic()
    {
        if (!_bossMode) return;
        _bossMode = false;
        if (_bossLoopRoutine != null)
        {
            StopCoroutine(_bossLoopRoutine);
            _bossLoopRoutine = null;
        }
        if (_introRoutine != null)
        {
            StopCoroutine(_introRoutine);
            _introRoutine = null;
        }
        _inIntroPhase = false;
        if (_sceneTrack != null)
            CrossFadeTo(_sceneTrack);          // 复用缓入缓出,回场景模式(loop=true)
        else if (_activeSource != null)
            _activeSource.Stop();
    }

    /// <summary>
    /// Boss 双源交叠循环:主源播到 loopPoint → 副源从 0 播(新圈),主源继续播完结尾段(交叠),
    /// 主源停用后副源升为主源,循环重复。时钟始终 = 当前主源。
    /// </summary>
    private IEnumerator BossLoopRoutine()
    {
        while (_bossMode && _currentTrack != null && _currentTrack.loopPoint > 0f)
        {
            float loopAt = _currentTrack.loopPoint;
            while (_bossMode && TrackTime < loopAt) yield return null;   // 等主源到 loopPoint
            if (!_bossMode) break;

            AudioSource oldSource = _activeSource;
            AudioSource newSource = oldSource == audioSourceA ? audioSourceB : audioSourceA;
            if (newSource == null) yield break;

            newSource.clip = _currentTrack.clip;
            newSource.loop = false;
            newSource.time = 0f;
            newSource.Play();
            _activeSource = newSource;         // 时钟切到新圈
            RestartSchedule();

            // 旧源(交叠尾巴)播到自然结束停用
            while (_bossMode && oldSource != null && oldSource.isPlaying
                   && oldSource.time < oldSource.clip.length - 0.01f)
                yield return null;
            if (oldSource != null)
            {
                oldSource.Stop();
                oldSource.clip = null;
            }
        }
    }

    /// <summary>
    /// 设置面板等界面打开时淡出 BGM,关闭时恢复(只渐变音量,不暂停播放,音频时钟照走)。
    /// </summary>
    public void SetBgmMuted(bool muted)
    {
        float target = muted ? 0f : (AudioManager.Instance != null ? AudioManager.Instance.BgmVolume : 1f);
        if (_fadeRoutine != null) StopCoroutine(_fadeRoutine);
        _fadeRoutine = StartCoroutine(FadeVolumeRoutine(target));
    }

    private IEnumerator FadeVolumeRoutine(float target)
    {
        float from = audioSourceA != null && audioSourceA.isPlaying ? audioSourceA.volume
                   : audioSourceB != null && audioSourceB.isPlaying ? audioSourceB.volume
                   : target;
        float elapsed = 0f;
        while (elapsed < crossFadeDuration)
        {
            elapsed += Time.unscaledDeltaTime;
            float k = Mathf.Clamp01(elapsed / Mathf.Max(0.01f, crossFadeDuration));
            ApplyVolumeToActive(Mathf.Lerp(from, target, k));
            yield return null;
        }
        ApplyVolumeToActive(target);
        _fadeRoutine = null;
    }

    /// <summary>对当前在播的源统一设音量(A/B 都可能响:CrossFade 交叠期 / Boss 双源)</summary>
    private void ApplyVolumeToActive(float v)
    {
        if (audioSourceA != null && audioSourceA.isPlaying) audioSourceA.volume = v;
        if (audioSourceB != null && audioSourceB.isPlaying) audioSourceB.volume = v;
    }

    /// <summary>停用源并清 clip(切换前清理)</summary>
    private static void StopSource(AudioSource source)
    {
        if (source == null) return;
        source.Stop();
        source.clip = null;
    }

    // 调试:当前音频时间 / 距下一点(标点验证用,可开关)
    private void OnGUI()
    {
        if (!debugDisplay) return;
        GUI.Label(new Rect(12f, 12f, 400f, 24f),
            string.Format("Time {0:F2}  Next {1:F2}  ToNext {2:F2}  Window {3}",
                TrackTime, NextPointTime, TimeToNextPoint, _inWindow ? "OPEN" : "closed"));
    }
}
