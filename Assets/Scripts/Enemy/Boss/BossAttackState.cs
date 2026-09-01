using UnityEngine;

/// <summary>
/// Boss 攻击状态 — 动画驱动（独立于普通 enemy 的事件链）。
/// 进入:IsAttacking=true → 动画器 Entry 路由进 Attack 状态播放动画。
/// 保持:攻击动画完整播完一圈才回追击(不再因玩家出范围截断)。
/// 退出:动画播完(loop 动画 normalizedTime 回绕检测,或动画结束事件 OnBossAttackEnd)。
/// 不做伤害判定(当前阶段只验证状态动画流转;伤害/技能后续接入)。
/// </summary>
public class BossAttackState : EntityState
{
    private float _lastNormalized;   // 上一帧动画进度(loop 回绕检测)
    private bool _hitDone;           // 本次攻击是否已结算伤害(命中帧一次性)

    /// <summary>攻击持续 VFX 锚点(attack_VFX 子物体上的 AttackVFXAnchor;未配置时为 null,空安全)</summary>
    private AttackVFXAnchor _vfx;

    public BossAttackState(CharacterBase owner, StateMachine stateMachine, Animator anim = null)
        : base(owner, stateMachine, anim, new[] { AnimParams.IsAttacking })
    {
    }

    public override void OnEnter()
    {
        base.OnEnter(); // IsAttacking=true → 动画器 Entry 路由进 Attack
        var boss = (FirstBoss)owner;
        boss.moveInput = 0f;
        boss.StartMeleeInterval();   // 普攻间隔起点(5 秒内不再普攻;技能/重击不走 CanAttack,不受限)

        // 面朝玩家
        float dir = boss.DirectionToPlayer();
        if (dir != 0f)
            boss.UpdateFacing(dir);

        _lastNormalized = 0f;
        _hitDone = false;

        // 攻击持续 VFX:普攻播 slot_attack
        if (_vfx == null) _vfx = owner.GetComponentInChildren<AttackVFXAnchor>(true);
        _vfx?.Show("slot_attack");
    }

    public override void OnUpdate()
    {
        var boss = (FirstBoss)owner;
        if (boss.IsDead) return;

        // 命中帧:动画进度过半时结算一次伤害(动画事件驱动可后续替换,当前 Attack.anim 无事件)
        if (!_hitDone && anim != null)
        {
            var info = anim.GetCurrentAnimatorStateInfo(0);
            if (info.IsName("Attack") && info.normalizedTime >= 0.5f)
            {
                boss.PerformDefaultMelee();
                _hitDone = true;
            }
        }

        // 攻击动画完整播完一圈才回追击(不再因玩家出范围截断)。
        // Attack.anim 是循环动画且无结束事件,用 normalizedTime 回绕检测"播完一圈";
        // 若后续在动画上挂了 OnBossAttackEnd 事件,由 OnAnimEnd 事件路径更快接管。
        if (IsAttackAnimFinished())
        {
            ReturnToChase(boss);
        }
    }

    /// <summary>攻击动画是否播完一圈(loop 动画 normalizedTime 从高位回绕到低位,或到 1)</summary>
    private bool IsAttackAnimFinished()
    {
        if (anim == null) return false;
        var info = anim.GetCurrentAnimatorStateInfo(0);
        if (!info.IsName("Attack")) return false;

        float nt = info.normalizedTime;
        bool wrapped = _lastNormalized > 0.8f && nt < _lastNormalized - 0.5f;
        _lastNormalized = nt;
        return wrapped || nt >= 1f;
    }

    /// <summary>Boss 独立动画事件(经 BossAnimationRelay 转发):Attack 动画结束帧 → 回追击(事件未挂时由 OnUpdate 进度检测接管)</summary>
    public void OnAnimEnd()
    {
        var boss = (FirstBoss)owner;
        if (boss.IsDead) return;
        ReturnToChase(boss);
    }

    /// <summary>攻击结束统一出口:回追击</summary>
    private void ReturnToChase(FirstBoss boss)
    {
        boss.Fsm.ChangeState(boss.CreateChaseState());
    }

    public override void OnExit()
    {
        base.OnExit(); // IsAttacking=false → 动画器 Exit,Entry 重判
        var boss = (FirstBoss)owner;
        boss.moveInput = 0f;

        // 攻击结束:收起持续特效(淡出)
        _vfx?.Hide();
    }
}
