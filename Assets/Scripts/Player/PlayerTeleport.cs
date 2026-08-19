using UnityEngine;

/// <summary>
/// 玩家传送（技能组阶段 5,树A A-02 线）— 组件挂 Player（执行器 GetComponent，缺失时 AddComponent）。
///
/// TeleportTo(Vector2)：
/// 1. 瞬移 rb.position（贴墙钳制：起点→落点射线命中墙则截断到墙面前；落点仍进墙则沿墙外推，防卡地形）
/// 2. 清速度（瞬移后不残留动量）
/// 3. 短暂无敌帧（序列化；期间 PlayerHealth.CanBeDamaged=false → CombatResolver 整条结算短路，
///    防传送进 enemy 身体瞬间被接触伤害命中，验收点 4）
/// 4. 触发 PlayerTeleportedEvent（占位，特效/音效后续订阅）
/// </summary>
public class PlayerTeleport : MonoBehaviour
{
    [Header("传送落点钳制")]
    [Tooltip("墙/地面层（Ground=3 + Wall=11；与 Projectile 墙层同款）")]
    [SerializeField] private LayerMask wallMask = (1 << 3) | (1 << 11);

    [Tooltip("落点探测半径（米）— 按玩家碰撞体半宽估，进墙判定用")]
    [SerializeField] private float probeRadius = 0.45f;

    [Tooltip("贴墙外推步长（米）")]
    [SerializeField] private float pushStep = 0.25f;

    [Tooltip("贴墙外推最大距离（米）")]
    [SerializeField] private float pushMaxDistance = 2f;

    [Header("无敌帧")]
    [Tooltip("传送落地无敌帧时长（秒）— 防传送进 enemy 身体瞬间受伤")]
    [SerializeField] private float invincibleDuration = 0.5f;

    /// <summary>传送到指定落点（自动贴墙钳制 + 清速度 + 无敌帧 + 特效事件占位）</summary>
    public void TeleportTo(Vector2 destination)
    {
        PlayerController pc = GetComponent<PlayerController>();
        if (pc == null) return;
        Rigidbody2D rb = pc.GetRigidbody();
        if (rb == null) return;

        Vector2 from = rb.position;
        Vector2 to = ResolveLandingPoint(from, destination);

        rb.position = to;
        rb.velocity = Vector2.zero;

        PlayerHealth ph = GetComponent<PlayerHealth>();
        ph?.SetInvincible(invincibleDuration);

        // 特效事件（占位；素材后续接入，订阅方自行挂特效）
        EventBus.Trigger(new PlayerTeleportedEvent(from, to));
    }

    /// <summary>
    /// 落点钳制：① 起点→落点射线，命中墙则截断到墙面前（贴墙不穿模）；
    /// ② 截断后若落点仍与墙体重叠（OverlapCircle），沿墙法线/上/左/右按步长外推，取第一个空位。
    /// </summary>
    private Vector2 ResolveLandingPoint(Vector2 from, Vector2 dest)
    {
        Vector2 delta = dest - from;
        float dist = delta.magnitude;
        Vector2 dir = dist > 0.001f ? delta / dist : Vector2.right;
        Vector2 wallNormal = Vector2.up; // 默认沿上外推（无命中信息时最安全）

        // ① 起点→落点射线截断
        if (dist > 0.001f)
        {
            RaycastHit2D hit = Physics2D.Raycast(from, dir, dist, wallMask);
            if (hit.collider != null)
            {
                if (hit.normal.sqrMagnitude > 0.01f)
                    wallNormal = hit.normal;
                dest = hit.point - dir * 0.05f; // 墙面外侧留 5cm 余量
            }
        }

        // ② 落点仍进墙 → 沿墙外推
        if (IsBlocked(dest))
        {
            Vector2[] dirs = { wallNormal.normalized, Vector2.up, Vector2.right, Vector2.left };
            for (float d = pushStep; d <= pushMaxDistance; d += pushStep)
            {
                foreach (Vector2 candidateDir in dirs)
                {
                    Vector2 candidate = dest + candidateDir * d;
                    if (!IsBlocked(candidate))
                        return candidate;
                }
            }
        }
        return dest;
    }

    private bool IsBlocked(Vector2 point)
        => Physics2D.OverlapCircle(point, probeRadius, wallMask) != null;
}
