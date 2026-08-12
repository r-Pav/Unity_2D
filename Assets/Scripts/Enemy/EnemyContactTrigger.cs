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

    [SerializeField] private float pushForce = 0f;      // 0 = 未设置，SO Lv 档 / 内置兜底
    [SerializeField] private float cooldown = 0f;
    [SerializeField] private float detectRadius = 0f;
    [SerializeField] private LayerMask playerLayer;

    private float _cooldownTimer;
    private Rigidbody2D _rb;
    private EnemyControllerBase _owner;

    void Awake()
    {
        // [Lv 收敛] 从 controller 的 LvStats 档取值（组件自身 config 字段保留防序列化丢失，不再参与取值）
        _owner = GetComponent<EnemyControllerBase>();
        if (_owner?.LvStats != null)
        {
            if (pushForce <= 0f && _owner.LvStats.contactPushForce > 0f) pushForce = _owner.LvStats.contactPushForce;
            if (cooldown <= 0f && _owner.LvStats.contactCooldown > 0f) cooldown = _owner.LvStats.contactCooldown;
            if (detectRadius <= 0f && _owner.LvStats.contactDetectRadius > 0f) detectRadius = _owner.LvStats.contactDetectRadius;
            if (pushForce <= 0f) pushForce = 3f;
            if (cooldown <= 0f) cooldown = 0.3f;
            if (detectRadius <= 0f) detectRadius = 0.6f;
        }

        _rb = GetComponent<Rigidbody2D>();
    }

    void Update()
    {
        if (_cooldownTimer > 0f) { _cooldownTimer -= Time.deltaTime; return; }
        if (_rb == null) return;

        // 只做推开（不再触发伤害——伤害只由攻击动画命中帧产生）
        Collider2D hit = Physics2D.OverlapCircle(transform.position, detectRadius, playerLayer);
        if (hit == null) return;

        float dir = transform.position.x > hit.transform.position.x ? 1f : -1f;
        _rb.AddForce(Vector2.right * dir * pushForce, ForceMode2D.Impulse);

        _cooldownTimer = cooldown;
    }
}
