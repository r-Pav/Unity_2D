using UnityEngine;

/// <summary>
/// 接触伤害 — 挂在 Player 上。Enemy 的 ContactTrigger 触碰时调用 OnEnemyContact()。
/// 走完整 TakeDamage 管线 + 无敌帧防止连续触发。无敌期间不影响攻击/移动。
/// </summary>
public class PlayerContactDamage : MonoBehaviour
{
    [SerializeField] private float contactDamage = 1f;
    [SerializeField] private float invincibilityDuration = 1f;

    private PlayerHealth _health;
    private bool _invincible;

    void Awake()
    {
        _health = GetComponent<PlayerHealth>();
    }

    /// <summary>由 EnemyContactTrigger 调用</summary>
    public void OnEnemyContact()
    {
        if (_invincible) return;
        if (_health == null || _health.IsDead) return;

        _health.TakeDamage(contactDamage);
        StartCoroutine(InvincibilityRoutine());
    }

    private System.Collections.IEnumerator InvincibilityRoutine()
    {
        _invincible = true;
        yield return new WaitForSeconds(invincibilityDuration);
        _invincible = false;
    }
}
