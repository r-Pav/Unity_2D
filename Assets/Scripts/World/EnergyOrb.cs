using UnityEngine;

/// <summary>
/// [技能点能量球] 能量球本体 — 挂 orbPrefab 根节点。
/// 生成后运动分三阶段(2026-09-08):
///   Launch    从生成点向上喷发(抛物线,受 launchGravity),落回生成点高度=落地
///   GroundWait在地面停留 groundDelay 秒(不做物理,transform 移动,轻量)
///   Chase     朝玩家平滑移动(吸附),碰到玩家 → SkillPointManager.AddEnergy(value) → 销毁
/// CircleCollider2D(isTrigger) 接触玩家(Player 层,判定同 DropItem 模式)。
/// 兜底:超时 / 玩家死亡 / 玩家丢失(场景切换)自动销毁,不残留。
///
/// Prefab 创建指引(Unity Editor 手动创建):
///   GameObject "EnergyOrb"(Layer: Default)
///   ├── CircleCollider2D  — isTrigger=true, radius≈0.35(脚本 Awake 强制 isTrigger)
///   ├── SpriteRenderer    — 球体视觉(sortingOrder 高于地面)
///   └── EnergyOrb.cs (this)
/// 无需 Rigidbody2D:玩家侧刚体驱动 trigger 事件即可。
/// </summary>
[RequireComponent(typeof(CircleCollider2D))]
public class EnergyOrb : MonoBehaviour
{
    /// <summary>运动阶段:喷发上抛 → 地面停留 → 吸附玩家。</summary>
    private enum OrbPhase { Launch, GroundWait, Chase }

    // ============================================================
    // 配置参数
    // ============================================================

    [Header("配置")]
    [Tooltip("单枚携带的能量球进度值(掉落组件 Spawn 时写入,>=1)")]
    [SerializeField] private int value;

    [Tooltip("追踪玩家移动速度(米/秒)")]
    [SerializeField] private float moveSpeed = 8f;

    [Tooltip("存活时间(秒),超时未接触玩家自动销毁")]
    [SerializeField] private float lifetime = 5f;

    [Header("喷发-落地(2026-09-08)")]
    [Tooltip("喷发向上初速(米/秒)")]
    [SerializeField] private float launchSpeed = 7f;

    [Tooltip("喷发重力(米/秒²),越大弧线越短越急促")]
    [SerializeField] private float launchGravity = 20f;

    [Tooltip("落地后停留秒数,再开始吸附玩家")]
    [SerializeField] private float groundDelay = 1f;

    [Tooltip("吸附尾(中心光球的 TrailRenderer,拖引用;可空=不控制)。喷发/停留阶段自动关闭,进吸附才开,避免抛物线折叠难看拖尾")]
    [SerializeField] private TrailRenderer chaseTrail;

    // ============================================================
    // 运行时状态
    // ============================================================

    private LayerMask _playerMask;
    private float _remainingTime;
    private bool _collected;

    private OrbPhase _phase = OrbPhase.Chase; // 未走 Spawn 的手动摆放兜底:直接追
    private bool _launchInited;
    private float _groundY;
    private float _velocityY;
    private float _waitTimer;

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

        // 目标丢失(玩家死亡 / 玩家不存在)→ 销毁不残留
        PlayerController player = PlayerController.Instance;
        if (player == null || player.IsDead)
        {
            Destroy(gameObject);
            return;
        }

        // 阶段推进(首次 Update 记地面高度=生成点高度;Spawn 已把位置放到死亡点)
        if (!_launchInited)
        {
            _launchInited = true;
            _groundY = transform.position.y;
            _phase = OrbPhase.Launch;
            _velocityY = launchSpeed;

            // 喷发阶段先关吸附尾(拖了引用才控制),避免抛物线 180° 折叠的难看拖尾
            if (chaseTrail != null)
            {
                chaseTrail.Clear();
                chaseTrail.emitting = false;
            }
        }

        switch (_phase)
        {
            case OrbPhase.Launch:
                // 抛物线:重力减速上升→下落,落回生成点高度视为落地
                _velocityY -= launchGravity * Time.deltaTime;
                Vector3 pos = transform.position;
                pos.y += _velocityY * Time.deltaTime;
                transform.position = pos;

                if (_velocityY < 0f && pos.y <= _groundY)
                {
                    pos.y = _groundY;
                    transform.position = pos;
                    _phase = OrbPhase.GroundWait;
                    _waitTimer = groundDelay;
                }
                break;

            case OrbPhase.GroundWait:
                _waitTimer -= Time.deltaTime;
                if (_waitTimer <= 0f)
                    _phase = OrbPhase.Chase;
                break;

            case OrbPhase.Chase:
                // 吸附开始:开中心球移动尾(轨迹是向玩家的平滑移动,无折返)
                if (chaseTrail != null && !chaseTrail.emitting)
                    chaseTrail.emitting = true;
                // 朝玩家 transform 平滑移动(吸附)
                transform.position = Vector3.MoveTowards(
                    transform.position,
                    player.transform.position,
                    moveSpeed * Time.deltaTime
                );
                break;
        }
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
