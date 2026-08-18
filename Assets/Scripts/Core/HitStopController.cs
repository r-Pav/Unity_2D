using Cinemachine;
using UnityEngine;

/// <summary>
/// 全局命中停顿控制器 — 命中时短暂减慢时间以增强打击感
/// 单例，DontDestroyOnLoad，通过 HitStopController.Instance?.Trigger() 调用
/// 命中震屏统一入口：可选参数传震屏时长/幅度（真实时间驱动，不受卡帧冻结影响）
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

    // ── 命中震屏 — 懒加载相机引用（DontDestroyOnLoad 单例跨场景，旧 VCam 销毁后引用失效自动重查）──
    private CameraShakeExtension _shakeExtension;

    /// <summary>
    /// 触发命中停顿（默认 0.04s，timeScale 置 0 冻结）。支持重叠调用，只有最后一次结束时才恢复原始 timescale。
    /// 可选震屏参数：shakeDuration > 0 时同入口触发相机震动（真实时间驱动，卡帧冻结期间照常播放）；
    /// 传 0 = 不震（PlayerHealth 玩家受击调用不传震屏 → 不震，行为不变）。
    /// </summary>
    public void Trigger(float duration = 0.04f, float shakeDuration = 0f, float shakeMagnitude = 0f)
    {
        // 震屏独立于卡肉：卡肉时长为 0 时照样震
        if (shakeDuration > 0f)
            TriggerShake(shakeDuration, shakeMagnitude);

        if (duration <= 0f) return;   // 时长为 0 = 停顿关闭，避免无意义的 timeScale 置 0/恢复
        if (activeCount == 0)
            savedTimeScale = Time.timeScale;
        activeCount++;
        StartCoroutine(Routine(duration));
    }

    /// <summary>触发相机震动 — 走 CameraShakeExtension（与下坠攻击同路径；找不到 VCam 时静默跳过）</summary>
    private void TriggerShake(float duration, float magnitude)
    {
        if (_shakeExtension == null)
        {
            var vcam = FindObjectOfType<CinemachineVirtualCamera>();
            if (vcam != null)
            {
                _shakeExtension = vcam.GetComponent<CameraShakeExtension>();
                if (_shakeExtension == null)
                    _shakeExtension = vcam.gameObject.AddComponent<CameraShakeExtension>();
            }
        }
        if (_shakeExtension == null) return;
        _shakeExtension.Shake(duration, magnitude);
    }

    private System.Collections.IEnumerator Routine(float duration)
    {
        Time.timeScale = 0f;
        yield return new WaitForSecondsRealtime(duration);
        activeCount--;
        if (activeCount <= 0)
        {
            activeCount = 0;
            Time.timeScale = savedTimeScale;
        }
    }
}
