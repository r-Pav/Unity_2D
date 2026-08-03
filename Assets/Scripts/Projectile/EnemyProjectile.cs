using UnityEngine;

/// <summary>
/// 敌人子弹 — 使用通用 ObjectPool<EnemyProjectile>
/// </summary>
public class EnemyProjectile : Projectile
{
    [Header("敌人子弹外观")]
    private Color bulletColor = Color.red;

    private static ObjectPool<EnemyProjectile> pool;

    private static Transform _container;

    private static Transform Container
    {
        get
        {
            if (_container == null)
            {
                var go = new GameObject("EnemyProjectilePool");
                go.hideFlags = HideFlags.HideInHierarchy;
                _container = go.transform;
            }
            return _container;
        }
    }

    private static ObjectPool<EnemyProjectile> Pool
    {
        get
        {
            if (pool == null)
            {
                pool = new ObjectPool<EnemyProjectile>(
                    factory: () =>
                    {
                        GameObject go = new GameObject("EnemyProjectile");
                        go.transform.SetParent(Container);
                        return go.AddComponent<EnemyProjectile>();
                    },
                    onGet: p => p.OnSpawnFromPool(),
                    onReturn: p => p.OnReturnToPool(),
                    maxSize: 30
                );
            }
            return pool;
        }
    }

    public static EnemyProjectile Spawn(Vector2 position, Vector2 direction,
        float damage, float speed, LayerMask hitLayers,
        float radius, Color color, Transform parent = null,
        LayerMask wallLayers = default, LayerMask sourceLayer = default)
    {
        EnemyProjectile p = Pool.Get();
        if (parent != null) p.transform.SetParent(parent);

        p.transform.position = position;
        p.transform.rotation = Quaternion.identity;
        p.Initialize(direction, damage, speed, hitLayers, sourceLayer);
        p.wallLayers = wallLayers;
        p.SetAppearance(radius, color);
        // 确保子弹渲染在最前面，不被背景遮挡
        if (p.spriteRenderer != null)
            p.spriteRenderer.sortingOrder = 10;
        p.gameObject.layer = LayerMask.NameToLayer("EnemyBullet");
        // Debug.Log($"[DEBUG] EnemyProjectile.Spawn完成: pos={position}, dir={direction}, radius={radius}, color={color}, sr不为null={p.spriteRenderer != null}, go.active={p.gameObject.activeSelf}");
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
