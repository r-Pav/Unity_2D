using UnityEngine;

/// <summary>
/// 踩头机制 — 玩家空中下落时踩到敌人头部触发
/// 效果：敌人硬直、玩家斜向弹跳、重置空中跳跃
/// </summary>
public class PlayerStomp : MonoBehaviour
{
    [Header("检测")]
    [SerializeField] private LayerMask enemyLayer = 1 << 7;
    [SerializeField] private float footAreaHeight = 0.15f;
    [SerializeField] private float footWidthMultiplier = 0.8f;
    [SerializeField] private float headAreaRatio = 0.25f;

    [Header("弹跳")]
    [SerializeField] private float bounceForceX = 7f;
    [SerializeField] private float bounceForceY = 14f;

    [Header("冷却")]
    [SerializeField] private float stompCooldown = 1f;

    private PlayerController owner;
    private float cooldownTimer;

    public void OnPlayerUpdate(PlayerController pc)
    {
        owner = pc;

        if (cooldownTimer > 0f) cooldownTimer -= Time.deltaTime;
        if (cooldownTimer > 0f) return;

        Rigidbody2D rb = owner.GetRigidbody();
        Collider2D col = owner.Col;
        if (rb == null || col == null) return;

        if (rb.velocity.y > 0f) return;
        if (owner.IsGrounded()) return;

        TryStomp(col, rb);
    }

    private void TryStomp(Collider2D playerCol, Rigidbody2D rb)
    {
        Bounds bounds = playerCol.bounds;
        var (footMin, footMax, footWidth) = BuildFootArea(bounds);

        Collider2D[] hits = Physics2D.OverlapAreaAll(footMin, footMax, enemyLayer);
        if (hits.Length == 0) return;

        EnemyControllerBase nearestEnemy = FilterValidStompTarget(hits, playerCol, bounds, footWidth);
        if (nearestEnemy == null) return;
        ExecuteStomp(nearestEnemy, rb);
    }

    private (Vector2 footMin, Vector2 footMax, float footWidth) BuildFootArea(Bounds bounds)
    {
        float footWidth = bounds.size.x * footWidthMultiplier;
        Vector2 footCenter = new Vector2(bounds.center.x, bounds.min.y + footAreaHeight * 0.5f);
        Vector2 footSize = new Vector2(footWidth, footAreaHeight);
        return (footCenter - footSize * 0.5f, footCenter + footSize * 0.5f, footWidth);
    }

    private EnemyControllerBase FilterValidStompTarget(Collider2D[] hits, Collider2D playerCol,
        Bounds bounds, float footWidth)
    {
        EnemyControllerBase nearest = null;
        float nearestDist = float.MaxValue;

        foreach (Collider2D hit in hits)
        {
            if (hit == null || hit == playerCol) continue;
            if (!hit.TryGetComponent<EnemyControllerBase>(out var enemy)) continue;

            Bounds enemyBounds = hit.bounds;
            float headTop = enemyBounds.max.y;
            float headBottom = enemyBounds.max.y - enemyBounds.size.y * headAreaRatio;

            float playerFootY = bounds.min.y;
            if (playerFootY < headBottom || playerFootY > headTop) continue;

            float playerLeft = bounds.center.x - footWidth * 0.5f;
            float playerRight = bounds.center.x + footWidth * 0.5f;
            if (playerRight < enemyBounds.min.x || playerLeft > enemyBounds.max.x) continue;

            float dist = Vector2.Distance(bounds.center, enemyBounds.center);
            if (dist < nearestDist)
            {
                nearestDist = dist;
                nearest = enemy;
            }
        }
        return nearest;
    }

    private void ExecuteStomp(EnemyControllerBase enemy, Rigidbody2D rb)
    {
        enemy.EnterStunState();

        float dirX = owner.transform.position.x > enemy.transform.position.x ? 1f : -1f;
        rb.velocity = new Vector2(dirX * bounceForceX, bounceForceY);

        PlayerJump jump = GetComponent<PlayerJump>();
        jump?.ResetJumps();

        cooldownTimer = stompCooldown;
    }
}
