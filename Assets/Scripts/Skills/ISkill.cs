/// <summary>
/// 技能组件接口 — 所有具体技能类必须实现
/// Phase 1 仅定义接口，具体实现留 Phase 2
/// </summary>
public interface ISkill
{
    /// <summary>关联的技能配置数据</summary>
    SkillData Data { get; }

    /// <summary>当前冷却剩余（秒）</summary>
    float CooldownTimer { get; }

    /// <summary>是否在冷却中</summary>
    bool IsOnCooldown { get; }

    /// <summary>是否处于激活状态（Toggle 类技能使用）</summary>
    bool IsActive { get; }

    /// <summary>每帧由 SkillManager 调用，用于更新冷却、Toggle 消耗等</summary>
    void OnSkillUpdate(PlayerController owner);

    /// <summary>检查技能是否满足激活条件（冷却、法力、状态等）</summary>
    bool CanActivate(PlayerController owner);

    /// <summary>执行技能逻辑</summary>
    void Activate(PlayerController owner);

    /// <summary>取消技能（Toggle 关闭 / 被打断）</summary>
    void Deactivate(PlayerController owner);
}
