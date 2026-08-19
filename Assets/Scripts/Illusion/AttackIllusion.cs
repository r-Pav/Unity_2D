using System.Collections;
using UnityEngine;

/// <summary>
/// 攻击幻象生成参数（技能组阶段 6,B-02 线）— 由 DashComboExecutor / ComboLv2Executor 组装后传入。
/// 0 值 = 使用 AttackIllusion 序列化默认。
/// </summary>
public struct AttackIllusionConfig
{
    /// <summary>幻象寿命（秒；≤0 = 序列化默认）</summary>
    public float lifetime;
    /// <summary>单次攻击伤害（资产 lv2Right.damage 注入）</summary>
    public float hitDamage;
    /// <summary>连击间隔（秒；≤0 = 序列化默认）</summary>
    public float hitInterval;
    /// <summary>连击次数（三连击 = 3）</summary>
    public int hitCount;
    /// <summary>攻击判定矩形尺寸</summary>
    public Vector2 hitBoxSize;
    /// <summary>判定矩形中心相对幻象前方的偏移</summary>
    public float hitForwardOffset;
    /// <summary>幻象朝向（1 / -1，攻击判定与击退方向按此镜像）</summary>
    public int facing;

    // A01B02 合成（落点攻击幻象三连击 + 每击发射分裂弹）专用
    /// <summary>每击是否向命中 enemy 发射分裂弹（合成技能 A01B02 开启）</summary>
    public bool fireSplitBoltOnHit;
    /// <summary>分裂弹父弹伤害（子弹伤害 = 父弹 × SplitBolt.subDamageRatio）</summary>
    public float splitBoltDamage;
    /// <summary>分裂弹飞行速度</summary>
    public float splitBoltSpeed;
    /// <summary>分裂弹半径</summary>
    public float splitBoltRadius;
    /// <summary>分裂弹颜色</summary>
    public Color splitBoltColor;
}

/// <summary>
/// 攻击型幻象（B-02 线）— 自动三连击 = 简化版近战判定：
/// 独立 OverlapBox（不带 PlayerCombat 的连击/卡肉/VFX 系统）+ DamageInfo(source=player 侧 ICombatant、
/// element=触发时刻当前元素、走 CombatResolver.Resolve，元素 proc 自动生效）。
/// 每次攻击间隔序列化（hitInterval）；三连击打完自动销毁（或寿命到点由基类销毁）。
/// 每击命中 enemy 时可选发射分裂弹（fireSplitBoltOnHit，合成技能 A01B02 用）。
/// 计数/顶替：由 IllusionManager.SpawnAttackIllusion 统一处理（N3 每类独立计数,上限 2）。
/// </summary>
public class AttackIllusion : IllusionController
{
    [Header("攻击判定")]
    [Tooltip("单次攻击伤害（由资产 lv2Right.damage 注入）")]
    [SerializeField] private float hitDamage = 15f;
    [Tooltip("连击间隔（秒）")]
    [SerializeField] private float hitInterval = 0.5f;
    [Tooltip("连击次数（三连击 = 3）")]
    [SerializeField] private int hitCount = 3;
    [Tooltip("攻击判定矩形尺寸")]
    [SerializeField] private Vector2 hitBoxSize = new Vector2(1.4f, 1.2f);
    [Tooltip("判定矩形中心相对幻象前方的偏移")]
    [SerializeField] private float hitForwardOffset = 0.8f;
    [Tooltip("幻象朝向（1 / -1；生成时由执行器按玩家朝向注入）")]
    [SerializeField] private int facing = 1;
    [Tooltip("击退力度（沿幻象朝向；三连击轻推,不进敌人硬直分流）")]
    [SerializeField] private float knockbackForce = 3f;

    [Header("目标检测")]
    [Tooltip("攻击检测 Layer（默认 Enemy，Awake 兜底）")]
    [SerializeField] private LayerMask enemyMask;

    [Header("分裂弹（A01B02 合成开启）")]
    [Tooltip("每击是否向命中 enemy 发射分裂弹（合成技能 A01B02）")]
    [SerializeField] private bool fireSplitBoltOnHit;
    [Tooltip("分裂弹父弹伤害（子弹伤害 = 父弹 × 0.6）")]
    [SerializeField] private float splitBoltDamage = 25f;
    [Tooltip("分裂弹飞行速度")]
    [SerializeField] private float splitBoltSpeed = 10f;
    [Tooltip("分裂弹半径")]
    [SerializeField] private float splitBoltRadius = 0.25f;
    [Tooltip("分裂弹颜色")]
    [SerializeField] private Color splitBoltColor = new Color(0.2f, 0.9f, 1f, 1f);

    private ICombatant playerCombatant;   // player 侧 ICombatant（PlayerHealth 实现；DamageInfo.source）
    private ElementModule elementModule;  // 触发时刻读当前元素（决策 N5）
    private bool configured;
    private bool comboStarted;

    private void Awake()
    {
        if (enemyMask == 0)
            enemyMask = LayerMask.GetMask("Enemy"); // 默认值兜底（NameToLayer 仅允许在 Awake/Start 调用）
        if (PlayerController.Instance != null)
        {
            playerCombatant = PlayerController.Instance.GetComponent<ICombatant>();
            elementModule = PlayerController.Instance.GetComponent<ElementModule>();
        }
    }

    /// <summary>生成参数注入（由 IllusionManager.SpawnAttackIllusion 调用；0 值保留序列化默认）</summary>
    public void Configure(AttackIllusionConfig config)
    {
        if (config.lifetime <= 0f) config.lifetime = Lifetime; // 未传寿命时用基类当前值(已初始化)
        if (config.hitDamage > 0f) hitDamage = config.hitDamage;
        if (config.hitInterval > 0f) hitInterval = config.hitInterval;
        if (config.hitCount > 0) hitCount = config.hitCount;
        if (config.hitBoxSize != Vector2.zero) hitBoxSize = config.hitBoxSize;
        if (config.hitForwardOffset != 0f) hitForwardOffset = config.hitForwardOffset;
        if (config.facing != 0) facing = config.facing;
        fireSplitBoltOnHit = config.fireSplitBoltOnHit;
        if (config.splitBoltDamage > 0f) splitBoltDamage = config.splitBoltDamage;
        if (config.splitBoltSpeed > 0f) splitBoltSpeed = config.splitBoltSpeed;
        if (config.splitBoltRadius > 0f) splitBoltRadius = config.splitBoltRadius;
        if (config.splitBoltColor.a > 0f || config.splitBoltColor.r > 0f || config.splitBoltColor.g > 0f || config.splitBoltColor.b > 0f)
            splitBoltColor = config.splitBoltColor;
        configured = true;

        // 立即开始三连击（确定性：不等到下一帧 Start）
        if (!comboStarted)
        {
            comboStarted = true;
            StartCoroutine(ComboRoutine());
        }
    }

    private void Start()
    {
        // 安全兜底：编辑器直接摆放（未走 manager 配置）时按序列化默认开打
        if (!configured)
        {
            configured = true;
            if (!comboStarted)
            {
                comboStarted = true;
                StartCoroutine(ComboRoutine());
            }
        }
    }

    /// <summary>三连击协程：每 hitInterval 一击，打完全自动销毁（或寿命到点由基类 Update 销毁）</summary>
    private IEnumerator ComboRoutine()
    {
        for (int i = 0; i < hitCount; i++)
        {
            PerformHit();
            if (i < hitCount - 1)
                yield return new WaitForSeconds(hitInterval);
        }

        // 三连击打完自动销毁（N3 计数由管理器维护）
        var mgr = IllusionManager.Instance;
        if (mgr != null) mgr.Despawn(this);
        else Destroy(gameObject);
    }

    /// <summary>单次攻击判定：独立 OverlapBox + DamageInfo（简化版近战,source=player 侧,走元素管线）</summary>
    private void PerformHit()
    {
        if (this == null) return;
        // 惰性兜底：幻象 Awake 早于 PlayerController.Instance 就绪时重试获取
        if (playerCombatant == null && PlayerController.Instance != null)
        {
            playerCombatant = PlayerController.Instance.GetComponent<ICombatant>();
            elementModule = PlayerController.Instance.GetComponent<ElementModule>();
        }
        if (playerCombatant == null) return;

        Vector2 center = (Vector2)transform.position + Vector2.right * facing * hitForwardOffset;
        Collider2D[] hits = Physics2D.OverlapBoxAll(center, hitBoxSize, 0f, enemyMask);

        foreach (Collider2D col in hits)
        {
            if (col == null) continue;
            EnemyControllerBase enemy = col.GetComponentInParent<EnemyControllerBase>();
            if (enemy == null || enemy.IsDead || !enemy.CanBeDamaged) continue;

            // 触发时刻读取当前元素（决策 N5；火暴击仲裁与近战一致：不走 RollCrit,元素继承由 CombatResolver proc 生效）
            ElementType element = elementModule != null ? elementModule.CurrentElement : ElementType.None;

            CombatResolver.Resolve(playerCombatant, enemy, new DamageInfo
            {
                amount = hitDamage,
                source = playerCombatant,
                sourcePosition = (Vector2)transform.position,
                attackLabel = "IllusionAttack",
                knockback = new Knockback
                {
                    direction = Vector2.right * facing,
                    force = knockbackForce,
                    duration = 0f,
                    ignoreResistance = false
                },
                element = element,
                canTriggerElementProc = true,   // player 侧攻击默认可触发元素 proc（与近战/冲刺一致）
                critMultiplier = 0f             // 幻象不做暴击仲裁（数值烘焙进 hitDamage）
            });

            // A01B02 合成：每击向命中 enemy 发射分裂弹（命中后分裂 3 子弹自动追踪）
            if (fireSplitBoltOnHit)
                FireSplitBolt(enemy.transform.position, element);
        }
    }

    /// <summary>向目标位置发射一枚分裂弹（父弹,命中后分裂;元素继承触发时刻当前元素）</summary>
    private void FireSplitBolt(Vector2 targetPos, ElementType element)
    {
        if (playerCombatant == null) return;
        Vector2 dir = (targetPos - (Vector2)transform.position).normalized;
        if (dir.sqrMagnitude < 0.0001f) dir = Vector2.right * facing;

        SplitBolt.Spawn(
            position: (Vector2)transform.position + dir * 0.4f,
            direction: dir,
            damage: splitBoltDamage,
            speed: splitBoltSpeed,
            hitLayers: enemyMask,
            wallLayers: (1 << 3) | (1 << 11), // Ground + Wall,与魔法弹同款
            sourceLayer: 1 << PlayerController.Instance.gameObject.layer,
            source: playerCombatant,
            element: element,
            critMultiplier: 0f,
            radius: splitBoltRadius,
            color: splitBoltColor,
            canSplit: true,                   // 父弹:命中后分裂
            splitCount: 3,
            subDamageRatio: 0.6f,
            spreadAngleDeg: 25f,
            spreadDuration: 0.2f,
            homingEnabled: false,             // 父弹纯直线
            homingTurnRate: 8f,
            homingRange: 20f,
            maxLifetime: 4f
        );
    }
}
