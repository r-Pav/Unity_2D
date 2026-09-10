using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 重音背刺状态(方案 v2,无连打)+ 连音逐点推进(P7)— 自动重音窗口内按 F 触发,模板同 PlayerAttackState(继承 EntityState)。
/// OnEnter:选最近敌人 → 落点解析分两条路径:
///   ① 背后开阔(enemy 面朝墙/背后净空)→ 落点 = 敌人背后(敌人背对方向 × behindOffset,y 对齐目标中心,
///      空中背刺允许),ResolveBackstabLanding 射线兜底(管道/自身挡则翻 enemy 正面);
///   ② enemy 背靠墙/管道(IsWallBlockedOnSide(behindSide) 判 2.5m 带内堵)→ 换位挤出:玩家落到 enemy 原站位
///      (enemy 站得住 = 安全点,不穿墙),enemy 被挪到玩家面前攻击框中心(开阔侧),玩家回身朝 enemy 挥刺,
///      后续动画/命中帧/ExecuteBackstab 完全不变(精准打击,把 enemy 打出墙边);
///   → PlayerTeleport.TeleportTo(复用:瞬移+贴墙钳制+清速度+无敌帧+传送事件) → 强制转向敌人 → 播 Backstab 动画;
///   无目标:原地闪现(不位移,短无敌帧),播空挥动画。
/// 命中帧(动画事件 OnBackstabHitFrame → PlayerCombat → 本状态):对目标结算高伤害(3x)+ 强制硬直
///   (攻击标签 Sword_Heavy → Poise 近战路径 → EnterStunState);命中成功且未击杀 → 开启追击窗口
///   (PlayerController.BeginBackstabChase,时长 WeaponThrow.backstabChaseWindow 默认 2s):窗口内按攻击,
///   玩家吸附到 enemy 身边分流开打(空中 enemy → 空中攻击第 1 段;落地 enemy → 地面攻击第 1 段),消灭击飞后接不上普攻的空窗。
/// 结束(动画事件 OnBackstabEnd / 超时兜底 2.5s):回 Idle/Move。
///
/// ── 连音背刺(P7,规格 P6 状态层;动画/目标分配来自 P3/P6 产物)──
/// 目标来源:BackstabChainPlanner.GetTargetForPoint(组内点序号);拿不到分配(未准备/目标已销毁)→ 兜底 FindNearestTarget。
/// 逐点推进:判定入口(PlayerController,下一任务)在同一连音组的后续点判定通过时调 TryExecuteNextPoint(),
///   本状态不离开、不 ChangeState,就地再执行一刀(落点解析/隔墙/换位逻辑原样复用,判定条件一字未改):
///     瞬移 → BackstabClone.PlayAt(玩家落点, 朝向)演这一刀的动画(本体隐藏/本体事件冻结由 clone 侧处理,禁止 anim.Play 直切)
///     → 按点 ConsumePoint → 重置超时计时(2.5s 兜底变成单刀级,连音组跨多刀不被上一刀计时掐断)。
/// 命中归属:OnBackstabHitFrame / OnBackstabEnd 按「第 N 次事件 = 第 N 刀」回填刀记录(刀记录按执行顺序追加、组内不删),
///   命中结算用该刀记录里的目标/倍率/环序号,不用"当前目标"——连音点多时(如 A、B、A)前一刀的命中帧可能晚于下一刀开始。
/// 命中收环:只收「这一刀对应的那个环」(BeatFlashPoint.HideRing / BackstabAimIndicator.HideRing),
///   同一敌人身上的其它环继续缩(修 P5 报的"命中收整只标识实例"隐患);组结束/退出仍 Hide() 收全部。
/// 伤害递减:同组内对同一目标第 n 刀 × 0.7^(n-1)(第二刀 0.7、第三刀 0.49),不同目标不递减;
///   计数按"执行刀"累加,组切换/退出清空(ExecuteBackstab 签名未改,递减乘进 damageMultiplier)。
/// 退出:组内点全部执行完 且 最后一刀动画结束(动画结束事件;每刀重置的 MaxBackstabDuration 2.5s 兜底);
///   组结束(当前组切走)/ 玩家漏按导致组内点不再可执行时,等最后一刀动画结束即退,不锁死。
///   退出时:收全部环、clone StopAll、递减计数清空(BackstabChainPlanner.ClearChain 由预告侧 P5 负责,本状态不重复调)。
///
/// ── 与判定入口(P8 PlayerController)的约定(必须遵守,否则目标/环序号错位)──
/// ① 消费由本状态负责(每刀执行时 ConsumePoint):入口判定用 mgr.IsInChainWindow 即可(它已排除已消费点),
///    不要自己 ConsumePoint —— 否则 OnEnter 读到的是"下一个点序号",目标与环都会指向错的那个点;
/// ② 当前状态已是 PlayerBackstabState 时不要 ChangeState(FSM 同状态直接 return),改为调 TryExecuteNextPoint();
/// ③ 当前连音组已切走时 TryExecuteNextPoint 返回 false(本状态正在退出):此时按"新一组"重新走入口。
/// </summary>
public class PlayerBackstabState : EntityState
{
    /// <summary>状态最大存活时长(秒):动画事件丢失/Play 失败时兜底退出,防 LocksInput 永久锁死(参考 PlayerAttackState 2.5s)。
    /// 连音下每执行一刀重置 → 兜底是"单刀级"(最后一刀的动画没结束/结束事件丢了最多再等 2.5s 就退)</summary>
    private const float MaxBackstabDuration = 2.5f;

    /// <summary>同组同目标伤害递减系数:第 n 刀伤害 × 0.7^(n-1)(第二刀 0.7、第三刀 0.49)</summary>
    private const float ChainDamageFalloff = 0.7f;

    /// <summary>点时刻比较容差(与 MusicPointManager 的 0.001 口径一致)</summary>
    private const float TimeEpsilon = 0.001f;

    private readonly PlayerCombat combat;
    private readonly PlayerTeleport teleport;
    private readonly float searchRadius;      // 目标搜索半径
    private readonly float behindOffset;      // 背后落点偏移(米)
    private readonly float damageMultiplier;  // 背刺伤害倍率(基础伤害 × 此值)
    private readonly Vector2 knockback;       // 背刺击退向量(x 水平镜像,y 上挑,与三连击同语义)
    private readonly float hoverDuration;     // 空中背刺命中后的滞空停顿(玩家+敌人一起停)
    private readonly float chaseWindow;       // 背刺追击窗口时长(秒;0 = 关闭)
    private readonly bool chaseEnabled;       // 背刺追击总开关(调试关闭用)

    private EnemyControllerBase _target;   // 最近一刀的目标(追击窗口/退出路径引用只读;命中结算一律用刀记录里的目标)
    private float _stateTimer;             // 距上一次执行刀的时长(每刀重置;超时兜底)

    // ── 背刺残影(待办1,DashGhostTrail 复用)──
    private DashGhostTrail _ghostTrail;   // 懒缓存:OnEnter GetComponentInChildren 找(未挂组件=null → 判空跳过,不影响背刺本体)

    // ── 背刺持续特效锚点(AttackVFXAnchor 统一入口;未挂 = null → 跳过,不影响背刺)──
    private AttackVFXAnchor _vfx;

    // ── 空中背刺缓落:单例协程 + 原始重力只记一次(连音多刀并发会互相污染,见 BeginHover)──
    private Coroutine _hoverRoutine;
    private float _savedGravity = -1f;   // <0 = 未记录(状态内只记一次,恢复后复位)

    // ── 连音:替身 / 分配器(懒缓存,OnEnter 未找到就再找一次,不在 Update 里查)──
    private BackstabClone _clone;                 // clone 替身(P6):每一刀的动画由它演,本体不播
    private BackstabChainPlanner _planner;        // 目标分配器(P3):连音组目标单一数据源

    // ── 连音:刀记录 + 按事件回填 ──
    /// <summary>一刀的记录(执行时追加;命中帧/结束事件按"第 N 次事件 = 第 N 刀"回填)。
    /// 组内不删除条目:删了会让后续事件序号错位;数量上界 = 组内点数(每个点最多执行一次),组结束/退出整体清空。</summary>
    private sealed class StrikeRecord
    {
        public EnemyControllerBase target;    // 本刀目标(null = 空挥/隔墙不成立 → 命中帧只做空跳过)
        public int pointIndex = -1;           // 本组内的点序号(-1 = 非连音,自动重音/单点路径)
        public int ringIndex;                 // 该目标身上的金色环序号(命中时只收这一只;0 = 单环/自动重音路径)
        public float pointTime = -1f;         // 本刀对应的标点时刻(-1 = 无,不参与消费)
        public float damageMultiplier = 1f;   // 本刀实际倍率(已含同组同目标递减)
        public bool hitResolved;              // 命中帧已结算(同一刀只结一次)
        public bool endReceived;              // 本刀动画结束事件已收到
    }

    private readonly List<StrikeRecord> _strikes = new List<StrikeRecord>(8);
    private int _hitEventSeq;    // 已收到的命中帧事件计数(第 N 次 → _strikes[N];clone 每刀只发一次)
    private int _endEventSeq;    // 已收到的动画结束事件计数(同上)

    /// <summary>同组同目标已被刺次数(递减计数):键 = 敌人;组切换/退出清空</summary>
    private readonly Dictionary<EnemyControllerBase, int> _stabCount = new Dictionary<EnemyControllerBase, int>(4);

    /// <summary>本状态绑定的连音组身份(= 该组首点时刻;NaN = 未绑定连音组)</summary>
    private float _chainFirstPoint = float.NaN;

    public override bool LocksInput => true;

    public PlayerBackstabState(CharacterBase owner, StateMachine stateMachine, Animator anim,
        PlayerCombat combat, PlayerTeleport teleport, float searchRadius, float behindOffset,
        float damageMultiplier, Vector2 knockback, float hoverDuration,
        float chaseWindow, bool chaseEnabled)
        : base(owner, stateMachine, anim, new[] { AnimParams.IsBackstabbing })   // Entry 路由:IsBackstabbing=true 进 Backstab,Exit 清 false
    {
        this.combat = combat;
        this.teleport = teleport;
        this.searchRadius = searchRadius;
        this.behindOffset = behindOffset;
        this.damageMultiplier = damageMultiplier;
        this.knockback = knockback;
        this.hoverDuration = hoverDuration;
        this.chaseWindow = chaseWindow;
        this.chaseEnabled = chaseEnabled;
    }

    public override void OnEnter()
    {
        base.OnEnter();
        _target = null;
        _stateTimer = 0f;
        _strikes.Clear();
        _hitEventSeq = 0;
        _endEventSeq = 0;
        _stabCount.Clear();
        _chainFirstPoint = float.NaN;

        // 懒缓存残影/特效/替身组件(GetComponentInChildren 含 inactive;未挂组件 = null → 判空跳过)
        if (_ghostTrail == null)
            _ghostTrail = owner.GetComponentInChildren<DashGhostTrail>(true);
        if (_vfx == null)
            _vfx = owner.GetComponentInChildren<AttackVFXAnchor>(true);
        if (_clone == null)
            _clone = owner.GetComponentInChildren<BackstabClone>(true);
        // 目标分配器:只在进入状态这一帧找(层级 → MusicPointManager 同物体 → 一次 FindObjectOfType),不在 Update 里查
        if (_planner == null)
            _planner = ResolvePlanner();

        var mgr = MusicPointManager.Instance;
        int pointIndex = ResolveStrikePointIndex(mgr);      // -1 = 非连音路径(自动重音/无连音组)
        ExecuteStrike(ResolveStrikeTarget(pointIndex, mgr), pointIndex, mgr);
    }

    /// <summary>
    /// 连音逐点推进入口(判定入口 P8 在"同一连音组的后续点判定通过"时调用):取组内下一个该执行的点,就地再打一刀。
    /// 返回 false:状态不是当前状态 / 当前曲没有连音组 / 本状态绑定的组已切走(状态正在退出)/
    ///   组内已无可执行点(全部执行完,或当前没有"活跃且未消费"的点窗口)。
    /// 调用方注意:不要自行 ConsumePoint(消费由本状态每刀执行时完成),也不要在已是本状态时 ChangeState(FSM 会直接 return)。
    /// </summary>
    public bool TryExecuteNextPoint()
    {
        if (stateMachine == null || !ReferenceEquals(stateMachine.CurrentState, this)) return false;   // 状态未激活
        var mgr = MusicPointManager.Instance;
        if (mgr == null || !mgr.HasChain) return false;      // 非连音路径:不在本入口的职责内
        if (!IsCurrentChainGroup(mgr)) return false;         // 已切到别的连音组:本状态即将退出,交给入口重进
        int pointIndex = ResolveStrikePointIndex(mgr);
        if (pointIndex < 0) return false;                    // 组内已全部执行 / 无活跃未消费点
        ExecuteStrike(ResolveStrikeTarget(pointIndex, mgr), pointIndex, mgr);
        return true;
    }

    public override void OnUpdate()
    {
        _stateTimer += Time.deltaTime;
        if (_stateTimer > MaxBackstabDuration)
        {
            ExitBackstab();   // 单刀级超时兜底(执行刀时重置):动画事件丢失也不会永久锁死输入
            return;
        }

        // 退出判定:最后一刀动画已结束 且 组内已无未执行点(非连音路径恒满足)
        if (_strikes.Count == 0) return;                          // 未执行过刀(理论不出现):交给超时兜底
        if (!_strikes[_strikes.Count - 1].endReceived) return;    // 最后一刀还在演
        if (!NoPendingChainPoint()) return;                       // 组内还有未执行点:等下一刀,或等该组窗口全部过完
        ExitBackstab();
    }

    /// <summary>背刺命中帧(动画事件 OnBackstabHitFrame → PlayerCombat → 本状态;clone 替身同链路转发):
    /// 第 N 次命中帧事件 = 第 N 刀(clone 每刀只发一次、本体兜底同链路),按记录结算该刀目标/倍率/环序号;
    /// 多余/迟到事件(池复用丢帧等)直接丢弃。</summary>
    public void OnBackstabHitFrame()
    {
        StrikeRecord rec = _hitEventSeq < _strikes.Count ? _strikes[_hitEventSeq] : null;
        _hitEventSeq++;
        if (rec == null || rec.hitResolved) return;
        rec.hitResolved = true;
        ResolveStrikeHit(rec);
    }

    /// <summary>某一刀的命中结算:对目标结算伤害(含同组同目标递减)+ 强制硬直;
    /// 命中即收「这一刀对应的那个环」(多环时其它环继续缩);空中背刺:刷新空中攻击计数 + 玩家缓落。</summary>
    private void ResolveStrikeHit(StrikeRecord rec)
    {
        EnemyControllerBase target = rec.target;
        if (target == null || target.IsDead) return;
        combat?.ExecuteBackstab(target, rec.damageMultiplier, knockback);
        // 重音成功:头顶 combo 计数 +1(BeatComboIndicator,不存在则跳过);
        // 在非空非死分支内执行,挥空/目标死亡不计数,天然满足"挥空无效"
        owner.GetComponentInChildren<BeatComboIndicator>(true)?.NotifyBeatHit();
        // 命中即收标识:只收这一刀对应的那个环(P5 隐患修复)——连音时同一敌人身上还有别的点的环,继续缩
        // (非连音/自动重音路径 ringIndex=0 = 环池第一个环,与 ShowChain 单元素行为一致)
        target.GetComponentInChildren<BeatFlashPoint>(true)?.HideRing(rec.ringIndex);

        // 背刺命中成功 → 开启追击窗口(玩家侧共享数据,PlayerController.BeginBackstabChase):
        // 窗口内按攻击 → 玩家吸附到 enemy 身边开打,消灭"背刺把 enemy 击飞后玩家在原地接不上攻击"的空窗。
        // 数据放 PlayerController(攻击输入分发侧),本状态 OnExit 只清 _target 不影响窗口;
        // 开关/时长由 WeaponThrow 背刺参数区配置(backstabChaseEnabled/backstabChaseWindow),命中帧读取,
        // 不在状态内每帧轮询,只用过期时间戳(输入事件时校验)。目标若被背刺这刀直接击杀 → 不开窗(按攻击走普攻)。
        if (chaseEnabled && chaseWindow > 0f && !target.IsDead)
            ((PlayerController)owner)?.BeginBackstabChase(target, chaseWindow);

        var pc = (PlayerController)owner;
        if (pc == null || pc.IsGrounded()) return;

        // 空中背刺:刷新空中攻击计数 + 玩家缓落(仅玩家侧,enemy 不再滞空吸附——背刺=终结技,
        // enemy 由击退自然飞出落地,suppressAirHang 已在 ExecuteBackstab 置位跳过 OnHitBy 吸附)
        if (pc.JumpComp != null)
            pc.JumpComp.ResetAirAttackOnly();
        if (hoverDuration > 0f)
        {
            BeginHover(pc, hoverDuration);   // 单例协程 + 原始重力只记一次(连音多刀不再互相污染)
        }
    }

    /// <summary>玩家背刺后缓落:小重力缓慢下落(不清速度,避免定身后突然坠落),停 hoverDuration 秒后恢复原重力。
    /// 不跟随 enemy——背刺=终结技,enemy 由击退自然飞出落地,玩家原地缓落,不每帧贴 enemy(贴随会造成左右闪/瞬移跳变)</summary>
    private void BeginHover(PlayerController pc, float duration)
    {
        var rb = pc.GetRigidbody();
        if (rb == null) return;
        // 原始重力只记第一次:连音多刀各记一次的话,后一刀会把 0.3 当成原始值记下来,
        // 恢复后重力永久停在 0.3 → 玩家像失去重力一样跳很高(2026-09-10 实测 bug)
        if (_savedGravity < 0f) _savedGravity = rb.gravityScale;
        if (_hoverRoutine != null) pc.StopCoroutine(_hoverRoutine);   // 重入:先停旧协程,防多个协程各自恢复互相覆盖
        _hoverRoutine = pc.StartCoroutine(HoverRoutine(pc, duration));
    }

    private System.Collections.IEnumerator HoverRoutine(PlayerController pc, float duration)
    {
        var rb = pc.GetRigidbody();
        float restore = _savedGravity >= 0f ? _savedGravity : 1f;
        if (rb != null)
            rb.gravityScale = Mathf.Min(restore, 0.3f);   // 缓落:小重力(参考空中攻击悬停),不清速度
        float t = 0f;
        while (t < duration)
        {
            t += Time.unscaledDeltaTime;
            yield return null;
        }
        if (rb != null)
            rb.gravityScale = restore;
        _hoverRoutine = null;
        _savedGravity = -1f;
    }

    /// <summary>恢复原始重力(状态退出/被打断/死亡全路径兜底):停掉缓落协程并把重力写回记录值,
    /// 防"重力消失"永久残留。幂等:没有记录值(未缓落过)时什么都不做。</summary>
    private void RestoreHoverGravity()
    {
        var pc = owner as PlayerController;
        if (_hoverRoutine != null)
        {
            if (pc != null) pc.StopCoroutine(_hoverRoutine);
            _hoverRoutine = null;
        }
        if (_savedGravity >= 0f)
        {
            var rb = pc != null ? pc.GetRigidbody() : null;
            if (rb != null) rb.gravityScale = _savedGravity;
            _savedGravity = -1f;
        }
    }

    /// <summary>背刺动画结束(动画事件 OnBackstabEnd → PlayerCombat → 本状态;clone 替身同链路转发):
    /// 第 N 次结束事件 = 第 N 刀;只有"最后一刀结束 + 组内已无未执行点"才退出(连音中途等下一刀)。</summary>
    public void OnBackstabEnd()
    {
        if (_endEventSeq < _strikes.Count) _strikes[_endEventSeq].endReceived = true;
        _endEventSeq++;
        TryFinishExit();
    }

    /// <summary>结束事件后的退出判定(与 OnUpdate 同一把尺子:最后一刀结束 + 组内无未执行点)</summary>
    private void TryFinishExit()
    {
        if (_strikes.Count == 0)
        {
            ExitBackstab();   // 无刀记录(异常/迟到事件):保持旧行为——结束事件即退出
            return;
        }
        if (!_strikes[_strikes.Count - 1].endReceived) return;   // 这一刀不是最后一刀(还有更晚开始的刀在演)
        if (!NoPendingChainPoint()) return;                      // 组内还有未执行点:继续等下一次推进
        ExitBackstab();
    }

    public override void OnExit()
    {
        base.OnExit();          // 先清 IsBackstabbing(解冻本体动画器的前提,见 BackstabClone.StopAll 注释)
        _target = null;
        _clone?.StopAll();      // 收掉全部替身并恢复本体显隐/动画器(P6 接口;退出/打断/死亡全路径兜底,幂等)
        _vfx?.Stop();           // 收起背刺持续特效(动画结束/超时退出/被打断兜底;幂等)
        _strikes.Clear();
        _hitEventSeq = 0;
        _endEventSeq = 0;
        _stabCount.Clear();
        _chainFirstPoint = float.NaN;
        RestoreHoverGravity();   // 被打断/死亡:缓落协程可能没跑完,统一恢复原始重力(防重力永久变小)
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

    // ============================================================
    // 一刀执行(第一刀 = OnEnter;同组后续刀 = TryExecuteNextPoint)
    // ============================================================

    /// <summary>
    /// 执行一刀:落点解析(背后净空 / 换位挤出 + 隔墙检测,判定条件与改动前逐字一致)→ 瞬移 + 转向 →
    /// clone 在落点播这一刀的完整动画 → 记一条刀记录 → 按点消费 → 重置超时计时。
    /// 本状态不离开、不 ChangeState(靠 Entry 路由播动画的老路径:未挂 clone 时本体自己播第一刀)。
    /// </summary>
    private void ExecuteStrike(EnemyControllerBase target, int pointIndex, MusicPointManager mgr)
    {
        _stateTimer = 0f;   // 每刀重置:超时兜底变成"单刀级"(连音组跨多刀不被第一刀的计时掐断)

        // ── 本刀快照:点时刻 + 组身份(非连音路径保持原样)──
        float pointTime = -1f;
        if (mgr != null && mgr.HasChain && pointIndex >= 0)
        {
            var chainPts = mgr.CurrentChainPoints;
            if (pointIndex < chainPts.Length) pointTime = chainPts[pointIndex];
            BindChainGroup(chainPts);                                  // 组身份变更 → 递减计数清零
        }

        var pc = (PlayerController)owner;
        bool validBackstab = target != null;   // 落点可达才算数;不可达(隔墙)→ 原地空挥
        Vector2 playerDest = (Vector2)pc.transform.position;   // 玩家最终落点(默认原位)
        bool canSwap = false;
        Vector2 enemyOld = Vector2.zero;   // canSwap:玩家落点 = enemy 原站位(enemy 站得住 = 安全点)
        Vector2 enemyNew = Vector2.zero;   // canSwap:enemy 被挪到的攻击框中心(ForceSetPosition 钳制后为准)

        if (target != null)
        {
            // 背刺方向永远按 enemy 朝向:玩家出现在 enemy 背后。
            // 落点侧 = enemy 背对方向(behindSide)。IsWallBlockedOnSide 判该侧 2.5m 半带内是否有堵
            // (实心墙/地面/管道 trigger;内部已排除 enemy 自身/其它 enemy/玩家,普通 trigger 不算)。
            int behindSide = -target.Facing;
            // 换位挤出需要玩家面前攻击框指示器提供 enemy 新站位;未配置(RangeIndicator 空,理论不出现)
            // 时退化走原射线兜底路径,不硬凑换位
            canSwap = target.IsWallBlockedOnSide(behindSide)
                && combat != null && combat.RangeIndicator != null;

            if (canSwap)
            {
                // ── 背后被堵(enemy 背靠墙/管道,玩家侧开阔)→ 换位挤出:玩家 ↔ enemy 互换 ──
                // 玩家落 enemy 背后会进墙 → 玩家去 enemy 原站位(enemy 站得住 = 安全点);
                // enemy 挪到玩家面前攻击框中心(RangeIndicator.Center,开阔侧)——被挪后其 Facing 不变,
                // 玩家天然落在 enemy 背后;后续动画/命中帧/ExecuteBackstab 完全照常(精准打击),
                // 击退方向 = 玩家→enemy,把 enemy 打出墙边。
                enemyOld = target.transform.position;
                playerDest = enemyOld;
                // 玩家面前攻击框中心(世界坐标;此刻玩家还没瞬移,以玩家当前站位为基准)。
                // 极端:enemy 已几乎在攻击框中心(enemyNew≈enemyOld)→ 仍执行,重叠由
                // "先挪 enemy 再移玩家"的瞬移顺序吸收;玩家贴墙时中心可能探进墙,
                // ForceSetPosition 会钳制到墙外侧(2026-09-07 背刺穿墙修复)。
                enemyNew = (Vector2)combat.RangeIndicator.Center;
            }
            else
            {
                // ── 背后净空 → 原逻辑:落点 = 敌人背后(敌人背对方向)──
                // x = enemy.x - Facing × offset;y 对齐目标中心(空中背刺允许)
                playerDest = new Vector2(
                    target.transform.position.x - target.Facing * behindOffset,
                    target.transform.position.y);
                playerDest = ResolveBackstabLanding(playerDest, target);   // 落点避开管道(PlayerTeleport 只钳制墙层,管道 Channel 层会直接传进去)
            }

            // 隔墙检测(方案 A,2026-09-07):玩家当前位置 → 落点 路径上命中实心墙/地形
            // (Ground=3 + Wall=11,同 PlayerTeleport)= 玩家与 enemy 隔墙,背刺不成立(偷袭绕不过墙),
            // 本次按无目标处理:原地闪现空挥,不瞬移不穿墙。
            // 只在 F 触发进入本状态这一帧测一次(按键事件非轮询),与 OnEnter 现有
            // OverlapCircle/OverlapBox/Raycast 查询同帧叠加,不新增持续开销。
            if (IsPathBlockedByWall((Vector2)pc.transform.position, playerDest))
            {
                target = null;
                validBackstab = false;
            }
        }
        _target = target;   // 隔墙/无目标已被置空 → 本刀记为空挥(命中帧判空跳过)

        // 本刀倍率与环序号:只用"真的打出去了的刀"计算(被墙挡的空挥不计入递减,也就不吞下一次的 0.x 档)
        int ringIndex = 0;
        float multiplier = damageMultiplier;
        if (target != null && mgr != null && mgr.HasChain && pointIndex >= 0)
        {
            ringIndex = ComputeRingIndex(target, pointIndex, mgr);   // 该刀在目标身上的环序号(与 P5 出环顺序一致)
            multiplier = NextStrikeMultiplier(target);               // 同组同目标递减
        }

        if (validBackstab)
        {
            // 瞬移前留起点残影(玩家还在原位,拷贝当前帧 → 瞬移后残影停在原地淡出 = 闪现残像)
            SpawnGhost();
            if (canSwap)
            {
                // 先挪 enemy(物理体位 + 清速度,防旧击退速度把它带跑;无 rb 走 transform),再移玩家。
                // ForceSetPosition 返回钳制后实际落点(玩家贴墙时攻击框中心可能探进墙,已被外推到墙外),
                // 后续朝向/击退方向都以实际落点为准。
                enemyNew = target.ForceSetPosition(enemyNew);
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
                if (teleport != null)
                    teleport.TeleportTo(playerDest);
                else
                    pc.transform.position = playerDest;   // 未挂 PlayerTeleport 时兜底直接位移(无敌帧等由挂载后生效)
                // 强制转向敌人:按敌人与玩家实际落点(playerDest)的相对位置(不能用 pc.transform.position——
                // TeleportTo 走 rb.position,同帧 transform.position 未同步还是瞬移前旧值,会把朝向判反;
                // 也不能用 enemy.Facing——靠墙时落点改到 enemy 正面,enemy.Facing 朝玩家,用它玩家会背朝 enemy)
                pc.UpdateFacing(target.transform.position.x >= playerDest.x ? 1f : -1f);
            }
        }
        else
        {
            // 无目标 / 落点被墙挡(隔墙):原地闪现(复用 TeleportTo 自身位置 = 无敌帧+事件,无位移);朝向跟随当前输入
            SpawnGhost();   // 闪现残像(原地,表示闪身动作)
            if (teleport != null)
                teleport.TeleportTo((Vector2)pc.transform.position);
            float h = Input.GetAxisRaw("Horizontal");
            if (Mathf.Abs(h) > 0.1f) pc.UpdateFacing(h);
        }

        // 背刺持续特效:进背刺动作播背刺槽(统一入口;有目标瞬刺/无目标原地空挥都播;退出 OnExit Stop)。
        // 槽子物体位置 saika 编辑器摆(attack_VFX 下 slot_backstab,相对玩家);空槽/未挂锚点 = 判空跳过不崩。
        _vfx?.PlayBackstab();

        // 动画交给 clone 替身:每一刀都在玩家落点完整播一遍 Backstab(本体隐藏 + 本体事件冻结在 clone 侧处理)。
        // 位置用"落点"而不是 pc.transform.position:TeleportTo 走 rb.position,同帧 transform 可能还是瞬移前旧值。
        // 朝向用 pc.FacingDir(UpdateFacing 已在本帧写定),与 CharacterBase.UpdateFacing 的 scale.x 符号同口径。
        PlayCloneStrike(validBackstab ? playerDest : (Vector2)pc.transform.position, pc);

        var rec = new StrikeRecord
        {
            target = target,
            pointIndex = pointIndex,
            ringIndex = ringIndex,
            pointTime = pointTime,
            damageMultiplier = multiplier,
        };
        _strikes.Add(rec);

        // 按点消费:让 MusicPointManager 的消费记录与状态推进一致(同点重复调用幂等;没打出去的点不消费)
        if (pointTime >= 0f) mgr.ConsumePoint(pointTime);

        // 未挂 clone 替身的降级:本体只为第一刀播动画(状态内不再重播,禁止 anim.Play 直切/ChangeState 重播),
        // 后续刀没有动画事件 → 当场结算这一刀,防连音后续刀"瞬移过去却不掉血"(配置正常时走不到这里)。
        if (_clone == null && _strikes.Count > 1)
        {
            rec.hitResolved = true;
            rec.endReceived = true;
            ResolveStrikeHit(rec);
        }
    }

    /// <summary>在玩家落点播这一刀的 Backstab 动画(未挂 BackstabClone = 判空跳过,本体走原 Entry 路由播动画)</summary>
    private void PlayCloneStrike(Vector2 position, PlayerController pc)
    {
        if (_clone == null) return;
        _clone.PlayAt(position, pc != null && pc.FacingDir < 0);
    }

    // ============================================================
    // 连音:点序号 / 目标 / 环序号 / 递减计数
    // ============================================================

    /// <summary>
    /// 本刀该执行组内哪个点:优先"当前活跃且未消费的最早点"——与判定入口同一时点取号,
    /// 玩家漏按的中间点(窗口已过)不会把序号顶偏(否则会把 A、B、A 的第三刀打到 B 身上);
    /// 兜底退到"组内第一个未消费点"(事件迟到/时序抖动),再兜底 -1(= 非连音路径)。
    /// </summary>
    private int ResolveStrikePointIndex(MusicPointManager mgr)
    {
        if (mgr == null || !mgr.HasChain) return -1;
        var pts = mgr.CurrentChainPoints;
        if (pts.Length == 0) return -1;

        for (int i = 0; i < pts.Length; i++)           // ① 活跃且未消费的最早点
        {
            float p = pts[i];
            if (mgr.IsPointConsumed(p)) continue;
            if (mgr.IsPointActive(p)) return i;
        }

        int pending = mgr.PendingChainPointIndex;      // ② 组内第一个未消费点,且窗口已开(用 WindowSeconds 保守判定,不用 mgr 私有参数)
        if (pending >= 0 && pending < pts.Length && mgr.TrackTime >= pts[pending] - mgr.WindowSeconds)
            return pending;
        return -1;
    }

    /// <summary>本刀目标:连音组内该点分配到的敌人(P3 分配器单一数据源);拿不到分配(未准备/目标已销毁/屏内无候选)
    /// → 兜底回退到原"最近敌人"搜索,保证状态不挥空、不卡死</summary>
    private EnemyControllerBase ResolveStrikeTarget(int pointIndex, MusicPointManager mgr)
    {
        if (pointIndex >= 0 && mgr != null)
        {
            var planned = _planner != null ? _planner.GetTargetForPoint(pointIndex) : null;
            if (planned != null && !planned.IsDead) return planned;
        }
        return FindNearestTarget();
    }

    /// <summary>
    /// 本刀在目标身上的金色环序号:与 P5(EnemyBeatIndicator)出环顺序同口径——
    /// 该敌人身上第 i 个点的环按"组内点序号升序"排,故序号 = 比本点更早、且同样分配到这个敌人的点数。
    /// 拿到错的序号会把别人身上的环收掉,所以两边必须共用同一个分配器(BackstabChainPlanner)。
    /// </summary>
    private int ComputeRingIndex(EnemyControllerBase target, int pointIndex, MusicPointManager mgr)
    {
        if (target == null || pointIndex <= 0) return 0;
        if (_planner == null || mgr == null || !mgr.HasChain) return 0;
        var pts = mgr.CurrentChainPoints;
        int limit = Mathf.Min(pointIndex, pts.Length);
        int ring = 0;
        for (int i = 0; i < limit; i++)
        {
            if (_planner.GetTargetForPoint(i) == target) ring++;
        }
        return ring;
    }

    /// <summary>本刀倍率 = 基础倍率 × 0.7^(同组内对同一目标已被刺次数);每次调用即计入本刀</summary>
    private float NextStrikeMultiplier(EnemyControllerBase target)
    {
        if (target == null) return damageMultiplier;
        int n;
        _stabCount.TryGetValue(target, out n);
        n++;
        _stabCount[target] = n;

        float mult = damageMultiplier;
        for (int i = 1; i < n; i++) mult *= ChainDamageFalloff;   // n=1 → 1.0;n=2 → 0.7;n=3 → 0.49
        return mult;
    }

    /// <summary>绑定/校验本状态所属的连音组(组身份 = 组首点时刻);组变了 → 递减计数清零(规格:递减只在同一连音组内)</summary>
    private void BindChainGroup(float[] chainPts)
    {
        if (chainPts == null || chainPts.Length == 0) return;
        if (!float.IsNaN(_chainFirstPoint) && Mathf.Abs(chainPts[0] - _chainFirstPoint) < TimeEpsilon) return;
        _chainFirstPoint = chainPts[0];
        _stabCount.Clear();
    }

    /// <summary>当前是否仍是本状态绑定过的那个连音组(组结束后 pts 为空 / 切到下一组 → false)</summary>
    private bool IsCurrentChainGroup(MusicPointManager mgr)
    {
        if (float.IsNaN(_chainFirstPoint)) return false;
        var pts = mgr.CurrentChainPoints;
        if (pts.Length == 0) return false;
        return Mathf.Abs(pts[0] - _chainFirstPoint) < TimeEpsilon;
    }

    /// <summary>组内已无未执行点(可以退出了):非连音路径恒 true;连音路径要求仍是本组且组内点全部已消费。
    /// 当前组切走(pts 变空/换成下一组)→ true(本状态等最后一刀演完就退,不追到下一组去)。</summary>
    private bool NoPendingChainPoint()
    {
        var mgr = MusicPointManager.Instance;
        if (mgr == null || !mgr.HasChain) return true;
        if (!IsCurrentChainGroup(mgr)) return true;
        return mgr.PendingChainPointCount == 0;
    }

    /// <summary>解析连音目标分配器(P3):层级(父级/子级)→ MusicPointManager 同物体 → 一次 FindObjectOfType。
    /// 只在 OnEnter 未缓存时调一次,不在 Update/每刀里查。</summary>
    private BackstabChainPlanner ResolvePlanner()
    {
        var planner = owner.GetComponentInParent<BackstabChainPlanner>();
        if (planner == null) planner = owner.GetComponentInChildren<BackstabChainPlanner>(true);
        if (planner == null && MusicPointManager.Instance != null)
            planner = MusicPointManager.Instance.GetComponentInParent<BackstabChainPlanner>();
        if (planner == null) planner = Object.FindObjectOfType<BackstabChainPlanner>();
        return planner;
    }

    /// <summary>背刺落点避让(射线版):从 enemy 位置朝背后(-Facing)发射射线,
    /// 命中墙/地面/实心管道等(非 trigger collider)或管道 trigger → 背后被挡 → 落点改到 enemy 正面;
    /// 背后空 → 原落点(enemy 背后)。防背刺被传进管道/墙内。</summary>
    private Vector2 ResolveBackstabLanding(Vector2 dest, EnemyControllerBase target)
    {
        if (target == null) return dest;
        Vector2 behindDir = Vector2.right * (-target.Facing);
        RaycastHit2D hit = Physics2D.Raycast(target.transform.position, behindDir, behindOffset + 0.3f);
        if (hit.collider == null) return dest;   // 背后空 → 原落点
        // 命中自身 collider(射线从 enemy 中心发出可能扫到自身):忽略
        if (hit.transform == target.transform || hit.transform.IsChildOf(target.transform))
            return dest;
        // 命中其他 trigger(非管道):忽略,不算挡
        if (hit.collider.isTrigger && !AreaChannelTrigger.IsPointInChannel(hit.point))
            return dest;
        // 背后被挡(墙/地面/实心管道/管道 trigger)→ 改到 enemy 正面(面朝玩家方向,通常空地)
        return new Vector2(
            target.transform.position.x + target.Facing * behindOffset,
            target.transform.position.y);
    }

    /// <summary>隔墙检测墙层(Ground=3 + Wall=11,与 PlayerTeleport.wallMask / EnemyControllerBase 钳制层一致)</summary>
    private const int WallBlockMask = (1 << 3) | (1 << 11);

    /// <summary>
    /// 隔墙检测:玩家当前位置 → 落点 的路径上是否有实心墙/地形(Ground/Wall 层)挡住。
    /// 命中 = 背刺落点不可达(玩家与 enemy 隔墙),不进入背刺。
    /// RaycastAll 跳过玩家自身 collider(起点在玩家碰撞体内,普通 Raycast 会先命中自己 → 永远 true)。
    /// </summary>
    private bool IsPathBlockedByWall(Vector2 from, Vector2 to)
    {
        Vector2 delta = to - from;
        float dist = delta.magnitude;
        if (dist < 0.01f) return false;   // 原位/无位移不判
        Vector2 dir = delta / dist;
        RaycastHit2D[] hits = Physics2D.RaycastAll(from, dir, dist, WallBlockMask);
        foreach (RaycastHit2D hit in hits)
        {
            if (hit.collider == null) continue;
            if (hit.transform == owner.transform || hit.transform.IsChildOf(owner.transform)) continue;   // 跳过玩家自身
            if (hit.collider.GetComponentInParent<PlayerController>() != null) continue;                    // 保险:玩家身上的其它 collider
            return true;
        }
        return false;
    }

    /// <summary>背刺瞬移起点残影:玩家还在起点时生成,瞬移后残影停在原地淡出。未挂 DashGhostTrail 则跳过(不挡背刺)</summary>
    private void SpawnGhost()
    {
        if (_ghostTrail != null)
            _ghostTrail.SpawnOnce();
    }

    /// <summary>选最近非死亡敌人(Boss 也可,普通场景无 Boss;空中敌人同样可作目标,允许空中背刺)。
    /// 连音路径拿不到分配时的兜底(规格 §P6:防无分配时状态卡死)</summary>
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
