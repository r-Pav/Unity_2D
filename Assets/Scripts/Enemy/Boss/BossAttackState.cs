using UnityEngine;

/// <summary>
/// Boss 攻击状态 — 动画驱动（独立于普通 enemy 的事件链）。
/// 进入:IsAttacking=true → 动画器 Entry 路由进 Attack 状态播放动画。
/// 保持:攻击动画完整播完一圈才回追击(不再因玩家出范围截断)。
/// 退出:动画播完(loop 动画 normalizedTime 回绕检测,或动画结束事件 OnBossAttackEnd)。
/// 伤害:命中帧动画事件(OnBossAttackHitFrame → OnHitFrame)结算一次;
///       动画未挂事件时由 OnUpdate 的 normalizedTime >= 0.5 兜底,两条路共用 _hitDone 互斥。
/// </summary>
public class BossAttackState : EntityState
{
    private float _lastNormalized;   // 上一帧动画进度(loop 回绕检测)
    private bool _hitDone;           // 本次攻击是否已结算伤害(命中帧一次性)

    // [2026-09-07 AttackVFXAnchor 重构暂停:Boss 攻击持续特效待玩家侧验收后按新结构迁移]
    ///// <summary>攻击持续 VFX 锚点(attack_VFX 子物体上的 AttackVFXAnchor;未配置时为 null,空安全)</summary>
    //private AttackVFXAnchor _vfx;

    public BossAttackState(CharacterBase owner, StateMachine stateMachine, Animator anim = null)
        : base(owner, stateMachine, anim, new[] { AnimParams.IsAttacking })
    {
    }

    public override void OnEnter()
    {
        base.OnEnter(); // IsAttacking=true → 动画器 Entry 路由进 Attack
        var boss = (FirstBoss)owner;
        boss.moveInput = 0f;
        // 普攻冷却改由 BossAttackDirector 自己记(_nextMeleeAt = Time.time + attackCooldown),
        // 这里不再调 boss.StartMeleeInterval()(该接口保留给重击收尾用 → boss.IsMeleeIntervalActive 仍作额外门槛)

        // 面朝玩家
        float dir = boss.DirectionToPlayer();
        if (dir != 0f)
            boss.UpdateFacing(dir);

        _lastNormalized = 0f;
        _hitDone = false;

        // [2026-09-07 AttackVFXAnchor 重构暂停] 攻击持续 VFX:普攻播 slot_attack
        //if (_vfx == null) _vfx = owner.GetComponentInChildren<AttackVFXAnchor>(true);
        //_vfx?.Show("slot_attack");
    }

    public override void OnUpdate()
    {
        var boss = (FirstBoss)owner;
        if (boss.IsDead) return;

        // 兜底结算:优先走命中帧动画事件(BossAnimationRelay.OnBossAttackHitFrame → OnHitFrame),
        // 这里只是「Attack.anim 未挂命中帧事件」时的保底,老行为保持不变(进度过半即出伤)。
        // 两条路共用 _hitDone 一次性门控 → 同一次普攻只会结算一次伤害。
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

    /// <summary>
    /// 命中帧动画事件(经 BossAnimationRelay.OnBossAttackHitFrame 转发):Attack 片段命中帧触发 → 结算一次普攻伤害。
    /// 与 OnUpdate 的 normalizedTime >= 0.5 兜底共用 _hitDone:谁先到谁结算,另一方成为无操作 → 一次普攻只掉一次血。
    /// </summary>
    public void OnHitFrame()
    {
        if (_hitDone) return;              // 本帧/兜底已结算过,忽略
        var boss = (FirstBoss)owner;
        if (boss.IsDead) return;           // 与 OnUpdate/OnAnimEnd 同口径:死后不再出伤
        boss.PerformDefaultMelee();
        _hitDone = true;
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

        // [2026-09-07 AttackVFXAnchor 重构暂停] 攻击结束:收起持续特效(淡出)
        //_vfx?.Hide();
    }
}
