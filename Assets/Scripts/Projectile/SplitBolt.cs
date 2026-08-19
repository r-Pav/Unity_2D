using UnityEngine;

/// <summary>
/// 分裂弹（技能组阶段 6）— 继承 Projectile 基类（复用基类 TryDealDamage 元素继承/命中管线）。
///
/// 行为（决策 N2）：
///   - 父弹（canSplit=true）：直线飞行，命中 enemy/墙后不再结算伤害，原地分裂生成 splitCount(3) 个子弹并回池。
///   - 子弹（canSplit=false）：先按扇形扩散（spreadDuration 内沿固定方向直线飞行，且忽略命中），
///     随后自动追踪最近的 enemy（FindNearestEnemy + RotateTowards 转向，追踪速度/转向率序列化）。
///     伤害 = 父弹伤害 × subDamageRatio（序列化，如 0.6）；元素继承（element 实例字段透传 DamageInfo）；
///     禁止再分裂；同一目标可被多发命中（不做去重）。
///
/// 基类适配：Projectile 的 OnTriggerEnter2D/Update 已改为 virtual，OnValidHit 为子类重写钩子，
/// 命中判定（sourceLayer 排除 / 墙 / hitLayers）仍由基类完成。
/// </summary>
public class SplitBolt : Projectile
{
    // ============================================================
    // 静态对象池（照抄 PlayerProjectile 模式）
    // ============================================================

    private static ObjectPool<SplitBolt> pool;

    private static Transform _container;

    private static Transform Container
    {
        get
        {
            if (_container == null)
            {
                var go = new GameObject("SplitBoltPool");
                go.hideFlags = HideFlags.HideInHierarchy;
                _container = go.transform;
            }
            return _container;
        }
    }

    private static ObjectPool<SplitBolt> Pool
    {
        get
        {
            if (pool == null)
            {
                pool = new ObjectPool<SplitBolt>(
                    factory: () =>
                    {
                        GameObject go = new GameObject("SplitBolt");
                        go.transform.SetParent(Container);
                        return go.AddComponent<SplitBolt>();
                    },
                    onGet: b => b.OnSpawnFromPool(),
                    onReturn: b => b.OnReturnToPool(),
                    maxSize: 60
                );
            }
            return pool;
        }
    }

    // ============================================================
    // 分裂/追踪参数（实例字段,由 Spawn 工厂注入）
    // ============================================================

    /// <summary>是否可分裂（父弹=true;子弹=false 禁止再分裂）</summary>
    private bool canSplit;

    /// <summary>分裂子弹数量（父弹命中后生成）</summary>
    private int splitCount = 3;

    /// <summary>子弹伤害比例（子弹伤害 = 父弹伤害 × 此值）</summary>
    private float subDamageRatio = 0.6f;

    /// <summary>扇形扩散半角（度;子弹方向 = 父弹方向 ± spreadAngleDeg 均匀分布）</summary>
    private float spreadAngleDeg = 25f;

    /// <summary>扩散持续时长（秒;期间子弹直线飞行且不结算命中）</summary>
    private float spreadDuration = 0.2f;

    /// <summary>是否启用自动追踪（子弹=true;父弹=false 纯直线）</summary>
    private bool homingEnabled;

    /// <summary>追踪转向率（弧度/秒）</summary>
    private float homingTurnRate = 8f;

    /// <summary>索敌半径（追踪最近 enemy 的检测范围）</summary>
    private float homingRange = 20f;

    /// <summary>自生成起经过的时间（扩散期计时用）</summary>
    private float flightTimer;

    /// <summary>是否已进入追踪阶段（扩散结束瞬间做一次性重叠结算,防"生成在敌人体内永远不触发 Enter"）</summary>
    private bool homingStarted;

    // ============================================================
    // 工厂
    // ============================================================

    /// <summary>
    /// 分裂弹工厂（父弹与子弹共用；canSplit 区分行为）。
    /// 参数对齐 PlayerProjectile.Spawn + 分裂/追踪专用参数。
    /// </summary>
    public static SplitBolt Spawn(
        Vector2 position, Vector2 direction,
        float damage, float speed,
        LayerMask hitLayers, LayerMask wallLayers, LayerMask sourceLayer,
        ICombatant source, ElementType element, float critMultiplier,
        float radius, Color color,
        bool canSplit, int splitCount, float subDamageRatio,
        float spreadAngleDeg, float spreadDuration,
        bool homingEnabled, float homingTurnRate, float homingRange,
        float maxLifetime)
    {
        SplitBolt b = Pool.Get();
        b.transform.position = position;
        b.transform.rotation = Quaternion.identity;
        b.maxLifetime = maxLifetime;          // 先写 maxLifetime,Initialize 内部用其初始化寿命倒计时
        b.Initialize(direction, damage, speed, hitLayers, sourceLayer);
        b.wallLayers = wallLayers;
        b.SetAppearance(radius, color);
        b.SetSource(source);
        b.element = element;                  // 元素继承（发射端快照,决策 N5）
        b.critMultiplier = critMultiplier;
        b.attackType = "SplitBolt";
        b.canSplit = canSplit;
        b.splitCount = Mathf.Max(1, splitCount);
        b.subDamageRatio = subDamageRatio;
        b.spreadAngleDeg = spreadAngleDeg;
        b.spreadDuration = spreadDuration;
        b.homingEnabled = homingEnabled;
        b.homingTurnRate = homingTurnRate;
        b.homingRange = homingRange;
        b.flightTimer = 0f;
        b.homingStarted = false;
        b.gameObject.layer = LayerMask.NameToLayer("PlayerBullet");
        // 渲染层级与魔法弹/传送弹一致（不被背景遮挡）
        if (b.spriteRenderer != null)
            b.spriteRenderer.sortingOrder = 10;
        return b;
    }

    public override void ReturnToPool()
    {
        Pool.Return(this);
    }

    protected override void OnSpawnFromPool()
    {
        base.OnSpawnFromPool();
    }

    protected override void OnReturnToPool()
    {
        base.OnReturnToPool();
        // 复位实例字段,防旧值污染下一次复用
        canSplit = false;
        splitCount = 3;
        subDamageRatio = 0.6f;
        spreadAngleDeg = 25f;
        spreadDuration = 0.2f;
        homingEnabled = false;
        homingTurnRate = 8f;
        homingRange = 20f;
        flightTimer = 0f;
        homingStarted = false;
        element = ElementType.None;
        critMultiplier = 0f;
        attackType = "";
    }

    // ============================================================
    // 移动 / 追踪
    // ============================================================

    protected override void Update()
    {
        // 子弹：先扇形扩散（直线飞行）,随后自动追踪最近 enemy（决策 N2）
        if (homingEnabled)
        {
            flightTimer += Time.deltaTime;
            if (flightTimer >= spreadDuration)
            {
                if (!homingStarted)
                {
                    homingStarted = true;
                    // 扩散结束瞬间：结算已重叠的 enemy（防"生成在敌人体内 → 扩散期忽略 → 永不触发 Enter"）
                    HitTouchingEnemies();
                }
                SteerTowardNearestEnemy();
            }
        }
        base.Update();
    }

    /// <summary>一次性重叠结算：对当前重叠的 enemy 直接走基类伤害（与命中同规则,不去重）</summary>
    private void HitTouchingEnemies()
    {
        Collider2D[] hits = Physics2D.OverlapCircleAll(transform.position, sphereRadius, hitLayers);
        foreach (Collider2D col in hits)
        {
            if (col == null) continue;
            if (col.GetComponentInParent<EnemyControllerBase>() == null) continue;
            TryDealDamage(col);
        }
    }

    /// <summary>自动追踪最近 enemy：把飞行方向逐步转向目标（转向率限制）</summary>
    private void SteerTowardNearestEnemy()
    {
        EnemyControllerBase target = FindNearestEnemy();
        if (target == null) return;

        Vector2 toTarget = ((Vector2)target.transform.position - (Vector2)transform.position).normalized;
        if (toTarget.sqrMagnitude < 0.0001f) return;
        direction = Vector3.RotateTowards(direction, toTarget, homingTurnRate * Time.deltaTime, 1f).normalized;
    }

    /// <summary>查找最近的存活 enemy（检测范围 homingRange,层用 hitLayers 即 Enemy）</summary>
    private EnemyControllerBase FindNearestEnemy()
    {
        Collider2D[] hits = Physics2D.OverlapCircleAll(transform.position, homingRange, hitLayers);
        EnemyControllerBase best = null;
        float bestSqr = float.MaxValue;
        foreach (Collider2D col in hits)
        {
            if (col == null) continue;
            EnemyControllerBase enemy = col.GetComponentInParent<EnemyControllerBase>();
            if (enemy == null || enemy.IsDead || !enemy.CanBeDamaged) continue;
            float sqr = ((Vector2)enemy.transform.position - (Vector2)transform.position).sqrMagnitude;
            if (sqr < bestSqr)
            {
                bestSqr = sqr;
                best = enemy;
            }
        }
        return best;
    }

    // ============================================================
    // 命中处理（重写基类钩子）
    // ============================================================

    protected override bool OnValidHit(Collider2D other)
    {
        // 父弹：命中后分裂成子弹,自身不结算伤害（伤害由子弹携带）
        if (canSplit)
        {
            SpawnSubBolts(transform.position);
            return false; // 走默认流程:触发命中事件 + 回池
        }

        // 子弹：扩散期不结算命中（先扇形扩散,随后追踪再命中）
        if (homingEnabled && flightTimer < spreadDuration)
            return true; // 已消费:不触发事件、不回池,继续飞行

        // 子弹：正常结算伤害（元素继承由基类 TryDealDamage 透传;伤害已按比例烘焙进 damage）
        TryDealDamage(other);
        return false;
    }

    /// <summary>生成 splitCount 个子弹：方向 = 父弹方向 ± 扇形半角均匀分布（决策 N2 先扩散）</summary>
    private void SpawnSubBolts(Vector2 hitPoint)
    {
        float baseAngle = Mathf.Atan2(direction.y, direction.x) * Mathf.Rad2Deg;
        int count = splitCount;

        for (int i = 0; i < count; i++)
        {
            float angle = count == 1
                ? baseAngle
                : baseAngle - spreadAngleDeg + (2f * spreadAngleDeg * i / (count - 1));
            Vector2 dir = new Vector2(Mathf.Cos(angle * Mathf.Deg2Rad), Mathf.Sin(angle * Mathf.Deg2Rad));

            // 生成点沿子弹方向外移一小段,避免与命中目标重叠导致瞬时触发(扩散期也会忽略,双保险)
            Spawn(
                position: hitPoint + dir * 0.3f,
                direction: dir,
                damage: damage * subDamageRatio,      // 子弹伤害比例（序列化）
                speed: speed,
                hitLayers: hitLayers,
                wallLayers: wallLayers,
                sourceLayer: sourceLayer,
                source: source,
                element: element,                     // 元素继承
                critMultiplier: critMultiplier,
                radius: sphereRadius,
                color: sphereColor,
                canSplit: false,                      // 禁止再分裂
                splitCount: splitCount,
                subDamageRatio: subDamageRatio,
                spreadAngleDeg: spreadAngleDeg,
                spreadDuration: spreadDuration,
                homingEnabled: true,                  // 子弹自动追踪
                homingTurnRate: homingTurnRate,
                homingRange: homingRange,
                maxLifetime: maxLifetime
            );
        }
    }
}
