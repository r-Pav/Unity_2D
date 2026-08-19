using UnityEngine;

/// <summary>
/// 树 A A-02 线执行器（技能组阶段 5）— 注册 treeA_teleport（lv2Right）/ treeA_teleport_heal（lv3Right）。
/// 照抄 MagicBoltExecutor 模式：单 behaviorId 实例 ×2，注册表按分支 behaviorId 分发，不订阅 EventBus。
///
/// 激活型技能每次按技能键触发一次；执行器内部状态区分「发射 / 传送」：
///   - 有未使用的传送弹（_activeBolt 在场）→ 按技能键 = 传送（瞬移到弹位置，弹销毁）
///   - 否则 → 按技能键 = 发射（持续传送弹，悬停不消失）
///
/// lv3（treeA_teleport_heal）：发射时 DamageWindow.Begin()，传送时 End()，
///   PlayerHealth.Heal(total × 0.5f)（决策 B2；验收点 3 日志核对回血量）。
///
/// 二次激活与 CD：发射时 SetPendingReactivation(skillName,true)，SkillManager 在 CD 期间对该技能
/// 放行（见 SkillManager.TryActivate 冷却检查），否则再按技能键会被 CD 拦截无法传送。
/// 挂起标记在传送使用 / 玩家死亡 / 弹回池时清除（TeleportBolt.OnReturnToPool）。
/// </summary>
public class TeleportBoltExecutor : ISkillExecutor
{
    // ============================================================
    // 注册用行为标识（每分支一个实例）
    // ============================================================

    private readonly string _behaviorId;

    /// <summary>行为标识（treeA_teleport / treeA_teleport_heal）</summary>
    public string BehaviorId => _behaviorId;

    public TeleportBoltExecutor() : this("treeA_teleport") { }

    public TeleportBoltExecutor(string behaviorId)
    {
        _behaviorId = behaviorId;
    }

    // ============================================================
    // 运行时状态（单玩家项目：两分支实例共享静态状态）
    // ============================================================

    private static TeleportBolt _activeBolt;
    private static DamageWindow _window;
    private static bool _healBranchActive; // 当前挂起弹是否为回血分支（发射时刻决定,防升级换分支串窗口）

    // ============================================================
    // 发射配置（代码内可调；如策划后续要配数值可迁入 ActiveBranchData）
    // ============================================================

    /// <summary>传送弹飞行速度</summary>
    public float boltSpeed = 12f;

    /// <summary>传送弹最远飞行距离（达到后悬停）</summary>
    public float boltMaxDistance = 12f;

    /// <summary>传送弹半径</summary>
    public float boltRadius = 0.25f;

    /// <summary>传送弹颜色</summary>
    public Color boltColor = new Color(0.75f, 0.35f, 1f, 1f); // 紫色,与青色魔法弹区分

    /// <summary>传送回血比例（lv3A-02：窗口内 player 总伤害 × 50%）</summary>
    public float healRatio = 0.5f;

    /// <summary>火元素 200% 仲裁倍率（与 PlayerCombat.RollCrit ② / MagicBoltExecutor 同值）</summary>
    private const float FireCritMultiplier = 2.0f;

    /// <summary>发射点相对 player 的偏移（x 按朝向镜像，y 垂直）</summary>
    public Vector2 spawnOffset = new Vector2(0.6f, 0.4f);

    // 场景加载后注册两个分支实例（注册表 OnEnable 前挂载会自动缓冲；Register 幂等）
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void RegisterAll()
    {
        SkillExecutorRegistry.Register(new TeleportBoltExecutor("treeA_teleport"));
        SkillExecutorRegistry.Register(new TeleportBoltExecutor("treeA_teleport_heal"));
    }

    // ============================================================
    // 分发 — 有未使用的传送弹 = 传送；否则 = 发射
    // ============================================================

    public void Execute(SkillActivatedEvent e, SkillData data, ActiveSkillData.ActiveBranchData branch)
    {
        if (branch == null) return;

        GameObject playerGo = e.source != null ? e.source : PlayerController.Instance?.gameObject;
        if (playerGo == null) return;

        if (_activeBolt != null && _activeBolt.IsActive)
            DoTeleport(playerGo, branch);
        else
            DoFire(playerGo, branch, e.skillName);
    }

    // ============================================================
    // 发射
    // ============================================================

    private void DoFire(GameObject playerGo, ActiveSkillData.ActiveBranchData branch, string skillName)
    {
        // 清理已失效的旧弹（防极端情况下悬挂空引用）
        if (_activeBolt != null)
        {
            if (_activeBolt.IsActive) _activeBolt.Cancel();
            _activeBolt = null;
        }

        // 回血分支：发射时开启伤害统计窗口（重复 Begin 安全，自动丢弃旧窗口）
        _healBranchActive = _behaviorId == "treeA_teleport_heal";
        if (_healBranchActive)
        {
            if (_window == null) _window = new DamageWindow();
            _window.Begin();
        }

        PlayerController pc = playerGo.GetComponent<PlayerController>();
        int facing = pc != null ? pc.GetFacing() : 1;
        Vector2 dir = Vector2.right * facing;
        Vector2 pos = (Vector2)playerGo.transform.position + new Vector2(spawnOffset.x * facing, spawnOffset.y);

        // 元素：发射时读 ElementModule.CurrentElement（决策 N5，伤害实例按触发时刻 = 发射时刻）
        ElementType element = ElementType.None;
        ElementModule em = playerGo.GetComponent<ElementModule>();
        if (em != null) element = em.CurrentElement;

        // 倍率仲裁（与 MagicBoltExecutor.FireOne 同规则，决策 D2/D15）：传送弹无必暴来源，
        // 火元素 proc 200% 仲裁胜出（测试期 ProcChance=1f；验收点 6：传送弹带当前元素,火/雷 proc 生效）
        float mult = 1f;
        if (element == ElementType.Fire && Random.value < ElementProc.ProcChance)
            mult = Mathf.Max(mult, FireCritMultiplier);
        float boltDamage = branch.damage * mult;
        float critMult = mult > 1f ? mult : 0f; // 0=未暴击(透传 DamageInfo.critMultiplier)

        // source = player 侧 ICombatant（PlayerHealth 实现；伤害统计窗口按此识别归属）
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
            skillName: skillName
        );

        // 挂起二次激活标记：CD 期间允许再按技能键触发传送（SkillManager 冷却检查放行）
        SkillExecutorRegistry.SetPendingReactivation(skillName, true);
    }

    // ============================================================
    // 传送
    // ============================================================

    private void DoTeleport(GameObject playerGo, ActiveSkillData.ActiveBranchData branch)
    {
        Vector2 dest = _activeBolt.Position;

        // 瞬移（贴墙钳制 + 清速度 + 无敌帧 + 特效事件占位）；组件缺失时运行时挂载（默认参数可用）
        PlayerTeleport teleport = playerGo.GetComponent<PlayerTeleport>();
        if (teleport == null)
            teleport = playerGo.AddComponent<PlayerTeleport>();
        teleport.TeleportTo(dest);

        // 回血分支：传送时关闭窗口，按 窗口总伤害 × 50% 治疗（验收点 3 日志核对）
        if (_healBranchActive && _window != null)
        {
            float total = _window.End();
            PlayerHealth ph = playerGo.GetComponent<PlayerHealth>();
            if (ph != null)
            {
                float healAmount = total * healRatio;
                // 验收日志（阶段 5 验收点 3）：核对 回血量 = 窗口内 player 总伤害 × 50%
                Debug.Log($"[TeleportHeal] 窗口内 player 总伤害 {total:F1} × {healRatio} = 治疗 {healAmount:F1}");
                ph.Heal(healAmount);
            }
        }

        // 传送使用后销毁传送弹（OnReturnToPool 清除二次激活挂起标记）
        _activeBolt.Cancel();
        _activeBolt = null;
    }
}
