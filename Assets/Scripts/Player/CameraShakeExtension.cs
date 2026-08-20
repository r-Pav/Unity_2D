using Cinemachine;
using UnityEngine;

/// <summary>
/// 自定义相机震动扩展(替代 Cinemachine Impulse)。
/// 挂在 VCam 上,PostPipelineStageCallback 的 Finalize 阶段直接向相机 state 加随机偏移,
/// 绕过 ImpulseManager(团结引擎下 Impulse 链路不可靠)。外部调用 Shake() 触发。
/// </summary>
public class CameraShakeExtension : CinemachineExtension
{
    private float shakeDuration = 0.2f;
    private float shakeMagnitude = 0.3f;
    private float timer;
    private Vector2 shakeDirection = Vector2.zero;   // 0 = 随机方向(兼容旧调用)

    /// <summary>触发震动。可传 0/负值表示使用默认时长幅度;direction 非零时沿该方向为主(带随机垂直抖动)</summary>
    public void Shake(float duration, float magnitude, Vector2 direction = default)
    {
        if (duration > 0f) shakeDuration = duration;
        if (magnitude > 0f) shakeMagnitude = magnitude;
        if (direction != Vector2.zero) shakeDirection = direction.normalized;
        timer = shakeDuration;
    }

    protected override void PostPipelineStageCallback(
        CinemachineVirtualCameraBase vcam,
        CinemachineCore.Stage stage,
        ref CameraState state,
        float deltaTime)
    {
        if (stage != CinemachineCore.Stage.Finalize) return;
        if (timer <= 0f) return;

        // 2D 偏移：沿方向为主(带随机衰减),垂直方向少量抖动——"带一点方向"不完全直线；
        // 方向为零时保持原随机 X/Y 行为
        Vector3 offset;
        if (shakeDirection != Vector2.zero)
        {
            Vector2 perp = new Vector2(-shakeDirection.y, shakeDirection.x);
            float along = Random.Range(0.5f, 1f);
            float perpAmt = Random.Range(-0.35f, 0.35f);
            offset = (shakeDirection * along + perp * perpAmt) * shakeMagnitude;
        }
        else
        {
            offset = new Vector3(Random.Range(-1f, 1f), Random.Range(-1f, 1f), 0f) * shakeMagnitude;
        }
        state.PositionCorrection += new Vector3(offset.x, offset.y, 0f);

        // 用真实时间：全局卡肉 timeScale=0 期间震屏照常播放，不被定格
        timer -= Time.unscaledDeltaTime;
        if (timer < 0f) timer = 0f;
    }
}
