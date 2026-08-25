using UnityEngine;

/// <summary>
/// Boss 动画事件转发（独立,不混 EnemyControllerBase 的通用动画事件链）。
/// 挂在 Boss 的 Anim 子物体上(与 Animator 同物体),动画 clip 的事件帧直接调这里,
/// 由本类按当前 FSM 状态转发给 Boss 自己的状态类。
/// </summary>
public class BossAnimationRelay : MonoBehaviour
{
    private FirstBoss _boss;

    void Awake()
    {
        _boss = GetComponentInParent<FirstBoss>();
    }

    /// <summary>Attack 动画结束帧事件 — 转发给 BossAttackState.OnAnimEnd(回追击)。</summary>
    public void OnBossAttackEnd()
    {
        if (_boss != null && _boss.Fsm.CurrentState is BossAttackState atk)
            atk.OnAnimEnd();
    }

    // 后续接入伤害/技能时在这里加独立事件(如 OnBossAttackActiveStart/End → BossSkillSlots),
    // 与普通 enemy 的 AnimationRelay 保持隔离。
}
