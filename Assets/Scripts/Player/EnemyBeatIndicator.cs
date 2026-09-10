using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 重音背刺预告标识指示器 — 挂玩家根或场景空物体。
/// 双路径(选路口径与 P6 判定入口一致):
/// ① 当前曲配了 PlayerBackstab 连音组(mgr.HasChain)→ 连音路径:
///    仍是轻量只读轮询(只看 mgr 的连音只读查询,不每帧全场扫描、不每帧 FindObjectsOfType),
///    组首点前 leadSeconds 调 BackstabChainPlanner.PrepareChain 快照一次(同一组只调一次,防每帧重排),
///    然后按分配结果把「每个点各一个金色环」发给对应敌人的 BeatFlashPoint.ShowChain
///    (同一个敌人的多个点合并成一份 secondsToPoints 一次传入;不同敌人各自一份实例/数组;
///     该点无分配 / 敌人没挂 BeatFlashPoint / 没配 aimPrefab = 空安全跳过,行为与现状一致);
///    组结束(窗口全过)或切曲:收掉已发的环 + ClearChain 清分配。
/// ② 当前曲没有连音组 → 原自动重音路径(轮询 TimeToNextAutoBar,窗口前 leadSeconds 找最近非死亡普通敌人
///    调 BeatFlashPoint.Flash;IsBoss 跳过),逐帧行为与改动前完全一致(原逻辑整段保留在 UpdateAutoBar)。
/// 窗口事件(OnWindowEnter/OnWindowPassed)保留作兜底(预告时机错过/中途进入时补启动)与清理(窗口正常结束 Hide),
/// 只服务自动重音路径。
/// 连音预告时间点(P5 规格 §3):组首点前 leadSeconds 那一刻打一次代码标记(OnChainPreview 事件 + LastChainPreviewTime
/// 时间戳,同一组只发一次),供表现层(音效/UI)订阅,本身不产生任何美术表现。
/// 已知待接(P6):现 PlayerBackstabState.OnBackstabHitFrame 命中即 Hide 目标整只标识实例,
/// 会把「同一敌人本组后续点」的环一起收起(A、B、A 这种分配下 A 的第 3 点环会被第 1 刀收掉);
/// P6 改按点收环后此处不需要改(本组件按组首点去重,不会重发)。
/// Boss 战/标点窗口(非自动重音)不满足 HasAutoBar,本组件不响应,不干扰 PlayerBeatJudge。
/// </summary>
public class EnemyBeatIndicator : MonoBehaviour
{
    [Tooltip("搜索半径(找最近 enemy);<=0 用全场景 FindObjectsOfType")]
    public float searchRadius = 0f;

    [Tooltip("预告检测提前量(秒):距下个窗口 ≤ 此值开始找敌人触发标识。与 BackstabAimIndicator 派生 lead(起点-外环)/速度 匹配(0.8);调大 = 更早触发(组件内部自动对齐窗口起点)")]
    [SerializeField] private float leadSeconds = 0.8f;

    [Tooltip("连音目标分配器(P3 BackstabChainPlanner):连音组快照分配用。留空时运行时只找一次(本物体/父级/子级 → MusicPointManager 同物体 → FindObjectOfType),不在 Update 里查")]
    [SerializeField] private BackstabChainPlanner chainPlanner;

    private EnemyControllerBase _current;
    private float _aimStartedForNext = float.NegativeInfinity;  // 已尝试启动的 bar 时刻(防同一 bar 重复启动/每帧重扫)

    // ── 连音路径状态(P5)──
    private float _chainPreparedFirst = -1f;                        // 已准备过的连音组首点时刻(-1 = 无);同一组只准备一次,组内不重排
    private readonly List<EnemyControllerBase> _shownEnemies = new List<EnemyControllerBase>(8);  // 本轮已出环的敌人(组结束/切曲逐个 Hide)
    private readonly List<float> _tmpSeconds = new List<float>(8);  // 合并「同一敌人的多个点」的相对秒数(复用,不每组建新 List)
    private bool _plannerScanned;                                   // 分配器是否已尝试解析(每次激活最多扫一次场)

    /// <summary>连音预告时间点事件(P5 规格 §3):某连音组进入预告期(组首点前 leadSeconds)那一刻发一次,
    /// 参数 = 该组首点时刻。纯代码标记、无美术表现;供表现层(音效/UI)订阅。同一组只发一次(与 PrepareChain 同时机)。</summary>
    public event Action<float> OnChainPreview;

    /// <summary>最近一次连音预告时间点对应的组首点时刻(-1 = 本次激活内还没发生);给不便订阅事件的表现层只读轮询用</summary>
    public float LastChainPreviewTime { get; private set; } = -1f;

    private void OnEnable()
    {
        var mgr = MusicPointManager.Instance;
        if (mgr != null)
        {
            mgr.OnWindowEnter += OnWindowEnter;
            mgr.OnWindowPassed += OnWindowPassed;
        }
        LastChainPreviewTime = -1f;
        _plannerScanned = false;   // 本次激活允许重新解析一次分配器引用
    }

    private void OnDisable()
    {
        var mgr = MusicPointManager.Instance;
        if (mgr != null)
        {
            mgr.OnWindowEnter -= OnWindowEnter;
            mgr.OnWindowPassed -= OnWindowPassed;
        }
        if (_current != null)
            _current.GetComponentInChildren<BeatFlashPoint>(true)?.Hide();   // 组件停用/场景切换:把还亮着的标识收起,防残留
        _current = null;
        _aimStartedForNext = float.NegativeInfinity;

        // 连音路径:已发的环全收 + 分配合并清空
        HideChainIdentifiers();
        if (chainPlanner != null) chainPlanner.ClearChain();
        _chainPreparedFirst = -1f;
    }

    private void Update()
    {
        var mgr = MusicPointManager.Instance;
        if (mgr == null) return;

        // 选路:当前曲配了连音组 → 只走连音预告,自动重音标识让位(判定优先级与 P6 一致);没有才回退 bar 路径
        if (mgr.HasChain)
        {
            UpdateChain(mgr);
            return;
        }
        UpdateAutoBar(mgr);
    }

    // ============================================================
    // 路径② 自动重音(P5 前原逻辑整段保留,逐帧行为不变)
    // ============================================================

    private void UpdateAutoBar(MusicPointManager mgr)
    {
        if (!mgr.HasAutoBar) return;   // 无自动重音曲(含 Boss 标点窗口)不预告
        float ttn = mgr.TimeToNextAutoBar;            // 窗口已开后为负,不会误启动
        if (ttn <= 0f || ttn > leadSeconds) return;
        float next = mgr.NextAutoBarTime;
        if (next < 0f || next == _aimStartedForNext) return;   // 本 bar 已尝试过(含空安全失败),不每帧重扫
        StartAimForBar(next, ttn);
    }

    private void OnWindowEnter(float pointTime)
    {
        var mgr = MusicPointManager.Instance;
        if (mgr == null || mgr.HasChain) return;           // 有连音组:标识由连音路径驱动,不让 auto bar 事件插手
        if (!mgr.IsAutoBarWindow) return;   // 只响应自动重音窗口
        if (pointTime == _aimStartedForNext) return;       // Update 预告已处理过本 bar,跳过(不重播)
        // 兜底:预告启动时机错过/本组件中途激活/目标刚出现 → 窗口开立即启动标识(剩余≈0,组件内部兜底反推起点)
        StartAimForBar(pointTime, 0f);
    }

    private void OnWindowPassed(float pointTime)
    {
        // 窗口正常结束:标识消失(生命周期消失时机 1)
        if (_current != null)
            _current.GetComponentInChildren<BeatFlashPoint>(true)?.Hide();
        _current = null;
    }

    /// <summary>对某 bar 时刻启动预告标识:找最近敌人调其 BeatFlashPoint.Flash(secondsToNext, window)。
    /// secondsToNext = 触发时距窗口起点的真实剩余秒数(组件据此保证环在窗口起点到外环)。
    /// 每次都记 _aimStartedForNext(含空安全失败)→ 防 Update 在同一 bar 内每帧重复 FindObjectsOfType;
    /// 无敌人 / 敌人身上没挂 BeatFlashPoint = 空安全跳过,不显示不报错(槽位由 saika 场景侧配)。</summary>
    private void StartAimForBar(float barTime, float secondsToNext)
    {
        if (barTime < 0f) return;
        _aimStartedForNext = barTime;
        _current = FindNearestEnemy();
        if (_current == null) return;
        var mgr = MusicPointManager.Instance;
        float window = mgr != null ? mgr.WindowSeconds : 0.3f;   // 内环按当前曲窗口时长动态适配
        _current.GetComponentInChildren<BeatFlashPoint>(true)?.Flash(secondsToNext, window);
    }

    /// <summary>找最近的非死亡普通敌人(IsBoss 跳过;searchRadius>0 用 OverlapCircle,否则全场景遍历)</summary>
    private EnemyControllerBase FindNearestEnemy()
    {
        EnemyControllerBase nearest = null;
        float bestSqr = float.MaxValue;
        Vector2 origin = transform.position;

        if (searchRadius > 0f)
        {
            Collider2D[] cols = Physics2D.OverlapCircleAll(origin, searchRadius);
            foreach (var c in cols)
            {
                var e = c.GetComponentInParent<EnemyControllerBase>();
                if (e == null || e.IsDead || e.IsBoss) continue;
                float d = ((Vector2)e.transform.position - origin).sqrMagnitude;
                if (d < bestSqr) { bestSqr = d; nearest = e; }
            }
        }
        else
        {
            var all = FindObjectsOfType<EnemyControllerBase>();
            foreach (var e in all)
            {
                if (e == null || e.IsDead || e.IsBoss) continue;
                float d = ((Vector2)e.transform.position - origin).sqrMagnitude;
                if (d < bestSqr) { bestSqr = d; nearest = e; }
            }
        }
        return nearest;
    }

    // ============================================================
    // 路径① 连音组(P5):组前快照一次 → 按点出环 → 组结束收环清分配
    // ============================================================

    /// <summary>连音路径每帧入口(轻量只读轮询)。CurrentChainPoints 非空 = 有当前连音组(P2 口径:
    /// 已进预告期或正在执行);用「组首点时刻」去重,保证同一组只 PrepareChain 一次(防每帧重排/重发环)。</summary>
    private void UpdateChain(MusicPointManager mgr)
    {
        var points = mgr.CurrentChainPoints;              // 空 = 无当前组(还没进预告期 / 组已结束 / 切曲)
        if (points.Length == 0)
        {
            EndChain();                                   // 组结束/切曲:收环 + 清分配
            return;
        }

        float first = points[0];
        if (Mathf.Abs(first - _chainPreparedFirst) < 0.001f) return;   // 本组已准备过:组内不重排、不重发

        // 预告闸门:距「本组首点」≤ leadSeconds(复用现有 0.8)才快照;首点已过 = 相对秒数为负,
        // 晚到兜底(组件中途激活/预告时机错过)直接过,仍按「当前组点表」补一次,保证 P6 按索引取目标时与快照同组。
        // 用本组首点而非 mgr.TimeToNextChainStart:组首点未到时两者等价(NextChainStartTime 就是本组首点),
        // 但组内中途再激活时 TimeToNextChainStart 已跳指「下一组」——若下一组还在 leadSeconds 之外就会整组漏发,
        // 故一律按本组首点算(负值直接过 = 补发)。
        if (points[0] - mgr.TrackTime > leadSeconds) return;

        _chainPreparedFirst = first;
        NotifyChainPreview(first);                        // §3 连音预告时间点:只打代码标记,无美术表现
        PrepareAndShowChain(mgr, points);
    }

    /// <summary>准备一次(P3 快照)并按分配结果给敌人出环:
    /// 同一个敌人的多个点合并成一份 secondsToPoints(相对秒数 = 点时刻 - 当前曲时间)一次传给它(单敌人多点 = 错开多环);
    /// 不同敌人各自一份数组/各自实例;无分配 / 没挂 BeatFlashPoint / 没配 aimPrefab = 空安全跳过(不显示不报错)。</summary>
    private void PrepareAndShowChain(MusicPointManager mgr, float[] points)
    {
        var planner = ResolvePlanner();
        if (planner == null) return;                      // 场景未接线:不出环(不报错,不影响 P6 兜底)
        planner.PrepareChain(points);                     // 整组只在这里扫一次场(组内不重排)

        HideChainIdentifiers();                           // 上一组残留先收干净(幂等)

        // 第一遍:收集去重后的目标敌人(只认挂了 BeatFlashPoint 且配了 aimPrefab 的 → 空安全跳过口径与现状一致)
        for (int i = 0; i < points.Length; i++)
        {
            var enemy = planner.GetTargetForPoint(i);
            if (enemy == null || _shownEnemies.Contains(enemy)) continue;
            var bp = enemy.GetComponentInChildren<BeatFlashPoint>(true);
            if (bp == null || bp.aimPrefab == null) continue;
            _shownEnemies.Add(enemy);
        }

        // 第二遍:每个敌人一份合并数组,一次 ShowChain 驱动它身上所有点的环(各自错开收缩)
        float window = mgr.WindowSeconds;
        float now = mgr.TrackTime;
        for (int s = 0; s < _shownEnemies.Count; s++)
        {
            var enemy = _shownEnemies[s];
            if (enemy == null) continue;
            _tmpSeconds.Clear();
            for (int i = 0; i < points.Length; i++)
            {
                if (planner.GetTargetForPoint(i) != enemy) continue;
                _tmpSeconds.Add(points[i] - now);         // 该点相对秒数(晚触发可为负,标识组件内部反推起点兜底)
            }
            if (_tmpSeconds.Count == 0) continue;
            enemy.GetComponentInChildren<BeatFlashPoint>(true)?.ShowChain(_tmpSeconds.ToArray(), window);
        }
    }

    /// <summary>组结束(窗口全过)或切曲:收掉已发的环并把分配清空(下一组进预告期时重新快照)。
    /// 本来就没准备过 = 直接返回(每帧零开销)。</summary>
    private void EndChain()
    {
        if (_chainPreparedFirst < 0f && _shownEnemies.Count == 0) return;
        HideChainIdentifiers();
        ResolvePlanner()?.ClearChain();
        _chainPreparedFirst = -1f;
    }

    /// <summary>收起本轮发出去的所有标识(沿用现状 Hide 路径:敌人 BeatFlashPoint.Hide)并清空记录(幂等)</summary>
    private void HideChainIdentifiers()
    {
        for (int i = 0; i < _shownEnemies.Count; i++)
        {
            var enemy = _shownEnemies[i];
            if (enemy == null) continue;
            enemy.GetComponentInChildren<BeatFlashPoint>(true)?.Hide();
        }
        _shownEnemies.Clear();
    }

    /// <summary>§3 连音预告时间点:进预告期那一刻打一次标记(时间戳字段 + 事件),纯代码、无美术表现。</summary>
    private void NotifyChainPreview(float firstPointTime)
    {
        LastChainPreviewTime = firstPointTime;
        OnChainPreview?.Invoke(firstPointTime);
    }

    /// <summary>解析连音分配器(P3):序列化引用优先;留空时按「本物体/父级/子级 → MusicPointManager 同物体」找,
    /// 最后退化为一次 FindObjectOfType(每次激活最多一次,不在 Update/每帧里查)。</summary>
    private BackstabChainPlanner ResolvePlanner()
    {
        if (chainPlanner != null) return chainPlanner;
        if (_plannerScanned) return null;
        _plannerScanned = true;

        chainPlanner = GetComponentInParent<BackstabChainPlanner>();
        if (chainPlanner == null) chainPlanner = GetComponentInChildren<BackstabChainPlanner>(true);
        if (chainPlanner == null && MusicPointManager.Instance != null)
            chainPlanner = MusicPointManager.Instance.GetComponentInParent<BackstabChainPlanner>();
        if (chainPlanner == null) chainPlanner = FindObjectOfType<BackstabChainPlanner>();
        return chainPlanner;
    }
}
