// ============================================================
// [废弃 2026-08-21] 障碍球实体 — 旧 Q 技能(障碍球)的一部分,随 BarrierSkill 一起废弃
//   只被 BarrierSkill.cs 引用(已注释);场景/资产零实例
//   相关废弃资产:Prefab/ObstacleBall.prefab(可在编辑器删除)
// ============================================================
/*
using UnityEngine;

/// <summary>
/// 障碍球行为 — 飞行 → 碰撞停下 → 成为静态障碍物阻挡敌人
/// 
/// 碰撞逻辑：
/// - 碰到 Ground 层（墙/地面）→ 停在碰撞点
/// - 碰到 Enemy 层 → 停在敌人身上（阻挡敌人移动）
/// - 碰撞后 Rigidbody 变为 isKinematic，成为物理障碍
/// 
/// 编辑器操作：
/// 1. Layer 设置：Edit → Project Settings → Tags and Layers
///    添加 Obstacle 层（如 User Layer 8）
/// 2. Physics 设置：Edit → Project Settings → Physics
///    Layer Collision Matrix 中：
///    - Obstacle × Ground   = ✅（阻挡墙壁）
///    - Obstacle × Enemy    = ✅（阻挡敌人）
///    - Obstacle × Player   = ❌（玩家可穿过）
///    - Obstacle × Obstacle = ✅（可选，球之间碰撞）
/// 3. 敌人子弹拦截：EnemyRangedAttack 的 hitLayers 包含 Obstacle 层
///    使敌人子弹命中障碍球后消失
/// </summary>
[RequireComponent(typeof(Rigidbody2D), typeof(CircleCollider2D))]
public class ObstacleBall : MonoBehaviour
{
    // ============================================================
    // 配置参数
    // ============================================================

    [Header("外观")]
    [SerializeField] private float radius = 0.5f;
    [SerializeField] private Color ballColor = new Color(0.2f, 0.6f, 1f, 0.7f);

    [Header("碰撞层")]
    [Tooltip("碰到这些层会停下（Ground + Enemy）")]
    [SerializeField] private LayerMask stopLayers = -1;  // 默认全部，Awake 中自动设置

    // ============================================================
    // 运行时状态
    // ============================================================

    private Vector2 direction;
    private float speed;
    private float maxDistance;
    private float knockbackForce;
    private Vector2 spawnPosition;
    private bool stopped;
    private Rigidbody2D rb;
    private SpriteRenderer spriteRenderer;

    // ============================================================
    // 初始化
    // ============================================================

    void Awake()
    {
        // ── Rigidbody2D：非触发器、无重力、冻结旋转 ──
        rb = GetComponent<Rigidbody2D>();
        rb.isKinematic = false;
        rb.gravityScale = 0f;
        rb.constraints = RigidbodyConstraints2D.FreezeRotation;

        // ── CircleCollider2D：非触发器，物理碰撞阻挡 ──
        CircleCollider2D col = GetComponent<CircleCollider2D>();
        col.radius = radius;
        col.isTrigger = false;

        // ── 自动设置碰撞层（Ground=3, Enemy=7）──
        if (stopLayers == -1)
        {
            // 用名称查找 Layer，更健壮
            int groundLayer = LayerMask.NameToLayer("Ground");
            int enemyLayer = LayerMask.NameToLayer("Enemy");
            stopLayers = 0;
            if (groundLayer >= 0) stopLayers |= 1 << groundLayer;
            if (enemyLayer >= 0) stopLayers |= 1 << enemyLayer;

            // 兼容默认 Layer 编号（名称查找失败时）
            if (stopLayers == 0)
            {
                stopLayers = (1 << 3) | (1 << 7);
            }
        }

        // ── 创建球体外观（子对象，使用 SpriteRenderer）──
        GameObject visual = new GameObject("Visual");
        visual.transform.SetParent(transform);
        visual.transform.localPosition = Vector3.zero;
        visual.transform.localScale = Vector3.one * (radius * 2f);
        spriteRenderer = visual.AddComponent<SpriteRenderer>();
        // 创建纯色圆形 Sprite
        int size = 64;
        Texture2D tex = new Texture2D(size, size);
        Color[] pixels = new Color[size * size];
        float center = (size - 1) / 2f;
        float texRadius = center;
        for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
                pixels[y * size + x] = (Vector2.Distance(new Vector2(x, y), Vector2.one * center) <= texRadius)
                    ? Color.white : Color.clear;
        tex.SetPixels(pixels);
        tex.Apply();
        spriteRenderer.sprite = Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), size);
        spriteRenderer.color = ballColor;
    }

    // ============================================================
    // 发射
    // ============================================================

    /// <summary>由 BarrierSkill 调用：设定飞行方向和参数</summary>
    public void Launch(Vector2 dir, float spd, float maxDist, float knockback)
    {
        direction = dir.normalized;
        speed = spd;
        maxDistance = maxDist;
        knockbackForce = knockback;
        spawnPosition = (Vector2)transform.position;
    }

    // ============================================================
    // 每帧更新
    // ============================================================

    void Update()
    {
        if (stopped) return;

        // 最大距离检查
        if (Vector2.Distance(transform.position, spawnPosition) >= maxDistance)
        {
            Stop();
            return;
        }

        // 飞行移动 — 用 Rigidbody 移动，避免与物理引擎打架导致抖动
        rb.MovePosition(rb.position + direction * speed * Time.deltaTime);
    }

    // ============================================================
    // 碰撞检测（非触发器 → OnCollisionEnter）
    // ============================================================

    void OnCollisionEnter2D(Collision2D collision)
    {
        if (stopped) return;

        int layer = collision.gameObject.layer;
        if ((stopLayers & (1 << layer)) == 0) return;

        // 碰到敌人 → 击退
        if (layer == LayerMask.NameToLayer("Enemy") || layer == 7)
        {
            Rigidbody2D enemyRb = collision.gameObject.GetComponent<Rigidbody2D>();
            if (enemyRb != null)
            {
                Vector2 knockDir = ((Vector2)(collision.transform.position - transform.position)).normalized;
                enemyRb.AddForce(knockDir * knockbackForce, ForceMode2D.Impulse);
            }
        }

        Stop();
    }

    // ============================================================
    // 停下逻辑
    // ============================================================

    void Stop()
    {
        if (stopped) return;
        stopped = true;

        rb.velocity = Vector2.zero;
        rb.isKinematic = true;  // 变为静态障碍物，不再受物理推动

        // Debug.Log("[ObstacleBall] 障碍球停下，变为静态阻挡物");
    }

    // ============================================================
    // 销毁
    // ============================================================

    void OnDestroy()
    {
        // Debug.Log("[ObstacleBall] 障碍球销毁");
    }

    // ============================================================
    // Gizmos
    // ============================================================

    void OnDrawGizmosSelected()
    {
        Gizmos.color = ballColor;
        Gizmos.DrawWireSphere(transform.position, radius);
    }
}
*/
