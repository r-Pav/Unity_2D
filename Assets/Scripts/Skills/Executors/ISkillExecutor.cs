using UnityEngine;

/// <summary>
/// 技能执行器接口（阶段 0.5 框架底座,决策 N7）— 由 SkillExecutorRegistry 统一分发,执行器自身不订阅事件。
/// 双通道语义:
///   树执行器:        data 为 ActiveSkillData + branch 非 null（按分支 behaviorId 分发）
///   合成技能执行器:  只读 data（branch 为 null,BehaviorId = 产物 skillName,按 skillName 分发）
/// 行为实现一律读 ActiveBranchData 数值,禁止用 branchName/description 判断行为。
/// </summary>
public interface ISkillExecutor
{
    /// <summary>行为标识（树分支 behaviorId;合成技能执行器 = 产物 skillName,全局唯一）</summary>
    string BehaviorId { get; }

    /// <summary>
    /// 执行技能行为。
    /// </summary>
    /// <param name="e">技能激活事件（SkillManager 触发,携带 skillName/等级/来源）</param>
    /// <param name="data">技能数据（树 = ActiveSkillData;合成技能 = CombinationSkillData 等非树数据）</param>
    /// <param name="branch">树分支数据（树执行器非 null;合成技能执行器为 null）</param>
    void Execute(SkillActivatedEvent e, SkillData data, ActiveSkillData.ActiveBranchData branch);
}
