using System;
using System.Collections;
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
    private bool _inWindow;              // 当前是否在触发窗口内
    private float _activePointTime;      // 当前窗口对应的点时刻
    private bool _bossMode;              // Boss 战模式(双源交叠)
    private MusicTrackData _sceneTrack;  // 进 Boss 前保存的场景曲(退 Boss 时切回)

    /// <summary>窗口开启(参数=点时刻)</summary>
    public event Action<float> OnWindowEnter;

    /// <summary>窗口关闭/点已过(参数=点时刻)</summary>
    public event Action<float> OnWindowPassed;

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

    private void Awake()
    {
        if (initialTrack != null)
            PlayTrack(initialTrack);
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
        RestartSchedule();
    }

    /// <summary>重启点表排程(切曲/切圈时调用)</summary>
    private void RestartSchedule()
    {
        if (_scheduleRoutine != null)
            StopCoroutine(_scheduleRoutine);
        _scheduleRoutine = StartCoroutine(ScheduleRoutine());
    }

    /// <summary>
    /// 点表排程:逐个点等窗口开/关。事件驱动:协程内部只等待时间,不在 Update 轮询业务。
    /// 场景模式 loop 回绕:处理完最后一个点后,等 time 回落(loop 归 0)再从头排。
    /// </summary>
    private IEnumerator ScheduleRoutine()
    {
        var points = _currentTrack != null ? _currentTrack.points : null;
        if (points == null || points.Length == 0) yield break;

        int i = 0;
        while (true)
        {
            if (i >= points.Length)
            {
                // 本圈结束:等 loop 回绕(time 从末点之后回落到开头)再排下一圈
                float last = points[points.Length - 1];
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
            fadeOut.volume = 1f - k;
            fadeIn.volume = k;
            yield return null;
        }

        fadeOut.Stop();
        fadeOut.clip = null;
        fadeOut.volume = 1f;             // 恢复默认,下次作 fadeIn 时强制 0
        fadeIn.volume = 1f;
        _crossFadeRoutine = null;
        RestartSchedule();
    }

    /// <summary>进入 Boss 战:场景曲缓出,指定曲目双源交叠循环缓入(进 Boss 房调用,曲目由触发处传入)</summary>
    public void EnterBossMusic(MusicTrackData bossTrack)
    {
        if (_bossMode || bossTrack == null || bossTrack.clip == null) return;
        _sceneTrack = _currentTrack;   // 保存场景曲(可能为 null,退 Boss 时直接停)
        _bossMode = true;
        if (_crossFadeRoutine != null) StopCoroutine(_crossFadeRoutine);
        _crossFadeRoutine = StartCoroutine(EnterBossRoutine(bossTrack));
    }

    private IEnumerator EnterBossRoutine(MusicTrackData bossTrack)
    {
        AudioSource fadeOut = _activeSource;   // 场景曲主源(可能 null)
        AudioSource fadeIn = audioSourceA;
        fadeIn.clip = bossTrack.clip;
        fadeIn.loop = false;                   // Boss 曲:交叠循环由 BossLoopRoutine 控制
        fadeIn.time = 0f;
        fadeIn.volume = 0f;
        fadeIn.Play();
        _activeSource = fadeIn;
        _currentTrack = bossTrack;
        RestartSchedule();
        _bossLoopRoutine = StartCoroutine(BossLoopRoutine());

        if (fadeOut != null && fadeOut != fadeIn)
        {
            float elapsed = 0f;
            while (elapsed < crossFadeDuration)
            {
                elapsed += Time.unscaledDeltaTime;
                float k = Mathf.Clamp01(elapsed / crossFadeDuration);
                fadeOut.volume = 1f - k;
                fadeIn.volume = k;
                yield return null;
            }
            fadeOut.Stop();
            fadeOut.clip = null;
            fadeOut.volume = 1f;
        }
        fadeIn.volume = 1f;
        _crossFadeRoutine = null;
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
