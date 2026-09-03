using UnityEngine;

/// <summary>
/// 空中攻击状态 — 三连击单状态(与地面同构,复用 Attack1/2/3 动画 clip),继承 PlayerComboState。
/// 差异:悬停(重力小值缓沉)/ 上挑(ApplyLift,越打越高)/ 一滞空一套(每次进入强制从第 1 段)/
/// OnEnter 强制直切 AirAttack 子机(坑 39 兜底)/ 连段结束恢复重力落态。
/// 空中闪击(2026-09-03):每段挥击前 TryBlinkToAirEnemy —— 闪现到攻击范围内"距玩家最远"的
/// 空中 enemy 侧面(左右交替),y 居中对齐,再挥击;范围内无空中 enemy → 不闪,原地攻击。
/// </summary>
public class PlayerAirAttackState : PlayerComboState
{
    private readonly PlayerJump jump;

    private float _airAttackOriginalGravity = 1f;   // 空中攻击前的重力(连段结束/退出时恢复)
    private bool _airAttackGravityRestored = true;  // 重力是否已恢复(防重复恢复)
    private float _hoverTimer;                      // 滞空计时:落地退出需先滞空最小时间(防低空攻击瞬间退出)
    private WeaponThrow _weaponThrow;               // 武器配置缓存(空中闪击侧面间距读取;延迟解析,未挂时为 null 走兜底)

    // [2026-09-03] 空中闪击诊断日志开关(saika 验收调试用):TryBlinkToAirEnemy 内 AirBlinkDebug 引用处
    // 打印段号/目标/墙侧/落点解析日志;Inspector 勾选开启,默认关不影响行为
    [SerializeField] private bool _airBlinkDebug;
    private bool AirBlinkDebug => _airBlinkDebug;

    private const float MinHoverTime = 0.25f;
    private const float InputOpenTimeout = 0.5f;    // 输入门事件帧兜底超时(动画事件丢失时自动开门+恢复重力)
    private const float AirLiftForce = 1f;          // 每段出手上挑力(累加,连段越打越高)
    private const float MinLiftSpeed = 1.5f;        // 出手上挑最低速度
    private const float AirControlScale = 0.3f;     // 攻击中水平微调系数(弱于普通空中移动)

    public PlayerAirAttackState(CharacterBase owner, StateMachine stateMachine, Animator anim,
        PlayerCombat combat, PlayerJump jump, float comboResetTimer, float comboExitWindow)
        : base(owner, stateMachine, anim, new[] { AnimParams.IsAirAttacking }, combat, comboResetTimer, comboExitWindow)
    {
        this.jump = jump;
    }

    protected override bool IsAirAttack => true;

    protected override void OnComboEnter()
    {
        var pc = (PlayerController)owner;
        comboIndex = 1;   // 一滞空一套:每次进入强制从第 1 段(空中不推进续段)

        // [2026-08-24 兜底] 强制直切 AirAttack 子机内对应段:绕过 Jump/Fall 的 Exit 过渡竞争
        // (坑 39 同款:Jump 的 IsJumping==false→Exit 优先于 IsAirAttacking→子机,动画层回 Entry 后
        //  Entry 无 IsAirAttacking 过渡 → 落 Locomotion 卡住。子机内状态名:Attack/Attack2/Attack3)
        if (anim != null)
        {
            string stateName = comboIndex == 1 ? "Attack" : "Attack" + comboIndex;
            anim.Play("Base Layer.AirAttack." + stateName, 0, 0f);
        }

        // 攻击朝向跟随当前输入
        if (combat != null)
            pc.UpdateFacing(combat.AttackDir);

        // 悬停:水平速度减半 + 垂直速度保留 30%(不硬切 0,保留惯性,过渡自然)
        // + 重力小值缓沉(0.15,不归零,视觉更活;连段结束才恢复原重力,期间不下坠)
        Rigidbody2D rb = pc.GetRigidbody();
        if (rb != null)
        {
            rb.velocity = new Vector2(rb.velocity.x * 0.5f, rb.velocity.y * 0.3f);
            _airAttackOriginalGravity = rb.gravityScale;
            rb.gravityScale = Mathf.Min(_airAttackOriginalGravity, 0.3f);
            _airAttackGravityRestored = false;
        }
        _hoverTimer = 0f;

        // 第一段出手:给上挑力
        ApplyLift();

        // 一滞空一套:标记本次滞空已用过空中攻击(落地 ResetJumps 时清)
        jump?.MarkAirAttackUsed();

        // 空中闪击:第 1 段挥击前闪现到攻击范围内最远的空中 enemy 侧面(无空中目标 → 不闪,原地攻击)
        TryBlinkToAirEnemy();
    }

    protected override void OnComboUpdate()
    {
        var pc = (PlayerController)owner;
        _hoverTimer += Time.deltaTime;

        // 水平微调(弱化):攻击中左右可控,力度弱于普通空中移动;不控则保持惯性
        float h = Input.GetAxisRaw("Horizontal");
        if (Mathf.Abs(h) > 0.01f && pc.GetRigidbody() != null)
        {
            float targetX = h * pc.AirMaxSpeed * AirControlScale;
            float newX = Mathf.MoveTowards(pc.GetRigidbody().velocity.x, targetX,
                pc.AirAcceleration * AirControlScale * Time.deltaTime);
            pc.SetVelocityPublic(x: newX);
        }

        // 输入门事件帧兜底:动画事件未挂/丢失时,超时自动开门,防永久悬空锁输入
        // (悬停要持续到连段结束 EndComboAndChangeState,不在 OnInputOpen 恢复重力)
        if (!InputOpen && _hoverTimer > InputOpenTimeout)
            OnInputOpen();
    }

    protected override void OnComboCut()
    {
        base.OnComboCut();   // 特效换槽(空中 slot_Air;基类统一处理,否则空中切段不换槽)
        ApplyLift();         // 切段出手:再给一次上挑力(累加,越打越高)

        // 空中闪击:第 2/3 段切段瞬间再闪到下一目标侧面(玩家在 enemy 哪侧就闪另一侧 → 左右交替;
        // 无空中目标 → 不闪,原地攻击)。已在上段末尾由 ApplyLift 给了上挑,闪现不清 y,悬停不破坏
        TryBlinkToAirEnemy();
    }

    protected override void OnComboExit()
    {
        RestoreGravity();
    }

    protected override void AdvanceComboOnExit()
    {
        // 空中不推进连击段:一滞空一套,中断/打完后下次进入由 OnComboEnter 强制 comboIndex=1
    }

    protected override void EndComboAndChangeState(PlayerController pc, float h)
    {
        RestoreGravity();   // 开始下坠

        // 空中 → 落 FallState;已贴地(低空攻击)→ 直接落 Idle/Move
        if (pc.IsGrounded())
        {
            jump?.ResetJumps();
            stateMachine.ChangeState(Mathf.Abs(h) > 0.1f ? pc.MoveState : pc.IdleState);
        }
        else
        {
            stateMachine.ChangeState(pc.FallState);
        }
    }

    /// <summary>
    /// 空中闪击(每段挥击前调用一次,非每帧):闪现到攻击范围内"距玩家最远"的空中 enemy 侧面,再挥击。
    /// - 目标:MeleeHitDetector 扫 combat.RangeIndicator(与命中同源的方框)+ EnemyLayer → GetComponentInParent
    ///   拿 EnemyControllerBase,筛 !IsDead && !IsGrounded(空中);取距玩家最远的一只(多 enemy 防闪进中间)
    /// - 落点:玩家在 enemy 哪一侧就闪到另一侧(左右交替);y 与 enemy 居中对齐;
    ///   水平间距读 WeaponThrow.airBlinkSideGap(未挂 WeaponThrow 兜底 1.5);
    ///   优先用 enemy 空中靠墙标记(WallSide):首选侧靠墙/管道 → 直接落"远离墙的安全侧";
    ///   空中夹缝(两侧都堵) → 不闪,原地攻击
    /// - 执行:物理体瞬移(rb.position,防 transform 插值撕裂;不用 PlayerTeleport——无敌帧/清速/事件副作用都不要)
    ///   + 清水平速度防滑(保留 y:悬停/上挑惯性不清)+ 朝向敌人
    /// - 无空中目标(范围内全是地面 enemy / 目标死亡落地)→ return,不闪,原地攻击照旧
    /// 闪现不重置状态机/动画/悬停重力:动画正在播,位置跳变靠玩家视觉(闪=瞬移)掩盖。
    /// </summary>
    private void TryBlinkToAirEnemy()
    {
        var pc = (PlayerController)owner;
        if (pc == null || combat == null) return;

        Collider2D[] cols = MeleeHitDetector.Detect(combat.RangeIndicator, combat.EnemyLayer);
        if (AirBlinkDebug)
            Debug.Log($"[AirBlink] 段={comboIndex} pos={pc.transform.position} facing={pc.GetFacing()} 矩形={cols.Length}");
        if (cols == null || cols.Length == 0) return;

        // 目标 = 距玩家最远的非死亡空中 enemy(目标死亡/落地后检测自然为空 → 不闪,不报错)
        EnemyControllerBase target = null;
        float bestSqr = -1f;
        Vector2 playerPos = pc.transform.position;
        foreach (var col in cols)
        {
            var e = col != null ? col.GetComponentInParent<EnemyControllerBase>() : null;
            if (e == null || e.IsDead || e.IsGrounded) continue;
            float d = ((Vector2)e.transform.position - playerPos).sqrMagnitude;
            if (d > bestSqr)
            {
                bestSqr = d;
                target = e;
            }
        }
        if (target == null)
        {
            if (AirBlinkDebug) Debug.Log("[AirBlink] 无空中目标,不闪");
            return;   // 范围内没有空中 enemy → 不闪,原地攻击
        }
        if (AirBlinkDebug)
            Debug.Log($"[AirBlink] 目标 enemyPos={target.transform.position} grounded={target.IsGrounded} 左右靠墙: L={target.WallSide==-1||target.WallBlockedBothSides} R={target.WallSide==1||target.WallBlockedBothSides}");

        // 落点侧:玩家在 enemy 哪一侧就闪另一侧(左右交替)。
        // 优先用 enemy 空中靠墙标记(WallSide):首选侧靠墙/管道 → ResolveSafeSide 返回远离墙的安全侧,
        // 防闪进墙里;空中夹缝(两侧都堵)返回 0 → 不闪,原地攻击(与"范围内无空中目标"同款)。
        // 侧选不再自己算 side 翻转主逻辑——标记已含两侧墙信息;IsDestBlocked 重叠兜底只防标记过期。
        int preferredSide = playerPos.x >= target.transform.position.x ? -1 : 1;
        int side = target.ResolveSafeSide(preferredSide);
        if (AirBlinkDebug)
            Debug.Log($"[AirBlink] preferred={preferredSide} ResolveSafeSide={side}");
        if (side == 0) return;   // enemy 空中夹缝:两侧都靠墙,无侧向落点 → 不闪,原地攻击
        // 武器配置延迟解析(有目标才查;缓存,重复调用不重复 GetComponentInChildren)
        if (_weaponThrow == null && owner != null)
            _weaponThrow = owner.GetComponentInChildren<WeaponThrow>();
        float gap = (_weaponThrow != null) ? _weaponThrow.AirBlinkSideGap : 1.5f;
        if (gap <= 0f) gap = 1.5f;    // 配置异常(0/负)兜底,防重叠进 enemy
        Vector2 dest = new Vector2(target.transform.position.x + side * gap, target.transform.position.y);

        // 重叠兜底(防标记过期):标记可能因击退水平移动/墙距变化过期 —— ResolveSafeSide 给的侧仍被
        // 实心碰撞(墙/地面/其它实体)或管道挡住 → 翻到 enemy 另一侧重试一次;
        // 两侧都堵 → 不闪(原地攻击)。防止 enemy 靠墙时 player 闪进墙里穿墙/卡墙。
        if (IsDestBlocked(dest, target.transform))
        {
            if (AirBlinkDebug) Debug.Log($"[AirBlink] dest={dest} 被挡,翻侧");
            side = -side;
            dest = new Vector2(target.transform.position.x + side * gap, target.transform.position.y);
            if (IsDestBlocked(dest, target.transform))
            {
                if (AirBlinkDebug) Debug.Log($"[AirBlink] 另一侧 dest={dest} 也被挡,不闪");
                return;
            }
        }

        // 物理体瞬移(设置 rb.position 而非 transform.position,防物理插值在两位置间撕裂)
        Rigidbody2D rb = pc.GetRigidbody();
        if (rb == null) return;
        rb.position = dest;

        // 清水平速度防滑(保留 y,悬停/上挑惯性不清)
        pc.SetVelocityPublic(x: 0f);

        // 朝向敌人(按玩家与 enemy 相对位置,同背刺落点转向逻辑)。
        // 坑:必须用 dest 判定,不能读 pc.transform.position——rb.position 赋值后同帧
        // transform.position 尚未同步(还是闪前旧值),读它会朝向判反 → 攻击矩形朝敌人反侧 → 打空
        pc.UpdateFacing(target.transform.position.x >= dest.x ? 1f : -1f);
    }

    /// <summary>落点是否被碰撞挡住(防瞬移落点在实体内部):实心碰撞(墙/地面/实体)一律挡;
    /// trigger 仅管道(AreaChannelTrigger 区域)算挡——闪进管道会触发管道自动移动;
    /// 目标 enemy 自身与其子物体不算。检测半径 0.45 覆盖玩家/敌人半个身位</summary>
    private bool IsDestBlocked(Vector2 dest, Transform ignoreEnemy)
    {
        Collider2D[] hits = Physics2D.OverlapCircleAll(dest, 0.45f);
        foreach (var h in hits)
        {
            if (h == null) continue;
            Transform root = h.transform;
            if (root == ignoreEnemy) continue;                    // 目标 enemy 自身
            if (root.IsChildOf(ignoreEnemy) || ignoreEnemy.IsChildOf(root)) continue; // enemy 子物体/父级
            if (!h.isTrigger) return true;                        // 实心 = 挡
            if (AreaChannelTrigger.IsPointInChannel(dest)) return true;  // 管道 trigger = 挡
        }
        return false;
    }

    /// <summary>出手上挑:每段攻击开始时给一个向上的力(累加,连段越打越高)。
    /// 累加后若仍低于 MinLiftSpeed(如下落中攻击被 vy×0.3 削到负值),提升到最低上挑速度,
    /// 保证每段出手都有可见的上升感</summary>
    private void ApplyLift()
    {
        var pc = (PlayerController)owner;
        Rigidbody2D rb = pc != null ? pc.GetRigidbody() : null;
        if (rb != null)
        {
            float newVy = rb.velocity.y + AirLiftForce;
            if (newVy < MinLiftSpeed) newVy = MinLiftSpeed;
            rb.velocity = new Vector2(rb.velocity.x, newVy);
        }
    }

    /// <summary>恢复重力(带防重标志):连段结束/打断/受击/超时统一入口</summary>
    private void RestoreGravity()
    {
        if (_airAttackGravityRestored) return;
        var pc = (PlayerController)owner;
        Rigidbody2D rb = pc != null ? pc.GetRigidbody() : null;
        if (rb != null)
        {
            rb.gravityScale = _airAttackOriginalGravity;
            _airAttackGravityRestored = true;
        }
    }
}
