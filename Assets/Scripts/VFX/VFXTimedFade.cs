using System.Collections;
using UnityEngine;

/// <summary>
/// 特效定时淡出组件:显示 displayDuration 秒后停止粒子发射(ps.Stop 只停发射,
/// 已发射粒子按自身 Lifetime 自然消亡 = 淡出效果),等最长生长的粒子消亡后自动销毁。
/// 适用于循环粒子(loop)——它们没有"播完"概念,硬销毁会突然消失,必须停止发射让尾部渐散。
/// 由 WeaponThrow 按 WeaponAttackConfig.vfxDisplayDuration > 0 时挂载。
/// </summary>
public class VFXTimedFade : MonoBehaviour
{
    /// <summary>启动定时淡出。displayDuration <= 0 时立即淡出。</summary>
    public void Init(float displayDuration)
    {
        StartCoroutine(FadeRoutine(displayDuration));
    }

    private IEnumerator FadeRoutine(float displayDuration)
    {
        yield return new WaitForSeconds(Mathf.Max(0f, displayDuration));

        // 停止所有粒子系统发射(Stop 默认 StopEmitting:不再发射新粒子,已发射粒子继续飞)
        foreach (var ps in GetComponentsInChildren<ParticleSystem>(true))
        {
            ps.Stop();
        }

        // 等剩余粒子按自身 Lifetime 自然消亡:取所有粒子系统中最长的 startLifetime
        float maxLifetime = 0f;
        foreach (var ps in GetComponentsInChildren<ParticleSystem>(true))
        {
            if (ps == null) continue;
            float lifetime = ps.main.startLifetime.constantMax;
            if (lifetime > maxLifetime) maxLifetime = lifetime;
        }

        yield return new WaitForSeconds(maxLifetime + 0.1f);
        Destroy(gameObject);
    }
}
