using UnityEngine;

/// <summary>
/// 命中特效管理器 — 订阅 ProjectileHitEvent，在命中点生成火花等特效
/// 场景独立 GameObject（不依赖其他对象），通过 EventBus 解耦
/// </summary>
public class HitEffectManager : MonoBehaviour
{
    // [SerializeField] private GameObject sparkPrefab;  // 火花预制体（后续挂载）

    private void OnEnable()
    {
        EventBus.Subscribe<ProjectileHitEvent>(OnProjectileHit);
    }

    private void OnDisable()
    {
        EventBus.Unsubscribe<ProjectileHitEvent>(OnProjectileHit);
    }

    private void OnProjectileHit(ProjectileHitEvent e)
    {
        // 火花特效（暂时空壳，Instantiate 占位）
        // if (sparkPrefab != null)
        //     Instantiate(sparkPrefab, e.hitPoint, Quaternion.identity);
    }
}
