using UnityEngine;

/// <summary>
/// 空中攻击状态 — 三连击单状态(与地面同构,复用 Attack1/2/3 动画 clip),继承 PlayerComboState。
/// 差异:悬停(重力小值缓沉)/ 上挑(ApplyLift,越打越高)/ 一滞空一套(每次进入强制从第 1 段)/
/// OnEnter 强制直切 AirAttack 子机(坑 39 兜底)/ 连段结束恢复重力落态。
/// 空中闪击(2026-09-03 重做):第 1 段不做闪(正常挥,超出攻击范围挥空);第 2/3 段切段瞬间
/// TryBlinkToAirEnemy —— 闪现到"距玩家最远"的空中 enemy 侧面(玩家对侧),y 居中对齐,再挥击;
/// 对侧实时靠墙检测(EnemyControllerBase.IsWallBlockedOnSide):对侧净空 → 正常闪侧打;
/// 对侧堵(墙/地面/管道)→ 不闪侧面,玩家占 enemy 原位、enemy 被往远离墙方向推 AirBlinkPushDistance,
/// 该击把 enemy 继续推离墙,后续段 enemy 已离墙正常左右闪;范围内无空中目标 → 不闪,原地攻击。
/// </summary>
public class PlayerAirAttackState : PlayerComboState
{
    private readonly PlayerJump jump;

    private float _airAttackOriginalGravity = 1f;   // 空中攻击前的重力(连段结束/退出时恢复)
    private bool _airAttackGravityRestored = true;  // 重力是否已恢复(防重复恢复)
    private float _hoverTimer;                      // 滞空计时:落地退出需先滞空最小时间(防低空攻击瞬间退出)
    private WeaponThrow _weaponThrow;               // 武器配置缓存(空中闪击间距/推敌距离读取;延迟解析,未挂时为 null 走兜底)

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

        // 第 1 段不做闪(不做接近处理):正常挥;超范围挥空由伤害检测自然判空
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

        // 空中闪击:第 2/3 段切段瞬间闪现到下一目标侧面(玩家在 enemy 哪侧就闪另一侧 → 左右交替;
        // 对侧靠墙 → 占 enemy 原位并把 enemy 推离墙;无空中目标 → 不闪,原地攻击)。
        // 已在上段末尾由 ApplyLift 给了上挑,闪现不清 y,悬停不破坏
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
    /// 空中闪击(第 2/3 段切段瞬间调用一次,非每帧;第 1 段不做接近处理):
    /// 目标 = 空中 enemy,首选闪玩家对侧。对侧实时检测(以 enemy 当前位置为中心的水平带,
    /// 宽 2×wallCheckHalfWidth、高覆盖 enemy 碰撞体整高,OverlapBox)——命中实心墙/地面/管道 trigger = 该侧堵:
    /// - 对侧净空 → 闪对侧(target.x + 对侧×gap,target.y),正常打;
    /// - 对侧堵 → 不闪侧面:玩家占 enemy 当前位置(玩家可站性由 enemy 原本站得住保证),
    ///   enemy 手动往远离墙方向(玩家原所在的开阔侧)硬挪 airBlinkPushDistance(默认 0.8)并清速度,
    ///   玩家朝被挪开的 enemy 挥击,这一击把它继续推离墙;后续段 enemy 已离墙正常左右闪。
    /// 目标搜索:先 MeleeHitDetector 扫 combat.RangeIndicator 框内;框内无(第 1 段击退把 enemy
    /// 推出框断连)→ 玩家 facing 前方放大矩形再找一次;仍无 → 不闪,挥空。
    /// 执行统一走"物理体瞬移 rb.position + 清水平速度 + 朝向"(不重置状态机/动画/悬停重力;
    /// 不用 PlayerTeleport——无敌帧/清速/事件副作用都不要)。闪现不重置动画,位置跳变靠视觉掩盖。
    /// </summary>
    private void TryBlinkToAirEnemy()
    {
        var pc = (PlayerController)owner;
        if (pc == null || combat == null) return;
        Rigidbody2D rb = pc.GetRigidbody();
        if (rb == null) return;   // 玩家无物理体:直接放弃(避免推敌分支先挪了 enemy 又因无 rb 落空)

        // 目标 = 距玩家最远的非死亡空中 enemy(目标死亡/落地后检测自然为空 → 不闪,不报错)
        Vector2 playerPos = pc.transform.position;
        Collider2D[] cols = MeleeHitDetector.Detect(combat.RangeIndicator, combat.EnemyLayer);
        EnemyControllerBase target = FindFarthestAirEnemy(cols, playerPos);

        // 框内无目标 → 回退:玩家 facing 前方加宽范围再找一次(防第 1 段击退把 enemy 推出框断连):
        // 检测矩形中心 = 原攻击框中心,尺寸 x 放大 2 倍(前方延长),y 略放大容差;筛选逻辑与框内一致
        if (target == null && combat.RangeIndicator != null)
        {
            Vector2 indCenter = combat.RangeIndicator.Center;
            Vector2 indSize = combat.RangeIndicator.Size;
            cols = Physics2D.OverlapBoxAll(indCenter,
                new Vector2(indSize.x * 2f, Mathf.Max(indSize.y * 1.5f, 2f)), 0f, combat.EnemyLayer);
            target = FindFarthestAirEnemy(cols, playerPos);
        }
        if (target == null)
        {
            if (AirBlinkDebug) Debug.Log("[AirBlink] 无空中目标,不闪");
            return;   // 没有空中 enemy → 不闪,原地挥(第 2/3 段挥空照旧)
        }

        Vector2 enemyPos = target.transform.position;
        // 首选侧 = 玩家对侧(玩家在左 → 闪右侧;左右交替)
        int preferredSide = playerPos.x >= enemyPos.x ? -1 : 1;
        // 对侧实时检测(公共方法,不缓存不每帧):命中实心墙/地面/管道 = 该侧堵
        bool sideBlocked = target.IsWallBlockedOnSide(preferredSide);
        if (AirBlinkDebug)
            Debug.Log($"[AirBlink] 段={comboIndex} preferred={preferredSide} 对侧堵={sideBlocked} enemy={enemyPos}");

        // 武器配置延迟解析(有目标才查;缓存,重复调用不重复 GetComponentInChildren)
        if (_weaponThrow == null && owner != null)
            _weaponThrow = owner.GetComponentInChildren<WeaponThrow>();
        float gap = (_weaponThrow != null) ? _weaponThrow.AirBlinkSideGap : 1.5f;
        if (gap <= 0f) gap = 1.5f;    // 配置异常(0/负)兜底,防重叠进 enemy

        Vector2 dest;                 // 玩家瞬移落点
        Vector2 targetPosAfter;       // 结算朝向用的目标最终位置(推敌分支 enemy 已被挪走)
        if (!sideBlocked)
        {
            // 对侧净空 → 闪对侧(水平间距 gap,y 与 enemy 居中对齐)
            dest = new Vector2(enemyPos.x + preferredSide * gap, enemyPos.y);
            targetPosAfter = enemyPos;
        }
        else
        {
            // 对侧堵 → 占位推敌:玩家占 enemy 原位,enemy 沿远离墙方向(pushDir = 玩家原所在的开阔侧)硬挪。
            // 极端兜底:挪后落点仍被堵或与玩家新站位重叠 → 挪距拉大到 1.2 再试;仍不行 → 不闪原地。
            int pushDir = -preferredSide;
            float pushBase = (_weaponThrow != null) ? _weaponThrow.AirBlinkPushDistance : 0.8f;
            if (pushBase <= 0f) pushBase = 0.8f;   // 配置异常兜底
            Vector2 playerDest = enemyPos;         // 玩家占 enemy 的点(enemy 原本站得住 → 玩家可站)
            Vector2 enemyDest = enemyPos + Vector2.right * pushDir * pushBase;
            if (IsPushPlacementInvalid(enemyDest, playerDest, target, pc))
            {
                pushBase = 1.2f;
                enemyDest = enemyPos + Vector2.right * pushDir * pushBase;
                if (IsPushPlacementInvalid(enemyDest, playerDest, target, pc))
                {
                    if (AirBlinkDebug) Debug.Log("[AirBlink] 占位推敌落点仍无效,不闪");
                    return;
                }
            }
            if (AirBlinkDebug)
                Debug.Log($"[AirBlink] 对侧堵→占位推敌 pushDir={pushDir} enemy→{enemyDest} 玩家→{playerDest}");
            // 顺序:先挪 enemy 再移玩家(玩家落 target 原位时 enemy 已先挪走,同帧不重叠太久)
            target.ForceSetPosition(enemyDest);    // 硬挪 + 清速度(防旧击退把它拉回墙边);不动状态机/动画
            dest = playerDest;
            targetPosAfter = enemyDest;
        }

        // 物理体瞬移(设置 rb.position 而非 transform.position,防物理插值在两位置间撕裂)
        rb.position = dest;

        // 清水平速度防滑(保留 y,悬停/上挑惯性不清)
        pc.SetVelocityPublic(x: 0f);

        // 朝向敌人(按玩家与 enemy 相对位置;占位推敌分支 enemy 已被挪走,用其挪后位置)。
        // 坑:必须用 dest 判定,不能读 pc.transform.position——rb.position 赋值后同帧
        // transform.position 尚未同步(还是闪前旧值),读它会朝向判反 → 攻击矩形朝敌人反侧 → 打空
        pc.UpdateFacing(targetPosAfter.x >= dest.x ? 1f : -1f);
    }

    /// <summary>从命中 collider 里筛"非死亡 + 空中"的 enemy,取距玩家最远的一只(多 enemy 防闪进中间);无则返回 null</summary>
    private EnemyControllerBase FindFarthestAirEnemy(Collider2D[] cols, Vector2 origin)
    {
        if (cols == null) return null;
        EnemyControllerBase target = null;
        float bestSqr = -1f;
        foreach (var col in cols)
        {
            var e = col != null ? col.GetComponentInParent<EnemyControllerBase>() : null;
            if (e == null || e.IsDead || e.IsGrounded) continue;
            float d = ((Vector2)e.transform.position - origin).sqrMagnitude;
            if (d > bestSqr)
            {
                bestSqr = d;
                target = e;
            }
        }
        return target;
    }

    /// <summary>
    /// 占位推敌的敌人新落点校验(极端兜底):返回 true = 该落点不能用,调用方应加距重试或放弃。
    /// ① 挪开后 enemy 与玩家新站位(玩家将占 enemy 原位)水平重叠(敌我碰撞体半宽之和不够推距);
    /// ② 落点被实体/管道挡(墙/地面;被挪 enemy 自身、其它 enemy、瞬移中的玩家不算墙,规则对齐公共 IsWallBlockedOnSide)。
    /// </summary>
    private bool IsPushPlacementInvalid(Vector2 enemyDest, Vector2 playerDest, EnemyControllerBase movingEnemy, PlayerController pc)
    {
        // ① 与玩家新站位重叠:水平距离 < (敌我半宽之和 × 1.15 余量)
        float eHalf = movingEnemy.Col != null ? movingEnemy.Col.bounds.extents.x : 0.35f;
        float pHalf = pc.Col != null ? pc.Col.bounds.extents.x : 0.35f;
        if (Mathf.Abs(enemyDest.x - playerDest.x) < (eHalf + pHalf) * 1.15f) return true;

        // ② 落点被实体/管道挡:圆形探测半径覆盖 enemy 碰撞体半宽(0.55 兜底半个身位)
        float probeR = Mathf.Max(0.55f, eHalf * 1.2f);
        Collider2D[] hits = Physics2D.OverlapCircleAll(enemyDest, probeR);
        foreach (var h in hits)
        {
            if (h == null) continue;
            Transform root = h.transform;
            if (root == movingEnemy.transform || root.IsChildOf(movingEnemy.transform)) continue;  // 被挪 enemy 自身/子物体
            if (root.GetComponentInParent<EnemyControllerBase>() != null) continue;                // 其它 enemy 不算墙
            if (root.GetComponentInParent<PlayerController>() != null) continue;                   // 玩家瞬移中不算墙
            if (!h.isTrigger) return true;                                                         // 实心 = 堵
            if (h.GetComponentInParent<AreaChannelTrigger>() != null) return true;                 // 管道 trigger = 堵
            // 普通 trigger(门/攻击判定框等)不算挡
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
