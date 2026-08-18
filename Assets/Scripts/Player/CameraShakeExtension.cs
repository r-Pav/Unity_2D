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

    /// <summary>触发震动。可传 0/负值表示使用默认时长幅度。</summary>
    public void Shake(float duration, float magnitude)
    {
        if (duration > 0f) shakeDuration = duration;
        if (magnitude > 0f) shakeMagnitude = magnitude;
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

        // 2D 随机偏移：只动 X/Y
        state.PositionCorrection += new Vector3(
            Random.Range(-1f, 1f),
            Random.Range(-1f, 1f),
            0f) * shakeMagnitude;

        // 用真实时间：全局卡肉 timeScale=0 期间震屏照常播放，不被定格
        timer -= Time.unscaledDeltaTime;
        if (timer < 0f) timer = 0f;
    }
}
