using UnityEngine;

/// <summary>
/// [技能点能量球] 挂 Enemy Prefab 上：死亡时释放能量球(当前固定单球,总值=total 随机区间)。
/// 与 EnemyLootDrop 同款模式 — OnEnable/OnDisable 订阅 EnemyDeathEvent(不侵入 EnemyControllerBase 死亡结算)。
/// 多 enemy 各自挂独立实例,不做全局静态列表;每个 enemy 可单独调 totalMin/totalMax(Boss/精英可调大)。
/// 死亡爆炸特效走 EnemyDeathVFX 的槽位(独立于本组件)。
/// </summary>
public class EnemyEnergyOrbDrop : MonoBehaviour
{
    [HideInInspector] [SerializeField] private float totalMin = 30f;   // [SO 唯一] 只作兜底，面板已隐藏
    [HideInInspector] [SerializeField] private float totalMax = 50f;   // [SO 唯一] 只作兜底，面板已隐藏

    [Tooltip("能量球 Prefab 资产(根节点挂 EnergyOrb 组件;须引用 Prefab 资产而非场景物体)")]
    [SerializeField] private EnergyOrb orbPrefab;

    private EnemyControllerBase _owner;

    private void Awake()
    {
        _owner = GetComponent<EnemyControllerBase>();
    }

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
        // 只处理自己(同一死亡事件所有掉落组件都会收到)
        if (e.enemy == null || e.enemy.gameObject != gameObject)
            return;

        if (orbPrefab == null)
            return;

        // 固定掉落 1 枚,单枚携带全部进度总值
        // [SO 唯一锚点 2026-09-22] 掉落总量实时取 SO 的 Lv 档,取不到才用隐藏字段兜底
        var lv = _owner != null ? _owner.LvStats : null;
        float min = lv != null && lv.energyOrbTotalMin > 0f ? lv.energyOrbTotalMin : (totalMin > 0f ? totalMin : 30f);
        float max = lv != null && lv.energyOrbTotalMax > 0f ? lv.energyOrbTotalMax : (totalMax > 0f ? totalMax : 50f);
        int total = Mathf.RoundToInt(Random.Range(min, max));
        if (total <= 0)
            return;

        EnergyOrb.Spawn(orbPrefab, total, e.position);
    }
}
