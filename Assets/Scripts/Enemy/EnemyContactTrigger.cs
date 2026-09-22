using UnityEngine;

/// <summary>
/// 接触推开 — 挂在 Enemy 上。OverlapCircle 检测 Player，触碰时推开（防重叠卡位）。
/// [2026-08-10 用户裁决] 去掉接触伤害：只有攻击动画命中帧才掉血（OnMeleeAttackHitFrame → PerformAttack），
/// 接触只推开不掉血。
/// </summary>
public class EnemyContactTrigger : MonoBehaviour
{
    [Header("数值配置 SO")]
    [Tooltip("敌人数值配置 ScriptableObject（为空时使用下方序列化字段值，保持旧行为）")]
    [SerializeField] protected EnemyConfigSO config;

    [HideInInspector] [SerializeField] private float pushForce = 0f;      // [SO 唯一] 只作兜底
    [HideInInspector] [SerializeField] private float cooldown = 0f;       // [SO 唯一] 只作兜底
    [HideInInspector] [SerializeField] private float detectRadius = 0f;   // [SO 唯一] 只作兜底
    [SerializeField] private LayerMask playerLayer;

    private float _cooldownTimer;
    private Rigidbody2D _rb;
    private EnemyControllerBase _owner;

    void Awake()
    {
        // [SO 唯一锚点 2026-09-22] 数值不再在这里缓存 —— 统一在 Update 里实时取,避免组件 Awake 顺序问题。
        _owner = GetComponent<EnemyControllerBase>();
        _rb = GetComponent<Rigidbody2D>();
    }

    void Update()
    {
        if (_cooldownTimer > 0f) { _cooldownTimer -= Time.deltaTime; return; }
        if (_rb == null) return;

        // [SO 唯一锚点 2026-09-22] 实时取 SO 的 Lv 档,取不到才用隐藏字段兜底
        var lv = _owner != null ? _owner.LvStats : null;
        float useRadius = lv != null && lv.contactDetectRadius > 0f ? lv.contactDetectRadius
                          : (detectRadius > 0f ? detectRadius : 0.6f);
        float usePush = lv != null && lv.contactPushForce > 0f ? lv.contactPushForce
                        : (pushForce > 0f ? pushForce : 3f);
        float useCooldown = lv != null && lv.contactCooldown > 0f ? lv.contactCooldown
                            : (cooldown > 0f ? cooldown : 0.3f);

        // 只做推开（不再触发伤害——伤害只由攻击动画命中帧产生）
        Collider2D hit = Physics2D.OverlapCircle(transform.position, useRadius, playerLayer);
        if (hit == null) return;

        float dir = transform.position.x > hit.transform.position.x ? 1f : -1f;
        _rb.AddForce(Vector2.right * dir * usePush, ForceMode2D.Impulse);

        _cooldownTimer = useCooldown;
    }
}
