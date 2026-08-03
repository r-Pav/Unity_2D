using UnityEngine;

/// <summary>
/// 敌人死亡 VFX — 订阅 EnemyDeathEvent，在敌人死亡位置生成 VFX。
/// 使用 SpawnOnEnemy（归属 EnemyVFX 容器）。
/// </summary>
public class EnemyDeathVFX : MonoBehaviour
{
    [SerializeField] private GameObject enemyDeathVFXPrefab;

    private void Awake() => EventBus.Subscribe<EnemyDeathEvent>(OnEvent);
    private void OnDestroy() => EventBus.Unsubscribe<EnemyDeathEvent>(OnEvent);

    private void OnEvent(EnemyDeathEvent e)
    {
        if (enemyDeathVFXPrefab != null)
            VFXSpawner.SpawnOnEnemy(enemyDeathVFXPrefab, e.position);
    }
}
