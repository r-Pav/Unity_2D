using UnityEngine;

/// <summary>
/// 合成技能 lv3 执行器（技能组阶段 7,7.3）— 4 个 lv3 配方，实现 ISkillExecutor，按【产物 skillName】注册
/// （合成技能走注册表 skillName 通道，BehaviorId = 产物 skillName，B8 命名含等级防 SkillPool 覆盖）。
///
/// 配方 → 效果（材料读取：树 A = Skill_Active_Q，树 B = Skill_Active_E；参数一律读分支资产，手册 0.5.4）：
///   ① Combo_A01B01_Lv3（Q左+E左）：嘲讽幻象 + 持续暴击伤害（幻象 DoT 强制暴击 1.8）+ 牵引（嘲讽持续刷新）
///   ② Combo_A01B02_Lv3（Q左+E右）：沿途攻击必定暴击 + 固定当前元素（决策 D9，element 取触发时刻 ElementModule.CurrentElement）
///   ③ Combo_A02B01_Lv3（Q右+E左）：传送弹 + 范围内 20% 闪避（StatId.DodgeChance 临时 Modifier，进范围加/离开移除）
///        + 吸引 enemy（嘲讽幻象持续刷新嘲讽）+ 伤害 50% 回血（DamageWindow + PlayerHealth.Heal）
///   ④ Combo_A02B02_Lv3（Q右+E右）：减速圈传送 — 点技能键 = 玩家当前位置生成减速圈 + 瞄准（不发射）
///        + 左键两段（第1次发射传送弹 / 第2次传送到弹位置）+ 落点减速圈 + 路径伤害 50% 回血 + 充能 3（走 SkillManager 充能模型）
///        + 传送后慢动作 + AimLine 瞄准；有充能循环下一轮,无充能结束
///
/// 传送弹二次激活与 CD：照抄 TeleportBoltExecutor / ComboLv2Executor 模式 —— 发射时 SetPendingReactivation(skillName,true)，
/// SkillManager 在 CD 期间对该技能放行（再按技能键 = 传送）；挂起标记在传送使用 / 玩家死亡 / 弹回池时清除。
///
/// A02B02 瞄准流程（B9 出口生效：配方资产 interceptsStateAfterActivate=true → TryActivate 不切 0.25s 释放态）：
///   点技能键（消耗1充能）→ 玩家当前位置生成减速圈 + 进入瞄准（不发射）
///   → 左键第1次：发射传送弹（沿瞄准方向，距离沿用 boltLv3MaxDistance）
///   → 左键第2次：传送到弹位置 + 落点生成减速圈
///   → 有充能：退出瞄准态，慢动作保留，再点技能键开始下一轮；无充能：技能结束。
///   瞄准超时 = 技能干净结束（慢动作解除、player 恢复默认状态、充能按已消耗结算不返还）。
/// </summary>
public class ComboLv3Executor : ISkillExecutor
{
    // ============================================================
    // 注册用行为标识（= 产物 skillName；合成技能走注册表 skillName 通道）
    // ============================================================

    private readonly string _skillName;

    public string BehaviorId => _skillName;

    public ComboLv3Executor(string skillName)
    {
        _skillName = skillName;
    }

    // ============================================================
    // 传送弹运行时状态（A02B0x 两实例共用：单玩家,同一时刻只有一颗挂起弹）
    // ============================================================

    private static TeleportBolt _activeBolt;
    private static string _pendingSkill; // 当前挂起弹所属合成技能（区分 A02B01/B02 的二次激活归属）
    private static DamageWindow _window; // 路径伤害回血统计窗口（发射开 / 传送关）

    // A02B02 瞄准状态（慢动作 + 瞄准线 + 确认传送）
    // 注：saika 2026-08-19 交互定稿后确认传送走 PlayerAimingState.confirmedCleanup 直连回调，
    //     无需 _aiming/_aimingSkill 标志（历史字段已清除,CS0414 零新增）

    // ============================================================
    // 发射/效果配置（代码内可调；数值调优项，手册 11.6）
    // ============================================================

    public float boltSpeed = 12f;
    public float boltMaxDistance = 12f;        // lv2/A02B01 弹最远距离
    public float boltLv3MaxDistance = 18f;     // lv3 A02B02 距离加长
    public float boltRadius = 0.25f;
    public Color boltColor = new Color(0.75f, 0.35f, 1f, 1f); // 紫色,与传送弹一致
    public Vector2 spawnOffset = new Vector2(0.6f, 0.4f);
    public float illusionLifetime = 5f;
    public float healRatio = 0.5f;              // 伤害 50% 回血（与 lv3A-02 同值）
    public float dodgeAuraChance = 0.20f;       // A02B01 范围内 20% 闪避
    public float tauntRefreshInterval = 1f;     // 嘲讽持续刷新间隔（持续牵引）
    public float forcedCritMultiplier = 1.8f;   // A01B01 DoT / A01B02 沿途攻击强制暴击（决策 D15 同值）
    public float pathAttackDistance = 4.5f;     // A01B02 沿途攻击距离（与冲刺距离 ~3m × 1.5 同量级）
    public float pathCellWidth = 1f;            // 沿途攻击采样格宽
    public Vector2 pathHitBoxSize = new Vector2(1.2f, 1.0f); // 沿途攻击判定盒（与冲刺伤害同款）
    public float aimTimeout = 3f;               // A02B02 瞄准超时（秒）
    public float aimDistance = 18f;             // A02B02 瞄准最大距离（与 lv3 传送弹距离加长同级，可到达范围内任意点）
    public float slowMotionScale = 0.3f;        // A02B02 传送后慢动作倍率
    public float slowMotionDuration = 3f;       // A02B02 慢动作时长（秒；瞄准超时同样解除）

    private const float FireCritMultiplier = 2.0f; // 火元素 200% 仲裁（与 MagicBoltExecutor 同值）
    private const int WallMask = (1 << 3) | (1 << 11); // Ground + Wall,与魔法弹同款

    // 资产路径（与 CombinationCraftSystem 的 Resources 路径一致）
    private const string TreeAPath = "Skills/Active/Skill_Active_Q";
    private const string TreeBPath = "Skills/Active/Skill_Active_E";

    // ============================================================
    // 注册 + 静态清理（domain reload 关闭时复位残留状态；玩家死亡清瞄准态）
    // ============================================================

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void InitStatic()
    {
        _activeBolt = null;
        _pendingSkill = null;
        _window = null;
        // 玩家死亡清瞄准（防复活后按技能键误传送残留瞄准点）
        EventBus.Subscribe<PlayerDeathEvent>(OnPlayerDeath);
    }

    private static void OnPlayerDeath(PlayerDeathEvent e)
    {
        SlowMotionController.ExitSlow();
    }

    // 场景加载后注册 4 个 lv3 合成执行器（注册表未挂载时自动缓冲；Register 幂等）
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void RegisterAll()
    {
        SkillExecutorRegistry.Register(new ComboLv3Executor("Combo_A01B01_Lv3"));
        SkillExecutorRegistry.Register(new ComboLv3Executor("Combo_A01B02_Lv3"));
        SkillExecutorRegistry.Register(new ComboLv3Executor("Combo_A02B01_Lv3"));
        SkillExecutorRegistry.Register(new ComboLv3Executor("Combo_A02B02_Lv3"));
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
            case "Combo_A01B01_Lv3": ExecuteA01B01(playerGo); break;
            case "Combo_A01B02_Lv3": ExecuteA01B02(playerGo); break;
            case "Combo_A02B01_Lv3": ExecuteA02B01(playerGo); break;
            case "Combo_A02B02_Lv3": ExecuteA02B02(playerGo, e.slotIndex, branch); break;
            default: break; // 未知 skillName 静默跳过（注册表已按 skillName 分发,此分支为双保险）
        }
    }

    // ============================================================
    // ① A01B01（Q左+E左）：嘲讽幻象 + 持续暴击伤害（DoT 强制暴击 1.8）+ 牵引（嘲讽持续刷新）
    // ============================================================

    private void ExecuteA01B01(GameObject playerGo)
    {
        var mgr = IllusionManager.EnsureInstance();
        if (mgr == null) return;

        // E 树 lv3Left（B-01 大范围嘲讽线）：range/duration/damage（DoT 每跳伤害）
        var eBranch = GetBranchSide(LoadTreeB(), 3, "Left");
        mgr.SpawnTauntIllusion(playerGo.transform.position, new TauntIllusionConfig
        {
            tauntRadius = eBranch != null && eBranch.range > 0f ? eBranch.range : 5f,
            tauntDuration = eBranch != null && eBranch.duration > 0f ? eBranch.duration : 4f,
            lifetime = illusionLifetime,
            dotEnabled = true,
            dotDamage = eBranch != null && eBranch.damage > 0f ? eBranch.damage : 5f,
            dotInterval = 1f,
            dotCritMultiplier = forcedCritMultiplier, // DoT 强制暴击 1.8
            tauntRefreshInterval = tauntRefreshInterval // 持续牵引
        });
    }

    // ============================================================
    // ② A01B02（Q左+E右）：沿途攻击必定暴击 + 固定当前元素（决策 D9）
    // ============================================================

    private void ExecuteA01B02(GameObject playerGo)
    {
        PlayerController pc = playerGo.GetComponent<PlayerController>();
        if (pc == null) return;
        ICombatant source = playerGo.GetComponent<ICombatant>();
        if (source == null) return;

        // 决策 D9：element 取触发时刻 ElementModule.CurrentElement（固定快照,不逐击读取）
        ElementModule em = playerGo.GetComponent<ElementModule>();
        ElementType element = em != null ? em.CurrentElement : ElementType.None;

        // E 树 lv3Right（B-02 沿途攻击线）：damage = 每格伤害
        var bBranch = GetBranchSide(LoadTreeB(), 3, "Right");
        float cellDamage = bBranch != null && bBranch.damage > 0f ? bBranch.damage : 20f;
        float amount = cellDamage * forcedCritMultiplier; // 必定暴击：倍率烘焙进伤害（与魔法弹必暴同款）

        Vector2 start = (Vector2)playerGo.transform.position;
        Vector2 dir = Vector2.right * pc.GetFacing();
        int cells = Mathf.Max(1, Mathf.CeilToInt(pathAttackDistance / pathCellWidth));
        LayerMask enemyMask = LayerMask.GetMask("Enemy");

        for (int i = 0; i < cells; i++)
        {
            Vector2 center = start + dir * (pathCellWidth * (i + 0.5f));
            Collider2D[] hits = Physics2D.OverlapBoxAll(center, pathHitBoxSize, 0f, enemyMask);
            foreach (Collider2D col in hits)
            {
                if (col == null) continue;
                EnemyControllerBase enemy = col.GetComponentInParent<EnemyControllerBase>();
                if (enemy == null || enemy.IsDead || !enemy.CanBeDamaged) continue;

                CombatResolver.Resolve(source, enemy, new DamageInfo
                {
                    amount = amount,
                    source = source,
                    sourcePosition = start,
                    attackLabel = "ComboLv3Path",
                    knockback = new Knockback
                    {
                        direction = dir,
                        force = 3f,
                        duration = 0f,
                        ignoreResistance = false
                    },
                    element = element,                 // 固定当前元素（决策 D9）
                    canTriggerElementProc = true,      // player 侧攻击可触发元素 proc
                    critMultiplier = forcedCritMultiplier // 必定暴击 1.8 透传
                });
            }
        }
    }

    // ============================================================
    // ③ A02B01（Q右+E左）：传送弹 + 闪避领域 + 吸引 enemy + 伤害 50% 回血
    // ============================================================

    private void ExecuteA02B01(GameObject playerGo)
    {
        if (_activeBolt != null && _activeBolt.IsActive && _pendingSkill == _skillName)
            DoTeleportA02B01(playerGo);      // 二次激活：传送 + 闪避领域 + 嘲讽 + 回血
        else
            FireTeleportBolt(playerGo, boltMaxDistance, healEnabled: true); // 首次：发射传送弹 + 开回血窗口
    }

    private void DoTeleportA02B01(GameObject playerGo)
    {
        if (_activeBolt == null || !_activeBolt.IsActive) return;
        Vector2 dest = _activeBolt.Position;
        Vector2 preTeleportPos = (Vector2)playerGo.transform.position; // 传送前位置（嘲讽幻象留原地）

        // 瞬移（组件缺失时运行时挂载,默认参数可用）
        PlayerTeleport teleport = playerGo.GetComponent<PlayerTeleport>();
        if (teleport == null) teleport = playerGo.AddComponent<PlayerTeleport>();
        teleport.TeleportTo(dest);

        // 吸引 enemy：嘲讽幻象留在传送前位置 + 持续刷新嘲讽（新进入范围/失效续上）
        var eBranch = GetBranchSide(LoadTreeB(), 3, "Left");
        var mgr = IllusionManager.EnsureInstance();
        if (mgr != null)
        {
            mgr.SpawnTauntIllusion(preTeleportPos, new TauntIllusionConfig
            {
                tauntRadius = eBranch != null && eBranch.range > 0f ? eBranch.range : 5f,
                tauntDuration = eBranch != null && eBranch.duration > 0f ? eBranch.duration : 4f,
                lifetime = illusionLifetime,
                dotEnabled = false,
                tauntRefreshInterval = tauntRefreshInterval
            });
        }

        // 范围内 20% 闪避：以传送落点为锚点的闪避领域（进范围加/离开移除,由 DodgeAuraZone 驱动）
        float auraRadius = eBranch != null && eBranch.range > 0f ? eBranch.range : 5f;
        float auraDuration = eBranch != null && eBranch.duration > 0f ? eBranch.duration : 4f;
        var auraGo = new GameObject("ComboLv3_DodgeAura");
        auraGo.transform.position = dest;
        auraGo.AddComponent<DodgeAuraZone>().Init(dest, auraRadius, auraDuration, dodgeAuraChance);

        // 伤害 50% 回血：关闭窗口按 总伤害 × 50% 治疗
        HealFromWindow(playerGo);

        // 传送使用后销毁传送弹（OnReturnToPool 清除二次激活挂起标记）
        _activeBolt.Cancel();
        _activeBolt = null;
        _pendingSkill = null;
    }

    // ============================================================
    // ④ A02B02（Q右+E右）：减速圈传送 — 点技能键自身圈+瞄准,左键两段(发射→传送)+落点圈 + 充能 3 + 慢动作 + AimLine
    // ============================================================

    private void ExecuteA02B02(GameObject playerGo, int slotIndex, ActiveSkillData.ActiveBranchData branch)
    {
        // saika 2026-08-20 定稿(按键规则):技能键 = 消耗 1 充能 开圈+瞄准 / 已发射后技能键二次激活 = 传送(不扣充能);
        // 左键 = 仅发射传送弹(沿瞄准方向);循环:技能键(开圈) → 左键(发射) → 技能键(传送) → 自动瞄准 → ...
        // 二次激活 = 技能键传送(照抄 A02B01 模式):有挂起弹且归属本技能 → 传送,不再开新圈
        if (_activeBolt != null && _activeBolt.IsActive && _pendingSkill == _skillName)
        {
            DoTeleportA02B02(playerGo, slotIndex, branch); // 技能键传送:传送到刚创建的圈(弹落点圈)
            return;
        }

        // 首次激活:先清理上一轮残留传送弹(瞄准超时未确认等场景),避免左键误判为"已发射"
        if (_activeBolt != null)
        {
            if (_activeBolt.IsActive) _activeBolt.Cancel();
            _activeBolt = null;
        }
        _pendingSkill = null;

        // 减速圈参数读分支资产（A02B02 = Q右+E右 → E 树 lv3Right；与 A01B0x 的 GetBranchSide 模式一致,saika 在 Inspector 调）
        ActiveSkillData.ActiveBranchData slowBranch = branch ?? GetBranchSide(LoadTreeB(), 3, "Right");
        // 已在圈内不重复生成（圈是实体各自生命周期;传送后玩家站在圈里再释放技能不再叠圈）
        if (!SlowZone.IsPointInAnyZone(playerGo.transform.position))
        {
            SlowZone.Spawn(playerGo.transform.position, // 自身减速圈
                slowBranch != null && slowBranch.slowZoneRadius > 0f ? slowBranch.slowZoneRadius : 2f,
                slowBranch != null && slowBranch.slowZoneDuration > 0f ? slowBranch.slowZoneDuration : 5f,
                slowBranch != null && slowBranch.slowFactor > 0f ? slowBranch.slowFactor : 0.5f);
        }
        EnterAiming(playerGo, slotIndex);                                                          // 进入瞄准(不发射)
    }

    /// <summary>技能键二次激活传送 — 传送到刚创建的圈(弹落点圈,弹位置即圈位置)→ 路径伤害回血 → 有剩余充能自动进瞄准(无充能结束)</summary>
    private void DoTeleportA02B02(GameObject playerGo, int slotIndex, ActiveSkillData.ActiveBranchData branch)
    {
        if (_activeBolt == null || !_activeBolt.IsActive) return;
        Vector2 dest = _activeBolt.Position;

        // 落点生成减速圈(刚创建的圈 = 传送目标位置)
        ActiveSkillData.ActiveBranchData slowBranch = branch ?? GetBranchSide(LoadTreeB(), 3, "Right");
        SlowZone.Spawn(dest,
            slowBranch != null && slowBranch.slowZoneRadius > 0f ? slowBranch.slowZoneRadius : 2f,
            slowBranch != null && slowBranch.slowZoneDuration > 0f ? slowBranch.slowZoneDuration : 5f,
            slowBranch != null && slowBranch.slowFactor > 0f ? slowBranch.slowFactor : 0.5f);

        // 瞬移（组件缺失时运行时挂载,默认参数可用）
        PlayerTeleport teleport = playerGo.GetComponent<PlayerTeleport>();
        if (teleport == null) teleport = playerGo.AddComponent<PlayerTeleport>();
        teleport.TeleportTo(dest);

        // 路径伤害 50% 回血（弹飞行期间对 enemy 造成的伤害窗口）
        HealFromWindow(playerGo);

        // 传送使用后销毁传送弹
        _activeBolt.Cancel();
        _activeBolt = null;
        _pendingSkill = null;

        // 退出当前瞄准态(技能键传送发生时玩家处于瞄准态;confirmedCleanup 跳过清理回调,慢动作由后续 EnterAiming/无充能处理)
        PlayerController pc = playerGo.GetComponent<PlayerController>();
        if (pc != null && pc.AimingState != null && pc.PlayerFsm != null
            && pc.PlayerFsm.CurrentState == pc.AimingState)
        {
            pc.AimingState.confirmedCleanup = true;
            pc.PlayerFsm.ChangeState(pc.IdleState);
        }

        // 传送后慢动作 + AimLine 选下一次释放位置；无充能不进慢动作并解除慢动作
        SkillManager sm = playerGo.GetComponent<SkillManager>();
        if (sm != null && sm.GetCharges(slotIndex) > 0)
            EnterAiming(playerGo, slotIndex);
        else
            SlowMotionController.ExitSlow();
    }

    /// <summary>进入瞄准：慢动作 + 瞄准线显示 + FSM 切 PlayerAimingState（B9 出口：不被 0.25s 释放态打断）</summary>
    private void EnterAiming(GameObject playerGo, int slotIndex)
    {
        PlayerController pc = playerGo.GetComponent<PlayerController>();
        if (pc == null || pc.AimingState == null) return;

        // 确保瞄准线组件存在（场景未手动挂载时运行时补挂；RequireComponent 自动补 LineRenderer）
        PlayerAimLine aimLine = playerGo.GetComponent<PlayerAimLine>();
        if (aimLine == null) aimLine = playerGo.AddComponent<PlayerAimLine>();

        // 慢动作（HitStop 冻结期间自动挂起,由 SlowMotionController 协调）
        SlowMotionController.EnterSlow(slowMotionScale, slowMotionDuration);

        // 瞄准态：超时回调 = 技能干净结束（慢动作解除 + 清瞄准标记;状态类自己切回 Idle/Move）
        // 确认回调 = 左键仅发射(2026-08-20 定稿):未发射 → 发射传送弹;已发射 → 忽略(传送改技能键二次激活,防误按)
        pc.AimingState.Begin(aimTimeout, aimDistance, WallMask, slotIndex, CancelAiming,
            () =>
            {
                if (_activeBolt == null || !_activeBolt.IsActive)
                    FireTeleportBolt(playerGo, boltLv3MaxDistance, healEnabled: true);
            });
        pc.PlayerFsm.ChangeState(pc.AimingState);
    }

    /// <summary>清理瞄准状态 + 解除慢动作（超时 / 确认 / 玩家死亡共用）</summary>
    private void CancelAiming()
    {
        SlowMotionController.ExitSlow();
    }

    // ============================================================
    // 传送弹发射 / 回血（A02B0x 共用）
    // ============================================================

    /// <summary>发射传送弹（照抄 ComboLv2Executor.FireTeleportBolt：元素快照 + 火仲裁 + 挂起二次激活标记）。
    /// healEnabled=true 时开启伤害统计窗口（路径伤害 50% 回血；重复 Begin 安全）。</summary>
    private void FireTeleportBolt(GameObject playerGo, float maxDistance, bool healEnabled)
    {
        // 清理已失效的旧弹（防极端情况下悬挂空引用）
        if (_activeBolt != null)
        {
            if (_activeBolt.IsActive) _activeBolt.Cancel();
            _activeBolt = null;
        }

        if (healEnabled)
        {
            if (_window == null) _window = new DamageWindow();
            _window.Begin();
        }

        PlayerController pc = playerGo.GetComponent<PlayerController>();
        // saika 2026-08-19 定稿:传送弹沿鼠标瞄准方向发射(AimLine.AimDirection),不再固定 facing
        Vector2 dir = Vector2.right * (pc != null ? pc.GetFacing() : 1);
        PlayerAimLine aimLine = playerGo.GetComponent<PlayerAimLine>();
        if (aimLine != null && aimLine.IsAiming)
            dir = aimLine.AimDirection;
        Vector2 pos = (Vector2)playerGo.transform.position + new Vector2(spawnOffset.x * (dir.x >= 0f ? 1 : -1), spawnOffset.y);

        // 元素：发射时读 ElementModule.CurrentElement（决策 N5,伤害实例按触发时刻 = 发射时刻）
        ElementType element = ElementType.None;
        ElementModule em = playerGo.GetComponent<ElementModule>();
        if (em != null) element = em.CurrentElement;

        // 倍率仲裁（与 MagicBoltExecutor 同规则）：传送弹无必暴来源,火元素 proc 200% 仲裁胜出
        float mult = 1f;
        if (element == ElementType.Fire && Random.value < ElementProc.ProcChance)
            mult = Mathf.Max(mult, FireCritMultiplier);

        var aBranch = GetBranchSide(LoadTreeA(), 3, "Right"); // Q lv3Right（A-02 传送弹线）damage
        float boltDamage = (aBranch != null && aBranch.damage > 0f ? aBranch.damage : 55f) * mult;
        float critMult = mult > 1f ? mult : 0f; // 0=未暴击

        // source = player 侧 ICombatant（PlayerHealth 实现）
        ICombatant source = playerGo.GetComponent<ICombatant>();

        _activeBolt = TeleportBolt.Spawn(
            position: pos,
            direction: dir,
            damage: boltDamage,
            speed: boltSpeed,
            maxDistance: maxDistance,
            hitLayers: LayerMask.GetMask("Enemy"),
            wallLayers: WallMask,
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

    /// <summary>关闭伤害窗口并按 总伤害 × healRatio 治疗（回血验收日志：核对 回血量 = 窗口内 player 总伤害 × 50%）</summary>
    private void HealFromWindow(GameObject playerGo)
    {
        if (_window == null) return;
        float total = _window.End();
        PlayerHealth ph = playerGo.GetComponent<PlayerHealth>();
        if (ph != null && total > 0f)
        {
            float healAmount = total * healRatio;
            Debug.Log($"[ComboLv3] {_skillName} 窗口内 player 总伤害 {total:F1} × {healRatio} = 治疗 {healAmount:F1}");
            ph.Heal(healAmount);
        }
    }

    // ============================================================
    // 配置组装（参数一律读分支资产,手册 0.5.4）
    // ============================================================

    /// <summary>读取树指定分支数据（lv2Left/lv2Right/lv3Left/lv3Right 等；null 安全）</summary>
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
/// 闪避领域（lv3 合成 A02B01）— 以传送落点为锚点：player 进入范围获得 +dodgeChance 闪避
/// （StatId.DodgeChance 注入临时 Modifier，同 source 覆盖），离开范围移除；时长到点移除并自毁。
/// 计时用 unscaledDeltaTime（慢动作/卡帧期间领域照常计时，与技能 CD 抗卡帧一致）。
/// </summary>
public class DodgeAuraZone : MonoBehaviour
{
    private const string ModifierSource = "Combo_A02B01_Lv3_dodge";

    private Vector2 anchor;
    private float radius;
    private float remaining;
    private float chance;
    private StatModifierManager statMod;
    private Transform player;
    private bool applied;

    /// <summary>初始化领域参数（由执行器生成时调用）</summary>
    public void Init(Vector2 anchorPos, float radius, float duration, float dodgeChance)
    {
        anchor = anchorPos;
        this.radius = Mathf.Max(0.1f, radius);
        remaining = Mathf.Max(0.1f, duration);
        chance = dodgeChance;
        statMod = PlayerController.Instance != null
            ? PlayerController.Instance.GetComponent<StatModifierManager>()
            : null;
        player = PlayerController.Instance != null ? PlayerController.Instance.transform : null;
    }

    private void Update()
    {
        remaining -= Time.unscaledDeltaTime;
        if (remaining <= 0f)
        {
            if (applied) statMod?.RemoveModifier(ModifierSource);
            Destroy(gameObject);
            return;
        }

        bool inRange = player != null && Vector2.Distance(player.position, anchor) <= radius;
        if (inRange && !applied)
        {
            applied = true;
            statMod?.AddModifier(new Modifier(StatId.DodgeChance, chance, ModifierType.Flat, ModifierSource));
        }
        else if (!inRange && applied)
        {
            applied = false;
            statMod?.RemoveModifier(ModifierSource);
        }
    }
}
