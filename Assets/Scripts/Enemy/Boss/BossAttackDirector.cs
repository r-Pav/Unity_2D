using UnityEngine;

/// <summary>
/// Boss 攻击编排 — 音乐标点驱动(替代旧的「技能阶段 ↔ 普攻阶段」计数器循环)。
/// 分工:
///   ① 技能:MusicTrack 里名为 skillGroupName(默认 "BossSkill")的标点组,每个标点到达时释放一个技能,
///      技能从技能池袋装随机抽取(相邻不重复,抽空重洗,跨音乐圈续用不重置)。
///   ② 普攻:标点之间的空档按「攻击动画完整时长 + attackCooldown」填充,由 ChaseState 调 TryAttack() 发起。
///   ③ 拉距:距下一个技能点不足一次完整普攻动画时停手,并稍微远离玩家(收手 → 起手的观感)。
/// 设计口径(规格文档《Boss技能与普攻_音乐点驱动_实现规格》2026-09-19 §五 P1):
///   - 技能不受玩家位置约束(到点照常释放);技能执行中 / 重击中到达的点直接丢弃,不排队、不顺延。
///   - 技能起播提前量 skillLead = 0(起手即 Magic 动画,不加延时)。
///   - 到达判据(与规格伪码的差异,原因见下):MusicPointManager.NextPointInGroup 内部带 ±0.001 容差
///     —— 标点一旦不满足 p &gt; t + 0.001 就跳到下一个点(PlayerBeatJudge 也注释了这一点),因此规格伪码里的
///     `mgr.TrackTime >= NextPointInGroup()` 只对组内最后一个点成立(非末位的点会整体被跳过,实测见交付报告的
///     离线自测 B 组)。本实现改为:组内标点数组精确扫描(公开 API MusicTrackData.GetGroup,不用带容差的查询)
///     + 记住「即将到来的点」(_pendingPoint),TrackTime 追上它的那一帧释放(精度 = 1 帧,不提前起手)。
///     音乐回绕(TrackTime 明显倒退)时清掉本圈记录,避免单点组第二圈被旧记录吃掉。
///   - 普攻冷却由本组件自己记(_nextMeleeAt);BossAttackState 不再调 StartMeleeInterval
///     (StartMeleeInterval 保留给重击收尾用,boss.IsMeleeIntervalActive 仍作额外门槛)。
///   - 禁用 FindObjectOfType 之类运行时查找:音乐管理器走 MusicPointManager.Instance 单例。
/// </summary>
public class BossAttackDirector : MonoBehaviour
{
    [Header("引用")]
    [Tooltip("Boss 控制器")]
    [SerializeField] private BossControllerBase boss;
    [Tooltip("技能池组件")]
    [SerializeField] private BossSkillSlots skillSlots;
    [Tooltip("[留档] 普攻伤害组件 — 普攻伤害已由 BossAttackState 在动画命中帧结算,本组件不再使用(保留连线防丢失)")]
    [SerializeField] private EnemyMeleeAttack defaultMelee;

    [Header("技能点驱动")]
    [Tooltip("技能标点组名(MusicTrack 的 Point Groups 里配置的组名)")]
    [SerializeField] private string skillGroupName = "BossSkill";
    [Tooltip("技能点前多少秒进入起手前霸体(默认 1 秒):这段窗口内重击让位,且技能不被普攻/受击打断")]
    [SerializeField] private float preCastGuardLead = 1f;

    [Header("普攻填充")]
    [Tooltip("普攻冷却(秒):一次普攻发起后到下次可发起的最短间隔")]
    [SerializeField] private float attackCooldown = 4f;
    [Tooltip("取不到 Attack 动画片段时的兜底普攻完整时长(秒),只用于「临点停手」判定")]
    [SerializeField] private float fallbackMeleeDuration = 1f;

    [Header("临点拉距")]
    [Tooltip("后退时与玩家的水平距离达到此值即停(米)")]
    [SerializeField] private float backoffDistance = 1.5f;
    [Tooltip("后退速度(仅后退启动时打一条日志用;实际位移仍走 boss.moveInput → 基类 Move,不改移动系统)")]
    [SerializeField] private float backoffSpeed = 4f;

    // ============================================================
    // 运行时状态
    // ============================================================

    /// <summary>技能袋装随机(空池 Draw 返回 -1);跨音乐圈续用,只在技能池长度变化时重建</summary>
    private readonly ShuffleBag _bag = new ShuffleBag(0);

    /// <summary>建袋时的技能池长度(-1 = 还没建过;池变化 → 重建)</summary>
    private int _bagSkillCount = -1;

    /// <summary>本圈已消费的技能点时刻(-1 = 还没消费;防同一标点重复释放,同时是「本圈点已放完」的判据)</summary>
    private float _consumedPoint = -1f;

    /// <summary>待放技能点时刻(-1 = 无):由 SyncPendingPoint 每帧记录/更新,到点那一帧被消费</summary>
    private float _pendingPoint = -1f;

    /// <summary>上一帧的音乐时钟(检测回绕/重定位)</summary>
    private float _lastTrackTime;

    /// <summary>时钟倒退超过此值视为音轨回绕/重定位 → 清本圈消费记录</summary>
    private const float WrapBackwardThreshold = 0.5f;

    /// <summary>下次可普攻的时刻(Time.time 口径,由本组件自己记)</summary>
    private float _nextMeleeAt;

    /// <summary>普攻动画完整时长缓存(>0 = 已解析)</summary>
    private float _meleeFullDuration = -1f;
    private bool _meleeDurationResolved;

    /// <summary>是否正在临点后退(朝向锁 + 背离移动)</summary>
    private bool _backingOff;

    /// <summary>起手前霸体标记当前是否已置真(避免每帧重复写 boss)</summary>
    private bool _preCastGuardOn;

    private const string MeleeClipName = "Attack";

    // ============================================================
    // 公开门控(供 ChaseState / 普攻发起使用)
    // ============================================================

    /// <summary>
    /// 普攻动画完整时长(前摇 + 攻击段):从 boss 的 runtimeAnimatorController.animationClips 里
    /// 按名优先取 "Attack",其次取名字含 Attack 且不含 Heavy 的片段;取不到用 fallbackMeleeDuration。
    /// 只在解析成功时缓存(动画器/控制器还没就绪时下次再试),解析成功后不再遍历片段。
    /// </summary>
    public float MeleeFullDuration
    {
        get
        {
            if (!_meleeDurationResolved)
            {
                float d = ResolveMeleeFullDuration();
                if (d > 0f)
                {
                    _meleeFullDuration = d;
                    _meleeDurationResolved = true;
                }
            }
            return _meleeDurationResolved ? _meleeFullDuration : Mathf.Max(0.05f, fallbackMeleeDuration);
        }
    }

    /// <summary>
    /// 距待放技能点秒数(-1 = 无技能点 / 本圈已放完)。语义:
    ///   · 待放点在未来 → 正值;
    ///   · 待放点已到点但还没被 Update 释放(同一帧内) → 0,压住普攻(避免技能点与普攻抢同一帧);
    ///   · 本圈的点都放完(含末点之后到音乐回绕的空档) → -1,让普攻正常填充空档。
    /// </summary>
    public float TimeToNextSkillPoint
    {
        get
        {
            float point = SyncPendingPoint();
            if (point < 0f) return -1f;

            var mgr = MusicPointManager.Instance;
            if (mgr == null) return -1f;

            float toNext = point - mgr.TrackTime;
            return toNext > 0f ? toNext : 0f;
        }
    }

    /// <summary>能否发起一次普攻:无技能点,或剩余时间够放完一整套普攻动画(规格 §二 第 9 条)</summary>
    public bool CanStartMelee
    {
        get
        {
            float toNext = TimeToNextSkillPoint;
            return toNext < 0f || toNext >= MeleeFullDuration;
        }
    }

    /// <summary>后退时的移动输入(-1 / +1,背离玩家);非后退状态为 0</summary>
    public float BackoffMoveInput { get; private set; }

    // ============================================================
    // 生命周期
    // ============================================================

    private void Awake()
    {
        if (boss == null) boss = GetComponentInParent<BossControllerBase>();
        if (skillSlots == null) skillSlots = GetComponentInParent<BossSkillSlots>();
        if (defaultMelee == null) defaultMelee = GetComponentInParent<EnemyMeleeAttack>();
    }

    private void OnDisable()
    {
        // 组件被禁用时释放朝向锁(moveInput 由状态机负责清,这里只解自己上的锁)
        StopBackoff();
        SetPreCastGuard(false);
    }

    // ============================================================
    // 技能点调度(每帧一次浮点比较,不做任何运行时查找)
    // ============================================================

    private void Update()
    {
        if (boss == null || boss.IsDead || !boss.IsActivated) return;
        if (skillSlots == null) return;

        float point = SyncPendingPoint();
        UpdatePreCastGuard(point);              // 点临近 → 起手前霸体(防被普攻打断)
        if (point < 0f) return;                 // 本曲没配该组 / 组内点本圈已放完
        if (point == _consumedPoint) return;    // 防御:已消费的点不再重复释放

        var mgr = MusicPointManager.Instance;
        if (mgr == null || mgr.TrackTime < point) return;   // 还没到点

        // 到点即消费:丢点也不排队、不顺延(规格 §二 第 7 条)
        _consumedPoint = point;

        if (skillSlots.IsExecuting)
        {
            Debug.Log($"[BossAttackDirector] 技能点 {point:F2}s 到达但技能执行中 → 丢点");
            return;
        }
        if (boss.IsHeavyActive)
        {
            SetPreCastGuard(false);   // 重击优先:不由技能霸体锁住受击
            Debug.Log($"[BossAttackDirector] 技能点 {point:F2}s 到达但重击中 → 丢点");
            return;
        }

        CastSkill();
    }

    /// <summary>
    /// 同步「待放技能点」并返回它(-1 = 无)。Update 与 TimeToNextSkillPoint 都用这一份状态,
    /// 所以与组件执行顺序无关(ChaseState 先跑也不会误判)。
    /// 取点用「组内标点数组 + 精确比较」而不是 NextPointInGroup:后者内部带 ±0.001 容差
    /// (标点一旦不满足 p &gt; t + 0.001 就跳到下一个点),若某一帧正好落在 [p-0.001, p] 这 1ms 里,
    /// 该点永远不会被查询返回 → 整点丢失(60fps 下约 6%/点)。组名查表走 MusicTrackData.GetGroup
    /// (与 PlayerBeatJudge / BossSkill_Orb 同一套公开 API,不是运行时 Find)。
    /// 记法:先把即将到来的点记下来,等时钟追上它的那一帧释放(精度 = 1 帧,不提前)。
    /// </summary>
    private float SyncPendingPoint()
    {
        var mgr = MusicPointManager.Instance;
        if (mgr == null)
        {
            _pendingPoint = -1f;
            return -1f;
        }

        float t = mgr.TrackTime;

        // 音轨回绕/重定位(时钟明显倒退)→ 清本圈记录,否则单点组第二圈会被上一圈的消费记录吃掉
        if (t + WrapBackwardThreshold < _lastTrackTime)
        {
            _consumedPoint = -1f;
            _pendingPoint = -1f;
        }
        _lastTrackTime = t;

        if (_pendingPoint == _consumedPoint) _pendingPoint = -1f;   // 已消费的点不再是待放点
        if (_pendingPoint >= 0f && t >= _pendingPoint) return _pendingPoint;   // 已到点、等 Update 释放

        var points = GetGroupPoints(mgr, skillGroupName);
        if (points.Length == 0)
        {
            _pendingPoint = -1f;
            return -1f;
        }

        float upcoming = -1f;
        for (int i = 0; i < points.Length; i++)
        {
            if (points[i] > t) { upcoming = points[i]; break; }
        }

        if (upcoming < 0f)              // 本圈的点都过了(还没回绕):本圈不再有技能点,等 Wrap 重置
        {
            _pendingPoint = -1f;
            return -1f;
        }

        if (upcoming != _consumedPoint) _pendingPoint = upcoming;
        return _pendingPoint;
    }

    /// <summary>当前曲该组的标点数组(未配置该组/无点 → 空数组)</summary>
    private static float[] GetGroupPoints(MusicPointManager mgr, string groupName)
    {
        var track = mgr != null ? mgr.CurrentTrack : null;
        var group = track != null ? track.GetGroup(groupName) : null;
        return group != null && group.points != null ? group.points : System.Array.Empty<float>();
    }

    /// <summary>到点释放技能:袋装随机抽一个 → 转身朝玩家(起播提前量 0) → 交给技能槽执行</summary>
    private void CastSkill()
    {
        EnsureBag();
        int index = _bag.Draw();
        if (index < 0) return;   // 技能池为空:静默跳过(配置问题,不刷屏)

        var player = boss.PlayerTarget;
        if (player != null)
        {
            float dir = player.position.x >= boss.transform.position.x ? 1f : -1f;
            boss.UpdateFacing(dir);
        }

        boss.SetPreCastGuard(true);    // 起手前先霸体:起手这一帧起就不吃普攻/受击打断
        _preCastGuardOn = true;
        boss.CancelHurtForSkill();     // 正在受击硬直 → 技能优先,先退出硬直再起手
        skillSlots.Execute(index);
    }

    /// <summary>技能池长度变化时重建袋(跨音乐圈续用,不因回绕刷新)</summary>
    private void EnsureBag()
    {
        int count = skillSlots.SkillCount;
        if (count == _bagSkillCount) return;

        _bagSkillCount = count;
        _bag.Reset(skillSlots.GetAvailableSkills());
    }

    // ============================================================
    // 普攻发起(ChaseState 调用)
    // ============================================================

    /// <summary>
    /// 请求一次普攻。返回 true = 已切进普攻状态。
    /// 拦:技能执行中 / 重击中 / 临近技能点(CanStartMelee 为假)/ 重击收尾间隔 / 本组件自己记的普攻冷却。
    /// </summary>
    public bool TryAttack()
    {
        if (boss == null || boss.IsDead || !boss.IsActivated) return false;
        if (skillSlots == null) return false;
        if (skillSlots.IsExecuting) return false;      // 技能施法中不放普攻
        if (boss.IsHeavyActive) return false;          // 重击霸体中不放普攻
        if (!CanStartMelee) return false;              // 临近技能点:停手(ChaseState 随后走后退分支)
        if (boss.IsMeleeIntervalActive) return false;  // 重击收尾给的普攻间隔仍生效
        if (Time.time < _nextMeleeAt) return false;    // 普攻冷却(本组件自己记)

        var state = boss.CreateAttackState();
        if (state == null) return false;               // 子类没配普攻状态:不发

        boss.Fsm.ChangeState(state);
        _nextMeleeAt = Time.time + attackCooldown;     // 冷却起点 = 真正发起普攻那一刻
        return true;
    }

    // ============================================================
    // 临点拉距(规格 §二 第 9 条后半)
    // ============================================================

    /// <summary>
    /// 临点拉距:距下一个技能点不足一次完整普攻动画、且玩家已在攻击范围内 → 停手并背离玩家后退一小段。
    /// 返回 true = 本帧由后退接管(boss.moveInput 应设为 BackoffMoveInput)。
    /// 退出:技能起播 / 技能点消失或到达(TimeToNextSkillPoint 不再落在窗口内) / 玩家不在范围内 / 距离够了。
    /// </summary>
    public bool TryHandleBackoff()
    {
        if (boss == null || boss.IsDead || !boss.IsActivated) { StopBackoff(); return false; }
        if (skillSlots != null && skillSlots.IsExecuting) { StopBackoff(); return false; }   // 技能已起播 → 让位
        if (boss.IsHeavyActive) { StopBackoff(); return false; }                            // 重击优先

        float toNext = TimeToNextSkillPoint;
        bool window = toNext > 0f && toNext < MeleeFullDuration;   // 剩余时间不足一次完整普攻动画
        if (!window || !boss.IsPlayerInBossAttackRange())
        {
            StopBackoff();
            return false;
        }

        var player = boss.PlayerTarget;
        if (player == null) { StopBackoff(); return false; }

        float dx = player.position.x - boss.transform.position.x;
        if (Mathf.Abs(dx) >= backoffDistance)   // 距离够了:停手站住(TryAttack 已被 CanStartMelee 拦住)
        {
            StopBackoff();
            return false;
        }

        float sign = dx >= 0f ? 1f : -1f;       // 玩家在右 → +1
        if (!_backingOff)
        {
            _backingOff = true;
            boss.SetFacingLocked(true, (int)sign);   // 锁朝向盯住玩家(后退不转身)
            Debug.Log($"[BossAttackDirector] 临近技能点(剩 {toNext:F2}s)→ 后退拉距(参考速度 {backoffSpeed:F1})");
        }

        BackoffMoveInput = -sign;               // 背离玩家
        return true;
    }

    /// <summary>退出后退:清输入 + 解锁朝向(重击霸体中不动它的锁,交给重击自己收尾)</summary>
    private void StopBackoff()
    {
        BackoffMoveInput = 0f;
        if (!_backingOff) return;

        _backingOff = false;
        if (boss != null && !boss.IsHeavyActive)
            boss.SetFacingLocked(false, 0);
    }

    /// <summary>
    /// 维护「起手前霸体」:待放技能点进入 preCastGuardLead(默认 1 秒)窗口内 → 置真,
    /// 本圈点放完 / 无该组 / 重击丢点 → 置假(2026-09-19 saika:释放技能前也要霸体,窗口 1 秒)。
    /// </summary>
    private void UpdatePreCastGuard(float pendingPoint)
    {
        if (boss == null) return;

        bool on = false;
        if (pendingPoint >= 0f)
        {
            var mgr = MusicPointManager.Instance;
            if (mgr != null)
            {
                float toNext = pendingPoint - mgr.TrackTime;
                on = toNext < preCastGuardLead;    // 含已到点但同帧还没释放(0)的情况
            }
        }

        if (on == _preCastGuardOn) return;
        _preCastGuardOn = on;
        boss.SetPreCastGuard(on);
    }

    /// <summary>清起手前霸体(带缓存复位,供内部直接调用)</summary>
    private void SetPreCastGuard(bool on)
    {
        _preCastGuardOn = on;
        if (boss != null) boss.SetPreCastGuard(on);
    }

    // ============================================================
    // 内部工具
    // ============================================================

    /// <summary>解析普攻完整时长;返回 ≤ 0 = 动画器/片段还没就绪(调用方下次再试)</summary>
    private float ResolveMeleeFullDuration()
    {
        var animator = boss != null ? boss.Animator : null;
        var controller = animator != null ? animator.runtimeAnimatorController : null;
        if (controller == null) return -1f;

        var clips = controller.animationClips;   // 属性只读一次
        if (clips == null || clips.Length == 0) return -1f;

        AnimationClip fallback = null;
        for (int i = 0; i < clips.Length; i++)
        {
            var clip = clips[i];
            if (clip == null) continue;

            string clipName = clip.name;
            if (clipName == MeleeClipName)
                return clip.length > 0f ? clip.length : -1f;   // 同名优先

            if (fallback == null && clipName.IndexOf("Attack", System.StringComparison.OrdinalIgnoreCase) >= 0
                && clipName.IndexOf("Heavy", System.StringComparison.OrdinalIgnoreCase) < 0)
                fallback = clip;   // 次选:名字含 Attack 且不是重击段
        }

        return fallback != null && fallback.length > 0f ? fallback.length : -1f;
    }
}
