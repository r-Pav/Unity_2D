using UnityEngine;

/// <summary>
/// 树 B B-02 线（冲刺落点攻击幻象 + 沿途攻击）执行器（技能组阶段 6）— 非 ISkillExecutor：
/// 树 B 是「升级解锁被动型」，不注册激活执行器（注册表不处理树 B，lv2Right/lv3Right behaviorId 留空）。
/// 订阅两个事件（与 DashIllusionExecutor 同款 RuntimeInitializeOnLoadMethod 模式）：
///   a. SkillLevelChangedEvent：TreeB_Dash 升到 Lv2 且 chosenBranch=="Right" → 启用「冲刺落点攻击幻象」；
///      Lv3 且 branch=="Right" → 追加「沿途攻击 + 冲刺距离增加」（参数从 lv2Right/lv3Right 资产读取）。
///   b. DashEndedEvent：启用后每次冲刺结束在落点（e.endPosition，阶段 6 起事件携带落点）生成攻击幻象
///      （lv2/lv3 都生成；lv3 额外执行沿途采样攻击）。
/// 读档恢复：SkillManager.SetSlot 触发 SkillLevelChangedEvent → 自动重新应用（手册 8.1）。
/// 分支判断：Resources.Load&lt;ActiveSkillData&gt;("Skills/Active/Skill_Active_E") 与槽位共享同一 SO 实例
/// （BranchUpgradeSystem 先写 chosenBranch 再发事件，此处读到的是最新分支）。
/// </summary>
public class DashComboExecutor
{
    private const string TreeBDashSkillName = "TreeB_Dash";
    private const string TreeBAssetPath = "Skills/Active/Skill_Active_E";

    // 沿途攻击「格宽」/判定盒 / 距离修饰（数值调优项，手册 11.6：lv3B-02 格宽待统一调）
    private const float PathCellWidth = 1f;
    private static readonly Vector2 PathHitBoxSize = new Vector2(1.2f, 1.0f);
    private const float PathKnockbackForce = 3f;
    private const float Lv3DistanceMultiplier = 1.5f;
    private const float IllusionLifetime = 5f;

    /// <summary>是否已启用「冲刺落点攻击幻象」（Lv2+Right 后 true；Init 时复位）</summary>
    private static bool attackIllusionEnabled;

    /// <summary>攻击幻象生成参数（Lv2 由 SkillLevelChangedEvent 从 lv2Right 分支资产读取）</summary>
    private static AttackIllusionConfig cachedAttackConfig;

    /// <summary>是否已启用「沿途攻击 + 距离增加」（Lv3+Right 后 true）</summary>
    private static bool pathAttackEnabled;

    /// <summary>沿途攻击参数（lv3Right 分支资产；damage = 每格伤害,range = 判定参考半径）</summary>
    private static ActiveSkillData.ActiveBranchData cachedPathBranch;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Init()
    {
        // 复位运行时状态（防 domain reload 关闭时上次运行的残留标记）
        attackIllusionEnabled = false;
        cachedAttackConfig = default;
        pathAttackEnabled = false;
        cachedPathBranch = null;

        // 与 DashIllusionExecutor 同款：静态订阅，场景切换 EventBus.Clear() 后由本入口重新订阅
        EventBus.Subscribe<SkillLevelChangedEvent>(OnSkillLevelChanged);
        EventBus.Subscribe<DashEndedEvent>(OnDashEnded);
    }

    private static void OnSkillLevelChanged(SkillLevelChangedEvent e)
    {
        if (e.skillName != TreeBDashSkillName || e.newLevel < 2) return;

        // 分支判断：右分支（B-02 攻击幻象线）。chosenBranch 是 SO 共享实例运行时字段，槽位升级时已写入
        var tree = Resources.Load<ActiveSkillData>(TreeBAssetPath);
        if (tree == null || tree.chosenBranch != "Right") return;

        // Lv2：启用「落点攻击幻象」（参数读 lv2Right 分支资产；lv3 沿用同一落点幻象）
        if (e.newLevel >= 2 && !attackIllusionEnabled)
        {
            var branch = tree.GetBranchData(2); // Lv2 Right → lv2Right
            if (branch != null)
            {
                attackIllusionEnabled = true;
                cachedAttackConfig = new AttackIllusionConfig
                {
                    lifetime = IllusionLifetime,
                    hitDamage = branch.damage > 0f ? branch.damage : 15f, // lv2Right.damage
                    hitInterval = 0.5f,
                    hitCount = 3,
                    hitBoxSize = new Vector2(1.4f, 1.2f),
                    hitForwardOffset = 0.8f,
                    facing = 1, // 生成时按冲刺方向覆写
                    fireSplitBoltOnHit = false
                };
            }
        }

        // Lv3：追加「沿途攻击 + 冲刺距离增加」（参数读 lv3Right 分支资产）
        if (e.newLevel >= 3)
        {
            var branch3 = tree.GetBranchData(3); // Lv3 Right → lv3Right
            if (branch3 != null)
            {
                pathAttackEnabled = true;
                cachedPathBranch = branch3;

                PlayerDash dash = PlayerController.Instance != null
                    ? PlayerController.Instance.GetComponent<PlayerDash>()
                    : null;
                dash?.SetDashDistanceMultiplier(Lv3DistanceMultiplier); // 距离增加（幂等）
            }
        }
    }

    private static void OnDashEnded(DashEndedEvent e)
    {
        if (!attackIllusionEnabled && !pathAttackEnabled) return; // 未解锁 B-02（或未选右分支）：忽略

        if (attackIllusionEnabled)
        {
            // 落点生成攻击幻象（三连击在 AttackIllusion 内部自驱动；朝向 = 冲刺方向）
            AttackIllusionConfig cfg = cachedAttackConfig;
            cfg.facing = e.direction.x >= 0f ? 1 : -1;
            var mgr = IllusionManager.EnsureInstance();
            if (mgr != null)
                mgr.SpawnAttackIllusion(e.endPosition, cfg);
        }

        if (pathAttackEnabled && cachedPathBranch != null)
        {
            // 沿途攻击：冲刺路径按「每格」采样生成攻击判定（起点 → 落点）
            PerformPathAttack(e.position, e.endPosition, e.direction);
        }
    }

    /// <summary>
    /// 沿途攻击（lv3B-02）：从冲刺起点到落点按 PathCellWidth 采样，每格中心一次 OverlapBox 攻击。
    /// 复用冲刺伤害的 DamageInfo 构造（source=player 侧、element=触发时刻当前元素、走 CombatResolver）。
    /// </summary>
    private static void PerformPathAttack(Vector2 start, Vector2 end, Vector2 direction)
    {
        PlayerController pc = PlayerController.Instance;
        if (pc == null) return;
        ICombatant source = pc.GetComponent<ICombatant>();
        if (source == null) return;
        ElementModule em = pc.GetComponent<ElementModule>();
        ElementType element = em != null ? em.CurrentElement : ElementType.None;

        Vector2 dir = direction.normalized;
        if (dir.sqrMagnitude < 0.01f) dir = Vector2.right * pc.GetFacing();

        float dist = Vector2.Distance(start, end);
        if (dist <= 0.001f) return;

        int cells = Mathf.Max(1, Mathf.CeilToInt(dist / PathCellWidth));
        LayerMask enemyMask = LayerMask.GetMask("Enemy");

        for (int i = 0; i < cells; i++)
        {
            Vector2 center = start + dir * (PathCellWidth * (i + 0.5f));
            if (Vector2.Distance(start, center) > dist) center = end; // 最后一格截断到落点

            Collider2D[] hits = Physics2D.OverlapBoxAll(center, PathHitBoxSize, 0f, enemyMask);
            foreach (Collider2D col in hits)
            {
                if (col == null) continue;
                EnemyControllerBase enemy = col.GetComponentInParent<EnemyControllerBase>();
                if (enemy == null || enemy.IsDead || !enemy.CanBeDamaged) continue;

                CombatResolver.Resolve(source, enemy, new DamageInfo
                {
                    amount = cachedPathBranch.damage,               // lv3Right.damage（每格伤害）
                    source = source,
                    sourcePosition = start,
                    attackLabel = "DashPath",
                    knockback = new Knockback
                    {
                        direction = dir,
                        force = PathKnockbackForce,
                        duration = 0f,
                        ignoreResistance = false
                    },
                    element = element,                              // 触发时刻读取（决策 N5）
                    canTriggerElementProc = true,                   // player 侧攻击可触发元素 proc
                    critMultiplier = 0f                             // 沿途不做暴击仲裁
                });
            }
        }
    }
}
