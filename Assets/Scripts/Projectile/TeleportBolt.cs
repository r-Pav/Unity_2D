using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 传送弹（技能组阶段 5,树A A-02 线）— 独立于 Projectile 基类（阶段 2 魔法弹）：
/// - 基类 maxLifetime 倒计时不适用：改为持续存在直到被传送使用或手动取消（玩家死亡自动作废）
/// - 沿发射方向飞行：撞墙 / 达到最大距离后悬停（不销毁、不被玩家近战消除）
/// - 命中 enemy 造成伤害（每敌一次，带当前元素 → 火/雷 proc 生效），不因命中而销毁
/// - 暴露 Position 供传送执行器读取；被传送使用后由执行器 Cancel() 回池
///
/// 池化照抄 PlayerProjectile 静态 ObjectPool 模式；挂起标记：
/// 发射时执行器 SetPendingReactivation(skillName,true)，回池时清 false，
/// 使 SkillManager 在 CD 期间放行「二次激活」（再按技能键 = 传送）。
/// </summary>
public class TeleportBolt : MonoBehaviour
{
    private static ObjectPool<TeleportBolt> pool;
    private static Transform _container;

    private static Transform Container
    {
        get
        {
            if (_container == null)
            {
                var go = new GameObject("TeleportBoltPool");
                go.hideFlags = HideFlags.HideInHierarchy;
                _container = go.transform;
            }
            return _container;
        }
    }

    private static ObjectPool<TeleportBolt> Pool
    {
        get
        {
            if (pool == null)
            {
                pool = new ObjectPool<TeleportBolt>(
                    factory: () =>
                    {
                        GameObject go = new GameObject("TeleportBolt");
                        go.transform.SetParent(Container);
                        return go.AddComponent<TeleportBolt>();
                    },
                    onGet: b => b.OnSpawnFromPool(),
                    onReturn: b => b.OnReturnToPool(),
                    maxSize: 8
                );
            }
            return pool;
        }
    }

    /// <summary>
    /// 传送弹工厂。
    /// </summary>
    /// <param name="skillName">所属技能名（TreeA_MagicBolt）— 回池时注销二次激活挂起标记</param>
    public static TeleportBolt Spawn(
        Vector2 position, Vector2 direction,
        float damage, float speed, float maxDistance,
        LayerMask hitLayers, LayerMask wallLayers, LayerMask sourceLayer,
        ICombatant source, ElementType element, float critMultiplier,
        float radius, Color color, string skillName)
    {
        TeleportBolt b = Pool.Get();
        b.transform.position = position;
        b.transform.rotation = Quaternion.identity;
        b.direction = direction.normalized;
        b.damage = damage;
        b.speed = speed;
        b.maxDistance = Mathf.Max(0.1f, maxDistance);
        b.hitLayers = hitLayers;
        b.wallLayers = wallLayers;
        b.sourceLayer = sourceLayer;
        b.source = source;
        b.element = element;
        b.critMultiplier = critMultiplier;
        b.skillName = skillName ?? string.Empty;
        b.origin = position;
        b.stopped = false;
        b.hitEnemies.Clear();
        b.SetAppearance(radius, color);
        b.gameObject.layer = LayerMask.NameToLayer("PlayerBullet");
        return b;
    }

    /// <summary>传送落点（供传送执行器读取）</summary>
    public Vector2 Position => transform.position;

    /// <summary>是否在场（未回池）</summary>
    public bool IsActive => gameObject.activeInHierarchy;

    /// <summary>手动取消（传送使用 / 玩家死亡 / 执行器清理）— 回池并注销挂起标记</summary>
    public void Cancel()
    {
        if (_inPool) return;
        Pool.Return(this);
    }

    // ============================================================
    // 运行时状态
    // ============================================================

    private Vector2 direction;
    private float speed;
    private float maxDistance;
    private float damage;
    private ElementType element;
    private float critMultiplier;
    private ICombatant source;
    private LayerMask hitLayers;
    private LayerMask wallLayers;
    private LayerMask sourceLayer;
    private string skillName;
    private Vector2 origin;
    private bool stopped;
    private bool _inPool;
    private readonly HashSet<EnemyControllerBase> hitEnemies = new();

    // ============================================================
    // 生命周期
    // ============================================================

    private void OnEnable()
    {
        EventBus.Subscribe<PlayerDeathEvent>(OnPlayerDeath);
    }

    private void OnDisable()
    {
        EventBus.Unsubscribe<PlayerDeathEvent>(OnPlayerDeath);
    }

    private void OnPlayerDeath(PlayerDeathEvent e)
    {
        // 玩家死亡：传送弹作废（回池时清挂起标记），防止死亡后 Q 键仍被 CD 放行
        Cancel();
    }

    private void Update()
    {
        if (stopped) return;

        float step = speed * Time.deltaTime;
        float travelled = Vector2.Distance(origin, (Vector2)transform.position);
        if (travelled + step >= maxDistance)
        {
            transform.position = origin + direction * maxDistance; // 悬停
            stopped = true;
            return;
        }
        transform.position += (Vector3)direction * step;
    }

    private void OnTriggerEnter2D(Collider2D other)
    {
        // 悬停后（已传送标记）不再响应任何碰撞
        if (stopped) return;

        int otherLayer = 1 << other.gameObject.layer;

        // ① 排除发射源自身层
        if (sourceLayer != 0 && (sourceLayer & otherLayer) != 0) return;

        // ② 管道回弹:命中 Channel 层 → 反向继续飞（距离从回弹点重新计）,不悬停。
        // 回弹后最终撞普通墙/到 maxDistance 才悬停,瞬移落点在管道另一侧。
        if ((LayerMask.GetMask("Channel") & otherLayer) != 0)
        {
            direction = -direction;
            origin = transform.position;
            return;
        }

        // ③ 墙：停止悬停（不销毁 — 传送标记不能因撞墙消失）
        if (wallLayers != 0 && (wallLayers & otherLayer) != 0)
        {
            stopped = true;
            return;
        }

        // ④ 命中 enemy：每敌一次伤害（带元素，proc 生效），不销毁
        if ((hitLayers & otherLayer) == 0) return;
        EnemyControllerBase enemy = other.GetComponentInParent<EnemyControllerBase>();
        if (enemy == null || !enemy.CanBeDamaged) return;
        if (!hitEnemies.Add(enemy)) return; // 同一 enemy 只结算一次

        CombatResolver.Resolve(source, enemy, new DamageInfo
        {
            amount = damage,
            source = source,
            // 击退源点用发射者位置（与 PlayerProjectile 同款：弹近身时用弹位置会翻转击退方向）
            sourcePosition = source != null ? (Vector2)source.Transform.position : (Vector2)transform.position,
            attackLabel = "TeleportBolt",
            knockback = Knockback.None,
            element = element,                                     // 元素继承（发射时刻快照,决策 N5）
            canTriggerElementProc = element != ElementType.None,   // 有元素才允许 proc
            critMultiplier = critMultiplier                        // 必暴倍率透传（当前无必暴来源,0=未暴击）
        });
    }

    // ============================================================
    // 池化回调
    // ============================================================

    private void OnSpawnFromPool()
    {
        _inPool = false;
    }

    private void OnReturnToPool()
    {
        _inPool = true;
        // 注销二次激活挂起标记（传送使用 / 玩家死亡 / 手动取消统一清）
        SkillExecutorRegistry.SetPendingReactivation(skillName, false);
        // 复位实例字段，防旧值污染下一次复用
        skillName = string.Empty;
        direction = Vector2.zero;
        speed = 0f;
        maxDistance = 0f;
        damage = 0f;
        element = ElementType.None;
        critMultiplier = 0f;
        source = null;
        hitLayers = 0;
        wallLayers = 0;
        sourceLayer = 0;
        stopped = false;
        hitEnemies.Clear();
    }

    // ============================================================
    // 外观（照抄 Projectile.SetAppearance 的纯色圆）
    // ============================================================

    private SpriteRenderer spriteRenderer;

    private void Awake()
    {
        CircleCollider2D col = GetComponent<CircleCollider2D>();
        if (col == null)
            col = gameObject.AddComponent<CircleCollider2D>();
        col.isTrigger = true;
        col.radius = 0.5f;

        Rigidbody2D rb = GetComponent<Rigidbody2D>();
        if (rb == null)
            rb = gameObject.AddComponent<Rigidbody2D>();
        rb.isKinematic = true;
        rb.gravityScale = 0f;

        spriteRenderer = GetComponent<SpriteRenderer>();
        if (spriteRenderer == null)
            spriteRenderer = gameObject.AddComponent<SpriteRenderer>();

        // 创建纯色圆形 Sprite（同 Projectile 基类做法）
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
        spriteRenderer.sortingOrder = 10; // 与魔法弹一致：不被背景遮挡
    }

    private void SetAppearance(float radius, Color color)
    {
        transform.localScale = Vector3.one * (radius * 2f);
        if (spriteRenderer != null)
            spriteRenderer.color = color;
    }
}
