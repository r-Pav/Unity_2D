using UnityEngine;

/// <summary>
/// 近战攻击组件 — 挂在敌人身上，通过 IEnemyAttack 接口被 EnemyController 调用
/// 攻击范围由 MeleeRangeIndicator 的 Transform 定义
/// </summary>
public class EnemyMeleeAttack : MonoBehaviour, IEnemyAttack
{
    [SerializeField] private float damage = 1f;
    [SerializeField] private Color attackFlashColor = Color.white;
    [SerializeField] private float attackFlashDuration = 0.08f;

    [Header("近战范围指示器")]
    [Tooltip("拖入敌人下的攻击范围 GameObject（挂 MeleeRangeIndicator）")]
    [SerializeField] private MeleeRangeIndicator rangeIndicator;

    public void PerformAttack(EnemyControllerBase owner)
    {
        if (rangeIndicator == null) return;

        // rangeIndicator 位置跟随 Enemy facing（已由 AttackState 在调用前同步）
        Vector3 lp = rangeIndicator.transform.localPosition;
        lp.x = Mathf.Abs(lp.x) * owner.Facing;
        rangeIndicator.transform.localPosition = lp;

        owner.IsInAttackFrame = true;

        try
        {
            Collider2D[] hits = MeleeHitDetector.Detect(rangeIndicator, ~0);

            foreach (var col in hits)
            {
                var pc = col.GetComponent<PlayerController>();
                if (pc == null) continue;

                Vector2 attackDir = ((Vector2)(pc.transform.position - owner.transform.position)).normalized;
                pc.TakeDamageWithKnockback(damage, attackDir);
                break; // 只打第一个命中的 player
            }

            rangeIndicator.Flash();
            owner.FlashColor(attackFlashColor, attackFlashDuration);
        }
        finally
        {
            owner.IsInAttackFrame = false;
        }
    }
}
