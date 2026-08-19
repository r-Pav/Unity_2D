using UnityEngine;

/// <summary>
/// 落雷实体（技能组阶段 1）— 雷元素 proc 生成在触发命中的 defender 位置。
///
/// 行为（决策 D8/D14）：
/// - 单次独立伤害：damageMultiplier × player 基础伤害（PlayerCombat.CurrentBaseDamage），
///   判定半径内所有 enemy 各结算一次，与触发攻击的暴击互不影响（独立伤害数字）。
/// - attackLabel = "Thunder_Strike"：CombatResolver 对该标签跳过韧性累计（霸体也硬直），
///   EnemyControllerBase.OnHitBy 对该标签强制 EnterStunState（不区分近战/远程路径）。
/// - canTriggerElementProc = false + element = None：衍生伤害禁止再触发元素 proc（防递归，验收点 4）。
/// - 专属特效挂点：素材待补，先挂 strikeVFXPrefab（空则用运行时生成的占位圆盘）。
///
/// 倍率 / 判定半径 / 特效均序列化：后续 saika 建预制体时可在 Inspector 调整。
/// </summary>
public class ThunderStrike : MonoBehaviour
{
    /// <summary>落雷攻击标签 — CombatResolver 韧性跳过 / OnHitBy 强制硬直的匹配键</summary>
    public const string AttackLabel = "Thunder_Strike";

    [Header("落雷")]
    [Tooltip("伤害倍率（× player 基础伤害）")]
    [SerializeField] private float damageMultiplier = 1.3f;

    [Tooltip("落雷判定半径 — 范围内所有 enemy 均受击")]
    [SerializeField] private float strikeRadius = 1.5f;

    [Tooltip("落雷判定的敌方 Layer（默认 ~0 = 全部，按 EnemyControllerBase 组件过滤）")]
    [SerializeField] private LayerMask enemyLayer = ~0;

    [Header("特效（素材待补，先用占位）")]
    [Tooltip("落雷专属特效预制体（后续替换；为空时用运行时生成的占位圆盘）")]
    [SerializeField] private GameObject strikeVFXPrefab = null;

    [Tooltip("占位特效存活时长（秒），之后销毁本实体")]
    [SerializeField] private float vfxLifetime = 0.4f;

    // ============================================================
    // 静态工厂
    // ============================================================

    /// <summary>
    /// 在 defender 位置生成落雷：立即对判定范围内所有 enemy 结算一次伤害，随后播放特效并销毁。
    /// </summary>
    public static void SpawnAt(DamageInfo triggerInfo, ICombatant defender)
    {
        if (defender == null) return;

        GameObject go = new GameObject("ThunderStrike");
        go.transform.position = defender.Transform.position;

        ThunderStrike strike = go.AddComponent<ThunderStrike>();
        strike.Init(triggerInfo, defender);
    }

    // ============================================================
    // 初始化
    // ============================================================

    private void Init(DamageInfo triggerInfo, ICombatant defender)
    {
        // 伤害来源 = 触发攻击的 source（player 侧；source 归属 player，影响后续伤害统计窗口）
        ICombatant source = triggerInfo.source;

        // player 基础伤害：优先取 PlayerCombat 管线值（attackDamage × DamageMultiplier），
        // 组件缺失（非玩家来源）时回退触发伤害值兜底。
        float baseDamage = triggerInfo.amount;
        if (source != null && source.GameObject != null)
        {
            PlayerCombat pc = source.GameObject.GetComponent<PlayerCombat>();
            if (pc != null)
                baseDamage = pc.CurrentBaseDamage;
        }
        float strikeDamage = baseDamage * damageMultiplier;

        // 判定范围内所有 enemy 各结算一次（触发者也在范围内，一并受击）
        Collider2D[] hits = Physics2D.OverlapCircleAll(transform.position, strikeRadius, enemyLayer);
        foreach (Collider2D col in hits)
        {
            EnemyControllerBase enemy = col.GetComponent<EnemyControllerBase>();
            if (enemy == null) continue;

            DamageInfo strikeInfo = new DamageInfo
            {
                amount = strikeDamage,
                source = source,
                sourcePosition = transform.position,
                attackLabel = AttackLabel,
                knockback = Knockback.None,
                element = ElementType.None,            // 防递归（决策 D14）
                canTriggerElementProc = false,          // 防递归（决策 D14）
                critMultiplier = 0f                     // 落雷不参与暴击仲裁
            };
            CombatResolver.Resolve(source, enemy, strikeInfo);
        }

        // 特效：优先预制体挂点，空则运行时生成占位圆盘
        if (strikeVFXPrefab != null)
            Instantiate(strikeVFXPrefab, transform.position, Quaternion.identity, transform);
        else
            CreatePlaceholderVFX();

        // 实体只存活到特效播完
        Destroy(gameObject, vfxLifetime);
    }

    // ============================================================
    // 占位特效（素材待补；替换 strikeVFXPrefab 后此方法不再被调用）
    // ============================================================

    /// <summary>运行时生成白色圆盘贴图，染电光蓝，半径 = 判定半径</summary>
    private void CreatePlaceholderVFX()
    {
        const int size = 32;
        Texture2D tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
        float center = (size - 1) * 0.5f;
        float inner = center * 0.85f;   // 边缘 15% 渐隐，避免硬边
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float dist = Vector2.Distance(new Vector2(x, y), new Vector2(center, center));
                float alpha = Mathf.Clamp01((center - dist) / (center - inner));
                tex.SetPixel(x, y, new Color(1f, 1f, 1f, alpha));
            }
        }
        tex.Apply();

        Sprite sprite = Sprite.Create(tex, new Rect(0f, 0f, size, size), new Vector2(0.5f, 0.5f), size);
        SpriteRenderer sr = gameObject.AddComponent<SpriteRenderer>();
        sr.sprite = sprite;
        sr.color = new Color(0.65f, 0.85f, 1f, 0.85f);   // 电光蓝占位
        transform.localScale = new Vector3(strikeRadius * 2f, strikeRadius * 2f, 1f);
    }
}
