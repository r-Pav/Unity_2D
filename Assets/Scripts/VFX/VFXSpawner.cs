using UnityEngine;

/// <summary>
/// VFX 生成静态工具类 — 统一管理 VFX Instantiate，按分类挂到对应容器节点下。
/// 容器自动查找/创建，Transform 缓存避免重复 Find。
/// </summary>
public static class VFXSpawner
{
    // ============================================================
    // 容器 Transform 缓存
    // ============================================================

    private static Transform playerVFXContainer;
    private static Transform enemyVFXContainer;
    private static Transform bossVFXContainer;
    private static Transform worldVFXContainer;

    // ============================================================
    // 核心生成方法
    // ============================================================

    /// <summary>
    /// 按分类在对应容器下 Instantiate 一个 VFX prefab。
    /// prefab 为 null 时静默返回 null。
    /// </summary>
    public static GameObject Spawn(VFXCategory category, GameObject prefab, Vector2 position, Quaternion rotation)
    {
        if (prefab == null) return null;

        Transform container = GetOrCreateContainer(category);
        GameObject instance = Object.Instantiate(prefab, position, rotation, container);

        // 自动挂载自毁脚本（检测 Animator/ParticleSystem 时长，到时 Destroy）
        if (!instance.TryGetComponent<VFXAutoDestruct>(out _))
            instance.AddComponent<VFXAutoDestruct>();

        return instance;
    }

    // ============================================================
    // 便捷方法 — 无需指定 category
    // ============================================================

    /// <summary>在 PlayerVFX 容器下生成 VFX</summary>
    public static GameObject SpawnOnPlayer(GameObject prefab, Vector2 position)
        => Spawn(VFXCategory.PlayerVFX, prefab, position, Quaternion.identity);

    /// <summary>在 EnemyVFX 容器下生成 VFX</summary>
    public static GameObject SpawnOnEnemy(GameObject prefab, Vector2 position)
        => Spawn(VFXCategory.EnemyVFX, prefab, position, Quaternion.identity);

    /// <summary>在 BossVFX 容器下生成 VFX</summary>
    public static GameObject SpawnOnBoss(GameObject prefab, Vector2 position)
        => Spawn(VFXCategory.BossVFX, prefab, position, Quaternion.identity);

    /// <summary>在 WorldVFX 容器下生成 VFX</summary>
    public static GameObject SpawnInWorld(GameObject prefab, Vector2 position)
        => Spawn(VFXCategory.WorldVFX, prefab, position, Quaternion.identity);

    // ============================================================
    // 容器管理
    // ============================================================

    /// <summary>获取或创建指定分类的容器 Transform（带缓存）</summary>
    private static Transform GetOrCreateContainer(VFXCategory category)
    {
        switch (category)
        {
            case VFXCategory.PlayerVFX:
                if (playerVFXContainer == null)
                    playerVFXContainer = FindOrCreateContainer("PlayerVFX");
                return playerVFXContainer;

            case VFXCategory.EnemyVFX:
                if (enemyVFXContainer == null)
                    enemyVFXContainer = FindOrCreateContainer("EnemyVFX");
                return enemyVFXContainer;

            case VFXCategory.BossVFX:
                if (bossVFXContainer == null)
                    bossVFXContainer = FindOrCreateContainer("BossVFX");
                return bossVFXContainer;

            case VFXCategory.WorldVFX:
                if (worldVFXContainer == null)
                    worldVFXContainer = FindOrCreateContainer("WorldVFX");
                return worldVFXContainer;

            default:
                return null;
        }
    }

    /// <summary>
    /// 按名称查找容器 GameObject，找不到则自动创建。
    /// 如果场景中存在 "--- VFX ---" 根节点，则将新容器挂到其下。
    /// </summary>
    private static Transform FindOrCreateContainer(string containerName)
    {
        GameObject go = GameObject.Find(containerName);
        if (go != null)
            return go.transform;

        go = new GameObject(containerName);

        // 尝试挂到 "--- VFX ---" 根节点下
        GameObject root = GameObject.Find("--- VFX ---");
        if (root != null)
            go.transform.SetParent(root.transform);

        return go.transform;
    }
}
