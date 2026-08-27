using System.Collections;
using UnityEngine;

/// <summary>技能执行上下文(Boss/玩家/动画器引用,由 BossSkillSlots 注入)</summary>
public class BossSkillContext
{
    public BossControllerBase boss;
    public Transform player;
    public BossSkillSlots slots;
    public Animator animator;
}

/// <summary>
/// Boss 技能执行器(抽象)— 挂技能 prefab 根上,由 BossSkillSlots 实例化并注入 data 后执行。
/// 子类实现 ExecuteSkill 具体逻辑(移动/生成/判定),通用能力由基类提供:
/// - PlaySkillAnim:按 data.animState 播放动画
/// - WaitAnimRound:等动画播完一圈(技能动画无事件时用 normalizedTime 兜底)
/// - 命中帧 OnHitFrame / 结束帧 OnAnimEnd 由 BossAnimationRelay 转发(BossSkillSlots 路由到当前执行器)
/// </summary>
public abstract class BossSkillExecutor : MonoBehaviour
{
    /// <summary>技能数据(由 BossSkillSlots 注入)</summary>
    public BossSkillData Data { get; set; }

    /// <summary>执行技能主逻辑(动画由基类 PlaySkillAnim 启动,子类做移动/生成/判定;协程结束后技能结束)</summary>
    public abstract IEnumerator ExecuteSkill(BossSkillContext ctx);

    /// <summary>动画命中帧回调(由动画事件经 relay 转发,子类覆写做判定)</summary>
    public virtual void OnHitFrame() { }

    /// <summary>动画结束帧回调(子类覆写,默认无操作;协程可自行 yield 等动画播完)</summary>
    public virtual void OnAnimEnd() { }

    /// <summary>按 data.animState 播放技能动画(状态名直接 Play,不走 Entry 路由)</summary>
    protected void PlaySkillAnim(Animator animator)
    {
        if (animator != null && Data != null && !string.IsNullOrEmpty(Data.animState))
            animator.Play(Data.animState);
    }

    /// <summary>
    /// 等指定动画状态播完一圈(loop 动画 normalizedTime 回绕检测;状态不在播则立即返回)。
    /// 技能动画挂了结束事件时优先用事件(更快),此方法做无事件兜底。
    /// </summary>
    protected IEnumerator WaitAnimRound(Animator animator, string stateName)
    {
        if (animator == null) yield break;
        float last = 0f;
        while (true)
        {
            var info = animator.GetCurrentAnimatorStateInfo(0);
            if (!info.IsName(stateName)) yield break;

            float nt = info.normalizedTime;
            bool wrapped = last > 0.8f && nt < last - 0.5f;
            last = nt;
            if (wrapped || nt >= 1f) yield break;
            yield return null;
        }
    }
}
