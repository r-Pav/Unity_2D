using UnityEngine;

/// <summary>
/// [技能点能量球] 挂 Enemy Prefab 上：死亡时按单怪进度总值随机拆 3~6 枚能量球释放。
/// 与 EnemyLootDrop 同款模式 — OnEnable/OnDisable 订阅 EnemyDeathEvent（不侵入 EnemyControllerBase 死亡结算）。
/// 多 enemy 各自挂独立实例，不做全局静态列表；每个 enemy 可单独调 totalMin/totalMax（Boss/精英可调大）。
/// </summary>
public class EnemyEnergyOrbDrop : MonoBehaviour
{
    [Tooltip("单怪进度总值下限（普通怪默认 30；Boss/精英可调大）")]
    [SerializeField] private float totalMin = 30f;

    [Tooltip("单怪进度总值上限（普通怪默认 50）")]
    [SerializeField] private float totalMax = 50f;

    [Tooltip("释放球数下限")]
    [SerializeField] private int orbMin = 3;

    [Tooltip("释放球数上限")]
    [SerializeField] private int orbMax = 6;

    [Tooltip("能量球 Prefab 资产（根节点挂 EnergyOrb 组件；须引用 Prefab 资产而非场景物体）")]
    [SerializeField] private EnergyOrb orbPrefab;

    private void OnEnable()
    {
        EventBus.Subscribe<EnemyDeathEvent>(OnEnemyDeath);
    }

    private void OnDisable()
    {
        EventBus.Unsubscribe<EnemyDeathEvent>(OnEnemyDeath);
    }

    private void OnEnemyDeath(EnemyDeathEvent e)
    {
        // 只处理自己（同一死亡事件所有掉落组件都会收到）
        if (e.enemy == null || e.enemy.gameObject != gameObject)
            return;

        if (orbPrefab == null)
            return;

        // 单怪进度总值（区间随机取整）+ 随机球数 3~6
        int total = Mathf.RoundToInt(Random.Range(totalMin, totalMax));
        int orbCount = Random.Range(orbMin, orbMax + 1);
        if (total <= 0 || orbCount <= 0)
            return;

        // 均分：每枚 = total / orbCount（整数），余数加给最后一枚
        int baseValue = total / orbCount;
        int remainder = total - baseValue * orbCount;

        for (int i = 0; i < orbCount; i++)
        {
            int value = baseValue + (i == orbCount - 1 ? remainder : 0);
            if (value <= 0)
                continue;

            // 死亡点小随机偏移散开，避免多枚叠在同一像素
            Vector2 spawnPos = e.position + Random.insideUnitCircle * 0.4f;
            EnergyOrb.Spawn(orbPrefab, value, spawnPos);
        }
    }
}
