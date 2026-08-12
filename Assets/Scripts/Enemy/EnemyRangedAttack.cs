using UnityEngine;

/// <summary>
/// 远程攻击组件 — 挂在敌人身上，通过 IEnemyAttack 接口被 EnemyController 调用
/// 矩形检测距离 + 发射 EnemyProjectile 子弹
/// </summary>
public class EnemyRangedAttack : MonoBehaviour, IEnemyAttack
{
    [Header("数值配置 SO")]
    [Tooltip("敌人数值配置 ScriptableObject（为空时使用下方序列化字段值，保持旧行为）")]
    [SerializeField] protected EnemyConfigSO config;

    [Header("攻击参数")]
    [SerializeField] private float damage = 0f;   // 0 = 未设置，SO Lv 档 / 内置兜底
    private float attackWidth = 10f;
    private float attackHeight = 3f;
    private float retreatWidth = 3f;
    private float retreatHeight = 3f;

    [Header("子弹")]
    [Tooltip("子弹飞行速度（0 = 未设置，SO Lv 档 / 内置兜底）")]
    [SerializeField] private float bulletSpeed = 0f;
    [SerializeField] private float bulletRadius = 0f;
    [SerializeField] private Color bulletColor = Color.red;
    [SerializeField] private LayerMask hitLayers;

    /// <summary>管线消费方缓存（enemy prefab 挂 StatModifierManager 后生效）</summary>
    private StatModifierManager statModManager;
    private EnemyControllerBase _owner;

    private void Awake()
    {
        // [Lv 收敛] 从 controller 的 LvStats 档取值（组件自身 config 字段保留防序列化丢失，不再参与取值）
        _owner = GetComponent<EnemyControllerBase>();
        if (_owner?.LvStats != null)
        {
            if (damage <= 0f && _owner.LvStats.rangedDamage > 0f) damage = _owner.LvStats.rangedDamage;
            if (bulletSpeed <= 0f && _owner.LvStats.bulletSpeed > 0f) bulletSpeed = _owner.LvStats.bulletSpeed;
            if (bulletRadius <= 0f && _owner.LvStats.bulletRadius > 0f) bulletRadius = _owner.LvStats.bulletRadius;
            if (damage <= 0f) damage = 1f;
            if (bulletSpeed <= 0f) bulletSpeed = 6f;
            if (bulletRadius <= 0f) bulletRadius = 0.5f;
        }

        statModManager = GetComponent<StatModifierManager>();

        var controller = GetComponent<EnemyRangedController>();
        if (controller != null)
        {
            attackWidth = controller.AttackWidth;
            attackHeight = controller.AttackHeight;
            retreatWidth = controller.RetreatWidth;
            retreatHeight = controller.RetreatHeight;
        }
    }

    public void PerformAttack(EnemyControllerBase owner)
    {
        EnemyRangedController rangedOwner = owner as EnemyRangedController;
        if (rangedOwner == null)
        {
            Debug.LogWarning($"[{owner.name}] 远程攻击跳过：不是EnemyRangedController");
            return;
        }

        PlayerController pc = PlayerController.Instance;
        if (pc == null)
        {
            Debug.LogWarning($"[{owner.name}] 远程攻击跳过：找不到Player");
            return;
        }

        // P2b-2: 伤害终值走管线（无 manager 回退 baseValue，对齐 PlayerCombat.GetEffectiveDamage 写法）
        float finalDamage = statModManager != null ? statModManager.GetFinalValue(damage, StatId.EnemyDamage) : damage;

        // 2D 方向（含 Y，支持斜上/斜下射击）
        Vector2 dir = ((Vector2)(pc.transform.position - owner.transform.position)).normalized;

        Vector2 spawnPos = (Vector2)owner.transform.position + dir * 0.8f + Vector2.up * 0.5f;

        // 初始化 hitLayers（默认 Player 层）
        if (hitLayers == 0)
            hitLayers = LayerMask.GetMask("Player");

        EnemyProjectile.Spawn(
            position: spawnPos,
            direction: dir,
            damage: finalDamage,
            speed: bulletSpeed,
            hitLayers: hitLayers,
            radius: bulletRadius,
            color: bulletColor,
            parent: null,
            wallLayers: (1 << 3) | (1 << 11),  // Ground(3) + Wall(11)
            sourceLayer: 1 << owner.gameObject.layer,
            source: owner // P1a:携带发射者，命中玩家时作为 DamageInfo.source 触发弹反等结算
        );

        // Debug.Log($"[{owner.name}] 远程攻击player！发射子弹 伤害={damage}");
        // Debug.Log($"[DEBUG] RangedAttack Spawn参数: spawnPos={spawnPos}, dir={dir}, bulletRadius={bulletRadius}, color={bulletColor}, speed={bulletSpeed}");

        owner.FlashColor(Color.white, 0.08f);
    }
}
