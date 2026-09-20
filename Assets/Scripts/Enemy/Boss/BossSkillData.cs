using UnityEngine;

/// <summary>
/// Boss 技能数据(归一化 ScriptableObject)— 只存通用状态。
/// 范围/特效/挂点全部在技能 prefab 子 obj 上(子 GameObject 即数据源),data 只提供参数。
/// 执行逻辑 = skillPrefab 上的 BossSkillExecutor(执行器),data.animState 是动画 Bool 参数名(施法期间点亮对应动画开关)。
/// 旧 BossAttackSO(按类型分发 + 专属字段)已废弃,新技能全部走本类型 + prefab。
/// </summary>
[CreateAssetMenu(fileName = "BossSkill_", menuName = "Game/BossSkillData", order = 100)]
public class BossSkillData : ScriptableObject
{
    [Header("通用")]
    [Tooltip("技能显示名(如\"双火墙\")")]
    public string skillName = "New Skill";

    [Tooltip("动画 Bool 参数名(对应 Boss Animator 的 Bool 参数,如 IsMagic);技能开始时置真、结束/中断时置假,状态由动画器 Entry 路由决定。一个开关可被多个技能复用")]
    public string animState;

    [Tooltip("技能预制体(实例化挂 Boss 下,根上挂 BossSkillExecutor 执行器;范围/特效/挂点用子 obj 配)")]
    public GameObject skillPrefab;

    [Header("状态")]
    [Tooltip("伤害值")]
    public float damage = 20f;

    [Tooltip("击退向量(x 按朝向镜像,y 直接控制上挑;(0,0)=无击退)")]
    public Vector2 knockback = new Vector2(4f, 0f);

    [Tooltip("可否被格挡")]
    public bool canBeBlocked = true;

    [Tooltip("可否被弹反")]
    public bool canBeParried = true;

    [Header("表现")]
    [Tooltip("命中特效 prefab(可选,由执行器命中时生成)")]
    public GameObject hitVFXPrefab;

    [Tooltip("音效 key(可选)")]
    public string sfxKey;

    [Header("起手瞬移(P4,可选)")]
    [Tooltip("起手瞬移标记:勾选后,本技能释放前先把 Boss 瞬移到玩家身边再起手(追踪方式 = 瞬移,与重击落点算法同口径:落点 = 玩家面朝方向 × 1.5,玩家该侧被实心墙/管道挡住则翻到另一侧)。瞬移期间技能霸体已生效(施法中 + P1 起手前霸体),玩家普攻打不进 Hurt。默认 false = 原地起手,行为与勾选前完全一致")]
    public bool trackPlayerBeforeCast = false;

    [Tooltip("瞬移「消失」表现(可选,留空则无表现):在 Boss 原位置生成,配合出现表现做出「消失—出现」的位移观感。仅在勾选 trackPlayerBeforeCast 时生效")]
    public GameObject disappearVFXPrefab;

    [Tooltip("瞬移「出现」表现(可选,留空则无表现):在落点(ForceSetPosition 钳制后的实际位置)生成。仅在勾选 trackPlayerBeforeCast 时生效")]
    public GameObject appearVFXPrefab;

    /// <summary>
    /// 构造伤害结算信息(统一入口:伤害/击退/标签全部从 data 读)。
    /// faceDir.x 用于击退 x 镜像(朝左 = -1,朝右 = 1)。
    /// </summary>
    public DamageInfo BuildDamageInfo(ICombatant source, Vector2 sourcePos, Vector2 faceDir)
    {
        Vector2 dir = knockback;
        if (dir.x != 0f)
            dir.x *= Mathf.Sign(faceDir.x == 0f ? 1f : faceDir.x);
        float force = dir.magnitude;
        if (force < 0.0001f)
            dir = Vector2.zero;

        return new DamageInfo
        {
            amount = damage,
            source = source,
            sourcePosition = sourcePos,
            attackLabel = string.IsNullOrEmpty(skillName) ? "BossSkill" : skillName,
            knockback = new Knockback
            {
                direction = force > 0.0001f ? dir.normalized : Vector2.zero,
                force = force,
                duration = 0.2f,
                ignoreResistance = false
            }
        };
    }
}
