using UnityEngine;

/// <summary>
/// 远程攻击组件 — attack2 远程攻击的执行器（蓄力/发射两阶段，由动画事件驱动）：
///   OnCharge()：蓄力帧 — firePoint 位置生成蓄力粒子（chargeVFXPrefab 预留，空跳过）
///   OnFire()：  发射帧 — firePoint 位置 EnemyProjectile.Spawn 子弹 + 发射粒子（fireVFXPrefab 预留，空跳过）
/// 判定（何时 attack1/attack2、攻击框）由 RangedAttackState 负责，本组件只做执行。
/// PerformAttack 保留 IEnemyAttack 接口兼容（Boss RangedWrap 复用），转发到 OnFire() 维持发射能力。
/// </summary>
public class EnemyRangedAttack : MonoBehaviour, IEnemyAttack
{
    [Header("数值配置 SO")]
    [Tooltip("敌人数值配置 ScriptableObject（为空时使用下方序列化字段值，保持旧行为）")]
    [SerializeField] protected EnemyConfigSO config;

    [Header("攻击参数")]
    [HideInInspector] [SerializeField] private float damage = 0f;   // [SO 唯一] 只作兜底，面板已隐藏

    [Header("发射点与特效（粒子预留，允许空）")]
    [Tooltip("attack2 子弹出生点（子物体，saika 手动摆放；空时回退自身位置）")]
    [SerializeField] private Transform firePoint;
    [Tooltip("蓄力 VFX 预制体（粒子预留；空跳过）")]
    [SerializeField] private GameObject chargeVFXPrefab;
    [Tooltip("发射 VFX 预制体（粒子预留；空跳过）")]
    [SerializeField] private GameObject fireVFXPrefab;

    [Header("子弹")]
    [HideInInspector] [SerializeField] private float bulletSpeed = 0f;   // [SO 唯一] 只作兜底
    [HideInInspector] [SerializeField] private float bulletRadius = 0f; // [SO 唯一] 只作兜底
    [SerializeField] private Color bulletColor = Color.red;
    [SerializeField] private LayerMask hitLayers;

    /// <summary>管线消费方缓存（enemy prefab 挂 StatModifierManager 后生效）</summary>
    private StatModifierManager statModManager;
    private EnemyControllerBase _owner;

    private void Awake()
    {
        // [SO 唯一锚点 2026-09-22] 数值不再在这里缓存 —— 统一在 OnFire 里实时取,避免组件 Awake 顺序问题。
        _owner = GetComponent<EnemyControllerBase>();
        statModManager = GetComponent<StatModifierManager>();
    }

    /// <summary>蓄力帧事件 — firePoint 位置生成蓄力粒子（prefab 空跳过）</summary>
    public void OnCharge()
    {
        if (chargeVFXPrefab == null) return;
        SpawnVFX(chargeVFXPrefab);
    }

    /// <summary>发射帧事件 — firePoint 位置生成子弹 + 发射粒子（prefab 空跳过）</summary>
    public void OnFire()
    {
        if (_owner == null) return;

        // B11（阶段 4 嘲讽目标层）：瞄准走 owner.PlayerTarget —— 嘲讽期间远程敌人朝幻象射击
        // （真实玩家兜底，与 _owner.Awake 的 player 缓存一致；PlayerController.Instance 仅作 fallback）
        Transform target = _owner.PlayerTarget;
        if (target == null)
            target = PlayerController.Instance != null ? PlayerController.Instance.transform : null;
        if (target == null)
        {
            Debug.LogWarning($"[{_owner.name}] 远程发射跳过：找不到目标");
            return;
        }

        // [SO 唯一锚点 2026-09-22] 伤害与子弹参数实时取 SO 的 Lv 档,取不到才用隐藏字段兜底。
        float soDamage = _owner.LvStats != null ? _owner.LvStats.rangedDamage : 0f;
        float baseDamage = soDamage > 0f ? soDamage : (damage > 0f ? damage : 1f);
        float soSpeed = _owner.LvStats != null ? _owner.LvStats.bulletSpeed : 0f;
        float useSpeed = soSpeed > 0f ? soSpeed : (bulletSpeed > 0f ? bulletSpeed : 6f);
        float soRadius = _owner.LvStats != null ? _owner.LvStats.bulletRadius : 0f;
        float useRadius = soRadius > 0f ? soRadius : (bulletRadius > 0f ? bulletRadius : 0.5f);

        // P2b-2: 伤害终值走管线（无 manager 回退 baseValue，对齐 PlayerCombat.GetEffectiveDamage 写法）
        float finalDamage = statModManager != null ? statModManager.GetFinalValue(baseDamage, StatId.EnemyDamage) : baseDamage;

        // 2D 方向（含 Y，支持斜上/斜下射击）
        Vector2 dir = ((Vector2)(target.position - _owner.transform.position)).normalized;

        // 初始化 hitLayers（默认 Player 层）
        if (hitLayers == 0)
            hitLayers = LayerMask.GetMask("Player");

        EnemyProjectile.Spawn(
            position: GetSpawnPos(),
            direction: dir,
            damage: finalDamage,
            speed: useSpeed,
            hitLayers: hitLayers,
            radius: useRadius,
            color: bulletColor,
            parent: null,
            wallLayers: (1 << 3) | (1 << 11),  // Ground(3) + Wall(11)
            sourceLayer: 1 << _owner.gameObject.layer,
            source: _owner // P1a:携带发射者，命中玩家时作为 DamageInfo.source 触发弹反等结算
        );

        // 发射粒子（预留，空跳过）
        if (fireVFXPrefab != null)
            SpawnVFX(fireVFXPrefab);

        _owner.FlashColor(Color.white, 0.08f);
    }

    /// <summary>
    /// IEnemyAttack 接口兼容 — 旧调用方（Boss RangedWrap）复用；转发到 OnFire()。
    /// 敌人侧新流程不走此方法（attack2 由动画事件 OnCharge/OnFire 驱动）。
    /// </summary>
    public void PerformAttack(EnemyControllerBase owner)
    {
        if (owner == null) return;
        if (_owner == null) _owner = owner;
        OnFire();
    }

    // ============================================================
    // 私有辅助
    // ============================================================

    /// <summary>子弹出生点：优先 firePoint（子物体手动摆放）；空则回退自身位置（略偏上贴近旧手感）</summary>
    private Vector2 GetSpawnPos()
    {
        if (firePoint != null)
            return firePoint.position;
        return (Vector2)_owner.transform.position + Vector2.up * 0.5f;
    }

    /// <summary>生成 VFX — 统一走 VFXSpawner 入口（容器 / 强制激活 / 逐个 Play / 自动销毁都在入口里做）</summary>
    private void SpawnVFX(GameObject prefab)
    {
        if (prefab == null || _owner == null) return;
        VFXSpawner.Spawn(VFXCategory.EnemyVFX, prefab, GetSpawnPos(), Quaternion.identity);
    }
}
