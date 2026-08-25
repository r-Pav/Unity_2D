using UnityEngine;

// ============================================================
// FirstBoss — 第一个 Boss（"石卫"），大型近战守卫
// ============================================================

/// <summary>
/// FirstBoss — 继承 BossControllerBase，实现三阶段（P1/P2/P3）行为切换。
/// 特殊技能由 BossSkillSlots + BossAttackSO 管理，普攻走 defaultMelee fallback。
/// 技能选择覆写为权重池模式。
/// </summary>
public class FirstBoss : BossControllerBase
{
    // ============================================================
    // Inspector — 基础属性（倍率）
    // ============================================================

    [Header("基础属性（倍率）")]
    [SerializeField] private float hpMultiplier = 12f;
    [SerializeField] private float moveSpeedMultiplier = 0.5f;
    [SerializeField] private float attackRangeMultiplier = 1.5f;

    // ============================================================
    // Inspector — 阶段移速倍率
    // ============================================================

    [Header("阶段移速倍率")]
    [SerializeField] private float p2MoveSpeedMult = 1.2f;
    [SerializeField] private float p3MoveSpeedMult = 1.5f;

    // ============================================================
    // Inspector — 技能权重（对应 allSkills 数组索引）
    // ============================================================

    [Header("技能权重")]
    [Tooltip("P1 权重数组（对应 allSkills 中 P1 已解锁的技能 index）")]
    [SerializeField] private float[] p1Weights = { 5f, 3f };

    [Tooltip("P2 权重数组")]
    [SerializeField] private float[] p2Weights = { 4f, 3f, 2f, 3f };

    [Tooltip("P3 权重数组")]
    [SerializeField] private float[] p3Weights = { 3f, 2f, 1f, 2f, 4f };

    // ============================================================
    // Inspector — 阶段切换 VFX
    // ============================================================

    [Header("阶段切换 VFX")]
    [Tooltip("P1→P2 阶段切换 VFX（进入 P2 时生成）")]
    [SerializeField] private GameObject phaseP2VFXPrefab;
    [Tooltip("P2→P3 阶段切换 VFX（进入 P3 时生成）")]
    [SerializeField] private GameObject phaseP3VFXPrefab;

    // ============================================================
    // 运行时状态
    // ============================================================

    private float currentMoveSpeedMult = 1.0f;

    // ============================================================
    // 抽象方法实现
    // ============================================================

    protected override IState GetInitialState() => new BossIdleState(this, Fsm, Animator);
    public override IState CreateChaseState() => new BossChaseState(this, Fsm, Animator);
    public override IState CreateFallbackState() => new BossIdleState(this, Fsm, Animator);

    // ============================================================
    // 生命周期
    // ============================================================

    protected override void Awake()
    {
        base.Awake();

        // [Boss 单独设计] EnemyConfigSO 已 Lv 收敛化（不含 Boss 专属字段），此覆盖块注释保留——
        // 后续剥离到独立 BossConfigSO 时恢复：hpMultiplier/moveSpeedMultiplier/attackRangeMultiplier/
        // p2MoveSpeedMult/p3MoveSpeedMult 从 BossConfigSO 读取。
        // if (config != null)
        // {
        //     hpMultiplier = config.hpMultiplier;
        //     moveSpeedMultiplier = config.moveSpeedMultiplier;
        //     attackRangeMultiplier = config.attackRangeMultiplier;
        //     p2MoveSpeedMult = config.p2MoveSpeedMult;
        //     p3MoveSpeedMult = config.p3MoveSpeedMult;
        // }

        // 应用基础属性倍率
        maxHealth *= hpMultiplier;
        currentHealth = maxHealth;
        initialMaxHealth = maxHealth;

        currentMoveSpeedMult = 1.0f;
    }

    private new void Start()
    {
        stunState = new EnemyStunState(this, Fsm);
        SetStunState(stunState);
        base.Start();
    }

    // ============================================================
    // 阶段切换覆写
    // ============================================================

    protected override void OnPhaseChanged(int newPhase)
    {
        // 同步技能槽阶段
        if (skillSlots != null)
            skillSlots.SetPhase(newPhase);

        switch (newPhase)
        {
            case 1: // 进入 P2
                currentMoveSpeedMult = p2MoveSpeedMult;
                // P2 首次必定展示冲撞（index=2，即 allSkills 中第 3 个技能）
                StartCoroutine(DelayedForceSkill(2, 0.5f));
                // 阶段切换 VFX
                if (phaseP2VFXPrefab != null)
                    VFXSpawner.SpawnOnBoss(phaseP2VFXPrefab, transform.position);
                break;

            case 2: // 进入 P3
                currentMoveSpeedMult = p3MoveSpeedMult;
                // 阶段切换 VFX
                if (phaseP3VFXPrefab != null)
                    VFXSpawner.SpawnOnBoss(phaseP3VFXPrefab, transform.position);
                break;
        }
    }

    /// <summary>延迟 ForceSkill（等阶段无敌协程进入后再触发）</summary>
    private System.Collections.IEnumerator DelayedForceSkill(int index, float delay)
    {
        yield return new WaitForSeconds(delay);
        ForceSkill(index);
    }

    // ============================================================
    // 技能选择覆写 — 加权随机（权重池模式）
    // ============================================================

    protected override int SelectSkillIndex(int[] available)
    {
        if (available == null || available.Length == 0) return -1;

        float[] weights = currentPhase switch
        {
            0 => p1Weights,
            1 => p2Weights,
            _ => p3Weights
        };

        // 计算总权重
        float totalWeight = 0f;
        foreach (int idx in available)
        {
            if (idx >= 0 && idx < weights.Length)
                totalWeight += weights[idx];
        }

        if (totalWeight <= 0f)
        {
            // 无有效权重：均匀随机
            return available[Random.Range(0, available.Length)];
        }

        // 加权随机
        float roll = Random.Range(0f, totalWeight);
        float cumulative = 0f;
        foreach (int idx in available)
        {
            if (idx >= weights.Length) continue;
            cumulative += weights[idx];
            if (roll < cumulative)
                return idx;
        }

        // fallback: 最后一个可用技能
        return available[available.Length - 1];
    }

    // ============================================================
    // 公开属性（FSM 状态使用）
    // ============================================================

    /// <summary>是否正在攻击中（委托给技能槽）</summary>
    public bool IsAttacking => skillSlots != null && skillSlots.IsExecuting;

    /// <summary>当前移动速度</summary>
    public float CurrentMoveSpeed => baseMoveSpeed * moveSpeedMultiplier * currentMoveSpeedMult;

    // ============================================================
    // 移动覆写 — 使用 Boss 当前速度
    // ============================================================

    protected override void Move(float direction)
    {
        SetVelocity(x: direction * CurrentMoveSpeed);
    }

    // ============================================================
    // Gizmos
    // ============================================================

#if UNITY_EDITOR
    protected override void OnDrawGizmosSelected()
    {
        base.OnDrawGizmosSelected();

        // 扩展后的攻击范围
        float rangeX = attackWidth * attackRangeMultiplier;
        float rangeY = attackHeight * attackRangeMultiplier;

        Gizmos.color = new Color(1f, 0f, 0f, 0.3f);
        Gizmos.DrawWireCube(transform.position, new Vector3(rangeX, rangeY, 0.01f));
        Gizmos.color = new Color(1f, 0f, 0f, 0.06f);
        Gizmos.DrawCube(transform.position, new Vector3(rangeX, rangeY, 0.01f));
    }
#endif
}
