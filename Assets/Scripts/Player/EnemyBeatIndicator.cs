using UnityEngine;

/// <summary>
/// 重音背刺预告标识指示器 — 挂玩家根或场景空物体。
/// 预告驱动:Update 每帧轮询 MusicPointManager 下个自动重音剩余时间(轻量只读,BeatPreviewIndicator 同款先例),
/// 在窗口开启前 leadSeconds 找到最近非死亡普通敌人(IsBoss 跳过),调其子物体 BeatFlashPoint.Flash()
/// (BeatFlashPoint 改造后 = 挂点 + prefab 槽生成器:拖 aimPrefab 时在其位置实例化 BackstabAim_Template,
/// Ring 粒子从头播一轮收缩穿过固定判定圈;没拖 = 原 SpriteRenderer 头顶闪点,Boss 重音兼容)。
/// 窗口事件保留作兜底(预告时机错过/中途进入时补启动)与清理(窗口正常结束 BeatFlashPoint.Hide)。
/// 每个 bar 只启动一次(记 _aimStartedForNext 防重),敌人身上没挂 BeatFlashPoint = 空安全跳过。
/// Boss 战/标点窗口(非自动重音)不满足 HasAutoBar/IsAutoBarWindow,本组件不响应,不干扰 PlayerBeatJudge。
/// </summary>
public class EnemyBeatIndicator : MonoBehaviour
{
    [Tooltip("搜索半径(找最近 enemy);<=0 用全场景 FindObjectsOfType")]
    public float searchRadius = 0f;

    [Tooltip("预告检测提前量(秒):距下个窗口 ≤ 此值开始找敌人触发标识。与 BackstabAimIndicator 派生 lead(起点-外环)/速度 匹配(0.8);调大 = 更早触发(组件内部自动对齐窗口起点)")]
    [SerializeField] private float leadSeconds = 0.8f;

    private EnemyControllerBase _current;
    private float _aimStartedForNext = float.NegativeInfinity;  // 已尝试启动的 bar 时刻(防同一 bar 重复启动/每帧重扫)

    private void OnEnable()
    {
        var mgr = MusicPointManager.Instance;
        if (mgr != null)
        {
            mgr.OnWindowEnter += OnWindowEnter;
            mgr.OnWindowPassed += OnWindowPassed;
        }
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
    }

    private void Update()
    {
        var mgr = MusicPointManager.Instance;
        if (mgr == null || !mgr.HasAutoBar) return;   // 无自动重音曲(含 Boss 标点窗口)不预告
        float ttn = mgr.TimeToNextAutoBar;            // 窗口已开后为负,不会误启动
        if (ttn <= 0f || ttn > leadSeconds) return;
        float next = mgr.NextAutoBarTime;
        if (next < 0f || next == _aimStartedForNext) return;   // 本 bar 已尝试过(含空安全失败),不每帧重扫
        StartAimForBar(next, ttn);
    }

    private void OnWindowEnter(float pointTime)
    {
        var mgr = MusicPointManager.Instance;
        if (mgr == null || !mgr.IsAutoBarWindow) return;   // 只响应自动重音窗口
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
}
