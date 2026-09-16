using System.Collections;
using UnityEngine;

/// <summary>
/// Boss 重击 — 独立机制,由 BossHeavy 标点组驱动(全程霸体,受击不掉硬直/击退、不中断,照常掉血)。
///
/// ── 流程(2026-09-15 v2 重定锚点)──
/// 锚点:金色判定环收缩到判定外环(半径 1.5)的那一帧 = x = 音乐重音(BossHeavy 标点)。
///   同一帧上重合:环到判定外环、蓄力动画结束、攻击动画开始、判定窗口关闭。
///   判定窗口 = [x - judgeWindowBefore, x](只取前半段,出伤之后不再判定;玩家侧 PlayerBeatJudge 自算)。
/// 标点 x 前(judgeWindowBefore + warningLead)秒:发现标点(未激活不响应;技能执行中让位等下一个)
///   → 快照玩家当前位置与朝向(之后不再追踪) → 算落点 → 过 ForceSetPosition(贴墙钳制 + 清速度)
///   → 面向玩家锁死朝向(SetFacingLocked,收尾才解锁) + 冻结位置(gravityScale 置 0、每帧 moveInput = 0)
///   → 到「x - 蓄力动画时长」:IsHeavyAttack=false 起播蓄力动画 + 生成蓄力 VFX(蓄力只占末段)
///   → 到「x - judgeWindowBefore」:对 Boss 挂点的 BeatFlashPoint 出判定圈(与判定窗口同刻开)
///   → 到 x:IsHeavyAttack=true 切攻击动画(锚点帧)
///   → 出伤:严格由动画事件驱动(Heavy-Attack 出伤帧 → BossAnimationRelay.OnBossHeavyHitFrame
///     → OnHeavyHitFrame):非重击期忽略、已出伤忽略、被抵消不出伤,否则读重击框(heavyRangeIndicator)
///     判玩家当前位置,在框内才结算伤害;代码不做任何定时伤害,事件没挂就是不出伤。出伤帧同时收起判定圈
///   → 收尾:等 Heavy-Attack 末帧事件(BossAnimationRelay.OnBossHeavyEnd → NotifyHeavyAnimEnd),
///     超时(攻击动画时长 + 0.5 秒;查不到片段则 prepareLead + 3 秒)强制收尾 —— 两条路都走统一收尾出口。
///     兜底必须保留:玩家卡点成功时背刺动画会顶掉攻击动画,末帧事件不会来
///
/// ── 关键约定 ──
/// · 禁用 animator.Play 直切动画状态:重击动画一律走 Animator 参数(IsHeavy / IsHeavyAttack)
/// · 蓄力动画时长不手填:运行时从 Animator 的 runtimeAnimatorController.animationClips 里按名字含 Charge
///   反推 clip.length,结果缓存字段(只反推一次,不每帧遍历);找不到时按 prepareLead 起播(等于闪现即蓄力)
/// · 旧字段 animState 保留仅为不丢序列化值,不再被读取
/// · 重击框 / 挂点 / VFX 槽 / 动画器连线由 saika 在编辑器接;代码只留槽位与空槽兜底
/// </summary>
public class BossHeavyAttack : MonoBehaviour
{
    [Header("标点")]
    [Tooltip("重击标点组名(MusicTrackData Point Groups 里的组名)")]
    public string groupName = "BossHeavy";
    [Tooltip("[v2] 现仅用于让位/等待超时兜底;闪现提前量 = judgeWindowBefore + warningLead")]
    public float prepareLead = 2f;

    [Header("闪现")]
    [Tooltip("闪现到 player 面前的距离")]
    public float teleportDistance = 2f;
    [Tooltip("墙/空气墙层(player 面朝方向有墙则闪背后;掩码已含地面层,玩家在空中时也用它取地面点)")]
    public LayerMask wallLayer;

    [Header("重击动画/表现")]
    [Tooltip("[2026-09-15 废弃,保留仅为不丢序列化值] 旧 animator.Play 直切用的状态名;新流程一律走 Animator 参数")]
    public string animState = "Heavy";
    [Tooltip("重击特效 prefab 槽(出伤命中时在玩家位置生成)")]
    public GameObject heavyVFXPrefab;

    [Header("重击判定框")]
    [Tooltip("重击独立判定框(HeavyRange 子物体上的 MeleeRangeIndicator);留空 = 打一条 warning 并按无条件结算兜底(保持旧行为)")]
    public MeleeRangeIndicator heavyRangeIndicator;

    [Header("[v2] 时序参数")]
    [Tooltip("预警秒数 P:闪现时刻 = 标点 x - 判定窗口前段 - P。闪现在前,蓄力只占末段")]
    public float warningLead = 1f;
    [Tooltip("判定窗口前段长度(秒):出圈时刻与判定窗口起点都是 x - 该值;必须与玩家侧 PlayerBeatJudge.windowBefore 一致")]
    public float judgeWindowBefore = 0.4f;

    [Header("蓄力表现")]
    [Tooltip("蓄力表现 prefab 槽(P2 用:蓄力开始时生成、出伤/收尾时收起);P1 只保留引用与收起出口")]
    public GameObject heavyChargeVFXPrefab;

    [Header("背刺圈")]
    [Tooltip("Boss 身上的背刺圈挂点(拖 Boss 下 BeatPoint_Heavy 物体上的 BeatFlashPoint);留空 = 不出圈")]
    public BeatFlashPoint backstabRingPoint;

    [Header("重击伤害")]
    [Tooltip("重击伤害值")]
    public float damage = 30f;
    [Tooltip("重击击退力度")]
    public float knockbackForce = 6f;
    [Tooltip("重击击退上挑(0 = 水平)")]
    public float knockbackUp = 1f;

    // ============================================================
    // 运行时
    // ============================================================

    private FirstBoss _boss;
    private BossSkillSlots _slots;
    private Animator _animator;
    private Rigidbody2D _rb;
    private PlayerController _playerCtrl;
    private Transform _player;
    private Coroutine _loopRoutine;

    private bool _heavyActive;   // 重击施放中(霸体)
    private bool _cancelled;     // 被 player 攻击抵消(该次重击不造成伤害)
    private bool _finalized;     // 本次重击已走收尾出口(防重复收尾)

    // ── 位置冻结(重击期间) ──
    private bool _gravityFrozen;          // 已关重力(收尾/禁用时恢复)
    private float _savedGravityScale = 1f; // 冻结前重力(与 BossSkill_FireWall 同口径)

    // ── 蓄力表现实例(P2 蓄力开始时生成后赋值;收尾出口与出伤时收起) ──
    private GameObject _chargeVFXInstance;

    // ── 两段动画(P2) ──
    private float _chargeAnimDuration;    // 蓄力动画时长(秒):从 Animator 反推后缓存
    private bool _chargeTimingResolved;   // 是否已反推过(只反推一次,不每帧遍历 animationClips)
    private bool _chargeStarted;          // 本次重击的蓄力段是否已起播
    private bool _heavyEndNotified;       // 本次重击是否已收到 Heavy-Attack 末帧的动画事件
    private float _heavyAttackAnimDuration;   // 攻击段动画时长(秒):收尾兜底用,从 Animator 反推(2026-09-15)

    // ── P3:伤害结算状态与背刺圈 ──
    private bool _damageSettled;              // 本次重击是否已过伤害判定帧(玩家侧判定有效期)
    private bool _hitDone;                    // [v2] 本次重击的出伤事件是否已结算(防动画事件重复)
    private bool _ringShown;                  // 本次重击的背刺圈是否已出(每次重击只出一次)
    private bool _flashPointMissingLogged;    // 槽位为空只抱怨一次(防每圈刷日志)

    /// <summary>Animator 参数名 — 重击总开关(重击全程 true,收尾置 false 走编辑器的「任意状态 → Exit」过渡)</summary>
    private const string AnimParamIsHeavy = "IsHeavy";

    /// <summary>Animator 参数名 — 两段切换:false = 蓄力段(Charge),true = 攻击段(Heavy-Attack)</summary>
    private const string AnimParamIsHeavyAttack = "IsHeavyAttack";

    /// <summary>蓄力片段名关键字(在 Animator 的 animationClips 里按名字含它找蓄力 clip)</summary>
    private const string ChargeClipKeyword = "Charge";

    /// <summary>攻击段片段名关键字(收尾兜底按它的时长算,名字含 Heavy-Attack)</summary>
    private const string HeavyAttackClipKeyword = "Heavy-Attack";

    /// <summary>空中玩家取地面点的向下射线长度(米):够覆盖关卡最大落差</summary>
    private const float GroundProbeDistance = 30f;

    // [v2] 出圈提前量改用可调字段 judgeWindowBefore(与判定窗口起点同步,默认 0.4);
    // 旧实现:private const float RingLeadSeconds = 0.8f;(v2 已废弃)

    /// <summary>等待超时余量(秒):等待标点/让位/等动画结束事件的上限都 = prepareLead + 该值</summary>
    private const float WaitTimeoutMargin = 3f;

    /// <summary>临时日志统一标签(验收期用;saika 验收后可删)</summary>
    private const string LogTag = "[BossHeavy]";

    // [2026-09-07 AttackVFXAnchor 重构暂停:Boss 重击持续特效待玩家侧验收后按新结构迁移]
    ///// <summary>攻击持续 VFX 锚点(attack_VFX 子物体上的 AttackVFXAnchor;未配置时为 null,空安全)</summary>
    //private AttackVFXAnchor _vfx;

    public bool IsActive => _heavyActive;

    /// <summary>
    /// P3:本次重击是否已过伤害判定帧(含被抵消/未命中 —— 判定帧一过就不再接受玩家侧卡点判定)。
    /// 玩家侧判定入口用它做「判定有效期」闸门:已出伤则本次判定不生效(不闪、不瞬移、不算成功),
    /// 避免出现「显示判定成功却已经挨打」。每次重击在 ExecuteHeavy 开头复位。
    /// </summary>
    public bool DamageSettled => _damageSettled;

    private void Awake()
    {
        _boss = GetComponent<FirstBoss>();
        _slots = GetComponent<BossSkillSlots>();
        _animator = GetComponentInChildren<Animator>();
        _rb = GetComponent<Rigidbody2D>();

        if (_boss == null)
            Debug.LogWarning($"{LogTag} 组件必须挂在 FirstBoss 上(当前找不到 FirstBoss),重击不会触发");
    }

    private void Start()
    {
        CachePlayer();
    }

    private void OnEnable()
    {
        _loopRoutine = StartCoroutine(HeavyLoop());
    }

    private void OnDisable()
    {
        if (_loopRoutine != null) StopCoroutine(_loopRoutine);

        // 协程被 Stop 后体内清理不会执行:这里兜底走一次收尾(幂等),
        // 防「禁用/销毁发生在重击中途」留下重力 0 + 朝向锁死的残留。
        if (_heavyActive || _gravityFrozen) FinalizeHeavy();

        _heavyActive = false;
    }

    /// <summary>重击被 player 攻击命中:标记抵消(该次重击不造成伤害),Boss 照常掉血
    /// [2026-09-15 v2.1] 同时立刻收起判定圈:玩家判定已成功,圈继续收缩会被看成"又被刺之后圈又走了一遍"</summary>
    public void NotifyHit()
    {
        if (!_heavyActive) return;
        _cancelled = true;
        HideBackstabRing();
    }

    /// <summary>
    /// 被玩家背刺成功(终结技命中)时强制中断本次重击:标记抵消 + 走统一收尾
    /// (解除霸体/解锁朝向/恢复重力/复位 IsHeavy 与 IsHeavyAttack/收圈),好让 Boss 接着进受击状态。
    /// 不中断的话动画器停在 Charge / Heavy-Attack 里(它们的出口条件只看 IsHeavyAttack / IsHeavy),
    /// 置了 IsHurt 也切不进 Hurt 动画。由 BossControllerBase.OnHitBy 的背刺分支调用。
    /// </summary>
    public void InterruptHeavy()
    {
        if (!_heavyActive && !_gravityFrozen) return;   // 不在重击中:空操作
        _cancelled = true;      // 双保险:即使出伤窗口还没走完也不再造成伤害
        FinalizeHeavy();
    }

    /// <summary>
    /// [v2] 重击出伤帧(纯动画事件入口):由 Heavy-Attack 动画的出伤事件帧调用
    /// (BossAnimationRelay.OnBossHeavyHitFrame 转发)。代码不做定时出伤,事件没挂就是不出伤。
    /// 逻辑:非重击期忽略;已出伤忽略(防重复);被抵消则不出伤;否则读重击框(heavyRangeIndicator)
    /// 判玩家当前位置,在框内才结算伤害与击退。同时把判定有效期标记为已出伤并收起背刺圈。
    /// </summary>
    public void OnHeavyHitFrame()
    {
        if (!_heavyActive || _finalized) return;
        if (_hitDone) return;
        _hitDone = true;
        _damageSettled = true;      // 出伤之后玩家侧判定不再生效(窗口本已在 x 关闭,这里是双保险)
        // [2026-09-15] 出伤帧不再主动收圈:环会继续缩到内圈后自行消失,
        //   否则会出现"环缩到一半被收掉"(出伤事件帧早于环到内圈时看得最明显)。
        //   收尾出口 FinalizeHeavy 里还有一次幂等收起兜底。

        if (_cancelled) return;   // 已被玩家卡点抵消,本次不出伤
        bool hit = IsPlayerInHeavyRange();
        if (hit) PerformHeavyHit();
        // [2026-09-16 清理临时调试] Debug.Log($"{LogTag} 出伤帧:是否命中={hit}");
    }

    /// <summary>缓存玩家引用(Start 一次;玩家重建/延迟生成时由 ExecuteHeavy 兜底重取)</summary>
    private void CachePlayer()
    {
        _playerCtrl = PlayerController.Instance;
        _player = _playerCtrl != null ? _playerCtrl.transform : null;
    }

    // ============================================================
    // 标点监听循环(每帧查询标点形态)
    // ============================================================

    private IEnumerator HeavyLoop()
    {
        while (true)
        {
            if (_boss == null || _boss.IsDead) yield break;

            // 未激活(玩家还没进 Boss 房):不响应标点
            if (!_boss.IsActivated)
            {
                yield return null;
                continue;
            }

            var mgr = MusicPointManager.Instance;
            if (mgr == null)
            {
                yield return null;
                continue;
            }

            float next = mgr.NextPointInGroup(groupName);
            float toNext = next >= 0f ? next - mgr.TrackTime : -1f;

            // [v2] 闪现提前量 = 判定窗口前段 + 预警秒数(闪现在前,蓄力只占末段)
            if (toNext >= 0f && toNext <= judgeWindowBefore + warningLead)
            {
                // 不打断技能:技能执行中重击让位,等本次标点过去再查下一个。
                // 让位等待必须能退出:Boss 死亡 / 管理器丢失 / 超时(音乐换源 TrackTime 回绕时不至于死等)。
                if (_slots != null && _slots.IsExecuting)
                {
                    float deadline = Time.time + judgeWindowBefore + warningLead + WaitTimeoutMargin;
                    while (mgr != null && mgr.TrackTime < next)
                    {
                        if (_boss == null || _boss.IsDead) break;
                        if (Time.time > deadline) break;
                        yield return null;
                    }
                    continue;
                }
                yield return StartCoroutine(ExecuteHeavy(next));
                continue;
            }
            yield return null;
        }
    }

    // ============================================================
    // 单次重击
    // ============================================================

    /// <summary>执行一次重击:快照 → 闪现 → 锁朝向 → 冻结 → 等标点 → 判定出伤 → 等收尾 → 统一出口</summary>
    private IEnumerator ExecuteHeavy(float beatTime)
    {
        if (_boss == null || _boss.IsDead) yield break;

        _heavyActive = true;
        _cancelled = false;
        _finalized = false;
        _chargeStarted = false;
        _heavyEndNotified = false;
        _damageSettled = false;   // P3:新一轮重击的判定有效期重新打开
        _ringShown = false;       // P3:新一轮重击的背刺圈可以再出一次
        _hitDone = false;         // [v2] 新一轮重击的出伤事件重新待触发
        ResolveChargeTiming();   // 蓄力动画时长反推(只做一次,结果缓存)

        // 进重击(闪现时刻):两个动画参数都不置,动画器保持待机(准备期)。
        // 蓄力段起播由代码在「标点 x - 蓄力动画时长」那一帧控制(EnterChargeStage 里才置 IsHeavy=true)。
        // [2026-09-16 修正] 旧写法在这里同时置 IsHeavy=true + IsHeavyAttack=true,等价于当场宣告攻击段:
        //   只要动画器回一次 Entry 就直插 Heavy-Attack,蓄力段被整段跳过(实测现象:重击闪一下回 Idle)。

        // [2026-09-07 AttackVFXAnchor 重构暂停] 攻击持续 VFX:重击全程播 slot_heavy
        //if (_vfx == null) _vfx = GetComponentInChildren<AttackVFXAnchor>(true);
        //_vfx?.Show("slot_heavy");

        if (_player == null || _playerCtrl == null) CachePlayer();
        if (_player == null || _playerCtrl == null)
        {
            Debug.LogWarning($"{LogTag} 找不到 PlayerController,本次重击跳过");
            FinalizeHeavy();
            yield break;
        }

        // ── ① 快照:闪现这一刻的玩家位置与玩家朝向(之后不再追踪玩家位置) ──
        Vector2 playerSnap = _player.position;
        float dir = _playerCtrl.FacingDir >= 0 ? 1f : -1f;

        // ── ② 落点:x = 玩家快照 x + 玩家朝向 × 闪现距离;y 默认与 Boss 同高 ──
        float landingX = playerSnap.x + dir * teleportDistance;
        float landingY = _boss.transform.position.y;

        // 玩家在空中:向下取地面点,落点 y = 地面点 y + Boss 碰撞体半高
        // (注意 PlayerController 用 new 隐藏了基类的 IsGrounded 属性,这里是方法,必须带括号)
        if (!_playerCtrl.IsGrounded())
        {
            if (TryGetGroundPoint(playerSnap, out float groundY))
            {
                float halfHeight = _boss.Col != null ? _boss.Col.bounds.extents.y : 0f;
                landingY = groundY + halfHeight;
            }
            // 射线未命中地面(玩家在坑里/空中平台外):保持 Boss 当前 y
        }

        // ── ④ 可达性:玩家朝向侧被实心墙/管道挡住 → 落点翻到玩家另一侧 ──
        Vector2 landing = new Vector2(landingX, landingY);
        if (IsLandingSideBlocked(playerSnap, dir))
            landing.x = playerSnap.x - dir * teleportDistance;

  // [日志精简] Debug.Log($"{LogTag} 闪现落点 玩家快照={playerSnap} 朝向={dir} 目标落点={landing}");

        // ── ⑤ 落点执行:ForceSetPosition 内含贴墙钳制 + 清速度,返回值才是实际落点 ──
        Vector2 actual = _boss.ForceSetPosition(landing);

        // ── ⑥ 朝向:面向玩家后锁死(收尾才解锁) ──
        int facingDir = (_player.position.x - actual.x) >= 0f ? 1 : -1;
        _boss.UpdateFacing(facingDir);
        _boss.SetFacingLocked(true, facingDir);
  // [日志精简] Debug.Log($"{LogTag} 实际落点={actual} 朝向={facingDir}");

        // ── ⑦ 冻结:关重力(位置冻结),重击期间每帧 moveInput = 0 ──
        ApplyGravityFreeze();
        _boss.moveInput = 0f;

        // ── ⑧ 等到 TrackTime 到标点 x(超时兜底 + 死亡/管理器丢失检查) ──
        //    期间:到「标点 x - 蓄力动画时长」这一帧起播蓄力段(置 IsHeavyAttack = false + 生成蓄力 VFX)
        var mgr = MusicPointManager.Instance;
        float deadline = Time.time + prepareLead + WaitTimeoutMargin;
        float chargeStartTrackTime = beatTime - _chargeAnimDuration;
        bool timedOut = false;
        while (mgr != null && mgr.TrackTime < beatTime)
        {
            if (_boss == null || _boss.IsDead) break;
            if (Time.time > deadline) { timedOut = true; break; }
            _boss.moveInput = 0f;   // ChaseState 之外的状态也压住移动输入

            if (!_chargeStarted && mgr.TrackTime >= chargeStartTrackTime)
                EnterChargeStage(beatTime, mgr.TrackTime);

            // v2.1:距标点 x ≤ judgeWindowBefore 秒 → 出背刺圈(每次重击只出一次;
            //   被玩家抵消时立刻收起,并且本次重击不再重出 —— 否则收圈后条件又满足会"再走一遍")
            // 出圈时刻 = 标点 x - 环从起点缩到判定外环的用时(lead,读模板参数):
            //   环一出现就开始缩,没有"先亮圈不动"的静默段;改模板速度/起点后自动跟随,判定窗口与此无关。
            float ringLead = backstabRingPoint != null ? backstabRingPoint.AimLeadSeconds
                                                      : Mathf.Max(0.05f, judgeWindowBefore);
            if (!_ringShown && !_cancelled && mgr.TrackTime >= beatTime - ringLead)
                ShowBackstabRing(beatTime - mgr.TrackTime);

            yield return null;
        }

        if (_boss == null || _boss.IsDead)
        {
            // [2026-09-16 清理临时调试] Debug.Log($"{LogTag} Boss 死亡/销毁,本次重击中断");
            FinalizeHeavy();
            yield break;
        }
        if (mgr == null)
        {
            Debug.LogWarning($"{LogTag} MusicPointManager 丢失,本次重击中断");
            FinalizeHeavy();
            yield break;
        }
        if (timedOut)
        {
            Debug.LogWarning($"{LogTag} 等待标点 x={beatTime} 超时(TrackTime={mgr.TrackTime}),本次重击中断");
            FinalizeHeavy();
            yield break;
        }

        // ── ⑨ 到标点 x:锚点帧 —— 蓄力动画在此结束、攻击动画在此开始、环缩到判定外环、判定窗口在此关闭 ──
        //    出伤不在这里:v2 起出伤严格由动画事件驱动(saika 在 Heavy-Attack 的出伤帧挂事件 →
        //    BossAnimationRelay.OnBossHeavyHitFrame → BossHeavyAttack.OnHeavyHitFrame)。代码不做任何定时伤害。
        //    兜底:若一帧跨过了蓄力起播点(蓄力时长配置过短/卡帧),在这里补一次起播。
        if (!_chargeStarted) EnterChargeStage(beatTime, mgr.TrackTime);
        SetAnimBool(AnimParamIsHeavyAttack, true);
  // [日志精简] Debug.Log($"{LogTag} 锚点帧 x={beatTime:0.###}:蓄力结束、攻击动画开始(出伤等动画事件)");

        // ── ⑫ 收尾:等 Heavy-Attack 末帧的动画事件(saika 把 OnBossHeavyEnd 挂在片段最后一帧 → NotifyHeavyAnimEnd) ──
        //    超时(prepareLead + 3 秒)仍未收到则强制收尾,协程不卡死(没挂事件时退化为"慢一点但能解锁")
        // 收尾兜底:优先按 Heavy-Attack 动画时长(出伤帧就是该动画起点),查不到再退回固定超时。
        // 不这么做的话事件没挂时每次重击要多站 prepareLead+3 秒,叠加收尾后的普攻间隔 = 重击后长时间呆在原地(2026-09-15 实测)
        float endFallback = _heavyAttackAnimDuration > 0.01f
            ? _heavyAttackAnimDuration + 0.5f
            : prepareLead + WaitTimeoutMargin;
        float endDeadline = Time.time + endFallback;
        while (!_heavyEndNotified)
        {
            if (_boss == null || _boss.IsDead) break;
            if (Time.time > endDeadline)
            {
                Debug.LogWarning($"{LogTag} 等待重击结束事件超时({endFallback:0.###} 秒)仍未收到,强制收尾(检查 Heavy-Attack 片段末帧是否挂了 OnBossHeavyEnd)");
                break;
            }
            _boss.moveInput = 0f;
            yield return null;
        }
        FinalizeHeavy();
    }

    // ============================================================
    // 收尾统一出口
    // ============================================================

    /// <summary>
    /// 统一收尾出口 — 正常出伤后(收到动画结束事件)、Boss 死亡、管理器丢失、等待超时、禁用中断全走它(幂等)。
    /// 顺序:恢复重力 → Animator 两段参数复位 → 收起蓄力表现 → 解朝向锁并朝回玩家 → 解除霸体 → 给普攻上间隔。
    /// </summary>
    private void FinalizeHeavy()
    {
        if (_finalized) return;
        _finalized = true;

        ReleaseGravityFreeze();

        // IsHeavy=false → 走编辑器里「任意状态 → 退出(Exit)」那条过渡,动画复位。
        // IsHeavyAttack 一并复位,下一轮重击从「占位持住」重新开始(此时 IsHeavy 已是 false,不会误触发 Charge)。
        // 参数未接线时 Unity 会打 "Parameter 'IsHeavy' does not exist" warning —— 属预期(等 saika 接线后消失)
        SetAnimBool(AnimParamIsHeavy, false);
        SetAnimBool(AnimParamIsHeavyAttack, false);

        HideChargeVFX();
        HideBackstabRing();

        if (_boss != null)
        {
            _boss.moveInput = 0f;
            _boss.SetFacingLocked(false, 0);
            var p = _player != null ? _player : (_playerCtrl != null ? _playerCtrl.transform : null);
            if (p != null)
                _boss.UpdateFacing(p.position.x >= _boss.transform.position.x ? 1f : -1f);
            _boss.StartMeleeInterval();   // 重击结束后给普攻上间隔
        }

        _heavyActive = false;

        // 判定有效期重新打开:否则从出伤到下一次重击开始前的整圈时间里,玩家的卡点判定都会被
        // 「本次重击已出伤」拒掉 → 判定窗口内按 F 完全无反应(2026-09-15 实测根因)
        _damageSettled = false;
    }

    /// <summary>
    /// 收起蓄力表现:销毁蓄力开始时生成的实例(幂等;出伤与收尾两处都调,谁先到谁收)。
    /// 实例为空时是空操作(槽未接线 / 还没到起播点)。
    /// 注:P3 的 Boss 背刺收缩圈(BeatFlashPoint)收起走 HideBackstabRing,同样在出伤与收尾两处。
    /// </summary>
    private void HideChargeVFX()
    {
        if (_chargeVFXInstance != null) Destroy(_chargeVFXInstance);
        _chargeVFXInstance = null;
    }

    // ============================================================
    // P3:背刺圈(挂在 Boss 身上的 BeatFlashPoint)
    // ============================================================

    /// <summary>
    /// 出 Boss 背刺圈 —— 对 Boss 身上挂点的 BeatFlashPoint 调 Flash(距该标点的剩余秒数, 当前曲的判定窗口时长)。
    /// 挂点用 GetComponentInChildren 找(含 inactive:挂点常以隐藏态保存),只找一次并缓存;
    /// 没挂 → 空操作(不出圈,不打报错),符合「圈由编辑器接线,代码只留空槽兜底」的约定。
    /// 每次重击只出一次(_ringShown 守卫,由调用点保证)。
    /// </summary>
    private void ShowBackstabRing(float secondsToPoint)
    {
        _ringShown = true;

        // 圈挂点用显式槽位,不在层级里自动找:Boss 下同时存在旧的闪烁标识 BeatFlashPoint(aimPrefab 为空),
        // 自动找会先取到它 → 走闪烁模式,不出收缩圈(2026-09-15 场景实测根因,改为拖引用)
        if (backstabRingPoint == null)
        {
            if (!_flashPointMissingLogged)
            {
                _flashPointMissingLogged = true;
                Debug.Log($"{LogTag} 背刺圈挂点槽 backstabRingPoint 为空:不出圈(把 Boss 下 BeatPoint_Heavy 上的 BeatFlashPoint 拖进来)");
            }
            return;
        }

        // 先退掉挂点自己的自动窗口订阅:它若 autoSubscribe=true 会各自 Flash 一次,
        // 与这里叠加 → 环被第二次 Show 重启("圈缩一点就消失、又完整缩一遍"的根因)。
        // [对照实验结论 2026-09-15] 必须有:注释掉后问题立刻复现,日志实锤挂点运行时"自动订阅=True"
        //   → 挂点自己的窗口订阅会和重击这次叠加,圈被第二次 Show 重启。这里退订,保证单驱动。
        backstabRingPoint.DisableAutoSubscribe();

        var mgr = MusicPointManager.Instance;
        float window = mgr != null ? mgr.WindowSeconds : 0.3f;   // 管理器丢失时用默认窗口兜底
        backstabRingPoint.Flash(Mathf.Max(0f, secondsToPoint), window);
        // [2026-09-16 清理临时调试] Debug.Log($"{LogTag} 出圈 TrackTime={(mgr != null ? mgr.TrackTime : -1f):0.###} 点={(mgr != null ? mgr.TrackTime + secondsToPoint : -1f):0.###}");
    }

    /// <summary>收起背刺圈(P3:出伤帧与收尾出口两处都调,谁先到谁收;幂等,没出过圈时是空操作)</summary>
    private void HideBackstabRing()
    {
        if (!_ringShown) return;
        _ringShown = false;
        if (backstabRingPoint != null) backstabRingPoint.Hide();
    }

    // ============================================================
    // 两段动画 / 蓄力表现(P2)
    // ============================================================

    /// <summary>Animator 参数写入(空安全;参数未接线时 Unity 会打 warning,属预期)</summary>
    private void SetAnimBool(string paramName, bool value)
    {
        if (_animator == null) _animator = GetComponentInChildren<Animator>();
        if (_animator != null) _animator.SetBool(paramName, value);
    }

    /// <summary>
    /// 蓄力段起播:IsHeavyAttack 置 false(编辑器连线「任意状态 → Charge」的条件是 IsHeavy && !IsHeavyAttack)
    /// 并生成蓄力 VFX。一次重击只起播一次(_chargeStarted 守卫);到标点 x 后由攻击段切走。
    /// 起播时刻 = 标点 x - 蓄力动画时长(蓄力动画刚好在出伤帧结束)。
    /// </summary>
    private void EnterChargeStage(float beatTime, float trackTime)
    {
        _chargeStarted = true;
        // [2026-09-16] IsHeavy 在起播帧才置真(编辑器 Entry → Charge 的条件),蓄力段从这一帧开始;
        //   准备期不置 → Entry 无匹配 → 动画器保持待机,蓄力不会提前到闪现那一刻。
        SetAnimBool(AnimParamIsHeavy, true);
        SetAnimBool(AnimParamIsHeavyAttack, false);
        SpawnChargeVFX();
  // [日志精简] Debug.Log($"{LogTag} 蓄力起播 TrackTime={trackTime:0.###}(标点 x={beatTime:0.###} - 蓄力时长 {_chargeAnimDuration:0.###})");
    }

    /// <summary>生成蓄力 VFX:挂在 Boss 下(跟随 Boss),沿用 prefab 自身的局部变换;槽为空或已有实例时跳过</summary>
    private void SpawnChargeVFX()
    {
        if (heavyChargeVFXPrefab == null) return;   // 槽未接线:空操作,等 saika 拖 prefab
        if (_chargeVFXInstance != null) return;     // 防重复生成
        _chargeVFXInstance = Instantiate(heavyChargeVFXPrefab, transform, false);
    }

    /// <summary>
    /// 蓄力动画时长反推并缓存 —— 只做一次,不在协程里每帧遍历 animationClips。
    /// 口径:从 Animator 的 runtimeAnimatorController.animationClips 里按片段名含 Charge 找,取 clip.length。
    /// · 找不到 → warning,退化用 prepareLead(效果 = 闪现那一刻就起播蓄力)
    /// · 时长 > prepareLead → warning 提示调大 prepareLead 或缩短动画(此时蓄力放不完,到 x 就切攻击段)
    /// </summary>
    private void ResolveChargeTiming()
    {
        if (_chargeTimingResolved) return;
        _chargeTimingResolved = true;

        float len = FindClipLength(ChargeClipKeyword, out string clipName);
        if (len <= 0f)
        {
            _chargeAnimDuration = Mathf.Max(0f, prepareLead);
            Debug.LogWarning($"{LogTag} Animator 里没找到名字含 \"{ChargeClipKeyword}\" 的动画片段(Charge 状态的 clip 未接?):蓄力时长退化为 prepareLead={prepareLead} 秒,即闪现那一刻就起播蓄力");
        }
        else
        {
            _chargeAnimDuration = len;
  // [日志精简] Debug.Log($"{LogTag} 蓄力动画时长反推={len:0.###} 秒(片段 {clipName}),蓄力起播时刻 = 标点 x - {len:0.###} 秒");
            if (len > prepareLead)
                Debug.LogWarning($"{LogTag} 蓄力动画时长 {len:0.###} 秒 > prepareLead {prepareLead} 秒:蓄力会放不完,请调大 prepareLead 或缩短蓄力动画");
        }

        // 攻击段动画时长:收尾兜底用(事件没挂时按它收尾,不等固定超时)
        float atkLen = FindClipLength(HeavyAttackClipKeyword, out string atkName);
        _heavyAttackAnimDuration = atkLen;
        if (atkLen <= 0f)
            Debug.LogWarning($"{LogTag} Animator 里没找到名字含 \"{HeavyAttackClipKeyword}\" 的动画片段(Heavy-Attack 状态的 clip 未接?):收尾兜底退回固定 {prepareLead + WaitTimeoutMargin} 秒");
    }

    /// <summary>按名字含指定关键字找 clip 的时长(没找到/时长无效返回 0),顺手带出片段名供日志</summary>
    private float FindClipLength(string keyword, out string clipName)
    {
        clipName = null;
        if (_animator == null) _animator = GetComponentInChildren<Animator>();
        var controller = _animator != null ? _animator.runtimeAnimatorController : null;
        if (controller == null) return 0f;

        // animationClips 只取一次(它是属性,避免重复访问)
        AnimationClip[] clips = controller.animationClips;
        if (clips == null) return 0f;

        foreach (var clip in clips)
        {
            if (clip == null || string.IsNullOrEmpty(clip.name)) continue;
            if (clip.name.IndexOf(keyword, System.StringComparison.OrdinalIgnoreCase) < 0) continue;
            clipName = clip.name;
            return clip.length;
        }
        return 0f;
    }

    /// <summary>
    /// 收到 Heavy-Attack 末帧的动画事件(BossAnimationRelay.OnBossHeavyEnd 转发)—— 收到即走统一收尾,
    /// 动画播完才解锁、恢复移动;协程醒来看到标记后退出等待循环(不会重复收尾,_finalized 幂等守着)。
    /// 收尾已完成 / 非重击期的迟到事件直接忽略。
    /// </summary>
    public void NotifyHeavyAnimEnd()
    {
        if (!_heavyActive || _finalized || _heavyEndNotified) return;
        _heavyEndNotified = true;
  // [日志精简] Debug.Log($"{LogTag} 收到重击结束事件(Heavy-Attack 末帧),开始收尾");
        FinalizeHeavy();
    }

    // ============================================================
    // 冻结
    // ============================================================

    /// <summary>冻结位置:保存 gravityScale 并置 0(与 BossSkill_FireWall 同口径),清速度;收尾/禁用时恢复</summary>
    private void ApplyGravityFreeze()
    {
        if (_rb == null) _rb = GetComponent<Rigidbody2D>();
        if (_rb == null) return;

        if (!_gravityFrozen)
        {
            _savedGravityScale = _rb.gravityScale;
            _gravityFrozen = true;
  // [日志精简] Debug.Log($"{LogTag} 冻结开始 gravityScale={_savedGravityScale}→0");
        }
        _rb.gravityScale = 0f;
        _rb.velocity = Vector2.zero;
    }

    /// <summary>恢复重力(幂等) — 收尾与禁用两条路都走它,防 Boss 悬空</summary>
    private void ReleaseGravityFreeze()
    {
        if (!_gravityFrozen) return;
        _gravityFrozen = false;
        if (_rb != null) _rb.gravityScale = _savedGravityScale;
  // [日志精简] Debug.Log($"{LogTag} 冻结结束 恢复 gravityScale={_savedGravityScale}");
    }

    // ============================================================
    // 落点/命中判定
    // ============================================================

    /// <summary>
    /// 从 from 向下取地面点(玩家在空中时决定落点 y)。
    /// 用 RaycastAll 跳过玩家/Boss 自身 collider —— 射线起点在玩家碰撞体内,首个命中常常是玩家自己
    /// (与 PlayerBackstabState.ResolveBackstabLanding 的"射线可能扫到自身"同因);取最近的合法命中。
    /// 命中层 = wallLayer(掩码已含地面层 Ground)。
    /// </summary>
    private bool TryGetGroundPoint(Vector2 from, out float groundY)
    {
        groundY = 0f;
        bool found = false;
        float nearest = float.MaxValue;

        RaycastHit2D[] hits = Physics2D.RaycastAll(from, Vector2.down, GroundProbeDistance, wallLayer);
        foreach (RaycastHit2D hit in hits)
        {
            if (hit.collider == null) continue;
            if (hit.collider.GetComponentInParent<PlayerController>() != null) continue;                 // 玩家自身
            if (hit.transform == transform || hit.transform.IsChildOf(transform)) continue;              // Boss 自身
            if (hit.distance >= nearest) continue;
            nearest = hit.distance;
            groundY = hit.point.y;
            found = true;
        }
        return found;
    }

    /// <summary>
    /// 可达性判定:从玩家快照位置沿玩家朝向射线(闪现距离 + 0.3),命中实心墙/地面/实心管道/管道 trigger
    /// = 该侧不可达,落点翻到玩家另一侧(判定口径与 PlayerBackstabState.ResolveBackstabLanding 一致)。
    /// 跳过玩家自身与 Boss 自身 collider;普通 trigger(判定框/门等)不算挡。
    /// </summary>
    private bool IsLandingSideBlocked(Vector2 from, float dir)
    {
        float dist = teleportDistance + 0.3f;
        RaycastHit2D[] hits = Physics2D.RaycastAll(from, Vector2.right * dir, dist);
        foreach (RaycastHit2D hit in hits)
        {
            if (hit.collider == null) continue;
            if (hit.transform == transform || hit.transform.IsChildOf(transform)) continue;              // Boss 自身
            if (hit.collider.GetComponentInParent<PlayerController>() != null) continue;                 // 玩家自身
            if (hit.collider.isTrigger && !AreaChannelTrigger.IsPointInChannel(hit.point)) continue;     // 普通 trigger 不算挡
            return true;
        }
        return false;
    }

    /// <summary>
    /// 重击框命中判定:读重击框(heavyRangeIndicator)的视觉大小与玩家当前位置,框内才算出伤。
    /// 槽未接线(为空)或尺寸无效时:打一条 warning 并按"无条件结算"兜底 —— 保持旧行为,
    /// 避免 saika 还没接框时重击完全打不到。
    /// </summary>
    private bool IsPlayerInHeavyRange()
    {
        if (heavyRangeIndicator == null)
        {
            Debug.LogWarning($"{LogTag} 重击范围指示器(heavyRangeIndicator)为空:本次按无条件结算兜底,请在编辑器把 HeavyRange 接上");
            return true;
        }

        Vector2 size = heavyRangeIndicator.Size;
        if (size.x <= 0f || size.y <= 0f)
        {
            Debug.LogWarning($"{LogTag} 重击框尺寸无效({size}):本次按无条件结算兜底");
            return true;
        }

        Vector2 center = heavyRangeIndicator.Center;
        Vector2 playerPos = _player != null ? (Vector2)_player.position : center;
        return Mathf.Abs(playerPos.x - center.x) <= size.x * 0.5f
            && Mathf.Abs(playerPos.y - center.y) <= size.y * 0.5f;
    }

    /// <summary>重击攻击:对 player 结算伤害 + 出伤特效(数值沿用 Inspector 配置)</summary>
    private void PerformHeavyHit()
    {
        if (_player == null || _boss == null) return;
        var ph = _player.GetComponent<PlayerHealth>();
        if (ph == null) return;

        Vector2 faceDir = _player.position.x > _boss.transform.position.x ? Vector2.right : Vector2.left;
        Vector2 kbDir = new Vector2(faceDir.x, knockbackUp);
        var info = new DamageInfo
        {
            amount = damage,
            source = _boss,
            sourcePosition = _boss.transform.position,
            attackLabel = "BossHeavy",
            knockback = new Knockback
            {
                direction = kbDir.normalized,
                force = knockbackForce,
                duration = 0.2f,
                ignoreResistance = false
            }
        };
        CombatResolver.Resolve(_boss, ph, info);

        if (heavyVFXPrefab != null)
            VFXSpawner.SpawnOnPlayer(heavyVFXPrefab, _player.position);
    }
}
