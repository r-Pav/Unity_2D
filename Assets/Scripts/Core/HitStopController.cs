using UnityEngine;

/// <summary>
/// 全局命中停顿控制器 — 命中时短暂减慢时间以增强打击感
/// 单例，DontDestroyOnLoad，通过 HitStopController.Instance?.Trigger() 调用
/// </summary>
public class HitStopController : MonoBehaviour
{
    public static HitStopController Instance { get; private set; }

    private void Awake()
    {
        if (Instance != null)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
        DontDestroyOnLoad(gameObject);
    }

    private int activeCount;                // 当前活跃的停顿请求数
    private float savedTimeScale = 1f;      // 首次触发前保存的原始 timescale

    /// <summary>触发命中停顿（默认 0.04s，timeScale 降至 0.1）。支持重叠调用，只有最后一次结束时才恢复原始 timescale</summary>
    public void Trigger(float duration = 0.04f)
    {
        if (activeCount == 0)
            savedTimeScale = Time.timeScale;
        activeCount++;
        StartCoroutine(Routine(duration));
    }

    private System.Collections.IEnumerator Routine(float duration)
    {
        Time.timeScale = 0.1f;
        yield return new WaitForSecondsRealtime(duration);
        activeCount--;
        if (activeCount <= 0)
        {
            activeCount = 0;
            Time.timeScale = savedTimeScale;
        }
    }
}
