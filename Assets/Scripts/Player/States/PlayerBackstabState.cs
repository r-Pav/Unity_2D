using UnityEngine;

/// <summary>
/// 重音背刺状态(方案 v2,无连打)— 自动重音窗口内按 F 触发,模板同 PlayerAttackState(继承 EntityState)。
/// OnEnter:选最近敌人 → 落点 = 敌人背后(敌人背对方向 × behindOffset,y 对齐目标中心,空中背刺允许)
///   → PlayerTeleport.TeleportTo(复用:瞬移+贴墙钳制+清速度+无敌帧+传送事件) → 强制转向敌人 → 播 Backstab 动画;
///   无目标:原地闪现(不位移,短无敌帧),播空挥动画。
/// 命中帧(动画事件 OnBackstabHitFrame → PlayerCombat → 本状态):对目标结算高伤害(3x)+ 强制硬直
///   (攻击标签 Sword_Heavy → Poise 近战路径 → EnterStunState)。
/// 结束(动画事件 OnBackstabEnd / 超时兜底 2.5s):回 Idle/Move。
/// </summary>
public class PlayerBackstabState : EntityState
{
    /// <summary>状态最大存活时长(秒):动画事件丢失/Play 失败时兜底退出,防 LocksInput 永久锁死(参考 PlayerAttackState 2.5s)</summary>
    private const float MaxBackstabDuration = 2.5f;

    private readonly PlayerCombat combat;
    private readonly PlayerTeleport teleport;
    private readonly float searchRadius;      // 目标搜索半径
    private readonly float behindOffset;      // 背后落点偏移(米)
    private readonly float damageMultiplier;  // 背刺伤害倍率(基础伤害 × 此值)
    private readonly float knockbackForce;    // 背刺击退力

    private EnemyControllerBase _target;
    private bool _hitResolved;    // 命中帧已结算(防重复;目标死亡/空时跳过)
    private bool _endTriggered;   // 动画结束事件已触发(防重复退出)
    private float _stateTimer;    // 状态存活时长(超时兜底)

    public override bool LocksInput => true;

    public PlayerBackstabState(CharacterBase owner, StateMachine stateMachine, Animator anim,
        PlayerCombat combat, PlayerTeleport teleport, float searchRadius, float behindOffset,
        float damageMultiplier, float knockbackForce)
        : base(owner, stateMachine, anim)
    {
        this.combat = combat;
        this.teleport = teleport;
        this.searchRadius = searchRadius;
        this.behindOffset = behindOffset;
        this.damageMultiplier = damageMultiplier;
        this.knockbackForce = knockbackForce;
    }

    public override void OnEnter()
    {
        base.OnEnter();
        _target = FindNearestTarget();
        _hitResolved = false;
        _endTriggered = false;
        _stateTimer = 0f;

        var pc = (PlayerController)owner;

        if (_target != null)
        {
            // 落点 = 敌人背后(敌人背对方向):x = enemy.x - Facing × offset;y 对齐目标中心(空中背刺允许)
            Vector2 dest = new Vector2(
                _target.transform.position.x - _target.Facing * behindOffset,
                _target.transform.position.y);
            if (teleport != null)
                teleport.TeleportTo(dest);
            else
                pc.transform.position = dest;   // 未挂 PlayerTeleport 时兜底直接位移(无敌帧等由挂载后生效)
            pc.UpdateFacing(_target.Facing);    // 强制转向敌人
        }
        else
        {
            // 无目标:原地闪现(复用 TeleportTo 自身位置 = 无敌帧+事件,无位移);朝向跟随当前输入
            if (teleport != null)
                teleport.TeleportTo((Vector2)pc.transform.position);
            float h = Input.GetAxisRaw("Horizontal");
            if (Mathf.Abs(h) > 0.1f) pc.UpdateFacing(h);
        }

        anim?.Play("Backstab", 0, 0f);
    }

    public override void OnUpdate()
    {
        _stateTimer += Time.deltaTime;
        if (_stateTimer > MaxBackstabDuration)
        {
            ExitBackstab();
            return;
        }
        // 目标在背刺动画中死亡(被环境/其他伤害击杀):命中帧判空跳过,动画自然结束
    }

    /// <summary>背刺命中帧(动画事件 OnBackstabHitFrame → PlayerCombat → 本状态):对目标结算高伤害+强制硬直</summary>
    public void OnBackstabHitFrame()
    {
        if (_hitResolved) return;
        _hitResolved = true;
        if (_target == null || _target.IsDead) return;
        combat?.ExecuteBackstab(_target, damageMultiplier, knockbackForce);
    }

    /// <summary>背刺动画结束(动画事件 OnBackstabEnd → PlayerCombat → 本状态):回 Idle/Move</summary>
    public void OnBackstabEnd()
    {
        if (_endTriggered) return;
        _endTriggered = true;
        ExitBackstab();
    }

    public override void OnExit()
    {
        base.OnExit();
        _target = null;
    }

    /// <summary>退出背刺:贴地回 Idle/Move(带朝向输入),空中回 FallState(对齐 PlayerAirAttackState 落态)</summary>
    private void ExitBackstab()
    {
        var pc = (PlayerController)owner;
        float h = Input.GetAxisRaw("Horizontal");
        if (pc.IsGrounded())
            stateMachine.ChangeState(Mathf.Abs(h) > 0.1f ? pc.MoveState : pc.IdleState);
        else
            stateMachine.ChangeState(pc.FallState);
    }

    /// <summary>选最近非死亡敌人(Boss 也可,普通场景无 Boss;空中敌人同样可作目标,允许空中背刺)</summary>
    private EnemyControllerBase FindNearestTarget()
    {
        LayerMask mask = combat != null ? combat.EnemyLayer : ~0;
        Vector2 origin = owner.transform.position;
        Collider2D[] cols = Physics2D.OverlapCircleAll(origin, searchRadius, mask);
        EnemyControllerBase nearest = null;
        float bestSqr = float.MaxValue;
        foreach (var c in cols)
        {
            var e = c.GetComponentInParent<EnemyControllerBase>();
            if (e == null || e.IsDead) continue;
            float d = ((Vector2)e.transform.position - origin).sqrMagnitude;
            if (d < bestSqr) { bestSqr = d; nearest = e; }
        }
        return nearest;
    }
}
