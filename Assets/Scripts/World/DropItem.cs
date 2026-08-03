using UnityEngine;

/// <summary>
/// 世界掉落物 MonoBehaviour
/// 挂在 DropItem Prefab 上，负责：
/// 1. 显示物品图标和稀有度边框
/// 2. CircleCollider2D (isTrigger) 检测进入范围的 Player/Enemy
/// 3. 自动拾取（调用 IPickupReceiver.TryPickup）
/// 4. 超时自动销毁
///
/// Prefab 创建指引（在 Unity Editor 中手动创建）：
///   GameObject "DropItem" (Layer: DropItem)
///   ├── SpriteRenderer          — 物品图标 (sortingOrder: 1)
///   ├── CircleCollider2D        — isTrigger=true, radius=0.5~1.0
///   ├── Rigidbody2D (可选)       — bodyType=Kinematic（默认无物理）
///   ├── DropItem.cs (this)
///   └── RarityFrame (子节点)
///       └── SpriteRenderer      — 稀有度边框 (sortingOrder: 2)
/// </summary>
[RequireComponent(typeof(SpriteRenderer))]
[RequireComponent(typeof(CircleCollider2D))]
public class DropItem : MonoBehaviour
{
    [Header("配置")]
    [Tooltip("默认存活时间（秒），超时后自动销毁")]
    public float defaultLifetime = 30f;

    [Header("组件引用（Inspector 拖入或自动查找）")]
    [Tooltip("物品图标渲染器")]
    [SerializeField] private SpriteRenderer _iconRenderer;

    [Tooltip("稀有度边框渲染器（可选，在子节点 RarityFrame 上）")]
    [SerializeField] private SpriteRenderer _rarityFrame;

    [Tooltip("拾取触发器")]
    [SerializeField] private CircleCollider2D _pickupCollider;

    [Header("VFX")]
    [Tooltip("掉落物生成闪光 VFX — Spawn/Initialize 时生成一次")]
    [SerializeField] private GameObject sparkleVFXPrefab;

    // ── 运行时数据 ──

    /// <summary>携带的物品实例</summary>
    public ItemInstance ItemData { get; private set; }

    /// <summary>掉落等级（用于 Enemy 拾取时的等级缩放计算）</summary>
    public int DropLevel { get; private set; }

    /// <summary>谁可以拾取此掉落物（LayerMask）</summary>
    private LayerMask _ownerMask;

    /// <summary>剩余存活时间（秒），≤0 时销毁</summary>
    private float _remainingLifetime;

    /// <summary>正在被拾取中 — 防止同一帧被多个对象重复拾取</summary>
    private bool _isBeingPickedUp;

    /// <summary>落地动画相关（可选）</summary>
    private Rigidbody2D _rb;
    private float _landingAnimTimer;
    private bool _useLandingAnimation;

    // ── Unity 生命周期 ──

    private void Awake()
    {
        // 自动查找组件（Inspector 已拖入则优先）
        if (_iconRenderer == null)
            _iconRenderer = GetComponent<SpriteRenderer>();
        if (_pickupCollider == null)
            _pickupCollider = GetComponent<CircleCollider2D>();
        if (_rarityFrame == null)
        {
            Transform frameChild = transform.Find("RarityFrame");
            if (frameChild != null)
                _rarityFrame = frameChild.GetComponent<SpriteRenderer>();
        }

        // 确保 Collider 为 Trigger
        _pickupCollider.isTrigger = true;

        // 尝试获取 Rigidbody2D（可选）
        _rb = GetComponent<Rigidbody2D>();
        if (_rb != null)
            _rb.bodyType = RigidbodyType2D.Kinematic; // 默认无物理

        // 初始隐藏渲染器，等待 Initialize() 时 SetVisuals 激活
        if (_iconRenderer != null)
            _iconRenderer.enabled = false;
        if (_rarityFrame != null)
            _rarityFrame.enabled = false;
    }

    private void Update()
    {
        // 落地动画计时
        if (_useLandingAnimation && _landingAnimTimer > 0f)
        {
            _landingAnimTimer -= Time.deltaTime;
            if (_landingAnimTimer <= 0f && _rb != null)
            {
                // 动画结束，切为 Kinematic 节省性能
                _rb.bodyType = RigidbodyType2D.Kinematic;
                _rb.velocity = Vector2.zero;
            }
        }

        // 超时倒计时
        _remainingLifetime -= Time.deltaTime;
        if (_remainingLifetime <= 0f)
        {
            DestroySelf();
        }
    }

    private void OnTriggerStay2D(Collider2D other)
    {
        // 防止重复拾取
        if (_isBeingPickedUp) return;
        // 已超时不检测
        if (_remainingLifetime <= 0f) return;

        // LayerMask 检查：other 是否在允许拾取的 mask 中
        if ((_ownerMask.value & (1 << other.gameObject.layer)) == 0)
            return;

        // 获取拾取接口
        IPickupReceiver receiver = other.GetComponent<IPickupReceiver>();
        if (receiver == null)
            receiver = other.GetComponentInParent<IPickupReceiver>();

        if (receiver == null) return;

        // 尝试拾取
        _isBeingPickedUp = true;
        bool success = false;
        try
        {
            success = receiver.TryPickup(this);
        }
        finally
        {
            _isBeingPickedUp = false;
        }

        if (success)
        {
            DestroySelf();
        }
        // 失败则留在原地，下一帧继续检测
    }

    // ── 公开方法 ──

    /// <summary>
    /// 初始化掉落物并显示到世界中
    /// </summary>
    /// <param name="item">物品实例数据</param>
    /// <param name="level">掉落等级（用于 Enemy 拾取时等级缩放）</param>
    /// <param name="ownerMask">允许拾取的 LayerMask（如 Player|Enemy）</param>
    /// <param name="position">世界坐标位置</param>
    /// <param name="useAnimation">是否使用弹跳落地动画（默认 false）</param>
    /// <param name="lifetime">存活时间（秒），≤0 使用 defaultLifetime</param>
    public void Initialize(ItemInstance item, int level, LayerMask ownerMask,
        Vector2 position, bool useAnimation = false, float lifetime = -1f)
    {
        ItemData = item;
        DropLevel = level;
        _ownerMask = ownerMask;
        _remainingLifetime = lifetime > 0f ? lifetime : defaultLifetime;
        _isBeingPickedUp = false;

        // 设置位置
        transform.position = position;
        gameObject.SetActive(true);

        // 设置视觉
        if (item?.template != null)
        {
            SetVisuals(item.template.icon, RarityColor.GetColor(item.template.rarity));
        }

        // 掉落闪光 VFX
        if (sparkleVFXPrefab != null)
            VFXSpawner.SpawnInWorld(sparkleVFXPrefab, position);

        // 落地动画
        if (useAnimation)
        {
            StartLandingAnimation();
        }
    }

    /// <summary>
    /// 设置掉落物视觉表现
    /// </summary>
    public void SetVisuals(Sprite icon, Color rarityColor)
    {
        if (_iconRenderer != null)
        {
            _iconRenderer.sprite = icon;
            _iconRenderer.enabled = true;
        }

        if (_rarityFrame != null)
        {
            _rarityFrame.color = rarityColor;
            _rarityFrame.enabled = true;
        }
    }

    /// <summary>
    /// 对象池回调 — 取出时重置状态
    /// 用法：ObjectPool.Get() 后调用 Initialize()
    /// </summary>
    public void OnPoolGet()
    {
        _isBeingPickedUp = false;
        _remainingLifetime = defaultLifetime;
    }

    /// <summary>
    /// 对象池回调 — 归还时清理状态
    /// </summary>
    public void OnPoolRelease()
    {
        ItemData = null;
        _isBeingPickedUp = false;
        _remainingLifetime = 0f;
        if (_rb != null)
        {
            _rb.bodyType = RigidbodyType2D.Kinematic;
            _rb.velocity = Vector2.zero;
        }
        gameObject.SetActive(false);
    }

    // ── 私有方法 ──

    /// <summary>弹跳落地动画：给 Rigidbody2D 添加随机初速度，1 秒后切回 Kinematic</summary>
    private void StartLandingAnimation()
    {
        if (_rb == null) return;

        _rb.bodyType = RigidbodyType2D.Dynamic;
        _rb.gravityScale = 1f;

        // 随机方向初速度：向上为主 + 轻微水平偏移
        float randomX = Random.Range(-1f, 1f);
        float randomY = Random.Range(2f, 4f);
        _rb.velocity = new Vector2(randomX, randomY);

        _useLandingAnimation = true;
        _landingAnimTimer = 1.5f; // 1.5 秒后切回 Kinematic
    }

    /// <summary>销毁自身（后续 Phase 2 改为归还对象池）</summary>
    private void DestroySelf()
    {
        // TODO Phase 2: 改为 ObjectPool<DropItem>.Return(this)
        Destroy(gameObject);
    }

    // ── 静态工厂方法（方便从对象池创建）──

    /// <summary>
    /// 在世界中生成一个掉落物
    /// </summary>
    public static DropItem Spawn(DropItem prefab, ItemInstance item, int level,
        LayerMask ownerMask, Vector2 position, bool useAnimation = false, float lifetime = -1f)
    {
        // TODO Phase 2: 改为从 ObjectPool 获取
        DropItem drop = Instantiate(prefab);
        drop.Initialize(item, level, ownerMask, position, useAnimation, lifetime);
        return drop;
    }
}
