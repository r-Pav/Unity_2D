using UnityEngine;

/// <summary>
/// 玩家子弹 — 使用通用 ObjectPool<PlayerProjectile>
/// </summary>
public class PlayerProjectile : Projectile
{
    [Header("玩家子弹外观")]
    private Color bulletColor = Color.cyan;

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
                    maxSize: 50
                );
            }
            return pool;
        }
    }

    public static PlayerProjectile Spawn(Vector2 position, Vector2 direction,
        float damage, float speed, LayerMask hitLayers,
        float radius, Color color, Transform parent = null,
        LayerMask wallLayers = default, LayerMask sourceLayer = default,
        string attackType = "Bullet")
    {
        PlayerProjectile p = Pool.Get();
        if (parent != null) p.transform.SetParent(parent);

        p.transform.position = position;
        p.transform.rotation = Quaternion.identity;
        p.Initialize(direction, damage, speed, hitLayers, sourceLayer);
        p.wallLayers = wallLayers;
        p.attackType = attackType;
        p.SetAppearance(radius, color);
        return p;
    }

    public override void ReturnToPool()
    {
        Pool.Return(this);
    }

    protected override void OnSpawnFromPool()
    {
        base.OnSpawnFromPool();
        // 外观由 Spawn 通过 SetAppearance 设置，此处不再重复调用
    }
}
