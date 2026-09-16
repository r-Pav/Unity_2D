using UnityEngine;

/// <summary>
/// Boss 受击状态 — 动画(IsHurt)驱动。
/// 进入:IsHurt=true → 动画器 Entry 路由进 Hurt(与普通敌人 stun 同一套参数,但 Boss 不吃 stun)。
/// 退出:主路径 = Hurt 动画播完(非循环剪辑 normalizedTime 到 1)或末帧动画事件(经 BossAnimationRelay 转发);
///       兜底 = 按动画片段长度 + 余量超时退出(事件没挂 / anim 为空时也退得出去)。
/// 受击期间不免疫伤害、不进无敌帧,照常走 CombatResolver 扣血。
/// 被背刺成功时由 BossControllerBase 先中断重击(解除霸体)再进本状态,否则动画器停在重击状态出不来。
/// </summary>
public class BossHurtState : EntityState
{
    /// <summary>动画器里的受击状态名(单次剪辑,播完停末帧)</summary>
    private const string HurtStateName = "Hurt";

    /// <summary>兜底时长余量(秒):动画片段长度 + 该值</summary>
    private const float FallbackMargin = 0.3f;

    /// <summary>完全取不到片段时的兜底时长(秒)</summary>
    private const float DefaultFallback = 0.6f;

    private float _enterTime;
    private float _fallbackDuration = DefaultFallback;
    private bool _exiting;

    public BossHurtState(CharacterBase owner, StateMachine stateMachine, Animator anim = null)
        : base(owner, stateMachine, anim, new[] { AnimParams.IsHurt })
    {
    }

    public override void OnEnter()
    {
        base.OnEnter();   // IsHurt=true → 动画器 Entry 路由进 Hurt(子类必须调 base,漏了参数永不设置)
        var boss = (FirstBoss)owner;
        boss.moveInput = 0f;

        _enterTime = Time.time;
        _exiting = false;
        _fallbackDuration = ResolveDuration();
    }

    public override void OnUpdate()
    {
        var boss = (FirstBoss)owner;
        if (boss.IsDead) return;

        // 主路径:受击动画播完(非循环剪辑播到末帧会停住,normalizedTime 到 1 即完成)
        if (anim != null)
        {
            var info = anim.GetCurrentAnimatorStateInfo(0);
            if (info.IsName(HurtStateName) && info.normalizedTime >= 1f)
            {
                ReturnToChase(boss);
                return;
            }
        }

        // 兜底:anim 为空 / 状态名不符 / 事件与进度都拿不到 → 按片段长度超时退出
        if (Time.time - _enterTime >= _fallbackDuration)
            ReturnToChase(boss);
    }

    /// <summary>Hurt 末帧动画事件(经 BossAnimationRelay.OnBossHurtEnd 转发)→ 回追击</summary>
    public void OnHurtAnimEnd()
    {
        var boss = (FirstBoss)owner;
        if (boss.IsDead) return;
        ReturnToChase(boss);
    }

    /// <summary>结束统一出口:回追击(幂等,事件与超时两条路都走它)</summary>
    private void ReturnToChase(FirstBoss boss)
    {
        if (_exiting) return;
        _exiting = true;
        boss.Fsm.ChangeState(boss.CreateChaseState());
    }

    public override void OnExit()
    {
        base.OnExit();   // IsHurt=false → Hurt 状态 Exit,Entry 重判回落
        var boss = (FirstBoss)owner;
        boss.moveInput = 0f;
    }

    /// <summary>兜底时长:当前动画片段长度 + 余量(取不到片段时用 DefaultFallback)</summary>
    private float ResolveDuration()
    {
        if (anim == null) return DefaultFallback;
        var clips = anim.GetCurrentAnimatorClipInfo(0);
        if (clips != null && clips.Length > 0 && clips[0].clip != null)
            return Mathf.Max(0.1f, clips[0].clip.length) + FallbackMargin;
        return DefaultFallback;
    }
}
