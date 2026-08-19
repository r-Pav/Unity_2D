using System.Collections;
using UnityEngine;

/// <summary>
/// 树 A 魔法弹执行器(阶段 2,决策 N7/D15)— 魔法弹单发 / 连射 3 / 连射 5+必暴。
/// 注册表按单 behaviorId 注册执行器:本类注册 3 个实例(BehaviorId 分别为 treeA_bolt /
/// treeA_burst3 / treeA_burst5_crit),Execute 内按 branch.behaviorId 分支处理三个行为。
/// 不订阅 EventBus,由 SkillExecutorRegistry 按分支 behaviorId 分发;注册表 Register 在
/// 场景加载后(RuntimeInitializeOnLoadMethod,等效 Awake/OnEnable)调用,注册表未挂载时自动缓冲。
///
/// 发射参数:朝向 = PlayerController.GetFacing();发射点 = player 位置 + 前方偏移(按朝向镜像)。
/// 连射间隔默认 0.3s(不得小于 0.25s,B9:PlayerSkillCastState 固定 0.25s 锁输入)。
/// 元素:发射时读 ElementModule.CurrentElement 写入 PlayerProjectile,命中走基类 TryDealDamage → 元素 proc 自动生效。
/// 必暴:treeA_burst5_crit 由 PlayerCombat.ArmForcedCrit(1.8f) 注入(决策 D15);弹道不吃 RollCrit,
/// 倍率在发射时烘焙进单发伤害,仲裁规则与 RollCrit 一致(火 200% 胜出,取最高不叠加)。
/// </summary>
public class MagicBoltExecutor : ISkillExecutor
{
    // ============================================================
    // 注册用行为标识(每分支一个实例)
    // ============================================================

    private readonly string _behaviorId;

    /// <summary>行为标识(treeA_bolt / treeA_burst3 / treeA_burst5_crit)</summary>
    public string BehaviorId => _behaviorId;

    public MagicBoltExecutor() : this("treeA_bolt") { }

    public MagicBoltExecutor(string behaviorId)
    {
        _behaviorId = behaviorId;
    }

    // ============================================================
    // 发射配置(代码内可调;连射间隔/偏移如需策划配置可后续迁入 ActiveBranchData)
    // ============================================================

    /// <summary>连射间隔(秒)— 默认 0.3s;硬下限 0.25s(B9,SkillCastState 锁输入)</summary>
    public float burstInterval = 0.3f;

    /// <summary>发射点相对 player 的偏移(x 按朝向镜像,y 垂直)</summary>
    public Vector2 spawnOffset = new Vector2(0.6f, 0.4f);

    /// <summary>魔法弹飞行速度</summary>
    public float boltSpeed = 12f;

    /// <summary>魔法弹半径</summary>
    public float boltRadius = 0.25f;

    /// <summary>魔法弹颜色(无元素)</summary>
    public Color boltColor = Color.cyan;

    /// <summary>必暴倍率(决策 D15;treeA_burst5_crit 用)</summary>
    public float forcedCritMultiplier = 1.8f;

    /// <summary>火元素 200% 仲裁倍率(与 PlayerCombat.RollCrit ② 同值)</summary>
    private const float FireCritMultiplier = 2.0f;

    // 场景加载后注册三个分支实例(注册表 OnEnable 前挂载会自动缓冲;Register 幂等,重复调用仅覆盖字典条目)
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void RegisterAll()
    {
        SkillExecutorRegistry.Register(new MagicBoltExecutor("treeA_bolt"));
        SkillExecutorRegistry.Register(new MagicBoltExecutor("treeA_burst3"));
        SkillExecutorRegistry.Register(new MagicBoltExecutor("treeA_burst5_crit"));
    }

    // ============================================================
    // 分发 — 按 branch.behaviorId 分支处理三个行为
    // ============================================================

    public void Execute(SkillActivatedEvent e, SkillData data, ActiveSkillData.ActiveBranchData branch)
    {
        if (branch == null) return;

        switch (branch.behaviorId)
        {
            case "treeA_bolt":        FireBurst(e, branch, 1, false); break;
            case "treeA_burst3":      FireBurst(e, branch, 3, false); break;
            case "treeA_burst5_crit": FireBurst(e, branch, 5, true);  break;
            default: break; // 未知行为静默跳过(注册表已按 behaviorId 分发,此分支为双保险)
        }
    }

    // ============================================================
    // 连射
    // ============================================================

    private void FireBurst(SkillActivatedEvent e, ActiveSkillData.ActiveBranchData branch, int count, bool forcedCrit)
    {
        GameObject playerGo = e.source != null ? e.source : PlayerController.Instance?.gameObject;
        if (playerGo == null) return;

        PlayerController pc = playerGo.GetComponent<PlayerController>();
        if (pc == null) return;

        // 决策 D15:必暴技能发射前注入(用后清除语义:本次攻击=连射本身,弹道不吃 RollCrit,
        // 倍率在 FireOne 烘焙进单发伤害;发射结束后取消残留注入,防污染下一次近战仲裁)
        PlayerCombat combat = playerGo.GetComponent<PlayerCombat>();
        if (forcedCrit && combat != null)
            combat.ArmForcedCrit(forcedCritMultiplier);

        // 连射间隔硬下限 0.25s(B9)
        float interval = Mathf.Max(0.25f, burstInterval);
        pc.StartCoroutine(FireBurstRoutine(playerGo, branch, count, forcedCrit, combat, interval));
    }

    private IEnumerator FireBurstRoutine(GameObject playerGo, ActiveSkillData.ActiveBranchData branch,
        int count, bool forcedCrit, PlayerCombat combat, float interval)
    {
        for (int i = 0; i < count; i++)
        {
            FireOne(playerGo, branch, forcedCrit);
            if (i < count - 1)
                yield return new WaitForSeconds(interval);
        }

        // 取消残留的必暴注入(本次攻击=连射已完成,防泄漏到下一次近战)
        if (forcedCrit && combat != null)
            combat.ArmForcedCrit(0f);
    }

    // ============================================================
    // 单发
    // ============================================================

    private void FireOne(GameObject playerGo, ActiveSkillData.ActiveBranchData branch, bool forcedCrit)
    {
        PlayerController pc = playerGo.GetComponent<PlayerController>();
        if (pc == null) return;

        int facing = pc.GetFacing();
        Vector2 dir = Vector2.right * facing;
        Vector2 pos = (Vector2)playerGo.transform.position + new Vector2(spawnOffset.x * facing, spawnOffset.y);

        // 元素:发射时读 ElementModule.CurrentElement(决策 N5,伤害实例按触发时刻=发射时刻)
        ElementType element = ElementType.None;
        ElementModule em = playerGo.GetComponent<ElementModule>();
        if (em != null) element = em.CurrentElement;

        // 倍率仲裁(与 PlayerCombat.RollCrit 同规则,取最高不叠加,决策 D2/D15):
        // 必暴 1.8 为底,火元素 proc 200% 仲裁胜出(测试期 ProcChance=1f)
        float mult = 1f;
        if (forcedCrit) mult = Mathf.Max(mult, forcedCritMultiplier);
        if (element == ElementType.Fire && Random.value < ElementProc.ProcChance)
            mult = Mathf.Max(mult, FireCritMultiplier);

        float boltDamage = branch.damage * mult;
        float critMult = mult > 1f ? mult : 0f; // 0=未暴击(透传 DamageInfo.critMultiplier)

        // source = player 侧 ICombatant(PlayerHealth 实现);击退源点=玩家位置,与近战一致
        ICombatant source = playerGo.GetComponent<ICombatant>();

        // 墙层:Ground(3)+Wall(11),与 EnemyRangedAttack L93 同款;sourceLayer=玩家自身层,防自伤
        PlayerProjectile.Spawn(
            position: pos,
            direction: dir,
            damage: boltDamage,
            speed: boltSpeed,
            hitLayers: LayerMask.GetMask("Enemy"),
            radius: boltRadius,
            color: boltColor,
            parent: null,
            wallLayers: (1 << 3) | (1 << 11),
            sourceLayer: 1 << playerGo.layer,
            source: source,
            element: element,
            critMultiplier: critMult
        );
    }
}
