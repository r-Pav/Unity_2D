using UnityEngine;

/// <summary>
/// Boss 技能数据(归一化 ScriptableObject)— 只存通用状态。
/// 范围/特效/挂点全部在技能 prefab 子 obj 上(子 GameObject 即数据源),data 只提供参数。
/// 执行逻辑 = skillPrefab 上的 BossSkillExecutor(执行器),data.animState 决定播放哪个动画。
/// 旧 BossAttackSO(按类型分发 + 专属字段)已废弃,新技能全部走本类型 + prefab。
/// </summary>
[CreateAssetMenu(fileName = "BossSkill_", menuName = "Game/BossSkillData", order = 100)]
public class BossSkillData : ScriptableObject
{
    [Header("通用")]
    [Tooltip("技能显示名(如\"双火墙\")")]
    public string skillName = "New Skill";

    [Tooltip("动画状态名(对应 Boss Animator Controller 中的状态,如 Skill1/Skill2/Skill3)。一个动画可被多个技能复用")]
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
