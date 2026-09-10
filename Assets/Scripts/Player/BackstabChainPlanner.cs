using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 连音背刺目标分配器(P3,2026-09-10 规格)— 连音组目标的「单一数据源」。
/// 环预告(P5)与实际背刺(P6)都只从这里取目标,杜绝两处各自搜索导致的「环指 A、实际刺 B」。
///
/// 分工:
///   P2(MusicPointManager)负责切分连音组并给出 CurrentChainPoints;
///   P5(EnemyBeatIndicator)在连音组首点前 snapshotLeadSeconds 调一次 PrepareChain(当前组点表);
///   本组件负责一次快照:屏幕内敌人 → 与玩家距离升序 → 按点索引分配 → 缓存;
///   P6(PlayerBackstabState)按点索引 GetTargetForPoint(index) 取目标。
///
/// 分配规则(规格 §P3,已由 saika 拍板):
///   候选 = 视口内(外扩 viewportMargin)Enemy 层、存活、非 Boss 的敌人;
///   点 0 取最近;点 N 先排除点 N-1 已分配的目标,取剩下最近的;排除后无候选则回退到全部候选(允许重复);
///   实测口径:3 敌人 + 3 连音 = A、B、A;只有 1 只敌人时 3 点都指向它。
///
/// 性能与稳定性:
///   扫描只发生在 PrepareChain(每组一次),Update 内不做任何场景遍历、不调 Camera.main、不用 FindObjectsOfType;
///   组内不重排(快照结果按点索引稳定缓存),组结束/切曲由调用方 ClearChain() 清空。
///
/// 场景侧接线(saika):建议挂玩家根或 MusicPointManager 同物体,拖 worldCamera(Main Camera)、player(玩家根)、enemyLayer(Enemy)。
/// </summary>
public class BackstabChainPlanner : MonoBehaviour
{
    // ============================================================
    // 字段(全部 [SerializeField],禁止运行时查找;兜底只允许在 Awake 做一次)
    // ============================================================

    [Tooltip("世界相机(拖 Main Camera)。留空时 Awake 只做一次 Camera.main 兜底,不在 Update/PrepareChain 里查")]
    [SerializeField] private Camera worldCamera;

    [Tooltip("敌人层(拖 Enemy)。留 0(Nothing)时 Awake 兜底 LayerMask.GetMask(\"Enemy\")——不能在字段初始化器里调,那时 NameToLayer 还不可用")]
    [SerializeField] private LayerMask enemyLayer = 0;

    [Tooltip("玩家根 Transform(分配排序的距离原点)。留空时 Awake 回退 GetComponentInParent<PlayerController>()")]
    [SerializeField] private Transform player;

    [Tooltip("视口外扩比例(0 = 严格 0..1 视口;0.1 = 四周各外扩 10% 视口宽高)")]
    [SerializeField] private float viewportMargin = 0f;

    [Tooltip("快照提前量(秒):连音组首点前多久分配。必须与 MusicPointManager 的预告提前量 leadSeconds(0.8)保持一致,调用时机由 P5 决定")]
    [SerializeField] private float snapshotLeadSeconds = 0.8f;

    // ============================================================
    // 缓存(组内只读;组结束/切曲由 ClearChain 清空)
    // ============================================================

    /// <summary>按连音组内点索引缓存的分配结果(与 PrepareChain 传入的点表一一对应)</summary>
    private EnemyControllerBase[] _targets;
    private int _targetCount;

    /// <summary>快照时的候选表(距离升序)与并行的平方距离;复用 List 避免每次快照新分配</summary>
    private readonly List<EnemyControllerBase> _candidates = new List<EnemyControllerBase>(16);
    private readonly List<float> _candidateSqrDist = new List<float>(16);

    /// <summary>快照提前量(秒),供 P5 与 P2 的 leadSeconds 对齐(只读)</summary>
    public float SnapshotLeadSeconds => snapshotLeadSeconds;

    /// <summary>当前缓存的连音组点数(未准备/已清空 = 0)</summary>
    public int ChainLength => _targetCount;

    private void Awake()
    {
        // 兜底集中在这里做一次(每帧查找、字段初始化器调用都是禁止项;NameToLayer 只能在运行时问)。
        if (enemyLayer.value == 0) enemyLayer = LayerMask.GetMask("Enemy");

        if (player == null)
        {
            var pc = GetComponentInParent<PlayerController>();
            if (pc != null) player = pc.transform;
        }

        if (worldCamera == null) worldCamera = Camera.main;   // 一次性兜底(非每帧)
    }

    private void OnDisable()
    {
        // 组件停用/场景切换:清空分配,避免下一组读到过期目标(P6 无分配时会回退到原最近搜索)
        ClearChain();
    }

    // ============================================================
    // 对外接口
    // ============================================================

    /// <summary>
    /// 连音组首点前 snapshotLeadSeconds 由 P5 调用「一次」:扫一次屏幕内敌人 → 按与玩家距离升序 → 按点分配 → 缓存。
    /// chainPoints 为当前组的点时刻数组(升序,来自 MusicPointManager.CurrentChainPoints),只用其 Length 决定分配几个点。
    /// 组内禁止重复调用(重复调用 = 按当帧状态重排,违反「组内不重排」);组结束/切曲调 ClearChain()。
    /// </summary>
    public void PrepareChain(float[] chainPoints)
    {
        if (chainPoints == null || chainPoints.Length == 0)
        {
            ClearChain();
            return;
        }

        int n = chainPoints.Length;
        if (_targets == null || _targets.Length != n) _targets = new EnemyControllerBase[n];
        _targetCount = n;
        for (int i = 0; i < n; i++) _targets[i] = null;

        GatherCandidates();                 // 整组只在这里扫一次
        if (_candidates.Count == 0) return; // 屏内无可用敌人 → 全 null(P6 自行兜底)

        // 点 0 取最近;点 N 先排除点 N-1 的目标,取剩下最近的;全被排除(单敌人)→ 回退到最近候选,允许重复。
        for (int i = 0; i < n; i++)
        {
            EnemyControllerBase prev = i > 0 ? _targets[i - 1] : null;
            EnemyControllerBase pick = null;
            for (int c = 0; c < _candidates.Count; c++)   // _candidates 已按距离升序
            {
                if (_candidates[c] == prev) continue;
                pick = _candidates[c];
                break;
            }
            if (pick == null) pick = _candidates[0];
            _targets[i] = pick;
        }
    }

    /// <summary>组结束/切曲时清空分配(清空后 GetTargetForPoint / HasAssignment 一律返回 null / false)</summary>
    public void ClearChain()
    {
        if (_targets != null)
        {
            for (int i = 0; i < _targets.Length; i++) _targets[i] = null;
        }
        _targetCount = 0;
        _candidates.Clear();
        _candidateSqrDist.Clear();
    }

    /// <summary>连音组内第 index 个点分配到的敌人;无分配(未准备/越界/目标已销毁)返回 null</summary>
    public EnemyControllerBase GetTargetForPoint(int index)
    {
        return HasAssignment(index) ? _targets[index] : null;
    }

    /// <summary>第 index 个点是否有有效分配(已缓存且目标未被销毁——Unity 的 null 重载会识别已销毁对象)</summary>
    public bool HasAssignment(int index)
    {
        return _targets != null && index >= 0 && index < _targetCount && _targets[index] != null;
    }

    // ============================================================
    // 快照(只在 PrepareChain 里走一次)
    // ============================================================

    /// <summary>
    /// 采集本次快照的候选敌人:
    /// 物理粗筛(视口外扩后的世界矩形 ∩ enemyLayer)+ 逐个体精确判定(WorldToViewportPoint ∈ [-margin, 1+margin])
    /// + 非 IsDead + 非 IsBoss(规格 §P3:Boss 走自己的重击音/连打机制),最后按与玩家的欧氏距离升序。
    /// 距离比较全程用 sqrMagnitude,不开方(sqrt 只影响排序等价性,不影响顺序)。
    /// </summary>
    private void GatherCandidates()
    {
        _candidates.Clear();
        _candidateSqrDist.Clear();

        if (worldCamera == null) return;

        float minX, minY, maxX, maxY;
        if (!TryGetViewportWorldRect(out minX, out minY, out maxX, out maxY)) return;

        Vector2 origin = PlayerPosition();
        Collider2D[] cols = Physics2D.OverlapAreaAll(
            new Vector2(minX, minY), new Vector2(maxX, maxY), enemyLayer.value);

        for (int i = 0; i < cols.Length; i++)
        {
            var e = cols[i].GetComponentInParent<EnemyControllerBase>();
            if (e == null || e.IsDead) continue;                    // 存活判定与 PlayerBackstabState.FindNearestTarget 同口径
            if (e.IsBoss) continue;                                 // Boss 排除(规格 §P3 决策 8)
            if (!IsInViewport(e.transform.position)) continue;       // 精确视口判定(外扩 viewportMargin)
            if (_candidates.Contains(e)) continue;                   // 多碰撞体/多部位去重

            _candidates.Add(e);
            _candidateSqrDist.Add(((Vector2)e.transform.position - origin).sqrMagnitude);
        }

        SortCandidatesBySqrDistance();
    }

    /// <summary>插入排序:候选表通常 ≤ 10 个,不值得引入额外分配/闭包;两个并行 List 同步搬移</summary>
    private void SortCandidatesBySqrDistance()
    {
        for (int i = 1; i < _candidates.Count; i++)
        {
            EnemyControllerBase e = _candidates[i];
            float d = _candidateSqrDist[i];
            int j = i - 1;
            while (j >= 0 && _candidateSqrDist[j] > d)
            {
                _candidates[j + 1] = _candidates[j];
                _candidateSqrDist[j + 1] = _candidateSqrDist[j];
                j--;
            }
            _candidates[j + 1] = e;
            _candidateSqrDist[j + 1] = d;
        }
    }

    /// <summary>
    /// 视口外扩 viewportMargin 后的世界矩形(物理粗筛用)。
    /// 2D 相机默认正交、朝 -Z:按相机到 z=0 平面的距离换算两个角点即可;相机若带旋转,取 AABB 是超集
    /// (只会多筛不会漏筛),精确判定仍由 IsInViewport 逐个体完成。
    /// </summary>
    private bool TryGetViewportWorldRect(out float minX, out float minY, out float maxX, out float maxY)
    {
        minX = minY = maxX = maxY = 0f;
        Transform camT = worldCamera.transform;
        float dist = Mathf.Abs(camT.position.z);            // 相机到 z=0(2D 世界平面)的距离
        if (dist < 0.0001f) dist = Mathf.Max(0.0001f, worldCamera.nearClipPlane);

        Vector3 bl = worldCamera.ViewportToWorldPoint(new Vector3(-viewportMargin, -viewportMargin, dist));
        Vector3 tr = worldCamera.ViewportToWorldPoint(new Vector3(1f + viewportMargin, 1f + viewportMargin, dist));
        minX = Mathf.Min(bl.x, tr.x);
        maxX = Mathf.Max(bl.x, tr.x);
        minY = Mathf.Min(bl.y, tr.y);
        maxY = Mathf.Max(bl.y, tr.y);
        return maxX > minX && maxY > minY;
    }

    /// <summary>世界坐标是否落在视口 [-viewportMargin, 1+viewportMargin] 内(x、y 都要在;相机背后直接排除)</summary>
    private bool IsInViewport(Vector3 worldPos)
    {
        Vector3 v = worldCamera.WorldToViewportPoint(worldPos);
        if (v.z < 0f) return false;                                  // 相机背后
        return v.x >= -viewportMargin && v.x <= 1f + viewportMargin
            && v.y >= -viewportMargin && v.y <= 1f + viewportMargin;
    }

    /// <summary>分配排序的距离原点:玩家根;未接线时退化为本组件所在物体</summary>
    private Vector2 PlayerPosition()
    {
        return player != null ? (Vector2)player.position : (Vector2)transform.position;
    }

    // ============================================================
    // 调试(Gizmo)
    // ============================================================

    /// <summary>
    /// 把每个点分配到的敌人画连线并「标序号」,供 saika 目视验证(3 敌人 3 连音 = A、B、A;单敌人 = 三点同指)。
    /// 序号编码:同点一条线,颜色按点索引区分(HSV 轮转),并在敌人侧沿线摆「序号+1」个小球
    /// (点 0 = 1 个、点 1 = 2 个、点 2 = 3 个)。
    /// 规格要求只用 Gizmos.DrawLine、不引入额外依赖,故不用 Handles.Label 绘文字。
    /// </summary>
    private void OnDrawGizmosSelected()
    {
        if (_targets == null || _targetCount == 0) return;

        Vector3 anchor = player != null ? player.position : transform.position;
        for (int i = 0; i < _targetCount; i++)
        {
            var target = _targets[i];
            if (target == null) continue;

            Gizmos.color = Color.HSVToRGB(Mathf.Repeat(i * 0.37f, 1f), 0.9f, 1f);
            Vector3 enemyPos = target.transform.position;

            Gizmos.DrawLine(anchor, enemyPos);
            Gizmos.DrawWireSphere(enemyPos, 0.18f);

            Vector3 dir = anchor - enemyPos;
            dir = dir.sqrMagnitude > 0.0001f ? dir.normalized : Vector3.up;
            for (int k = 0; k <= i; k++)
                Gizmos.DrawWireSphere(enemyPos + dir * (0.30f + 0.22f * k), 0.06f);
        }
    }
}
