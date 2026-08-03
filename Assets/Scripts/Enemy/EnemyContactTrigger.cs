using UnityEngine;

/// <summary>
/// 接触推开 — 挂在 Enemy 上。OverlapCircle 检测 Player，触碰时触发接触伤害 + 推开。
/// 碰撞矩阵关闭后 Trigger 回调不触发，改用 Overlap 检测。
/// </summary>
public class EnemyContactTrigger : MonoBehaviour
{
    [SerializeField] private float pushForce = 3f;
    [SerializeField] private float cooldown = 0.3f;
    [SerializeField] private float detectRadius = 0.6f;
    [SerializeField] private LayerMask playerLayer;

    private float _cooldownTimer;
    private Rigidbody2D _rb;
    private PlayerContactDamage _cachedContact;

    void Awake()
    {
        _rb = GetComponent<Rigidbody2D>();
    }

    void Update()
    {
        if (_cooldownTimer > 0f) { _cooldownTimer -= Time.deltaTime; return; }
        if (_rb == null) return;

        Collider2D hit = Physics2D.OverlapCircle(transform.position, detectRadius, playerLayer);
        if (hit == null) return;

        if (_cachedContact == null || _cachedContact.gameObject != hit.gameObject)
            _cachedContact = hit.GetComponent<PlayerContactDamage>();
        if (_cachedContact == null) return;

        _cachedContact.OnEnemyContact();

        float dir = transform.position.x > hit.transform.position.x ? 1f : -1f;
        _rb.AddForce(Vector2.right * dir * pushForce, ForceMode2D.Impulse);

        _cooldownTimer = cooldown;
    }
}
