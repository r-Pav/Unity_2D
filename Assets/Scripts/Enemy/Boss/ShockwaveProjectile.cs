using UnityEngine;

// ============================================================
// ShockwaveProjectile — 地面波投射物（由 BossSkillSlots 实例化）
// ============================================================

/// <summary>
/// 地面波投射物组件，沿指定方向水平传播，触碰 Player 后造成伤害 + 击退并销毁。
/// 达到最大传播距离后自动销毁。
/// </summary>
[RequireComponent(typeof(Collider2D))]
public class ShockwaveProjectile : MonoBehaviour
{
    // ── VFX ──
    [Header("VFX")]
    [Tooltip("发射起始 VFX — Initialize 时生成一次")]
    [SerializeField] private GameObject startVFXPrefab;
    [Tooltip("飞行拖尾 VFX — 飞行中低频生成（约每 0.1s）")]
    [SerializeField] private GameObject trailVFXPrefab;
    [Tooltip("命中/销毁 VFX — 击中玩家或到达最大距离时生成")]
    [SerializeField] private GameObject hitVFXPrefab;

    // ── 初始化参数 ──
    private Vector2 direction;
    private float speed;
    private float maxDistance;
    private float height;
    private float damage;
    private float knockbackForce;
    private float traveled;
    private bool hit;

    // ── 拖尾计时 ──
    private float trailTimer;
    private const float TrailInterval = 0.08f;

    /// <summary>
    /// 由 BossSkillSlots.ExecuteShockwave 调用，设置子弹参数。
    /// </summary>
    public void Initialize(Vector2 dir, float spd, float maxDist, float h,
                           float dmg, float knockback)
    {
        direction = dir.normalized;
        speed = spd;
        maxDistance = maxDist;
        height = h;
        damage = dmg;
        knockbackForce = knockback;
        traveled = 0f;
        hit = false;
        trailTimer = 0f;

        // 发射起始 VFX
        if (startVFXPrefab != null)
            VFXSpawner.SpawnInWorld(startVFXPrefab, transform.position);
    }

    private void Update()
    {
        if (hit) return;

        float step = speed * Time.deltaTime;
        Vector3 move = (Vector3)(direction * step);
        transform.position += move;
        traveled += step;

        // 飞行拖尾 VFX — 低频生成
        if (trailVFXPrefab != null)
        {
            trailTimer += Time.deltaTime;
            if (trailTimer >= TrailInterval)
            {
                trailTimer = 0f;
                VFXSpawner.SpawnInWorld(trailVFXPrefab, transform.position);
            }
        }

        if (traveled >= maxDistance)
        {
            // 到达最大距离：命中 VFX
            if (hitVFXPrefab != null)
                VFXSpawner.SpawnInWorld(hitVFXPrefab, transform.position);
            Destroy(gameObject);
        }
    }

    private void OnTriggerEnter2D(Collider2D other)
    {
        if (hit) return;
        if (!other.CompareTag("Player")) return;

        hit = true;

        // 命中 VFX
        if (hitVFXPrefab != null)
            VFXSpawner.SpawnInWorld(hitVFXPrefab, transform.position);

        PlayerController player = other.GetComponent<PlayerController>();
        if (player != null)
        {
            Vector2 knockDir = direction;
            knockDir.y = 0f; // 地面波水平击退
            if (knockDir.magnitude < 0.01f) knockDir = Vector2.right;
            player.TakeDamageWithKnockback(damage, knockDir);
        }

        Destroy(gameObject);
    }

#if UNITY_EDITOR
    private void OnDrawGizmosSelected()
    {
        if (traveled > 0) return;

        Gizmos.color = new Color(0.3f, 0.5f, 1f, 0.4f);
        Gizmos.DrawWireSphere(transform.position, 0.3f);

        if (maxDistance > 0)
        {
            Vector3 end = transform.position + (Vector3)(direction * maxDistance);
            Gizmos.DrawLine(transform.position, end);
            Gizmos.DrawWireSphere(end, 0.2f);
        }
    }
#endif
}
