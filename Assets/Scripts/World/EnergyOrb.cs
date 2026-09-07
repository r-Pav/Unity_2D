using UnityEngine;

/// <summary>
/// [技能点能量球] 能量球本体 — 挂 orbPrefab 根节点。
/// 生成后自动朝玩家平滑移动（transform 移动，轻量；不做刚体磁吸/每帧物理查询）；
/// CircleCollider2D(isTrigger) 接触玩家（Player 层，判定同 DropItem 模式）→
/// SkillPointManager.AddEnergy(value) → 自身销毁。
/// 兜底：超时 / 玩家死亡 / 玩家丢失（场景切换）自动销毁，不残留。多个能量球互不干扰（瞬时数量小，不做池）。
///
/// Prefab 创建指引（Unity Editor 手动创建）：
///   GameObject "EnergyOrb"（Layer: Default）
///   ├── CircleCollider2D  — isTrigger=true, radius≈0.35（脚本 Awake 强制 isTrigger）
///   ├── SpriteRenderer    — 球体视觉（sortingOrder 高于地面）
///   └── EnergyOrb.cs (this)
/// 无需 Rigidbody2D：玩家侧刚体驱动 trigger 事件即可。
/// </summary>
[RequireComponent(typeof(CircleCollider2D))]
public class EnergyOrb : MonoBehaviour
{
    // ============================================================
    // 配置参数
    // ============================================================

    [Header("配置")]
    [Tooltip("单枚携带的能量球进度值（掉落组件 Spawn 时写入，>=1）")]
    [SerializeField] private int value;

    [Tooltip("追踪玩家移动速度（米/秒）")]
    [SerializeField] private float moveSpeed = 8f;

    [Tooltip("存活时间（秒），超时未接触玩家自动销毁")]
    [SerializeField] private float lifetime = 5f;

    // ============================================================
    // 运行时状态
    // ============================================================

    private LayerMask _playerMask;
    private float _remainingTime;
    private bool _collected;

    // ============================================================
    // 生命周期
    // ============================================================

    private void Awake()
    {
        CircleCollider2D col = GetComponent<CircleCollider2D>();
        col.isTrigger = true;
        _playerMask = LayerMask.GetMask("Player");
        _remainingTime = lifetime;
    }

    private void Update()
    {
        // 超时兜底
        _remainingTime -= Time.deltaTime;
        if (_remainingTime <= 0f)
        {
            Destroy(gameObject);
            return;
        }

        // 目标丢失（玩家死亡 / 玩家不存在）→ 销毁不残留
        PlayerController player = PlayerController.Instance;
        if (player == null || player.IsDead)
        {
            Destroy(gameObject);
            return;
        }

        // 朝玩家 transform 平滑移动
        transform.position = Vector3.MoveTowards(
            transform.position,
            player.transform.position,
            moveSpeed * Time.deltaTime
        );
    }

    private void OnTriggerEnter2D(Collider2D other)
    {
        if (_collected) return;

        // 层判断：只吃 Player（同 DropItem 模式）
        if ((_playerMask.value & (1 << other.gameObject.layer)) == 0)
            return;

        _collected = true;

        // 命中玩家 → 加能量进度 → 销毁
        PlayerController player = PlayerController.Instance;
        SkillPointManager spm = player != null ? player.SkillPointManager : null;
        if (spm != null)
            spm.AddEnergy(value);

        Destroy(gameObject);
    }

    // ============================================================
    // 初始化 / 工厂
    // ============================================================

    /// <summary>初始化单枚携带值（生成时由 Spawn 调用）</summary>
    public void Initialize(int orbValue)
    {
        value = orbValue;
        _remainingTime = lifetime;
    }

    /// <summary>在世界中生成一枚能量球并开始追踪玩家（仿 DropItem.Spawn）</summary>
    public static EnergyOrb Spawn(EnergyOrb prefab, int orbValue, Vector2 position)
    {
        EnergyOrb orb = Instantiate(prefab);
        orb.transform.position = position;
        orb.Initialize(orbValue);
        return orb;
    }
}
