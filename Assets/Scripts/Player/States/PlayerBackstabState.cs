using UnityEngine;

/// <summary>
/// 重音背刺状态(方案 v2,无连打)— 自动重音窗口内按 F 触发,模板同 PlayerAttackState(继承 EntityState)。
/// OnEnter:选最近敌人 → 落点解析分两条路径:
///   ① 背后开阔(enemy 面朝墙/背后净空)→ 落点 = 敌人背后(敌人背对方向 × behindOffset,y 对齐目标中心,
///      空中背刺允许),ResolveBackstabLanding 射线兜底(管道/自身挡则翻 enemy 正面);
///   ② enemy 背靠墙/管道(IsWallBlockedOnSide(behindSide) 判 2.5m 带内堵)→ 换位挤出:玩家落到 enemy 原站位
///      (enemy 站得住 = 安全点,不穿墙),enemy 被挪到玩家面前攻击框中心(开阔侧),玩家回身朝 enemy 挥刺,
///      后续动画/命中帧/ExecuteBackstab 完全不变(精准打击,把 enemy 打出墙边);
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
    private readonly Vector2 knockback;       // 背刺击退向量(x 水平镜像,y 上挑,与三连击同语义)
    private readonly float hoverDuration;     // 空中背刺命中后的滞空停顿(玩家+敌人一起停)

    private EnemyControllerBase _target;
    private bool _hitResolved;    // 命中帧已结算(防重复;目标死亡/空时跳过)
    private bool _endTriggered;   // 动画结束事件已触发(防重复退出)
    private float _stateTimer;    // 状态存活时长(超时兜底)

    public override bool LocksInput => true;

    public PlayerBackstabState(CharacterBase owner, StateMachine stateMachine, Animator anim,
        PlayerCombat combat, PlayerTeleport teleport, float searchRadius, float behindOffset,
        float damageMultiplier, Vector2 knockback, float hoverDuration)
        : base(owner, stateMachine, anim, new[] { AnimParams.IsBackstabbing })   // Entry 路由:IsBackstabbing=true 进 Backstab,Exit 清 false
    {
        this.combat = combat;
        this.teleport = teleport;
        this.searchRadius = searchRadius;
        this.behindOffset = behindOffset;
        this.damageMultiplier = damageMultiplier;
        this.knockback = knockback;
        this.hoverDuration = hoverDuration;
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
            // 背刺方向永远按 enemy 朝向:玩家出现在 enemy 背后。
            // 落点侧 = enemy 背对方向(behindSide)。IsWallBlockedOnSide 判该侧 2.5m 半带内是否有堵
            // (实心墙/地面/管道 trigger;内部已排除 enemy 自身/其它 enemy/玩家,普通 trigger 不算)。
            int behindSide = -_target.Facing;
            // 换位挤出需要玩家面前攻击框指示器提供 enemy 新站位;未配置(RangeIndicator 空,理论不出现)
            // 时退化走原射线兜底路径,不硬凑换位
            bool canSwap = _target.IsWallBlockedOnSide(behindSide)
                && combat != null && combat.RangeIndicator != null;
            if (canSwap)
            {
                // ── 背后被堵(enemy 背靠墙/管道,玩家侧开阔)→ 换位挤出 ──
                // 玩家落 enemy 背后会进墙,改为玩家 ↔ enemy 互换位置:
                // 玩家瞬移到 enemy 原站位(enemy 站得住的位置 = 安全点,不穿墙);
                // enemy 被挪到玩家面前攻击框中心(RangeIndicator.Center,开阔侧)——被挪后其 Facing 不变,
                // 玩家天然落在 enemy 背后;后续动画/命中帧/ExecuteBackstab 完全照常(精准打击),
                // 击退方向 = 玩家→enemy,把 enemy 打出墙边。
                Vector2 enemyOld = _target.transform.position;   // enemy 原站位(玩家落点)
                // 玩家面前攻击框中心(世界坐标;此刻玩家还没瞬移,以玩家当前站位为基准)。
                // 极端:enemy 已几乎在攻击框中心(enemyNew≈enemyOld)→ 仍执行,重叠由
                // "先挪 enemy 再移玩家"的瞬移顺序吸收,不做额外校验(假设:玩家面朝开阔侧,
                // 框中心不会被墙挡;如验收发现再补校验)。
                Vector2 enemyNew = (Vector2)combat.RangeIndicator.Center;
                // 先挪 enemy(物理体位 + 清速度,防旧击退速度把它带跑;无 rb 走 transform),再移玩家
                _target.ForceSetPosition(enemyNew);
                if (teleport != null)
                    teleport.TeleportTo(enemyOld);   // 复用原语义:瞬移+贴墙钳制+清速度+无敌帧+事件
                else
                    pc.transform.position = enemyOld;   // 未挂 PlayerTeleport 时兜底直接位移
                // 回身朝 enemy 新位置(玩家新站位 = enemyOld):enemyNew 在右 → 朝右,反之朝左。
                // 不能用 enemy.Facing(被挪后 Facing 不变,玩家在它背后,用它玩家会背朝 enemy);
                // 也不能读 pc.transform.position——TeleportTo 走 rb.position,同帧 transform 未同步(空中闪同坑)
                pc.UpdateFacing(enemyNew.x >= enemyOld.x ? 1f : -1f);
            }
            else
            {
                // ── 背后净空 → 原逻辑:落点 = 敌人背后(敌人背对方向)──
                // 落点 = 敌人背后(敌人背对方向):x = enemy.x - Facing × offset;y 对齐目标中心(空中背刺允许)
                Vector2 dest = new Vector2(
                    _target.transform.position.x - _target.Facing * behindOffset,
                    _target.transform.position.y);
                dest = ResolveBackstabLanding(dest);   // 落点避开管道(PlayerTeleport 只钳制墙层,管道 Channel 层会直接传进去)
                if (teleport != null)
                    teleport.TeleportTo(dest);
                else
                    pc.transform.position = dest;   // 未挂 PlayerTeleport 时兜底直接位移(无敌帧等由挂载后生效)
                // 强制转向敌人:按玩家与 enemy 相对位置(不能用 enemy.Facing——靠墙时落点改到 enemy 正面,
                // enemy.Facing 朝玩家,用它玩家会背朝 enemy)
                pc.UpdateFacing(_target.transform.position.x >= pc.transform.position.x ? 1f : -1f);
            }
        }
        else
        {
            // 无目标:原地闪现(复用 TeleportTo 自身位置 = 无敌帧+事件,无位移);朝向跟随当前输入
            if (teleport != null)
                teleport.TeleportTo((Vector2)pc.transform.position);
            float h = Input.GetAxisRaw("Horizontal");
            if (Mathf.Abs(h) > 0.1f) pc.UpdateFacing(h);
        }
        // 动画:Entry 路由(IsBackstabbing=true 由基类 OnEnter 设置,动画器 Entry → Backstab),不代码直切
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

    /// <summary>背刺命中帧(动画事件 OnBackstabHitFrame → PlayerCombat → 本状态):对目标结算高伤害+强制硬直;
    /// 命中后目标头顶标识立即消失(消失时机 2:被刺伤害帧事件);
    /// 空中背刺命中:只刷新空中攻击计数(AirAttackUsed,跳跃次数不动)+ 玩家与敌人一起短滞空停顿(hoverDuration)</summary>
    public void OnBackstabHitFrame()
    {
        if (_hitResolved) return;
        _hitResolved = true;
        if (_target == null || _target.IsDead) return;
        combat?.ExecuteBackstab(_target, damageMultiplier, knockback);
        // 重音成功:头顶 combo 计数 +1(BeatComboIndicator,不存在则跳过);
        // 在 _target 非空非死分支内执行,挥空/目标死亡不计数,天然满足"挥空无效"
        owner.GetComponentInChildren<BeatComboIndicator>(true)?.NotifyBeatHit();
        _target.GetComponentInChildren<BeatFlashPoint>(true)?.Hide();

        var pc = (PlayerController)owner;
        if (pc == null || pc.IsGrounded()) return;

        // 空中背刺:刷新空中攻击计数 + 玩家/敌人一起短滞空
        if (pc.JumpComp != null)
            pc.JumpComp.ResetAirAttackOnly();
        if (hoverDuration > 0f)
        {
            pc.StartCoroutine(HoverRoutine(pc, hoverDuration));   // EntityState 非 MonoBehaviour,协程挂宿主启动
            if (!_target.IsGrounded)
                _target.ApplyAirHangFreeze(hoverDuration);
        }
    }

    /// <summary>玩家背刺后缓落:小重力缓慢下落(不清速度,避免定身后突然坠落)+ 期间每帧吸附敌人身后
    /// (敌人被击退飞走,玩家跟着飘,保持连段距离),停 hoverDuration 秒后恢复原重力(真实时间,卡帧不影响)</summary>
    private System.Collections.IEnumerator HoverRoutine(PlayerController pc, float duration)
    {
        var rb = pc.GetRigidbody();
        float origGravity = rb != null ? rb.gravityScale : 1f;
        if (rb != null)
            rb.gravityScale = Mathf.Min(origGravity, 0.3f);   // 缓落:小重力(参考空中攻击悬停),不清速度
        float t = 0f;
        while (t < duration)
        {
            t += Time.unscaledDeltaTime;
            // 跟随目标:保持敌人背后(与落点同款计算),敌人被击退移动时玩家跟着;吸附位置同样避开管道
            if (_target != null && !_target.IsDead)
            {
                float behindX = _target.transform.position.x - _target.Facing * behindOffset;
                Vector2 follow = new Vector2(behindX, _target.transform.position.y);
                follow = ResolveBackstabLanding(follow);   // 防吸附进管道(和落点同款避让)
                pc.transform.position = new Vector3(follow.x, follow.y, pc.transform.position.z);
            }
            yield return null;
        }
        if (rb != null)
            rb.gravityScale = origGravity;
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

    /// <summary>退出背刺:贴地回 Idle/Move(带朝向输入),空中回 FallState(对齐 PlayerAirAttackState 落态)。
    /// 动画器由基类 OnExit 清 IsBackstabbing=false → Backstab → Exit → Entry 重判(IsIdle/IsMove),不代码直切。</summary>
    private void ExitBackstab()
    {
        var pc = (PlayerController)owner;
        float h = Input.GetAxisRaw("Horizontal");
        if (pc.IsGrounded())
            stateMachine.ChangeState(Mathf.Abs(h) > 0.1f ? pc.MoveState : pc.IdleState);
        else
            stateMachine.ChangeState(pc.FallState);
    }

    /// <summary>背刺落点避让(射线版):从 enemy 位置朝背后(-Facing)发射射线,
    /// 命中墙/地面/实心管道等(非 trigger collider)或管道 trigger → 背后被挡 → 落点改到 enemy 正面;
    /// 背后空 → 原落点(enemy 背后)。防背刺被传进管道/墙内。</summary>
    private Vector2 ResolveBackstabLanding(Vector2 dest)
    {
        if (_target == null) return dest;
        Vector2 behindDir = Vector2.right * (-_target.Facing);
        RaycastHit2D hit = Physics2D.Raycast(_target.transform.position, behindDir, behindOffset + 0.3f);
        if (hit.collider == null) return dest;   // 背后空 → 原落点
        // 命中自身 collider(射线从 enemy 中心发出可能扫到自身):忽略
        if (hit.transform == _target.transform || hit.transform.IsChildOf(_target.transform))
            return dest;
        // 命中其他 trigger(非管道):忽略,不算挡
        if (hit.collider.isTrigger && !AreaChannelTrigger.IsPointInChannel(hit.point))
            return dest;
        // 背后被挡(墙/地面/实心管道/管道 trigger)→ 改到 enemy 正面(面朝玩家方向,通常空地)
        return new Vector2(
            _target.transform.position.x + _target.Facing * behindOffset,
            _target.transform.position.y);
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
