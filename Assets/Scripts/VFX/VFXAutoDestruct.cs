using UnityEngine;

/// <summary>
/// VFX 自动销毁组件 — 根据 Animator 最长 clip 和 ParticleSystem duration 自动计算存活时间。
/// 由 VFXSpawner 自动挂载，用户无需手动操作。
/// </summary>
public class VFXAutoDestruct : MonoBehaviour
{
    private void Start()
    {
        float lifetime = 1f; // 默认 1 秒

        // 检测 Animator：取所有 clip 中的最大时长
        Animator anim = GetComponent<Animator>();
        if (anim != null && anim.runtimeAnimatorController != null)
        {
            foreach (var clip in anim.runtimeAnimatorController.animationClips)
            {
                if (clip.length > lifetime)
                    lifetime = clip.length;
            }
        }

        // 检测 ParticleSystem：取主模块 duration（循环的跳过）
        ParticleSystem ps = GetComponent<ParticleSystem>();
        if (ps != null && !ps.main.loop)
        {
            float psDuration = ps.main.duration + ps.main.startLifetime.constantMax;
            if (psDuration > lifetime)
                lifetime = psDuration;
        }

        // 检测子物体上的 ParticleSystem
        foreach (var childPs in GetComponentsInChildren<ParticleSystem>())
        {
            if (childPs != null && !childPs.main.loop)
            {
                float d = childPs.main.duration + childPs.main.startLifetime.constantMax;
                if (d > lifetime) lifetime = d;
            }
        }

        Destroy(gameObject, lifetime + 0.1f);
    }
}
