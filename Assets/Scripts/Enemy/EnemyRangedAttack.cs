using UnityEngine;

/// <summary>
/// 远程攻击组件 — 挂在敌人身上，通过 IEnemyAttack 接口被 EnemyController 调用
/// 矩形检测距离 + 发射 EnemyProjectile 子弹
/// </summary>
public class EnemyRangedAttack : MonoBehaviour, IEnemyAttack
{
    [Header("攻击参数")]
    [SerializeField] private float damage = 1f;
    private float attackWidth = 10f;
    private float attackHeight = 3f;
    private float retreatWidth = 3f;
    private float retreatHeight = 3f;

    [Header("子弹")]
    [Tooltip("子弹飞行速度")]
    [SerializeField] private float bulletSpeed = 6f;
    [SerializeField] private float bulletRadius = 0.5f;
    [SerializeField] private Color bulletColor = Color.red;
    [SerializeField] private LayerMask hitLayers;

    private void Awake()
    {
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

        // 2D 方向（含 Y，支持斜上/斜下射击）
        Vector2 dir = ((Vector2)(pc.transform.position - owner.transform.position)).normalized;

        Vector2 spawnPos = (Vector2)owner.transform.position + dir * 0.8f + Vector2.up * 0.5f;

        // 初始化 hitLayers（默认 Player 层）
        if (hitLayers == 0)
            hitLayers = LayerMask.GetMask("Player");

        EnemyProjectile.Spawn(
            position: spawnPos,
            direction: dir,
            damage: damage,
            speed: bulletSpeed,
            hitLayers: hitLayers,
            radius: bulletRadius,
            color: bulletColor,
            parent: null,
            wallLayers: (1 << 3) | (1 << 11),  // Ground(3) + Wall(11)
            sourceLayer: 1 << owner.gameObject.layer
        );

        // Debug.Log($"[{owner.name}] 远程攻击player！发射子弹 伤害={damage}");
        // Debug.Log($"[DEBUG] RangedAttack Spawn参数: spawnPos={spawnPos}, dir={dir}, bulletRadius={bulletRadius}, color={bulletColor}, speed={bulletSpeed}");

        owner.FlashColor(Color.white, 0.08f);
    }
}
