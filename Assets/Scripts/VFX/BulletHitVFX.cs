using UnityEngine;

/// <summary>
/// 子弹命中 VFX — 订阅 ProjectileHitEvent，在命中点生成 VFX。
/// 挂载到场景中任意持久化 GameObject，Inspector 拖入 prefab。
/// </summary>
public class BulletHitVFX : MonoBehaviour
{
    [SerializeField] private GameObject bulletHitVFXPrefab;

    private void Awake() => EventBus.Subscribe<ProjectileHitEvent>(OnEvent);
    private void OnDestroy() => EventBus.Unsubscribe<ProjectileHitEvent>(OnEvent);

    private void OnEvent(ProjectileHitEvent e)
    {
        if (bulletHitVFXPrefab != null)
            VFXSpawner.SpawnInWorld(bulletHitVFXPrefab, e.hitPoint);
    }
}
