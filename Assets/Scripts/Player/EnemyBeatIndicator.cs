using UnityEngine;

/// <summary>
/// 重音背刺头顶标识指示器 — 挂玩家根或场景空物体。
/// 订阅音乐窗口事件,只在「自动重音窗口」(IsAutoBarWindow)开启时找最近的
/// 非死亡普通敌人(IsBoss 跳过),手动触发其头顶 BeatFlashPoint.Flash()(只闪最近一只,不闪全部)。
/// Boss 战窗口(标点组)不满足 IsAutoBarWindow,本组件不响应,不干扰 PlayerBeatJudge。
/// </summary>
public class EnemyBeatIndicator : MonoBehaviour
{
    [Tooltip("搜索半径(找最近 enemy);<=0 用全场景 FindObjectsOfType")]
    public float searchRadius = 0f;

    private EnemyControllerBase _current;

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
        _current = null;
    }

    private void OnWindowEnter(float pointTime)
    {
        var mgr = MusicPointManager.Instance;
        if (mgr == null || !mgr.IsAutoBarWindow) return;   // 只响应自动重音窗口
        _current = FindNearestEnemy();
        if (_current != null)
            _current.GetComponentInChildren<BeatFlashPoint>(true)?.Flash();   // true:物体 inactive 也能找到组件(显示由 Flash 内部处理)
    }

    private void OnWindowPassed(float pointTime)
    {
        // 窗口正常结束:当前 enemy 头顶标识消失(生命周期消失时机 1)
        if (_current != null)
            _current.GetComponentInChildren<BeatFlashPoint>(true)?.Hide();
        _current = null;
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
