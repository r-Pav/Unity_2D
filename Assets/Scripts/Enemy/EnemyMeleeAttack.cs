using UnityEngine;

/// <summary>
/// 近战攻击组件 — 挂在敌人身上，通过 IEnemyAttack 接口被 EnemyController 调用
/// 攻击范围由 MeleeRangeIndicator 的 Transform 定义
/// </summary>
public class EnemyMeleeAttack : MonoBehaviour, IEnemyAttack
{
    [Header("数值配置 SO")]
    [Tooltip("敌人数值配置 ScriptableObject（为空时使用下方序列化字段值，保持旧行为）")]
    [SerializeField] protected EnemyConfigSO config;

    [SerializeField] private float damage = 0f;   // 0 = 未设置，用 SO 对应 Lv 档 / 内置 1f 兜底
    [SerializeField] private Color attackFlashColor = Color.white;
    [SerializeField] private float attackFlashDuration = 0.08f;

    [Header("近战范围指示器")]
    [Tooltip("拖入敌人下的攻击范围 GameObject（挂 MeleeRangeIndicator）")]
    [SerializeField] private MeleeRangeIndicator rangeIndicator;

    /// <summary>管线消费方缓存（enemy prefab 挂 StatModifierManager 后生效）</summary>
    private StatModifierManager statModManager;
    private EnemyControllerBase _owner;

    /// <summary>攻击范围指示器(拖入的子物体)— 供 Boss 攻击触发检测复用同一范围</summary>
    public MeleeRangeIndicator RangeIndicator => rangeIndicator;

    /// <summary>最终攻击力（管线终值；Inspector 运行时调试显示用）</summary>
    public float FinalDamage => statModManager != null
        ? statModManager.GetFinalValue(damage, StatId.EnemyDamage)
        : damage;

    private void Awake()
    {
        // [Lv 收敛] 从 controller 的 LvStats 档取值（组件自身 config 字段保留防序列化丢失，不再参与取值）
        _owner = GetComponent<EnemyControllerBase>();
        if (_owner?.LvStats != null)
        {
            if (damage <= 0f && _owner.LvStats.meleeDamage > 0f) damage = _owner.LvStats.meleeDamage;
            if (damage <= 0f) damage = 1f;   // 兜底：SO 档也无值
        }

        statModManager = GetComponent<StatModifierManager>();
    }

    public void PerformAttack(EnemyControllerBase owner)
    {
        if (rangeIndicator == null) return;

        // P2b-2: 伤害终值走管线（无 manager 回退 baseValue，对齐 PlayerCombat.GetEffectiveDamage 写法）
        float finalDamage = statModManager != null ? statModManager.GetFinalValue(damage, StatId.EnemyDamage) : damage;

        // rangeIndicator 位置跟随 Enemy facing（已由 AttackState 在调用前同步）
        Vector3 lp = rangeIndicator.transform.localPosition;
        lp.x = Mathf.Abs(lp.x) * owner.Facing;
        rangeIndicator.transform.localPosition = lp;

        owner.IsInAttackFrame = true;

        try
        {
            // P4c:攻击标签写入（弹反/结算用）
            owner.CurrentAttackLabel = "Melee_Enemy";

            Collider2D[] hits = MeleeHitDetector.Detect(rangeIndicator, ~0);

            bool hit = false;
            foreach (var col in hits)
            {
                if (TryHit(col, owner, finalDamage)) { hit = true; break; } // 只打第一个命中的 player
            }

            // 贴身/重叠兜底：player 在攻击矩形内（以 enemy 为中心，不依赖 facing）
            // 场景：player 绕到 enemy 身后/重叠时 facing 翻转不稳定 → 单侧矩形漏检
            if (!hit && owner.PlayerTarget != null)
            {
                Vector2 toPlayer = (Vector2)owner.PlayerTarget.position - (Vector2)owner.transform.position;
                if (Mathf.Abs(toPlayer.x) <= owner.AttackWidth * 0.5f
                    && Mathf.Abs(toPlayer.y) <= owner.AttackHeight * 0.5f)
                {
                    var pc = owner.PlayerTarget.GetComponent<PlayerController>();
                    if (pc != null)
                        TryHit(pc.GetComponent<Collider2D>(), owner, finalDamage);
                }
            }

            rangeIndicator.Flash();
            owner.FlashColor(attackFlashColor, attackFlashDuration);
        }
        finally
        {
            owner.IsInAttackFrame = false;
        }
    }

    /// <summary>对单个命中对象执行伤害结算（CombatResolver 统一入口），返回是否命中玩家</summary>
    private bool TryHit(Collider2D col, EnemyControllerBase owner, float finalDamage)
    {
        var pc = col.GetComponent<PlayerController>();
        if (pc == null) return false;

        Vector2 attackDir = ((Vector2)(pc.transform.position - owner.transform.position)).normalized;
        Vector2 knockDir = attackDir;
        knockDir.y = 0f;
        if (knockDir.magnitude < 0.01f) knockDir = Vector2.right; // 默认向右

        // P4c:统一走 CombatResolver 结算(原 pc.TakeDamageWithKnockback;击退按原硬编码 10f/0.2s)
        var ph = pc.GetComponent<PlayerHealth>();
        if (ph != null)
        {
            CombatResolver.Resolve(owner, ph, new DamageInfo
            {
                amount = finalDamage,
                source = owner,
                sourcePosition = (Vector2)owner.transform.position,
                attackLabel = "Melee_Enemy",
                knockback = new Knockback
                {
                    direction = knockDir,
                    force = 10f,     // 原 TakeDamageWithKnockback 硬编码击退力度
                    duration = 0.2f, // 原 KnockbackRoutine 硬编码硬直时长
                    ignoreResistance = false
                }
            });
        }
        return true;
    }
}
