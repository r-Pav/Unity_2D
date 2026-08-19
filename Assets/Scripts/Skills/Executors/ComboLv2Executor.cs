using UnityEngine;

/// <summary>
/// 合成技能 lv2 执行器（技能组阶段 6）— 4 个 lv2 配方，实现 ISkillExecutor，按【产物 skillName】注册
/// （合成技能走注册表 skillName 通道，BehaviorId = 产物 skillName，B8 命名含等级防 SkillPool 覆盖）。
///
/// 配方 → 效果（材料读取：树 A = Skill_Active_Q，树 B = Skill_Active_E；参数一律读分支资产，手册 0.5.4）：
///   ① Combo_A01B01_Lv2（Q左+E左）：嘲讽幻象 + 幻象持续发射魔法弹（幻象挂 IllusionBoltEmitter 简化发射器）
///   ② Combo_A01B02_Lv2（Q左+E右）：落点攻击幻象三连击，每击发射分裂弹（AttackIllusion.fireSplitBoltOnHit）
///   ③ Combo_A02B01_Lv2（Q右+E左）：传送弹 + 传送后原位生成嘲讽幻象（复用阶段 5 TeleportBolt / PlayerTeleport）
///   ④ Combo_A02B02_Lv2（Q右+E右）：传送弹 + 落点攻击幻象三连击
///
/// 传送弹二次激活与 CD：照抄 TeleportBoltExecutor 模式 —— 发射时 SetPendingReactivation(skillName,true)，
/// SkillManager 在 CD 期间对该技能放行（再按技能键 = 传送）；挂起标记在传送使用 / 玩家死亡 / 弹回池时清除。
/// </summary>
public class ComboLv2Executor : ISkillExecutor
{
    // ============================================================
    // 注册用行为标识（= 产物 skillName；合成技能走注册表 skillName 通道）
    // ============================================================

    private readonly string _skillName;

    public string BehaviorId => _skillName;

    public ComboLv2Executor(string skillName)
    {
        _skillName = skillName;
    }

    // ============================================================
    // 传送弹运行时状态（A02B0x 两实例共用：单玩家,同一时刻只有一颗挂起弹）
    // ============================================================

    private static TeleportBolt _activeBolt;
    private static string _pendingSkill; // 当前挂起弹所属合成技能（区分 A02B01/B02 的二次激活归属）

    // ============================================================
    // 发射配置（代码内可调；数值调优项，手册 11.6）
    // ============================================================

    public float boltSpeed = 12f;
    public float boltMaxDistance = 12f;
    public float boltRadius = 0.25f;
    public Color boltColor = new Color(0.75f, 0.35f, 1f, 1f); // 紫色,与传送弹一致
    public Vector2 spawnOffset = new Vector2(0.6f, 0.4f);
    public float emitterInterval = 1f;      // A01B01 幻象魔法弹发射间隔
    public float illusionLifetime = 5f;
    private const float FireCritMultiplier = 2.0f; // 火元素 200% 仲裁（与 MagicBoltExecutor 同值）

    // 资产路径（与 CombinationCraftSystem 的 Resources 路径一致）
    private const string TreeAPath = "Skills/Active/Skill_Active_Q";
    private const string TreeBPath = "Skills/Active/Skill_Active_E";

    // 场景加载后注册 4 个 lv2 合成执行器（注册表未挂载时自动缓冲；Register 幂等）
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void RegisterAll()
    {
        SkillExecutorRegistry.Register(new ComboLv2Executor("Combo_A01B01_Lv2"));
        SkillExecutorRegistry.Register(new ComboLv2Executor("Combo_A01B02_Lv2"));
        SkillExecutorRegistry.Register(new ComboLv2Executor("Combo_A02B01_Lv2"));
        SkillExecutorRegistry.Register(new ComboLv2Executor("Combo_A02B02_Lv2"));
    }

    // ============================================================
    // 分发（合成技能执行器：branch 恒为 null,只读 data 无分支语义,按 skillName 分流）
    // ============================================================

    public void Execute(SkillActivatedEvent e, SkillData data, ActiveSkillData.ActiveBranchData branch)
    {
        GameObject playerGo = e.source != null ? e.source : PlayerController.Instance?.gameObject;
        if (playerGo == null) return;

        switch (_skillName)
        {
            case "Combo_A01B01_Lv2": ExecuteA01B01(playerGo); break;
            case "Combo_A01B02_Lv2": ExecuteA01B02(playerGo); break;
            case "Combo_A02B01_Lv2": ExecuteA02(playerGo, IllusionKind.Taunt); break;
            case "Combo_A02B02_Lv2": ExecuteA02(playerGo, IllusionKind.Attack); break;
            default: break; // 未知 skillName 静默跳过（注册表已按 skillName 分发,此分支为双保险）
        }
    }

    // ============================================================
    // ① A01B01（Q左+E左）：嘲讽幻象 + 幻象持续发射魔法弹
    // ============================================================

    private void ExecuteA01B01(GameObject playerGo)
    {
        var mgr = IllusionManager.EnsureInstance();
        if (mgr == null) return;

        var taunt = mgr.SpawnTauntIllusion(playerGo.transform.position, BuildTauntConfig());
        if (taunt == null) return;

        // 幻象挂简化发射器：持续向最近 enemy 发射魔法弹（随幻象销毁自动停止）
        var emitter = taunt.gameObject.AddComponent<IllusionBoltEmitter>();
        var aBranch = GetBranchSide(LoadTreeA(), 2, "Left");
        emitter.interval = emitterInterval;
        emitter.damage = aBranch != null && aBranch.damage > 0f ? aBranch.damage : 25f; // Q lv2Left.damage
        emitter.speed = 12f;
        emitter.radius = 0.25f;
        emitter.color = Color.cyan;
    }

    // ============================================================
    // ② A01B02（Q左+E右）：落点攻击幻象三连击,每击发射分裂弹
    // ============================================================

    private void ExecuteA01B02(GameObject playerGo)
    {
        var mgr = IllusionManager.EnsureInstance();
        if (mgr == null) return;

        PlayerController pc = playerGo.GetComponent<PlayerController>();
        AttackIllusionConfig cfg = BuildAttackConfig(splitBolt: true); // 每击发射分裂弹
        cfg.facing = pc != null ? pc.GetFacing() : 1;
        mgr.SpawnAttackIllusion(playerGo.transform.position, cfg);
    }

    // ============================================================
    // ③④ A02B01 / A02B02（Q右+E左/右）：传送弹 → 二次激活传送到弹位置 → 落点生成幻象
    // ============================================================

    private enum IllusionKind { Taunt, Attack }

    private void ExecuteA02(GameObject playerGo, IllusionKind kind)
    {
        if (_activeBolt != null && _activeBolt.IsActive && _pendingSkill == _skillName)
            DoTeleport(playerGo, kind);      // 二次激活：传送 + 落点幻象
        else
            FireTeleportBolt(playerGo);       // 首次激活：发射传送弹（悬停等待传送）
    }

    /// <summary>发射传送弹（照抄 TeleportBoltExecutor.DoFire：元素快照 + 火仲裁 + 挂起二次激活标记）</summary>
    private void FireTeleportBolt(GameObject playerGo)
    {
        // 清理已失效的旧弹（防极端情况下悬挂空引用）
        if (_activeBolt != null)
        {
            if (_activeBolt.IsActive) _activeBolt.Cancel();
            _activeBolt = null;
        }

        PlayerController pc = playerGo.GetComponent<PlayerController>();
        int facing = pc != null ? pc.GetFacing() : 1;
        Vector2 dir = Vector2.right * facing;
        Vector2 pos = (Vector2)playerGo.transform.position + new Vector2(spawnOffset.x * facing, spawnOffset.y);

        // 元素：发射时读 ElementModule.CurrentElement（决策 N5,伤害实例按触发时刻 = 发射时刻）
        ElementType element = ElementType.None;
        ElementModule em = playerGo.GetComponent<ElementModule>();
        if (em != null) element = em.CurrentElement;

        // 倍率仲裁（与 MagicBoltExecutor 同规则）：传送弹无必暴来源,火元素 proc 200% 仲裁胜出
        float mult = 1f;
        if (element == ElementType.Fire && Random.value < ElementProc.ProcChance)
            mult = Mathf.Max(mult, FireCritMultiplier);

        var aBranch = GetBranchSide(LoadTreeA(), 2, "Right"); // Q lv2Right（A-02 传送弹线）damage
        float boltDamage = (aBranch != null && aBranch.damage > 0f ? aBranch.damage : 55f) * mult;
        float critMult = mult > 1f ? mult : 0f; // 0=未暴击

        // source = player 侧 ICombatant（PlayerHealth 实现）
        ICombatant source = playerGo.GetComponent<ICombatant>();

        _activeBolt = TeleportBolt.Spawn(
            position: pos,
            direction: dir,
            damage: boltDamage,
            speed: boltSpeed,
            maxDistance: boltMaxDistance,
            hitLayers: LayerMask.GetMask("Enemy"),
            wallLayers: (1 << 3) | (1 << 11), // Ground + Wall,与魔法弹同款
            sourceLayer: 1 << playerGo.layer,
            source: source,
            element: element,
            critMultiplier: critMult,
            radius: boltRadius,
            color: boltColor,
            skillName: _skillName
        );
        _pendingSkill = _skillName;

        // 挂起二次激活标记：CD 期间允许再按技能键触发传送（SkillManager 冷却检查放行）
        SkillExecutorRegistry.SetPendingReactivation(_skillName, true);
    }

    /// <summary>传送：瞬移到弹位置（贴墙钳制 + 清速度 + 无敌帧）。幻象位置按配方区分:
    /// A02B01（嘲讽幻象）= 传送前位置（原位置留嘲讽,设计如此）;A02B02（攻击幻象）= 落点</summary>
    private void DoTeleport(GameObject playerGo, IllusionKind kind)
    {
        if (_activeBolt == null || !_activeBolt.IsActive) return;
        Vector2 dest = _activeBolt.Position;
        Vector2 preTeleportPos = playerGo.transform.position; // 传送前位置（嘲讽幻象留原地）

        // 瞬移（组件缺失时运行时挂载,默认参数可用）
        PlayerTeleport teleport = playerGo.GetComponent<PlayerTeleport>();
        if (teleport == null)
            teleport = playerGo.AddComponent<PlayerTeleport>();
        teleport.TeleportTo(dest);

        // 幻象位置按配方：嘲讽型=传送前位置（enemy 在原位置附近才能被嘲讽到）；攻击型=落点
        Vector2 spawnPos = kind == IllusionKind.Taunt ? preTeleportPos : dest;
        SpawnIllusionAt(kind, spawnPos);

        // 传送使用后销毁传送弹（OnReturnToPool 清除二次激活挂起标记）
        _activeBolt.Cancel();
        _activeBolt = null;
        _pendingSkill = null;
    }

    /// <summary>落点幻象生成（按合成配方分支决定类型）</summary>
    private void SpawnIllusionAt(IllusionKind kind, Vector2 position)
    {
        var mgr = IllusionManager.EnsureInstance();
        if (mgr == null) return;

        if (kind == IllusionKind.Taunt)
            mgr.SpawnTauntIllusion(position, BuildTauntConfig());
        else
            mgr.SpawnAttackIllusion(position, BuildAttackConfig(splitBolt: false));
    }

    // ============================================================
    // 配置组装（参数一律读分支资产,手册 0.5.4）
    // ============================================================

    /// <summary>嘲讽幻象配置：E 树 lv2Left（B-01 线,range/duration）</summary>
    private TauntIllusionConfig BuildTauntConfig()
    {
        var bBranch = GetBranchSide(LoadTreeB(), 2, "Left");
        return new TauntIllusionConfig
        {
            tauntRadius = bBranch != null && bBranch.range > 0f ? bBranch.range : 3f,
            tauntDuration = bBranch != null && bBranch.duration > 0f ? bBranch.duration : 4f,
            lifetime = illusionLifetime,
            dotEnabled = false,     // lv2 合成不带 DoT
            dotDamage = 0f,
            dotInterval = 1f
        };
    }

    /// <summary>攻击幻象配置：E 树 lv2Right（B-02 线,damage）；分裂弹父弹伤害取 Q 树 lv2Left（A-01 线,damage）</summary>
    private AttackIllusionConfig BuildAttackConfig(bool splitBolt)
    {
        var bBranch = GetBranchSide(LoadTreeB(), 2, "Right");
        var aBranch = GetBranchSide(LoadTreeA(), 2, "Left");
        return new AttackIllusionConfig
        {
            lifetime = illusionLifetime,
            hitDamage = bBranch != null && bBranch.damage > 0f ? bBranch.damage : 15f,
            hitInterval = 0.5f,
            hitCount = 3,
            hitBoxSize = new Vector2(1.4f, 1.2f),
            hitForwardOffset = 0.8f,
            facing = 1, // 由调用方按玩家朝向覆写
            fireSplitBoltOnHit = splitBolt,
            splitBoltDamage = splitBolt && aBranch != null && aBranch.damage > 0f ? aBranch.damage : 25f,
            splitBoltSpeed = 10f,
            splitBoltRadius = 0.25f,
            splitBoltColor = new Color(0.2f, 0.9f, 1f, 1f)
        };
    }

    /// <summary>读取树指定分支数据（lv2Left/lv2Right 等；null 安全）</summary>
    private static ActiveSkillData.ActiveBranchData GetBranchSide(ActiveSkillData tree, int level, string side)
    {
        if (tree == null) return null;
        return level switch
        {
            2 => side == "Left" ? tree.lv2Left : tree.lv2Right,
            3 => side == "Left" ? tree.lv3Left : tree.lv3Right,
            _ => tree.lv1Data
        };
    }

    private static ActiveSkillData LoadTreeA() => Resources.Load<ActiveSkillData>(TreeAPath);
    private static ActiveSkillData LoadTreeB() => Resources.Load<ActiveSkillData>(TreeBPath);
}

/// <summary>
/// 幻象魔法弹发射器（合成技能 A01B01 用,挂嘲讽幻象上）— 简化发射器：
/// 每 interval 秒向最近 enemy 发射一枚玩家魔法弹（PlayerProjectile,元素/火仲裁与 MagicBoltExecutor 同规则）。
/// 随幻象销毁自动停止（同 GameObject 被 Destroy）。
/// </summary>
public class IllusionBoltEmitter : MonoBehaviour
{
    [Tooltip("发射间隔（秒）")]
    public float interval = 1f;
    [Tooltip("单发魔法弹伤害（Q 树 lv2Left.damage 注入）")]
    public float damage = 25f;
    [Tooltip("魔法弹飞行速度")]
    public float speed = 12f;
    [Tooltip("魔法弹半径")]
    public float radius = 0.25f;
    [Tooltip("魔法弹颜色")]
    public Color color = Color.cyan;
    [Tooltip("索敌半径（最近 enemy 检测范围）")]
    public float detectionRange = 20f;

    private ICombatant source;        // player 侧 ICombatant（DamageInfo.source）
    private ElementModule elementModule;
    private float timer;
    private LayerMask enemyMask;

    private void Awake()
    {
        if (enemyMask == 0)
            enemyMask = LayerMask.GetMask("Enemy");
        if (PlayerController.Instance != null)
        {
            source = PlayerController.Instance.GetComponent<ICombatant>();
            elementModule = PlayerController.Instance.GetComponent<ElementModule>();
        }
    }

    private void Update()
    {
        timer -= Time.deltaTime;
        if (timer > 0f) return;
        timer = interval;
        Fire();
    }

    /// <summary>向最近 enemy 发射魔法弹（无目标则跳过本次）</summary>
    private void Fire()
    {
        EnemyControllerBase target = FindNearestEnemy();
        if (target == null || target.IsDead) return;
        if (source == null) return;

        Vector2 dir = ((Vector2)target.transform.position - (Vector2)transform.position).normalized;
        if (dir.sqrMagnitude < 0.0001f) return;

        // 元素：发射时读 ElementModule.CurrentElement（决策 N5）
        ElementType element = elementModule != null ? elementModule.CurrentElement : ElementType.None;

        // 倍率仲裁（与 MagicBoltExecutor 同规则）：火元素 proc 200% 仲裁胜出
        float mult = 1f;
        if (element == ElementType.Fire && Random.value < ElementProc.ProcChance)
            mult = Mathf.Max(mult, 2.0f);
        float boltDamage = damage * mult;
        float critMult = mult > 1f ? mult : 0f;

        PlayerProjectile.Spawn(
            position: (Vector2)transform.position + dir * 0.4f,
            direction: dir,
            damage: boltDamage,
            speed: speed,
            hitLayers: enemyMask,
            radius: radius,
            color: color,
            parent: null,
            wallLayers: (1 << 3) | (1 << 11), // Ground + Wall,与魔法弹同款
            sourceLayer: PlayerController.Instance != null ? 1 << PlayerController.Instance.gameObject.layer : 0,
            source: source,
            element: element,
            critMultiplier: critMult
        );
    }

    /// <summary>查找最近的存活 enemy（检测范围 detectionRange）</summary>
    private EnemyControllerBase FindNearestEnemy()
    {
        Collider2D[] hits = Physics2D.OverlapCircleAll(transform.position, detectionRange, enemyMask);
        EnemyControllerBase best = null;
        float bestSqr = float.MaxValue;
        foreach (Collider2D col in hits)
        {
            if (col == null) continue;
            EnemyControllerBase enemy = col.GetComponentInParent<EnemyControllerBase>();
            if (enemy == null || enemy.IsDead || !enemy.CanBeDamaged) continue;
            float sqr = ((Vector2)enemy.transform.position - (Vector2)transform.position).sqrMagnitude;
            if (sqr < bestSqr)
            {
                bestSqr = sqr;
                best = enemy;
            }
        }
        return best;
    }
}
