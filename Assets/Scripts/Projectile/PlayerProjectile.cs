using UnityEngine;

/// <summary>
/// 玩家魔法弹 — 继承 Projectile 基类;静态对象池 + Spawn 工厂(照抄 EnemyProjectile 模式)。
/// 元素继承:发射时由执行器读 ElementModule.CurrentElement 写入 element 实例字段,
/// 命中走基类 TryDealDamage → DamageInfo.element → CombatResolver.Resolve,元素 proc 自动生效(决策 N5/D14)。
/// 必暴:发射端(执行器)把仲裁倍率写入 critMultiplier 实例字段,命中透传 DamageInfo.critMultiplier(决策 D15)。
/// </summary>
public class PlayerProjectile : Projectile
{
    private static ObjectPool<PlayerProjectile> pool;

    private static Transform _container;

    private static Transform Container
    {
        get
        {
            if (_container == null)
            {
                var go = new GameObject("PlayerProjectilePool");
                go.hideFlags = HideFlags.HideInHierarchy;
                _container = go.transform;
            }
            return _container;
        }
    }

    private static ObjectPool<PlayerProjectile> Pool
    {
        get
        {
            if (pool == null)
            {
                pool = new ObjectPool<PlayerProjectile>(
                    factory: () =>
                    {
                        GameObject go = new GameObject("PlayerProjectile");
                        go.transform.SetParent(Container);
                        return go.AddComponent<PlayerProjectile>();
                    },
                    onGet: p => p.OnSpawnFromPool(),
                    onReturn: p => p.OnReturnToPool(),
                    maxSize: 30
                );
            }
            return pool;
        }
    }

    /// <summary>
    /// 玩家魔法弹工厂(照抄 EnemyProjectile.Spawn 模式)。
    /// </summary>
    /// <param name="element">发射时快照的玩家当前元素(决策 N5:伤害实例按触发时刻读取;魔法弹以发射时刻为准)</param>
    /// <param name="critMultiplier">发射端仲裁倍率(0 = 未暴击;treeA_burst5_crit 注入 1.8 / 火 2.0 仲裁胜出)</param>
    public static PlayerProjectile Spawn(Vector2 position, Vector2 direction,
        float damage, float speed, LayerMask hitLayers,
        float radius, Color color, Transform parent = null,
        LayerMask wallLayers = default, LayerMask sourceLayer = default,
        ICombatant source = null,
        ElementType element = ElementType.None,
        float critMultiplier = 0f)
    {
        PlayerProjectile p = Pool.Get();
        if (parent != null) p.transform.SetParent(parent);

        p.transform.position = position;
        p.transform.rotation = Quaternion.identity;
        p.Initialize(direction, damage, speed, hitLayers, sourceLayer);
        p.wallLayers = wallLayers;
        p.SetAppearance(radius, color);
        // 携带发射者(player 侧 ICombatant)，命中结算时作为 DamageInfo.source
        p.SetSource(source);
        // 元素继承:发射时写入实例字段,命中由基类 TryDealDamage 透传
        p.element = element;
        p.critMultiplier = critMultiplier;
        // 攻击类型标签(发射端配置) — 匹配 VFX 变体
        p.attackType = "Projectile";
        // 确保子弹渲染在最前面,不被背景遮挡
        if (p.spriteRenderer != null)
            p.spriteRenderer.sortingOrder = 10;
        p.gameObject.layer = LayerMask.NameToLayer("PlayerBullet");
        return p;
    }

    public override void ReturnToPool()
    {
        Pool.Return(this);
    }

    protected override void OnSpawnFromPool()
    {
        base.OnSpawnFromPool();
        // 外观由 Spawn 通过 SetAppearance 设置;元素/必暴/攻击标签由 Spawn 注入,此处不重复设置
    }

    protected override void OnReturnToPool()
    {
        base.OnReturnToPool();
        // 复位实例字段,防旧值污染下一次复用
        element = ElementType.None;
        critMultiplier = 0f;
        attackType = "";
    }
}
