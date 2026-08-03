using UnityEngine;

/// <summary>
/// 地面冲击 VFX — 订阅 GroundPoundEvent，在砸地中心生成 VFX。
/// 挂载到场景中任意持久化 GameObject，Inspector 拖入 prefab。
/// </summary>
public class GroundPoundVFX : MonoBehaviour
{
    [SerializeField] private GameObject groundPoundVFXPrefab;

    private void Awake() => EventBus.Subscribe<GroundPoundEvent>(OnEvent);
    private void OnDestroy() => EventBus.Unsubscribe<GroundPoundEvent>(OnEvent);

    private void OnEvent(GroundPoundEvent e)
    {
        if (groundPoundVFXPrefab != null)
            VFXSpawner.SpawnInWorld(groundPoundVFXPrefab, e.center);
    }
}
