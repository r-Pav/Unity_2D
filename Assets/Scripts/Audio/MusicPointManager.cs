using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 音乐管理器(攻击-音乐 v2)— 场景 BGM 播放 + 音乐点窗口。
/// P2:音乐点排程(协程按点表等点,事件驱动,无每帧业务轮询)+ 查询接口。
/// P1:场景模式单源播放(loop=true 播完重复);P4 管道 CrossFadeTo;P5 Boss 双源交叠循环。
/// 时钟唯一参照 = 当前主源 AudioSource.time,不做系统计时累加。
/// 排程(多窗口模型):每个标点各自独立计时,在自己的 [点-lead-半宽, 点-lead+半宽] 区间活跃,
/// 前一个点关窗不阻塞后一个点开窗 → 相邻/重叠标点可同时处于活跃(连音背刺的前提)。
/// 事件 OnWindowEnter(point)/OnWindowPassed(point) 每个点各发一次,重叠时按时间顺序各发各的。
/// 窗口消费:标点路径按点消费(ConsumePoint/IsPointConsumed,一圈内一个点只消费一次);
/// 自动重音路径仍按 bar 消费(ConsumeAutoBarWindow,语义与改前一致)。
/// 最后一圈所有点处理完且无活跃窗口,等 loop 回绕后清空消费记录从头再排。
/// 连音分组(P2):当前曲 PlayerBackstab 组标点按 chainGapThreshold 切成若干连音组(相邻间隔 &lt; 阈值归一组的),
/// 分组结果缓存(只在切曲/切圈/资产重载时重算),对外提供 CurrentChainPoints/PendingChainPointIndex 等只读查询。
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

    [Header("连音背刺(标点组 PlayerBackstab)")]
    [Tooltip("连音分组阈值(秒):PlayerBackstab 组内相邻标点间隔 < 此值时归为同一个连音组;" +
             "默认 0.5 = 背刺动画 Backstab.anim 时长")]
    [SerializeField] private float chainGapThreshold = 0.5f;

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
    private bool _inWindow;              // 当前是否在触发窗口内(= 标点活跃窗口 || 自动重音窗口)
    private bool _autoBarActive;         // 当前窗口是否由自动重音开启(IsAutoBarWindow 区分背刺窗口)
    private bool _autoBarConsumed;       // 当前自动重音窗口是否已被消费(F 背刺用:每 bar 限一次,防窗口内连按 F 连触发)
    private bool _autoBarWindowOpen;     // 自动重音窗口当前是否开着(与标点活跃窗口各自独立计数,互不误关)
    private float _activePointTime;      // 最近进入窗口的点时刻(向后兼容;多窗口下 = 最后开窗的那个点)

    // 多窗口模型状态:活跃点表(升序,索引 0 = 最早该关的点)+ 本圈已消费点时刻表
    // 说明:两个 List 只在排程/开窗/关窗时增删,不做每帧全场景扫描,也不每帧分配。
    private readonly List<float> _activePoints = new List<float>();
    private readonly List<float> _consumedPoints = new List<float>();

    // 连音分组缓存(P2):只在切曲/切圈/资产重载时由 RebuildChainGroups() 重算,禁止每帧重算、禁止每帧全场扫描。
    // _chainPoints = 当前曲 PlayerBackstab 组标点(升序去重);分组以「起始下标 + 点数」表示,避免每组的数组分配。
    private static readonly float[] EmptyChainPoints = Array.Empty<float>();
    private readonly List<float> _chainPoints = new List<float>();
    private readonly List<int> _chainGroupStart = new List<int>();
    private readonly List<int> _chainGroupLen = new List<int>();
    private float[] _currentChainPoints = EmptyChainPoints;   // 当前组点表(缓存数组,组变化时才替换)
    private int _currentChainGroup = -1;                      // 缓存的当前组下标(-1 = 无当前组)
    private string _chainGroupSummary = "-";                  // 调试显示用分组摘要(重算时拼一次,不在 OnGUI 里拼)
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

    // ── 自动重音预告查询(供 EnemyBeatIndicator 轮询;公式与 AutoBarRoutine 的 next 对齐,纯只读)──
    // 注意:禁止为复用这些 getter 去重构 AutoBarRoutine 内部计算(AutoBarRoutine 在排程协程内自己算即可,改它有回归风险)。

    /// <summary>当前曲是否配置自动重音(barIntervalSeconds>0)</summary>
    public bool HasAutoBar => _currentTrack != null && _currentTrack.barIntervalSeconds > 0f;

    /// <summary>下一个自动重音窗口时刻(-1 = 无自动重音);公式与 AutoBarRoutine 的 next 对齐</summary>
    public float NextAutoBarTime
    {
        get
        {
            if (!HasAutoBar) return -1f;
            float interval = _currentTrack.barIntervalSeconds;
            return Mathf.Floor(TrackTime / interval) * interval + interval;
        }
    }

    /// <summary>距下个自动重音窗口剩余秒数(-1 = 无);窗口已开后为负,调用方用 >0 判断</summary>
    public float TimeToNextAutoBar => HasAutoBar ? NextAutoBarTime - TrackTime : -1f;

    /// <summary>当前自动重音判定窗口时长(秒)= 2×windowHalfWidth;背刺标识动态适配内环用</summary>
    public float WindowSeconds => windowHalfWidth * 2f;

    /// <summary>当前是否在触发窗口内(特殊攻击按键事件查询,不做每帧轮询)</summary>
    public bool IsInWindow() => _inWindow;

    /// <summary>当前在窗口内时,返回对应点时刻</summary>
    public bool IsInWindow(out float pointTime)
    {
        pointTime = _activePointTime;
        return _inWindow;
    }

    /// <summary>当前是否在「自动重音窗口」内(背刺判定用;Boss 标点窗口不满足,不干扰 PlayerBeatJudge)。
    /// 只看自动重音自己开的窗口(_autoBarWindowOpen),标点活跃窗口同时存在时不误判。</summary>
    public bool IsAutoBarWindow => _autoBarWindowOpen && _autoBarActive && !_autoBarConsumed;

    /// <summary>消费当前自动重音窗口:背刺成功进入状态后调用,本窗口内不再响应 F(每 bar 一次);
    /// 下一窗口开窗时自动重置</summary>
    public void ConsumeAutoBarWindow()
    {
        if (_autoBarWindowOpen && _autoBarActive)
            _autoBarConsumed = true;
    }

    // ============================================================
    // 多窗口:按点消费 + 活跃点只读查询(连音背刺标点层;纯只读/去重写入,无每帧扫描)
    // ============================================================

    /// <summary>当前活跃点数量(同一帧可 >1:相邻标点窗口重叠时并存)</summary>
    public int ActivePointCount => _activePoints.Count;

    /// <summary>当前活跃点数组(只读视图,升序;不要持有引用做跨帧缓存,切圈/切曲会清空)</summary>
    public IReadOnlyList<float> ActivePoints => _activePoints;

    /// <summary>取第 index 个活跃点时刻(升序;越界返回 -1)</summary>
    public float GetActivePoint(int index)
    {
        if (index < 0 || index >= _activePoints.Count) return -1f;
        return _activePoints[index];
    }

    /// <summary>某标点时刻当前是否处于活跃窗口内(容差 0.001)</summary>
    public bool IsPointActive(float pointTime)
    {
        for (int i = 0; i < _activePoints.Count; i++)
        {
            if (Mathf.Abs(_activePoints[i] - pointTime) < 0.001f) return true;
        }
        return false;
    }

    /// <summary>消费某个标点(连音背刺每点限一次):记录时间戳,同一圈内重复调用无副作用。
    /// 消费记录在本圈所有点处理完、loop 回绕时清空(下一圈每点可再消费一次)。</summary>
    public void ConsumePoint(float pointTime)
    {
        if (IsPointConsumed(pointTime)) return;
        _consumedPoints.Add(pointTime);
    }

    /// <summary>该标点是否已被消费(容差 0.001;消费后 IsInGroupWindow 对同一点返回 false)</summary>
    public bool IsPointConsumed(float pointTime)
    {
        for (int i = 0; i < _consumedPoints.Count; i++)
        {
            if (Mathf.Abs(_consumedPoints[i] - pointTime) < 0.001f) return true;
        }
        return false;
    }

    /// <summary>当前是否存在「属于该组且未被消费」的活跃标点(连音背刺判定入口用;纯查询)</summary>
    public bool HasUnconsumedActivePointInGroup(string groupName)
    {
        var group = _currentTrack != null ? _currentTrack.GetGroup(groupName) : null;
        if (group == null || group.points == null) return false;
        for (int i = 0; i < _activePoints.Count; i++)
        {
            float p = _activePoints[i];
            if (IsPointConsumed(p)) continue;
            foreach (float gp in group.points)
            {
                if (Mathf.Abs(gp - p) < 0.001f) return true;
            }
        }
        return false;
    }

    // ============================================================
    // 连音背刺标点层(P2):PlayerBackstab 组 → 连音分组 + 只读查询
    // 分组只在切曲/切圈/资产重载时由 RebuildChainGroups() 重算一次并缓存;
    // 下面的查询全是纯只读(O(组数),组数通常 1~3),不做每帧全场扫描、不做每帧分配。
    // 判定优先级(仅注释说明,判定入口改造在 P6):
    //   当前曲存在 PlayerBackstab 组 → 背刺判定走该组标点(按点判定/按点消费);
    //   不存在 → 回退自动重音窗口(现有行为不变,见 IsAutoBarWindow/ConsumeAutoBarWindow)。
    // ============================================================

    /// <summary>背刺标点组约定组名(MusicTrackData.pointGroups 里按此名查;仅服务玩家背刺,禁止接进 PlayerBeatJudge)</summary>
    private const string PlayerBackstabGroupName = "PlayerBackstab";

    /// <summary>当前曲是否配置了连音背刺标点组(有 PlayerBackstab 组且至少一个有效点)。P6 判定入口按此选路。</summary>
    public bool HasChain => _chainPoints.Count > 0;

    /// <summary>当前曲的连音组数量(孤立点也算一组)</summary>
    public int ChainGroupCount => _chainGroupLen.Count;

    /// <summary>连音分组阈值(秒):相邻标点间隔 < 此值归为同一连音组</summary>
    public float ChainGapThreshold => chainGapThreshold;

    /// <summary>
    /// 重算连音分组缓存(切曲/切圈/资产重载时调用,不在 Update 里调用):
    /// 取当前曲 PlayerBackstab 组标点 → 升序去重(容差 0.001,与排程点表去重口径一致)→
    /// 相邻间隔 < chainGapThreshold 归为同一连音组;与前后都不相邻的孤立点自成一组(长度 1)。
    /// </summary>
    private void RebuildChainGroups()
    {
        _chainPoints.Clear();
        _chainGroupStart.Clear();
        _chainGroupLen.Clear();
        _currentChainPoints = EmptyChainPoints;
        _currentChainGroup = -1;

        var group = _currentTrack != null ? _currentTrack.GetGroup(PlayerBackstabGroupName) : null;
        if (group != null && group.points != null && group.points.Length > 0)
        {
            var sorted = new List<float>(group.points);
            sorted.Sort();
            for (int i = 0; i < sorted.Count; i++)
            {
                if (sorted[i] < 0f) continue;   // 负时刻无意义(该点不会开窗),直接丢掉
                if (_chainPoints.Count > 0 && Mathf.Abs(sorted[i] - _chainPoints[_chainPoints.Count - 1]) < 0.001f)
                    continue;                   // 去重
                _chainPoints.Add(sorted[i]);
            }

            // 相邻间隔 < 阈值归为一组;扫描到 i == Count 收尾(最后一组必闭合)
            float gap = Mathf.Max(0f, chainGapThreshold);
            int start = 0;
            for (int i = 1; i <= _chainPoints.Count; i++)
            {
                bool closeGroup = i >= _chainPoints.Count || (_chainPoints[i] - _chainPoints[i - 1]) >= gap;
                if (!closeGroup) continue;
                _chainGroupStart.Add(start);
                _chainGroupLen.Add(i - start);
                start = i;
            }
        }

        // 调试摘要:如 "2 组 [3,1]"(重算时拼一次,OnGUI 直接取)
        var summary = new System.Text.StringBuilder();
        summary.Append(_chainGroupLen.Count).Append(" 组 [");
        for (int i = 0; i < _chainGroupLen.Count; i++)
        {
            if (i > 0) summary.Append(',');
            summary.Append(_chainGroupLen[i]);
        }
        summary.Append(']');
        _chainGroupSummary = summary.ToString();
    }

    /// <summary>强制重算连音分组(切曲/切圈已自动重算;运行时改过 MusicTrackData 的 PlayerBackstab 组后可手动调一次)</summary>
    public void RefreshChainGroups() => RebuildChainGroups();

    /// <summary>编辑器侧:Inspector 改 chainGapThreshold 后立即重算,运行时也能当场看到分组变化</summary>
    private void OnValidate()
    {
        if (Application.isPlaying) RebuildChainGroups();
    }

    /// <summary>第 g 个连音组末点在 _chainPoints 里的下标</summary>
    private int ChainGroupEndIndex(int g) => _chainGroupStart[g] + _chainGroupLen[g] - 1;

    /// <summary>
    /// 解析「当前连音组」下标(单游标,不并行预告):
    /// 从前往后找第一个还没结束的组(末点窗口还没关),该组即候选;
    /// 但只有进入预告期(首点 - previewLead,当前默认 1.0s;以字段实际值为准)之后才算真的「当前组」,
    /// 还没进预告期 → 返回 -1(此时用 NextChainStartTime 拿它的起点)。
    /// 组结束后自然跳到下一组;本圈全结束 → -1(等 loop 回绕后第一组重新成为当前组,与消费记录每圈重置一致)。
    /// 相邻两组靠得比 previewLead 近时,后一组进预告的时刻顺延到前一组结束 —— 有意为之(单游标,不做并行预告)。
    /// </summary>
    private int ResolveCurrentChainGroup()
    {
        int n = _chainGroupLen.Count;
        if (n == 0) return -1;
        float closeEdge = TrackTime + triggerLead - windowHalfWidth;   // 等价于「点 <= t + lead - 半宽 = 窗口已关」
        for (int g = 0; g < n; g++)
        {
            float endPoint = _chainPoints[ChainGroupEndIndex(g)];
            if (endPoint <= closeEdge + 0.001f) continue;              // 这组已经结束
            float firstPoint = _chainPoints[_chainGroupStart[g]];
            return (TrackTime + previewLead >= firstPoint - 0.001f) ? g : -1;
        }
        return -1;
    }

    /// <summary>
    /// 当前连音组的点数组(升序;无当前组 → 空数组,切曲/组结束后同样清空)。
    /// 「当前组」= 已进入预告期或正在执行的那一组(预告期 = 组首点前 previewLead,与 P3 分配快照/P5 预告的 leadSeconds 对齐)。
    /// 返回的是缓存数组(只在组变化时替换),可安全保留一帧,不要长期持有跨切曲缓存。
    /// </summary>
    public float[] CurrentChainPoints
    {
        get
        {
            int g = ResolveCurrentChainGroup();
            if (g != _currentChainGroup)
            {
                _currentChainGroup = g;
                if (g < 0)
                {
                    _currentChainPoints = EmptyChainPoints;
                }
                else
                {
                    int start = _chainGroupStart[g];
                    int len = _chainGroupLen[g];
                    var arr = new float[len];
                    for (int i = 0; i < len; i++) arr[i] = _chainPoints[start + i];
                    _currentChainPoints = arr;
                }
            }
            return _currentChainPoints;
        }
    }

    /// <summary>当前连音组的点数(无当前组 = 0)</summary>
    public int CurrentChainCount => CurrentChainPoints.Length;

    /// <summary>当前连音组内第 index 个点的时刻(升序;越界返回 -1)</summary>
    public float GetChainPoint(int index)
    {
        var pts = CurrentChainPoints;
        return (index < 0 || index >= pts.Length) ? -1f : pts[index];
    }

    /// <summary>当前是否有本组内某点处于活跃窗口且未被消费(P6 判定入口用)</summary>
    public bool IsInChainWindow
    {
        get
        {
            var pts = CurrentChainPoints;
            for (int i = 0; i < pts.Length; i++)
            {
                float p = pts[i];
                if (IsPointConsumed(p)) continue;
                if (IsPointActive(p)) return true;
            }
            return false;
        }
    }

    /// <summary>当前组内第一个还没执行(未被 ConsumePoint 消费)的点下标;当前组不存在或组内全部已执行 → -1(供 P6 按点取目标)</summary>
    public int PendingChainPointIndex
    {
        get
        {
            var pts = CurrentChainPoints;
            for (int i = 0; i < pts.Length; i++)
            {
                if (!IsPointConsumed(pts[i])) return i;
            }
            return -1;
        }
    }

    /// <summary>当前组内还没执行(未被消费)的点数;无当前组 → 0(供状态推进判断)</summary>
    public int PendingChainPointCount
    {
        get
        {
            var pts = CurrentChainPoints;
            int n = 0;
            for (int i = 0; i < pts.Length; i++)
            {
                if (!IsPointConsumed(pts[i])) n++;
            }
            return n;
        }
    }

    /// <summary>下一个连音组第一个点的时刻(-1 = 当前曲无连音组)。最后一组首点已过时返回该组首点(等 loop 回绕,与 NextPointInGroup 同约定)</summary>
    public float NextChainStartTime
    {
        get
        {
            if (_chainPoints.Count == 0) return -1f;
            float t = TrackTime;
            for (int g = 0; g < _chainGroupLen.Count; g++)
            {
                float first = _chainPoints[_chainGroupStart[g]];
                if (first > t + 0.001f) return first;
            }
            return _chainPoints[_chainGroupStart[_chainGroupLen.Count - 1]];
        }
    }

    /// <summary>距下一个连音组起点秒数(-1 = 无连音组);已过该起点时为负,调用方用 &gt;0 或 &lt;= previewLead 判断</summary>
    public float TimeToNextChainStart
    {
        get
        {
            float next = NextChainStartTime;
            return next < 0f ? -1f : next - TrackTime;
        }
    }

    /// <summary>下一个音乐点时刻(-1 = 无点)</summary>
    public float NextPointTime
    {
        get
        {
            // 有活跃标点(多窗口)→ 返回最后开窗的那个点(与旧单窗口时的 _activePointTime 语义一致)
            if (_activePoints.Count > 0) return _activePoints[_activePoints.Count - 1];
            if (_currentTrack == null || _currentTrack.points == null || _currentTrack.points.Length == 0)
                return -1f;
            // 否则找下一个未过的点
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

    /// <summary>当前是否处于指定组某标点的窗口内(事件驱动查询,不做每帧轮询)。
    /// 多窗口语义:当前存在活跃标点,且该活跃点属于该组,且该点未被消费。
    /// (自动重音窗口的旧兼容分支保留:窗口期内仍按 _activePointTime 比对,行为与改前一致)</summary>
    public bool IsInGroupWindow(string groupName)
    {
        var group = _currentTrack != null ? _currentTrack.GetGroup(groupName) : null;
        if (group == null || group.points == null) return false;

        // 标点窗口:任一活跃点属于该组且未消费 → 命中
        for (int i = 0; i < _activePoints.Count; i++)
        {
            float p = _activePoints[i];
            if (IsPointConsumed(p)) continue;
            foreach (float gp in group.points)
            {
                if (Mathf.Abs(gp - p) < 0.001f) return true;
            }
        }

        // 自动重音窗口(向后兼容:该路径点表通常为空,语义与改前完全一致)
        if (_autoBarWindowOpen && _autoBarActive)
        {
            foreach (float p in group.points)
            {
                if (Mathf.Abs(p - _activePointTime) < 0.001f) return true;
            }
        }
        return false;
    }

    /// <summary>当前窗口所属组名(遍历曲目标点组匹配;不在窗口/未匹配返回 null)</summary>
    public string CurrentWindowGroup
    {
        get
        {
            if (_currentTrack == null || _currentTrack.pointGroups == null) return null;
            if (_activePoints.Count == 0 && !_autoBarWindowOpen) return null;
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
        ResetScheduleWindowState();        // 旧窗口/消费记录残留清掉,新排程重新管理
        RestartSchedule();
        RestartAutoBar();                  // 新曲 barIntervalSeconds>0 时启动自动重音
    }

    /// <summary>重启点表排程(切曲/切圈时调用):排当前曲主体 points,合并所有组标点;连音分组同生命周期重算</summary>
    private void RestartSchedule()
    {
        if (_scheduleRoutine != null)
            StopCoroutine(_scheduleRoutine);
        RebuildChainGroups();   // 切曲:连音分组随曲目重建(与排程点表同一口径,不每帧重算)
        _scheduleRoutine = StartCoroutine(ScheduleRoutine(_currentTrack != null ? _currentTrack.points : null, true));
    }

    /// <summary>用指定点表启动排程(两段式前奏段 introPoints 用;合并组点,保证前奏段组标点也有窗口)</summary>
    private void StartScheduleWith(float[] points)
    {
        if (_scheduleRoutine != null)
            StopCoroutine(_scheduleRoutine);
        RebuildChainGroups();   // 进前奏段/切段:连音分组同步重建
        _scheduleRoutine = StartCoroutine(ScheduleRoutine(points, true));
    }

    /// <summary>窗口标志重算:_inWindow = 存在活跃标点窗口 || 自动重音窗口开着。
    /// 两条路径各维护自己的开关,谁关窗都不会误关另一条路径还开着的窗口(多窗口 + 自动重音并存安全)。</summary>
    private void RefreshWindowFlag()
    {
        _inWindow = _activePoints.Count > 0 || _autoBarWindowOpen;
    }

    /// <summary>清空标点窗口状态(切曲/进过渡期时调用):活跃点表 + 消费记录 + 自动重音开窗标志一起复位</summary>
    private void ResetScheduleWindowState()
    {
        _activePoints.Clear();
        _consumedPoints.Clear();
        _autoBarWindowOpen = false;
        RefreshWindowFlag();
    }

    /// <summary>开一个标点窗口:入活跃表(升序插入,保证同帧多个重叠点也按时间有序)、刷标志、发事件</summary>
    private void OpenPointWindow(float point)
    {
        int idx = _activePoints.Count;
        while (idx > 0 && _activePoints[idx - 1] > point) idx--;
        _activePoints.Insert(idx, point);
        _activePointTime = point;   // 最近进入窗口的点时刻(向后兼容)
        RefreshWindowFlag();
        OnWindowEnter?.Invoke(point);
    }

    /// <summary>关闭所有已到关窗时刻的活跃点(活跃表升序 → 从最早该关的点起关,各发各的 OnWindowPassed)</summary>
    private void CloseExpiredWindows(float t)
    {
        float closeThreshold = t + triggerLead - windowHalfWidth;   // 等价于 (点 - lead + 半宽) <= t
        while (_activePoints.Count > 0 && _activePoints[0] <= closeThreshold)
        {
            float p = _activePoints[0];
            _activePoints.RemoveAt(0);
            OnWindowPassed?.Invoke(p);
        }
        RefreshWindowFlag();
    }

    /// <summary>
    /// 点表排程(多窗口模型):每个点各自独立计时,在自己的 [点-lead-半宽, 点-lead+半宽] 区间活跃。
    /// 前一个点关窗不阻塞后一个点开窗 → 相邻/重叠标点可同时活跃(连音背刺的前提);事件每点各发一次。
    /// mergeGroups=true 时合并主 points + 所有 Point Groups 标点(升序去重),保证
    /// BossHeavy/BossHeavySound/PlayerCombo/BossOrb 等组标点也有窗口事件。
    /// 事件驱动:协程只按 TrackTime 推进,不在 Update 轮询业务,也不做每帧全场景扫描。
    /// 场景模式 loop 回绕:所有点处理完且活跃表清空后,等 time 回落(loop 归 0)清消费记录再排下一圈。
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

        // 每次重启排程(切曲/切圈/进前奏)都算新一圈:清活跃窗口与消费记录(消费每圈重置)
        _activePoints.Clear();
        _consumedPoints.Clear();
        RefreshWindowFlag();

        if (points.Count == 0) yield break;

        // 开窗条件:点 <= t + lead + 半宽;关窗条件:点 <= t + lead - 半宽(与 [点-lead-半宽, 点-lead+半宽] 等价)
        int i = 0;   // 下一个待开窗的点索引

        while (true)
        {
            float t = TrackTime;

            // 1) 先关窗(早的点先关):保证重叠点的 Passed 与 Enter 按时间顺序成对发生
            CloseExpiredWindows(t);

            // 2) 开窗:所有已到开窗时刻的点一起开(不等前一个点关窗,重叠点因此可并存)
            while (i < points.Count && points[i] <= t + triggerLead + windowHalfWidth)
            {
                OpenPointWindow(points[i]);
                i++;
            }

            // 3) 卡帧/时间跳变兜底:t 已越过刚开窗口的关窗时刻 → 本帧即关,不留悬空窗口(事件成对)
            CloseExpiredWindows(t);

            // 4) 本圈排完且无活跃窗口:等 loop 回绕,清消费记录后从头排下一圈
            if (i >= points.Count && _activePoints.Count == 0)
            {
                float last = points[points.Count - 1];
                while (TrackTime >= last) yield return null;
                i = 0;
                _consumedPoints.Clear();   // 新一圈:每点可再消费一次
                RebuildChainGroups();      // 切圈:连音分组重算(运行时改过资产的话从这一圈生效)
                continue;
            }

            yield return null;
        }
    }

    // ============================================================
    // 自动重音(普通场景曲 barIntervalSeconds>0):按小节对齐开窗,复用 OnWindowEnter/Passed 事件。
    // 与标点排程各自独立开关(_autoBarWindowOpen / _activePoints),_inWindow 由 RefreshWindowFlag 合成 —
    // 两路同时开窗时互不误关(行为与改前一致:每 bar 一窗、窗口时长 = 2×半宽)。loop 回绕天然安全:
    // next 每轮按 TrackTime 重新对齐(Floor 取整),TrackTime 倒退后 next 仍指向未来时刻,不会卡死。
    // 曲目切换(PlayTrack/CrossFadeTo/EnterBossMusic)时 StopAutoBar 停掉旧协程,防新曲时间内误开窗。
    // ============================================================

    /// <summary>停止自动重音协程并复位标志。_inWindow 不直接赋值,只清自动重音自己的开窗标志后重算 —
    /// 标点排程刚打开的手工窗口不会被误关(多窗口模型下两路窗口可并存)</summary>
    private void StopAutoBar()
    {
        if (_autoBarRoutine != null)
            StopCoroutine(_autoBarRoutine);
        _autoBarRoutine = null;
        _autoBarActive = false;
        _autoBarConsumed = false;
        _autoBarWindowOpen = false;   // 自动重音窗口标志独立复位:_inWindow 由 RefreshWindowFlag 重算,不会误关还开着的标点窗口
        RefreshWindowFlag();
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

            _autoBarWindowOpen = true;
            _autoBarActive = true;
            _autoBarConsumed = false;   // 新窗口重置消费标记(每 bar 可触发一次背刺)
            RefreshWindowFlag();        // 标点窗口可能同时在开:_inWindow 两路共享,不要直接赋值覆盖
            OnWindowEnter?.Invoke(next);

            while (_currentTrack != null && TrackTime < next + windowDuration) yield return null;
            if (_currentTrack == null) break;

            _autoBarWindowOpen = false;
            _autoBarActive = false;
            RefreshWindowFlag();
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
        ResetScheduleWindowState();        // 过渡期无窗口(活跃点表/消费记录一起清)
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
        ResetScheduleWindowState();
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
        // 多窗口验证用:活跃点数 >1 = 重叠窗口并存;Consumed = 本圈已按点消费的点数
        GUI.Label(new Rect(12f, 36f, 400f, 24f),
            string.Format("Active {0}  Last {1:F3}  Consumed {2}", _activePoints.Count, _activePointTime, _consumedPoints.Count));
        // 连音分组验证用:Chain = 本曲连音组摘要(如 "2 组 [3,1]" = 3点一组 + 1点一组)
        // Cur = 当前组序号/点数, Pending = 当前组内第一个未执行的点下标, Next = 下一组起点, ToNext = 距起点秒数
        var chainPts = CurrentChainPoints;   // 先读一次,让 _currentChainGroup 完成解析
        GUI.Label(new Rect(12f, 60f, 560f, 24f),
            string.Format("Chain {0}  Cur {1}/{2}  Pending {3}  Next {4:F2}  ToNext {5:F2}",
                _chainGroupSummary, _currentChainGroup, chainPts.Length, PendingChainPointIndex,
                NextChainStartTime, TimeToNextChainStart));
    }
}
