using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 玩家快速落地模块 — 空中按 Q 砸地
/// 落地时通过 EventBus 触发 GroundPoundEvent 替代直接操作敌人
/// </summary>
public class PlayerGroundPound : MonoBehaviour
{
    // ============================================================
    // 配置参数
    // ============================================================

    [Header("快速落地")]
    [Tooltip("触发所需最低高度（离 Ground 图层）")]
    [SerializeField] private float minHeight = 2f;
    [Tooltip("下落速度")]
    [SerializeField] private float poundSpeed = 30f;
    [Tooltip("冷却时间（秒）")]
    [SerializeField] private float cooldown = 0.5f;

    [Header("伤害 & 击退")]
    [Tooltip("落地 AOE 半径")]
    [SerializeField] private float blastRadius = 2f;
    [Tooltip("AOE 伤害")]
    [SerializeField] private float blastDamage = 2f;
    [Tooltip("击退力度")]
    [SerializeField] private float knockbackForce = 10f;

    [Header("图层")]
    [Tooltip("地面图层（检测高度用）")]
    [SerializeField] private LayerMask groundLayer = 1 << 3;
    [Tooltip("敌人图层（落地 AOE 用）")]
    [SerializeField] private LayerMask enemyLayer = 1 << 9;

    [Header("落地震动")]
    [SerializeField] private float shakeDuration = 0.2f;
    [SerializeField] private float shakeMagnitude = 0.3f;

    // ============================================================
    // 运行时状态
    // ============================================================

    private float cooldownTimer;
    private bool isPounding;
    private bool wasGrounded;
    private PlayerController owner;
    private CameraFollow cachedCam;
    private float heightDebugTimer;
    private HashSet<Collider2D> hitEnemies = new HashSet<Collider2D>();

    // ============================================================
    // 生命周期
    // ============================================================

    private void Awake()
    {
        cachedCam = CameraFollow.Instance;
    }

    // ============================================================
    // 父类调用接口
    // ============================================================

    public void OnPlayerUpdate(PlayerController pc)
    {
        owner = pc;
        UpdateTimers();

        bool grounded = pc.IsGrounded();

        if (isPounding)
        {
            HandlePoundState(pc, grounded);
            wasGrounded = grounded;
            return;
        }

        wasGrounded = grounded;
        HandleInput(pc, grounded);
    }

    private void UpdateTimers()
    {
        if (cooldownTimer > 0f) cooldownTimer -= Time.deltaTime;
    }

    private void HandlePoundState(PlayerController pc, bool grounded)
    {
        if (grounded && !wasGrounded)
            OnLand(pc);
        else
            HandleMidairEnemyCollisions(pc);
    }

    private void HandleMidairEnemyCollisions(PlayerController pc)
    {
        Collider2D[] hits = Physics2D.OverlapCircleAll((Vector2)pc.transform.position, blastRadius, enemyLayer);
        foreach (Collider2D hitEnemy in hits)
        {
            if (hitEnemies.Contains(hitEnemy)) continue;
            hitEnemies.Add(hitEnemy);

            Rigidbody2D enemyRb = hitEnemy.GetComponent<Rigidbody2D>();
            if (enemyRb == null) continue;

            float randomDir = Random.value > 0.5f ? 1f : -1f;
            Vector2 knockDir = new Vector2(randomDir, 1.5f).normalized;
            enemyRb.AddForce(knockDir * knockbackForce * 1.5f, ForceMode2D.Impulse);
        }
    }

    private void HandleInput(PlayerController pc, bool grounded)
    {
        if (!Input.GetKeyDown(KeyCode.S)) return;

        float height = GetHeightAboveGround(grounded);
        // Debug.Log($"[GroundPound] Q pressed | cooldownTimer={cooldownTimer:F3} (limit={0}) | grounded={grounded} | height={height:F2} (min={minHeight}) | RESULT={cooldownTimer <= 0f && !grounded && height >= minHeight}");

        if (cooldownTimer <= 0f && !grounded && height >= minHeight && !pc.IsTouchingWall)
            StartPound(pc);
    }

    // ============================================================
    // 快速落地
    // ============================================================

    private void StartPound(PlayerController pc)
    {
        isPounding = true;
        cooldownTimer = cooldown;
        hitEnemies.Clear();

        // 不再调用 IgnoreLayerCollision：Player 与 Enemy 层的碰撞由 Project Settings 矩阵控制
        // （旧代码落地时 false 会强制重开碰撞，覆盖矩阵设置）
        Rigidbody2D rb = pc.GetRigidbody();
        rb.velocity = new Vector2(0f, -poundSpeed);
    }

    private void OnLand(PlayerController pc)
    {
        isPounding = false;
        hitEnemies.Clear();

        // ── 相机震动（直接调用，保留现有逻辑）──
        if (cachedCam == null) cachedCam = CameraFollow.Instance;
        if (cachedCam != null) cachedCam.Shake(shakeDuration, shakeMagnitude);

        // ── 缩放脉冲 ──
        StopAllCoroutines();
        StartCoroutine(PoundSquash(pc.transform));

        // ── 通过事件总线广播 AOE（EnemyController 等自行订阅处理） ──
        EventBus.Trigger(new GroundPoundEvent(
            center: (Vector2)pc.transform.position,
            radius: blastRadius,
            damage: blastDamage,
            knockbackForce: knockbackForce,
            targetLayers: enemyLayer
        ));
    }

    // ============================================================
    // 高度检测（对 Ground 图层）
    // ============================================================

    private float GetHeightAboveGround(bool grounded)
    {
        if (owner == null) return 0f;

        Collider2D col = owner.Col;
        if (col == null) return 0f;

        Vector2 origin = new Vector2(col.bounds.center.x, col.bounds.min.y);

        RaycastHit2D hit = Physics2D.Raycast(origin, Vector2.down, 100f, groundLayer);

        // ── Height debug (rate-limited, only when airborne) ──
        if (!grounded)
        {
            heightDebugTimer -= Time.deltaTime;
            if (heightDebugTimer <= 0f)
            {
                heightDebugTimer = 0.3f;
                Debug.LogWarning($"[GroundPound] origin=({origin.x:F2}, {origin.y:F2}) groundLayer={groundLayer.value} hit={hit.collider?.name ?? "null"} distance={hit.distance:F2}");
            }
        }

        if (hit.collider != null)
            return hit.distance;

        return 0f;
    }

    // ============================================================
    // 缩放脉冲效果
    // ============================================================

    private System.Collections.IEnumerator PoundSquash(Transform t)
    {
        Vector3 original = t.localScale;
        int dir = owner != null ? owner.GetFacing() : 1;
        float duration = 0.15f;
        float half = duration * 0.5f;

        for (float timer = 0f; timer < half; timer += Time.deltaTime)
        {
            float p = timer / half;
            t.localScale = new Vector3(
                Mathf.Abs(original.x) * dir * (1f + p * 0.3f),
                original.y * (1f - p * 0.3f),
                original.z);
            yield return null;
        }

        for (float timer = 0f; timer < half; timer += Time.deltaTime)
        {
            float p = timer / half;
            t.localScale = new Vector3(
                Mathf.Abs(original.x) * dir * (1f + (1f - p) * 0.3f),
                original.y * (1f - (1f - p) * 0.3f),
                original.z);
            yield return null;
        }

        t.localScale = original;
    }

    // ============================================================
    // Gizmos
    // ============================================================

    private void OnDrawGizmosSelected()
    {
        Vector3 pos = transform.position;

        Gizmos.color = new Color(1f, 0.5f, 0f, 0.3f);
        Vector3 bottom = pos - Vector3.up * minHeight;
        Gizmos.DrawLine(pos + Vector3.left * 0.5f, pos + Vector3.right * 0.5f);
        Gizmos.DrawLine(bottom + Vector3.left * 0.5f, bottom + Vector3.right * 0.5f);
        Gizmos.DrawLine(pos, bottom);

        if (isPounding)
        {
            Gizmos.color = Color.red;
            Gizmos.DrawRay(pos, Vector3.down * 3f);
        }

        Gizmos.color = new Color(1f, 0.2f, 0f, 0.15f);
        Gizmos.DrawSphere(pos, blastRadius);
        Gizmos.color = new Color(1f, 0.2f, 0f, 0.5f);
        Gizmos.DrawWireSphere(pos, blastRadius);
    }
}
