using System;

/// <summary>
/// 技能槽 — 可序列化数据结构
/// 在 SkillManager 的 Inspector 中拖入 SkillData ScriptableObject 即可配置
/// Phase 2 扩展：加入 ISkill 实例引用，用于调用具体技能逻辑
/// </summary>
[Serializable]
public class SkillSlot
{
    /// <summary>技能配置数据（Inspector 拖 ScriptableObject）</summary>
    public SkillData data;

    // Phase 2 扩展：
    // public ISkill skillInstance;
}
