using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 幻象管理器（阶段 4）— 全局计数 + 统一销毁。
/// 静态 Instance（挂 Player 或场景任意对象；执行器可通过 EnsureInstance 惰性创建）。
/// 按类型分别计数（决策 N3）：每类上限 maxPerType（默认 2）；Spawn 超限时顶替同类型最早的。
/// 本阶段只生成嘲讽型（Taunt）；攻击型（Attack）阶段 6 落地，计数结构已预留。
/// </summary>
public class IllusionManager : MonoBehaviour
{
    public static IllusionManager Instance { get; private set; }

    [Header("预制体（saika 编辑器建；为空时由代码运行时生成半透明 player sprite 外观）")]
    [Tooltip("嘲讽幻象预制体（可选）")]
    [SerializeField] private GameObject tauntIllusionPrefab = null;
    [Tooltip("攻击幻象预制体（阶段 6 使用，本阶段可为空）")]
    [SerializeField] private GameObject attackIllusionPrefab = null;

    [Header("上限")]
    [Tooltip("每类型幻象数量上限（决策 N3：超限顶替同类型最早的）")]
    [SerializeField] private int maxPerType = 2;

    /// <summary>按类型维护的活跃幻象列表（最早的在最前，顶替取 list[0]）</summary>
    private readonly Dictionary<IllusionType, List<IllusionController>> active = new();

    /// <summary>每类型上限（供外部读取）</summary>
    public int MaxPerType => maxPerType;

    /// <summary>当前某类型活跃数量</summary>
    public int GetActiveCount(IllusionType type)
    {
        return active.TryGetValue(type, out var list) ? list.Count : 0;
    }

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
        active[IllusionType.Taunt] = new List<IllusionController>();
        active[IllusionType.Attack] = new List<IllusionController>();
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    /// <summary>
    /// 确保管理器存在 — 执行器（静态订阅，无场景引用）调用：
    /// 场景里已挂（Player 上）直接用；否则惰性创建一个。
    /// </summary>
    public static IllusionManager EnsureInstance()
    {
        if (Instance != null) return Instance;
        var go = new GameObject("IllusionManager");
        return go.AddComponent<IllusionManager>();
    }

    /// <summary>
    /// 通用生成入口（决策 N3 顶替逻辑在此统一）。
    /// 超限时销毁同类型最早的；生成成功后触发 IllusionSpawnedEvent（UI/特效订阅）。
    /// 攻击型本阶段不生成（阶段 6 落地），返回 null。
    /// </summary>
    public IllusionController SpawnIllusion(IllusionType type, Vector2 position, TauntIllusionConfig config)
    {
        if (maxPerType <= 0) return null;

        switch (type)
        {
            case IllusionType.Taunt:
                return SpawnTauntIllusion(position, config);
            default:
                // Attack 型：阶段 6 落地（attackIllusionPrefab 为预留预制体槽位，届时使用）
                _ = attackIllusionPrefab;
                return null;
        }
    }

    /// <summary>生成嘲讽幻象 — 超限顶替最早嘲讽幻象；返回生成的控制器（失败返回 null）</summary>
    public TauntIllusion SpawnTauntIllusion(Vector2 position, TauntIllusionConfig config)
    {
        List<IllusionController> list = GetOrCreateList(IllusionType.Taunt);
        PurgeDead(list);

        // 超限顶替：同类型最早的消失（决策 N3）
        while (list.Count >= maxPerType && list.Count > 0)
            Despawn(list[0]);

        GameObject go = CreateIllusionObject(tauntIllusionPrefab, position);
        TauntIllusion illusion = go.AddComponent<TauntIllusion>();
        illusion.Initialize(IllusionType.Taunt, config.lifetime);
        illusion.Configure(config);

        list.Add(illusion);
        EventBus.Trigger(new IllusionSpawnedEvent(IllusionType.Taunt, position));
        return illusion;
    }

    /// <summary>统一销毁 — 从活跃列表移除并 Destroy（寿命到点 / 超限顶替 / 场景清理调用）</summary>
    public void Despawn(IllusionController controller)
    {
        if (controller == null) return;
        if (active.TryGetValue(controller.Type, out var list))
            list.Remove(controller);
        if (controller != null)
            Destroy(controller.gameObject);
    }

    // ── 私有辅助 ──

    private List<IllusionController> GetOrCreateList(IllusionType type)
    {
        if (!active.TryGetValue(type, out var list))
        {
            list = new List<IllusionController>();
            active[type] = list;
        }
        return list;
    }

    /// <summary>清理列表中被销毁（外部 Destroy/场景卸载）的残留引用</summary>
    private static void PurgeDead(List<IllusionController> list)
    {
        for (int i = list.Count - 1; i >= 0; i--)
        {
            if (list[i] == null) list.RemoveAt(i);
        }
    }

    /// <summary>创建幻象根对象：有预制体实例化预制体，否则建空对象（外观由 IllusionController 程序生成）</summary>
    private GameObject CreateIllusionObject(GameObject prefab, Vector2 position)
    {
        if (prefab != null)
        {
            GameObject go = Instantiate(prefab, position, Quaternion.identity);
            go.SetActive(true);
            return go;
        }
        var obj = new GameObject("Illusion_Taunt");
        obj.transform.position = position;
        return obj;
    }
}
