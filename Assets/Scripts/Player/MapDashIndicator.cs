using UnityEngine;

/// <summary>
/// 地图元素冲刺预告指示器 [S6] —— 挂玩家根(与 EnemyBeatIndicator 同目录、同结构),只做"预告",不做判定。
///
/// 【要解决的问题】S3 之前,元素上的判定收缩环是「按 F 冲过去之后」才播的(当命中反馈),
///   体感慢一拍。S6 改成和背刺同款的**预告**:重音点到来的前 leadSeconds 就把环播出来 →
///   环缩到判定外环的时刻正好是重音点 → 玩家看到环缩到位时按 F 冲过去。
///
/// 【与 EnemyBeatIndicator 的分工(互不干扰)】两条完全独立:
///   · EnemyBeatIndicator 负责**敌人**身上的环(背刺预告),本任务一字不改它;
///   · MapDashIndicator 负责**地图元素**身上的环(MapDashPoint.judgeRing)。
///   本组件找不到元素时**什么都不播**(不兜底去播敌人那只,也不报错)—— 敌人侧的环由对方负责。
///
/// 【选路 / 时序口径 = 逐条对齐 EnemyBeatIndicator】每帧只做**只读轮询**(不每帧 FindObjectsOfType、
///   不每帧查 Camera.main、不每帧全场扫描),选路规则与 P6 判定入口一致:
///   `mgr.HasChain` → 连音路径(元素只认**组首音**,与 S5 口径一致);
///   否则 → 自动重音路径(轮询 `TimeToNextAutoBar`)。
///
///   ① 自动重音路径(对齐 EnemyBeatIndicator.UpdateAutoBar):
///      `if (!mgr.HasAutoBar) return;` → `ttn = mgr.TimeToNextAutoBar; if (ttn &lt;= 0 || ttn &gt; leadSeconds) return;`
///      → `next = mgr.NextAutoBarTime; if (next &lt; 0 || next == _previewedFor) return;` → 记 `_previewedFor = next` → 预告。
///      去重字段 `_previewedFor` 存的是「下一次重音点的曲内时刻」(与对方 `_aimStartedForNext` 同名同义):
///      同一 bar 只预告一次(含"没找到元素"的空安全失败,不每帧重扫)。
///   ② 连音路径(对齐 EnemyBeatIndicator.UpdateChain,但元素只看首音):
///      `points = mgr.CurrentChainPoints`;`points.Length == 0`(组结束/切曲)→ `_previewedFor = -1f` 并 return;
///      `first = points[0]`,已预告过(差值 &lt; 0.001)→ return;
///      `ttn = first - mgr.TrackTime`;`ttn &lt;= 0`(首音已过)→ 记下并 return(**不补发**:
///      首音过了元素冲刺也不可能触发 —— S5 只认首音活跃窗口);`ttn &gt; leadSeconds` → return(还没进预告期);
///      否则记下 → 预告。
///      去重字段在连音路径存的是**组首音时刻**:同一组只预告一次(组内后续点由背刺逐点推进,元素不参与)。
///
/// 【禁止项自查】Update 里不 Find、不 FindObjectsOfType、不查 Camera.main、不全场遍历;
///   `MapDashPoint.TryFindNearest` 遍历的是元素静态注册表(只在预告那一帧调用),属预期内。
///   本组件不做窗口消费、不触发冲刺、不碰 EnemyBeatIndicator / 背刺 / 音乐 API 的写入入口 —— 纯读 + 纯表现。
///
/// 挂点:玩家根(与 PlayerController / PlayerMapDash 同物体)。
/// </summary>
public class MapDashIndicator : MonoBehaviour
{
    // ============================================================
    // 序列化参数(全部 Inspector 可调,默认值即可用)
    // ============================================================

    [Tooltip("预告检测提前量(秒):距重音点 ≤ 此值开始找元素并提前播判定环。与 EnemyBeatIndicator.leadSeconds / BackstabChainPlanner 的预告口径同值(定稿 0.8)")]
    [SerializeField] private float leadSeconds = 0.8f;

    [Tooltip("元素查询的视口外扩余量(viewport 坐标 0~1,0 = 元素必须完全在屏幕内)。口径同 PlayerController 的 mapDashViewportMargin —— 建议两处同值,否则「预告找得到的元素」与「按 F 时找得到的元素」会不一致")]
    [SerializeField] private float viewportMargin = 0f;

    // ============================================================
    // 运行时状态 / 缓存(全部一次性解析,Update 里绝不做查找)
    // ============================================================

    /// <summary>已预告过的那一次:自动重音路径 = bar 的曲内时刻;连音路径 = 组首音时刻。
    /// -1 = 没有任何在预告中的点(也是组结束/切曲/组件停用后的复位值)。
    /// 只用于去重(防同一 bar / 同一组每帧重播环),与 EnemyBeatIndicator 的 _aimStartedForNext 同义。</summary>
    private float _previewedFor = -1f;

    /// <summary>同物体上的元素冲刺执行器:借它的缓存相机(与 S5 按键那一帧用的是同一只,视口口径一致)。
    /// 未挂时为 null —— 相机退回 Camera.main 兜底,预告链路照样可用。</summary>
    private PlayerMapDash _mapDash;

    /// <summary>预告用的相机(Awake 解析一次并缓存;Update 里绝不查 Camera.main)。
    /// 解析顺序:PlayerMapDash.CachedCamera → Camera.main 兜底一次。为 null = 场景里没有 MainCamera → 整局不预告(不报错)。</summary>
    private Camera _cam;

    // ============================================================
    // 生命周期
    // ============================================================

    /// <summary>一次性解析全部引用与相机(相机只在 Owner 这里查:Update 路径里不查 Camera.main)。</summary>
    private void Awake()
    {
        _mapDash = GetComponent<PlayerMapDash>();

        // ① 优先复用 S3 执行器缓存的那只相机(它自己的 Awake 里已兜底过一次 Camera.main);
        // ② 执行器缺失、或它的 Awake 还没跑(执行顺序不定)→ Camera.main 兜底一次。
        // 两次都取不到就保持 null:整局不预告,不报错(而不是每帧去查)。
        _cam = _mapDash != null ? _mapDash.CachedCamera : null;
        if (_cam == null) _cam = Camera.main;
    }

    /// <summary>组件停用(切场景 / 被关):复位去重字段,下次激活从干净状态起(不残留上一局的预告记录)。</summary>
    private void OnDisable()
    {
        _previewedFor = -1f;
    }

    private void Update()
    {
        var mgr = MusicPointManager.Instance;
        if (mgr == null) return;

        // 选路口径与 EnemyBeatIndicator / P6 判定入口一致:当前曲配了连音组 → 只走连音预告;否则回退自动重音
        if (mgr.HasChain)
        {
            UpdateChain(mgr);
            return;
        }
        UpdateAutoBar(mgr);
    }

    // ============================================================
    // 路径① 自动重音(逐条对齐 EnemyBeatIndicator.UpdateAutoBar)
    // ============================================================

    /// <summary>距下个自动重音 ≤ leadSeconds 时预告一次。同一 bar 只处理一次(成功/没找到元素都算处理过)。</summary>
    private void UpdateAutoBar(MusicPointManager mgr)
    {
        if (!mgr.HasAutoBar) return;                          // 无自动重音曲(含 Boss 标点窗口)不预告
        float ttn = mgr.TimeToNextAutoBar;                    // 窗口已开后为负 → 不会误启动
        if (ttn <= 0f || ttn > leadSeconds) return;
        float next = mgr.NextAutoBarTime;
        if (next < 0f || next == _previewedFor) return;       // 本 bar 已尝试过(含空安全失败),不每帧重扫
        _previewedFor = next;
        PreviewElement(ttn);
    }

    // ============================================================
    // 路径② 连音组(元素只认首音,与 S5 口径一致)
    // ============================================================

    /// <summary>连音路径每帧入口(轻量只读轮询)。CurrentChainPoints 非空 = 有当前连音组(已进预告期或正在执行);
    /// 用「组首音时刻」去重,保证同一组只预告一次(组内后续点不归元素管)。</summary>
    private void UpdateChain(MusicPointManager mgr)
    {
        float[] points = mgr.CurrentChainPoints;              // 空 = 无当前组(还没进预告期 / 组已结束 / 切曲)
        if (points.Length == 0)
        {
            _previewedFor = -1f;                              // 组结束/切曲:复位,下一组首音重新判断
            return;
        }

        float first = points[0];
        if (Mathf.Abs(first - _previewedFor) < 0.001f) return;   // 本组已预告过:不重排、不重播

        float ttn = first - mgr.TrackTime;                    // 距首音的剩余秒数(晚到可为负)
        if (ttn <= 0f)
        {
            _previewedFor = first;                            // 首音已过:**不补发**(首音过了元素冲刺也不可能触发)
            return;
        }
        if (ttn > leadSeconds) return;                        // 还没进预告期:不记,等到点再进来
        _previewedFor = first;
        PreviewElement(ttn);
    }

    // ============================================================
    // 表现(找元素 + 让它提前播判定环)
    // ============================================================

    /// <summary>找「视口内 + 非 CD」的最近元素,让它把判定环按剩余秒数提前播出来
    /// (环缩到判定外环的时刻 = 该重音点)。口径与背刺目标分配一致:**不判朝向**(目标可以在玩家身后)。
    /// 找不到元素 → 什么都不播(敌人侧的环由 EnemyBeatIndicator 负责)。</summary>
    /// <param name="secondsToPoint">距重音点的剩余秒数(可为 0f/负值:晚到兜底由 BackstabAimIndicator 内部反推起点)</param>
    private void PreviewElement(float secondsToPoint)
    {
        if (_cam == null) return;                             // Awake 没取到相机 → 不预告、不报错

        if (!MapDashPoint.TryFindNearest((Vector2)transform.position, _cam, viewportMargin, out var point))
            return;                                           // 没元素就什么都不播

        // 元素自己带 CD / IsReady 守卫(CD 中不播),这里不重复判 —— 单一数据源在元素身上
        point.ShowJudgeRing(secondsToPoint);
    }
}
