using UnityEngine;

/// <summary>
/// Boss 阶段切换 VFX — 订阅 BossPhaseChangedEvent，在 Boss 位置生成阶段切换特效。
/// 使用 SpawnOnBoss（归属 BossVFX 容器）。
/// 注意：BossPhaseChangedEvent 不含 position 字段，使用 e.boss.transform.position。
/// </summary>
public class BossPhaseVFX : MonoBehaviour
{
    [SerializeField] private GameObject bossPhaseVFXPrefab;

    private void Awake() => EventBus.Subscribe<BossPhaseChangedEvent>(OnEvent);
    private void OnDestroy() => EventBus.Unsubscribe<BossPhaseChangedEvent>(OnEvent);

    private void OnEvent(BossPhaseChangedEvent e)
    {
        if (bossPhaseVFXPrefab != null && e.boss != null)
            VFXSpawner.SpawnOnBoss(bossPhaseVFXPrefab, e.boss.transform.position);
    }
}
