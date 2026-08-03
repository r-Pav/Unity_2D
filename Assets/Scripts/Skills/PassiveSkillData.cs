using UnityEngine;

/// <summary>
/// [P2] 被动技能节点数据 — 继承 SkillData
/// 每个 SO 代表一条线的一个层级（共 5线 × 5层 = 25 个独立被动节点）
/// effects 数组定义该节点的属性加成，由 PassiveEquipManager 转为 Modifier 送入 StatModifierManager
/// </summary>
[CreateAssetMenu(fileName = "Skill_Passive_", menuName = "Game/SkillData/Passive")]
public class PassiveSkillData : SkillData
{
    [Header("被动节点定位")]
    [Tooltip("层级：1=TI ~ 5=TV")]
    [Range(1, 5)]
    public int layer = 1;

    [Tooltip("线ID：0=HP恢复, 1=伤害+攻速, 2=移速+闪避, 3=减伤+控制, 4=法力+CD")]
    [Range(0, 4)]
    public int lineId;

    [Header("被动效果列表")]
    [Tooltip("一个被动节点可以有多个属性效果（如 T3 伤害+攻速线同时加伤害和攻速）")]
    public PassiveEffect[] effects;

    /// <summary>
    /// 单个被动效果描述符 — 描述对某个属性的单次加成
    /// </summary>
    [System.Serializable]
    public class PassiveEffect
    {
        [Tooltip("目标属性标识符（使用 StatId 常量，如 StatId.MaxHealth）")]
        public string targetStat;

        [Tooltip("修饰值。Percent 类型填比率（如 +8% 填 0.08），Flat 类型填绝对值（如 +20 法力填 20）")]
        public float value;

        [Tooltip("修饰类型：Percent=百分比叠加(base×(1+Σ))，Flat=数值叠加(base+Σ)")]
        public ModifierType type;
    }
}
