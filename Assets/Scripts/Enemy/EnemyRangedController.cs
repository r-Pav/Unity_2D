using UnityEngine;

/// <summary>
/// 远程敌人控制器 — 继承 EnemyControllerBase，3 状态 FSM（Idle → Chase → Attack，无 Patrol）。
/// 通过三级距离策略保持与玩家的射击距离，在 retreatWidth/Height~attackWidth/Height 之间远程射击。
/// </summary>
public class EnemyRangedController : EnemyControllerBase
{
    // ============================================================
    // 配置参数
    // ============================================================

    [Header("后退策略 — 矩形")]
    [Tooltip("后退矩形半宽（X 轴，低于此距离后退；0 = 未设置）")]
    [SerializeField] private float retreatWidth = 0f;
    [Tooltip("后退矩形半高（Y 轴，低于此距离后退；0 = 未设置）")]
    [SerializeField] private float retreatHeight = 0f;
    [Tooltip("恢复追击矩形半宽（X 轴，迟滞区间上限；0 = 未设置）")]
    [SerializeField] private float retreatRecoverWidth = 0f;
    [Tooltip("恢复追击矩形半高（Y 轴，迟滞区间上限；0 = 未设置）")]
    [SerializeField] private float retreatRecoverHeight = 0f;

    /// <summary>retreat 内置默认（Inspector 与 SO 均未设置时兜底）</summary>
    private const float DefaultRetreatWidth = 3f;
    private const float DefaultRetreatHeight = 3f;
    private const float DefaultRetreatRecoverWidth = 10f;
    private const float DefaultRetreatRecoverHeight = 6f;

    /// <summary>暴露后退矩形半宽给攻击组件（EnemyRangedAttack）读取</summary>
    public float RetreatWidth => retreatWidth;
    /// <summary>暴露后退矩形半高给攻击组件（EnemyRangedAttack）读取</summary>
    public float RetreatHeight => retreatHeight;
    /// <summary>暴露恢复追击矩形半宽给状态类（RangedChaseState）读取</summary>
    public float RetreatRecoverWidth => retreatRecoverWidth;
    /// <summary>暴露恢复追击矩形半高给状态类（RangedChaseState）读取</summary>
    public float RetreatRecoverHeight => retreatRecoverHeight;

    // ============================================================
    // 抽象方法实现
    // ============================================================

    protected override IState GetInitialState() => new RangedIdleState(this, Fsm, Animator);
    public override IState CreateChaseState() => new RangedChaseState(this, Fsm, Animator);
    public override IState CreateFallbackState() => new RangedIdleState(this, Fsm, Animator);

    // ============================================================
    // 生命周期
    // ============================================================

    protected new void Start()
    {
        // [Lv 收敛] retreat 矩形：Inspector(>0) → SO 对应 Lv 档 → 内置默认（0 = 未设置）
        retreatWidth = Resolve(retreatWidth, LvStats?.retreatWidth ?? 0f, DefaultRetreatWidth);
        retreatHeight = Resolve(retreatHeight, LvStats?.retreatHeight ?? 0f, DefaultRetreatHeight);
        retreatRecoverWidth = Resolve(retreatRecoverWidth, LvStats?.retreatRecoverWidth ?? 0f, DefaultRetreatRecoverWidth);
        retreatRecoverHeight = Resolve(retreatRecoverHeight, LvStats?.retreatRecoverHeight ?? 0f, DefaultRetreatRecoverHeight);
        // 顺序坑 B：attackWidth/attackHeight 不再在此硬编码 10/6 — 改由基类 Awake 解析提供（远程资产填 10/6）

        stunState = new EnemyStunState(this, Fsm);
        SetStunState(stunState);
        base.Start();
    }

    // ============================================================
    // 覆盖：基类攻击条件 + 不在后退矩形区内
    // ============================================================

    public override bool CanAttack()
    {
        if (!base.CanAttack()) return false;
        if (player == null) return false;

        float deltaX = player.position.x - transform.position.x;
        float deltaY = player.position.y - transform.position.y;
        // 玩家不在后退矩形内（X 或 Y 超出后退半边界）才能攻击
        return Mathf.Abs(deltaX) >= retreatWidth * 0.5f || Mathf.Abs(deltaY) >= retreatHeight * 0.5f;
    }

    // ============================================================
    // 辅助：检查玩家是否在指定矩形内
    // ============================================================

    /// <summary>玩家是否在指定矩形内（供状态类 RangedChaseState 调用）</summary>
    public bool InRect(float w, float h)
    {
        if (player == null) return false;
        float dx = Mathf.Abs(player.position.x - transform.position.x);
        float dy = Mathf.Abs(player.position.y - transform.position.y);
        return dx <= w * 0.5f && dy <= h * 0.5f;
    }

    // ============================================================
    // Gizmos
    // ============================================================

#if UNITY_EDITOR
    protected override void OnDrawGizmosSelected()
    {
        base.OnDrawGizmosSelected();

        Vector3 pos = transform.position;
        float hw, hh;

        // 后退矩形（蓝色线框）
        hw = retreatWidth * 0.5f;
        hh = retreatHeight * 0.5f;
        DrawWireRect(pos, hw, hh, new Color(0f, 0.5f, 1f, 0.4f));

        // 恢复追击矩形（绿色线框，迟滞区间上限）
        hw = retreatRecoverWidth * 0.5f;
        hh = retreatRecoverHeight * 0.5f;
        DrawWireRect(pos, hw, hh, new Color(0f, 1f, 0f, 0.3f));
    }

    private static void DrawWireRect(Vector3 center, float halfW, float halfH, Color color)
    {
        Gizmos.color = color;
        Vector3 tl = center + new Vector3(-halfW,  halfH, 0f);
        Vector3 tr = center + new Vector3( halfW,  halfH, 0f);
        Vector3 br = center + new Vector3( halfW, -halfH, 0f);
        Vector3 bl = center + new Vector3(-halfW, -halfH, 0f);
        Gizmos.DrawLine(tl, tr);
        Gizmos.DrawLine(tr, br);
        Gizmos.DrawLine(br, bl);
        Gizmos.DrawLine(bl, tl);
    }
#endif
}
