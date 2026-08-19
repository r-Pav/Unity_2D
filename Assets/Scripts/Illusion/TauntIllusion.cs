using UnityEngine;

/// <summary>
/// 嘲讽幻象生成参数 — 由 DashIllusionExecutor 从 Skill_Active_E 的 lv2Left/lv3Left 分支数据读取后传入。
/// 0 值 = 使用 TauntIllusion 序列化默认。
/// </summary>
public struct TauntIllusionConfig
{
    /// <summary>嘲讽/DoT 作用半径</summary>
    public float tauntRadius;
    /// <summary>嘲讽时长（秒；Boss 减半在 EnemyControllerBase.SetTaunt 内处理）</summary>
    public float tauntDuration;
    /// <summary>幻象寿命（秒）</summary>
    public float lifetime;
    /// <summary>lv3B-01 解锁：定时对范围内 enemy 造成伤害</summary>
    public bool dotEnabled;
    /// <summary>DoT 单次伤害</summary>
    public float dotDamage;
    /// <summary>DoT 触发间隔（秒）</summary>
    public float dotInterval;
    /// <summary>[阶段7 lv3 合成 A01B01] DoT 强制暴击倍率（>0 时单次伤害 × 倍率并透传 critMultiplier；0 = 不暴击）</summary>
    public float dotCritMultiplier;
    /// <summary>[阶段7 lv3 合成] 嘲讽刷新间隔（秒；>0 = 周期性对半径内 enemy 重新施加嘲讽，持续牵引；0 = 仅生成时嘲讽一次）</summary>
    public float tauntRefreshInterval;
}

/// <summary>
/// 嘲讽型幻象（B-01 线）— 生成时对半径内 enemy 调 SetTaunt（仇恨拉向自身），
/// 牵引表现 = enemy 追幻象（PlayerTarget 重定向即牵引）。
/// lv3B-01（treeB_taunt_dot 解锁）时：范围变大 + 定时对范围内 enemy 走 CombatResolver 小额伤害
/// （element = 触发时刻当前元素，决策 N5；与冲刺伤害同款 player 来源带元素）。
/// </summary>
public class TauntIllusion : IllusionController
{
    [Header("嘲讽")]
    [Tooltip("嘲讽/DoT 作用半径（lv3 由资产 range 覆盖变大）")]
    [SerializeField] private float tauntRadius = 3f;
    [Tooltip("嘲讽时长（秒；由资产 duration 注入）")]
    [SerializeField] private float tauntDuration = 4f;

    [Header("DoT（lv3B-01 解锁）")]
    [Tooltip("是否开启持续伤害（由执行器按等级/分支注入）")]
    [SerializeField] private bool dotEnabled;
    [Tooltip("DoT 单次伤害（由资产 damage 注入）")]
    [SerializeField] private float dotDamage = 3f;
    [Tooltip("DoT 触发间隔（秒）")]
    [SerializeField] private float dotInterval = 1f;
    [Tooltip("[阶段7 lv3 合成 A01B01] DoT 强制暴击倍率（>0 时单次伤害 × 倍率并透传 critMultiplier；0 = 不暴击）")]
    [SerializeField] private float dotCritMultiplier;
    [Tooltip("[阶段7 lv3 合成] 嘲讽刷新间隔（秒；>0 = 周期性对半径内 enemy 重新施加嘲讽，持续牵引；0 = 仅生成时嘲讽一次）")]
    [SerializeField] private float tauntRefreshInterval;

    [Header("目标检测")]
    [Tooltip("嘲讽/DoT 检测 Layer（默认 Enemy，Awake 兜底）")]
    [SerializeField] private LayerMask enemyMask;

    private ElementModule elementModule;
    private float dotTimer;
    private float tauntRefreshTimer;
    private bool configured;

    private void Awake()
    {
        if (enemyMask == 0)
            enemyMask = LayerMask.GetMask("Enemy"); // 默认值兜底（NameToLayer 仅允许在 Awake/Start 调用）
        if (PlayerController.Instance != null)
            elementModule = PlayerController.Instance.GetComponent<ElementModule>();
    }

    /// <summary>生成参数注入（由 IllusionManager.SpawnIllusion 调用；0 值保留序列化默认）</summary>
    public void Configure(TauntIllusionConfig config)
    {
        if (config.tauntRadius > 0f) tauntRadius = config.tauntRadius;
        if (config.tauntDuration > 0f) tauntDuration = config.tauntDuration;
        dotEnabled = config.dotEnabled;
        if (config.dotDamage > 0f) dotDamage = config.dotDamage;
        if (config.dotInterval > 0f) dotInterval = config.dotInterval;
        if (config.dotCritMultiplier > 0f) dotCritMultiplier = config.dotCritMultiplier;
        if (config.tauntRefreshInterval > 0f) tauntRefreshInterval = config.tauntRefreshInterval;
        configured = true;

        // 生成时立即嘲讽（确定性：不等到下一帧 Start）
        ApplyTaunt();
        tauntRefreshTimer = tauntRefreshInterval; // 首次刷新间隔（0 = 不刷新）
    }

    private void Start()
    {
        // 安全兜底：编辑器直接摆放（未走 manager 配置）时按序列化默认嘲讽
        if (!configured)
        {
            configured = true;
            ApplyTaunt();
        }
    }

    protected override void Update()
    {
        base.Update();
        TickDot();
        TickTauntRefresh();
    }

    /// <summary>生成时对半径内 enemy 施加嘲讽 — 仇恨拉到自身，enemy 追幻象即被牵引</summary>
    private void ApplyTaunt()
    {
        if (tauntRadius <= 0f || tauntDuration <= 0f) return;
        Collider2D[] hits = Physics2D.OverlapCircleAll(transform.position, tauntRadius, enemyMask);
        foreach (Collider2D col in hits)
        {
            EnemyControllerBase enemy = col.GetComponentInParent<EnemyControllerBase>();
            if (enemy == null || enemy.IsDead) continue;
            enemy.SetTaunt(transform, tauntDuration);
        }
    }

    /// <summary>
    /// [阶段7] 周期性刷新嘲讽（tauntRefreshInterval > 0 时）：对半径内 enemy 重新施加嘲讽，
    /// 保证新进入范围的 enemy 也被牵引、已失效的嘲讽被续上（持续牵引语义）。
    /// </summary>
    private void TickTauntRefresh()
    {
        if (tauntRefreshInterval <= 0f) return;
        tauntRefreshTimer -= Time.deltaTime;
        if (tauntRefreshTimer > 0f) return;
        tauntRefreshTimer = tauntRefreshInterval;
        ApplyTaunt();
    }

    /// <summary>
    /// DoT 定时伤害（lv3B-01）：对半径内 enemy 走 CombatResolver 小额伤害。
    /// element = 触发时刻当前元素（N5 战斗中切换即时生效）；canTriggerElementProc=true 与冲刺伤害一致
    /// （player 来源伤害带元素，水无视护甲/雷落雷；落雷自身 canTriggerElementProc=false 防递归，链安全）。
    /// [阶段7 lv3 合成 A01B01]：dotCritMultiplier > 0 时强制暴击（单次伤害 × 倍率，critMultiplier 透传）。
    /// knockback 不配（小额 DoT 不推人；受击远程路径的兜底击退为既有管线行为）。
    /// </summary>
    private void TickDot()
    {
        if (!dotEnabled || dotInterval <= 0f) return;
        dotTimer -= Time.deltaTime;
        if (dotTimer > 0f) return;
        dotTimer = dotInterval;

        if (tauntRadius <= 0f || dotDamage <= 0f) return;
        ICombatant source = PlayerController.Instance != null
            ? PlayerController.Instance.GetComponent<ICombatant>()
            : null;
        if (source == null) return;

        // 触发时刻读取当前元素（决策 N5）
        ElementType element = elementModule != null ? elementModule.CurrentElement : ElementType.None;

        // 强制暴击：倍率烘焙进伤害，critMultiplier 透传（与魔法弹必暴同款仲裁透传语义）
        float dotMult = dotCritMultiplier > 0f ? dotCritMultiplier : 1f;
        float actualDamage = dotDamage * dotMult;

        Collider2D[] hits = Physics2D.OverlapCircleAll(transform.position, tauntRadius, enemyMask);
        foreach (Collider2D col in hits)
        {
            EnemyControllerBase enemy = col.GetComponentInParent<EnemyControllerBase>();
            if (enemy == null || enemy.IsDead || !enemy.CanBeDamaged) continue;

            CombatResolver.Resolve(source, enemy, new DamageInfo
            {
                amount = actualDamage,
                source = source,
                sourcePosition = (Vector2)transform.position,
                attackLabel = "IllusionDot",
                knockback = Knockback.None,
                element = element,
                canTriggerElementProc = true,
                critMultiplier = dotCritMultiplier > 0f ? dotCritMultiplier : 0f
            });
        }
    }
}
