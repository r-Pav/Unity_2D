using System;

/// <summary>
/// [P7] 技能池中的单个技能条目 — 可序列化数据结构。
/// 记录玩家拥有某个技能的数据引用、等级、获取来源和时间。
/// </summary>
[Serializable]
public class OwnedSkillEntry
{
    /// <summary>唯一标识（使用 skillData.skillName 作为默认 ID）</summary>
    public string id;

    /// <summary>技能配置 SO 引用</summary>
    public SkillData skillData;

    /// <summary>当前等级</summary>
    public int level;

    /// <summary>获得来源：initial / craft / unlock / quest / shop</summary>
    public string source;

    /// <summary>获取时间（用于排序/展示）</summary>
    public string acquiredAt;
}
