using UnityEngine;

/// <summary>
/// 弹药抽象基类 — 提供 Sphere 外观、移动、碰撞检测、寿命倒计时等公共行为。
/// 对象池和 Spawn 工厂由子类（PlayerProjectile / EnemyProjectile）各自实现。
/// 命中时触发 EventBus.Trigger<ProjectileHitEvent> 供其他模块响应。
/// </summary>
public abstract class Projectile : MonoBehaviour
{
    // ============================================================
    // 配置参数（子类可直接读写）
    // ============================================================

    protected float speed = 10f;
    protected float maxLifetime = 3f;
    protected float damage = 1f;
    protected LayerMask hitLayers = ~0;
    protected LayerMask sourceLayer = 0;
    protected LayerMask wallLayers = 0;
    protected bool piercing = false;
    protected float sphereRadius = 0.15f;
    protected Color sphereColor = Color.cyan;

    /// <summary>攻击类型标签 — 传给 Enemy TakeDamageFrom 用于匹配 VFX 变体</summary>
    protected string attackType = "";

    /// <summary>是否可被近战攻击消除 (玩家近战 OverlapBox 额外检测)</summary>
    protected bool canBeDestroyedByMelee = true;
    /// <summary>是否可被近战攻击消除 (公开只读)</summary>
    public bool CanBeDestroyedByMelee => canBeDestroyedByMelee;

    // ============================================================
    // 运行时状态
    // ============================================================

    private Vector2 direction;          // 飞行方向（单位向量）
    private float lifetimeTimer;        // 存活倒计时
    private CircleCollider2D col;         // 碰撞体引用
    protected SpriteRenderer spriteRenderer;  // 渲染器引用（子类可访问以设置 sorting layer）

    // ============================================================
    // 初始化
    // ============================================================

    /// <summary>
    /// 初始化子弹逻辑参数。由子类 Spawn 内部调用，外部也可直接调用。
    /// </summary>
    public void Initialize(Vector2 dir, float dmg, float spd, LayerMask hitLayers, LayerMask sourceLayer = default)
    {
        direction = dir.normalized;
        damage = dmg;
        speed = spd;
        this.hitLayers = hitLayers;
        this.sourceLayer = sourceLayer;
        lifetimeTimer = maxLifetime;
    }

    // ============================================================
    // 生命周期
    // ============================================================

    private void Awake()
    {
        // 1. 确保有 CircleCollider2D
        col = GetComponent<CircleCollider2D>();
        if (col == null)
            col = gameObject.AddComponent<CircleCollider2D>();
        col.isTrigger = true;
        col.radius = 0.5f;

        // 2. 确保有 Rigidbody2D（触发 Trigger 需要）
        Rigidbody2D rb = GetComponent<Rigidbody2D>();
        if (rb == null)
            rb = gameObject.AddComponent<Rigidbody2D>();
        rb.isKinematic = true;
        rb.gravityScale = 0f;

        // 3. 创建 SpriteRenderer（2D 圆形纹理）
        spriteRenderer = GetComponent<SpriteRenderer>();
        if (spriteRenderer == null)
            spriteRenderer = gameObject.AddComponent<SpriteRenderer>();
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
        spriteRenderer.color = sphereColor;
    }

    private void Update()
    {
        // 移动
        transform.position += (Vector3)direction * speed * Time.deltaTime;

        // 寿命倒计时
        lifetimeTimer -= Time.deltaTime;
        if (lifetimeTimer <= 0f)
            Expire();
    }

    private void OnTriggerEnter2D(Collider2D other)
    {
        int otherLayer = 1 << other.gameObject.layer;

        // ① 排除 sourceLayer
        if (IsSelfLayer(otherLayer)) return;

        // ② 墙检测（优先于 hitLayers，防止子弹穿过未在 hitLayers 中的墙）
        if (HandleWallCollision(otherLayer)) return;

        // ③ 检查是否在 hitLayers 中
        if ((hitLayers & otherLayer) == 0) return;

        // ④-⑥ 命中处理 + 回池（finally 保证即使伤害/事件异常也回池）
        try
        {
            TryDealDamage(other);

            EventBus.Trigger(new ProjectileHitEvent(
                target: other.gameObject,
                damage: damage,
                hitPoint: (Vector2)transform.position,
                source: gameObject
            ));
        }
        finally
        {
            if (!piercing)
                ReturnToPool();
        }
    }

    /// <summary>是否属于发射源的自身图层</summary>
    private bool IsSelfLayer(int otherLayer) => (sourceLayer & otherLayer) != 0;

    /// <summary>墙碰撞处理。返回true表示子弹已销毁。</summary>
    private bool HandleWallCollision(int otherLayer)
    {
        if (wallLayers != 0 && (wallLayers & otherLayer) != 0)
        {
            ReturnToPool();
            return true;
        }
        return false;
    }

    /// <summary>尝试对命中目标造成伤害（玩家子弹打敌人，敌人子弹打玩家）</summary>
    private void TryDealDamage(Collider2D other)
    {
        if (other.TryGetComponent(out EnemyControllerBase enemy))
            enemy.TakeDamageFrom(damage, transform.position, attackType);
        else if (other.TryGetComponent(out PlayerController player))
        {
            Vector2 knockDir = ((Vector2)(player.transform.position - transform.position)).normalized;
            player.TakeDamageWithKnockback(damage, knockDir);
        }
    }

    // ============================================================
    // 池化虚方法（子类可重写）
    // ============================================================

    protected virtual void OnSpawnFromPool() { }
    protected virtual void OnReturnToPool() { }

    // ============================================================
    // 抽象方法 — 子类必须实现
    // ============================================================

    public abstract void ReturnToPool();

    // ============================================================
    // 外观方法
    // ============================================================

    protected void SetAppearance(float radius, Color color)
    {
        sphereRadius = radius;
        sphereColor = color;

        float diameter = radius * 2f;
        transform.localScale = Vector3.one * diameter;

        if (col != null)
            col.radius = 0.5f;

        if (spriteRenderer != null)
            spriteRenderer.color = color;
    }

    // ============================================================
    // 销毁
    // ============================================================

    private void Expire()
    {
        ReturnToPool();
    }
}
