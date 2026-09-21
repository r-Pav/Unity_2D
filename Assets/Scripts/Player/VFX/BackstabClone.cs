using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 连音背刺 clone 替身(规格 P7 / 看板 P6)—— 把每一刀的 Backstab 动画从本体剥离:
/// 本体只做位移与逻辑,动画由池化 clone 在本体落点完整播一遍;上一刀的 clone 留在原地自然播完,不被下一刀打断。
///
/// 结构:每个 clone = 世界根的 clone 根 + 玩家视觉复制(visualRoot = Player/"Anim" 物体的整棵副本,
/// 含 Animator + SpriteRenderer + 同一个 runtimeAnimatorController)。
/// 池:poolSize(2~6,Inspector 可调)= 单组最大连音点数;池在 Start 时一次性预生成(prewarmOnStart,关掉则首次 PlayAt 懒建),
///     之后循环复用,绝不每次 Instantiate;池满(刀数 > 池大小)时复用最早启动的那只(与 BackstabAimIndicator 环池同口径)。
///
/// 动画事件隔离(clone 侧转发,本体不触发):
///   clone 视觉根挂 BackstabCloneRelay,把 clip 的 OnBackstabHitFrame / OnBackstabEnd 送回 PlayerCombat 的
///   同一入口(PlayerCombat.OnBackstabHitFrame / OnBackstabEnd),链路与本体 AnimationRelay 完全一致 →
///   伤害/状态推进仍只发生在 PlayerBackstabState,clone 不承载任何业务逻辑。
///   本体侧做法选②:clone 播期内把本体 Animator.speed 置 0(不冻结则本体自己的 Backstab 状态会把同一事件再发一次
///   → 同一刀两次伤害结算)。选①(本体不进 Backstab 状态)需要改 PlayerBackstabState,不在本任务范围。
///   解冻条件是"无活跃 clone 且本体 IsBackstabbing 参数已为 false(状态已退出)":本体冻结时停在 Backstab 起始帧,
///   命中帧事件尚未消费,条件没清就解冻会把积压事件补发出来。
///
/// 本体显隐:PlayAt 隐藏本体 SpriteRenderer(含 Anim 下的那个);clone 全部回收 / StopAll / OnDisable /
///   OnDestroy 无条件恢复显示,不留隐影(另有 StuckBaseRecoverSeconds 兜底)。
///
/// 残影:背刺期把 DashGhostTrail 的取帧来源切成"最新活跃 clone"的 SpriteRenderer(clone 就是玩家视觉的完整副本,
///   本体此时不可见);只在 PlayAt / 回收时写一次,不做每帧 Find。
///
/// ── 编辑器接线(saika)──
/// visualRoot   → Player 下的 "Anim" 物体(带 Animator + SpriteRenderer)
/// baseSprite   → 上面那个 SpriteRenderer(留空自动取 visualRoot 上的)
/// baseAnimator → 留空自动取 visualRoot 上的 Animator
/// ghostTrail   → Player 上的 DashGhostTrail(留空自动在层级里找一次)
/// 挂载位置:Player 根(与 PlayerCombat 同物体,clone 事件靠 GetComponentInParent&lt;PlayerCombat&gt; 回到链路)
/// </summary>
public class BackstabClone : MonoBehaviour
{
    /// <summary>Backstab 状态名(与 Assets/Anim/Player/Player.controller 里 Base Layer 的 Backstab 状态同名;显示闸门按名字判定)</summary>
    private const string BackstabStateName = "Backstab";

    /// <summary>缩放换算的最小分母(局部缩放接近 0 时不除,直接用世界缩放)</summary>
    private const float ScaleEpsilon = 1e-4f;

    /// <summary>隐影兜底(秒):无活跃 clone 却因异常仍没恢复显示时,强制恢复(本体状态自身 2.5s 超时更早,正常不会走到)</summary>
    private const float StuckBaseRecoverSeconds = 3f;

    /// <summary>clone 池上限:单组最大连音点数可能远大于 6(实测 11 点组),池不够时前面的刀会被复用顶掉、动画被截断</summary>
    private const int MaxClonePool = 16;

    // ============================================================
    // 引用(编辑器拖)
    // ============================================================

    [Header("玩家视觉来源(编辑器拖)")]
    [Tooltip("玩家视觉根(Player 下 Anim 物体:Animator + SpriteRenderer);clone 按它整棵副本生成")]
    [SerializeField] private Transform visualRoot;

    [Tooltip("本体 SpriteRenderer(Anim 上那个);PlayAt 起隐藏,clone 全部回收后恢复。留空自动取 visualRoot 上的")]
    [SerializeField] private SpriteRenderer baseSprite;

    [Tooltip("本体 Animator;留空自动取 visualRoot 上的。clone 播期内置 speed=0,防本体重复触发动画事件")]
    [SerializeField] private Animator baseAnimator;

    [Tooltip("冲刺残影组件(Player 上);背刺期残影帧改从最新活跃 clone 取。留空自动在层级里找一次")]
    [SerializeField] private DashGhostTrail ghostTrail;

    // ============================================================
    // clone 池参数
    // ============================================================

    [Header("clone 池")]
    [Tooltip("池大小 2~16;数量 = 单组最大连音点数(与 BackstabAimIndicator 环池同口径)。不够时复用最早启动的那只,不新建")]
    [SerializeField, Range(2, MaxClonePool)] private int poolSize = 3;

    [Tooltip("兜底回收时长(秒,缩放时间):动画结束事件丢失时按此超时回收,防 clone 卡在落点不回收")]
    [SerializeField] private float recycleTimeout = 1.2f;

    [Tooltip("启动时就建好池(推荐):避免第一刀当场 Instantiate 造成卡顿;关掉则首次 PlayAt 懒建池")]
    [SerializeField] private bool prewarmOnStart = true;

    [Tooltip("clone 透明度系数(1 = 与本体同透明,越小替身越虚)。建池时乘到各 SpriteRenderer 的 alpha 上,不覆盖原素材 alpha")]
    [SerializeField, Range(0f, 1f)] private float cloneAlpha = 0.85f;

    [Tooltip("视觉延迟补偿(秒,默认 0):>0 = 画面整体往后挪这么多(动画内容与动画事件一起挪),用于补偿显示器/输出链路的固定延迟。动画位置按音乐时钟锁定后再减此值")]
    [SerializeField, Range(-0.2f, 0.2f)] private float visualLatencyOffset = 0f;

    // ============================================================
    // 运行时
    // ============================================================

    /// <summary>单个 clone 槽:根(世界根)+ 视觉副本 + 事件转发 + 回收计时</summary>
    private sealed class CloneSlot
    {
        public GameObject root;                    // clone 根(世界根:不随玩家移动,留在落点)
        public Transform visual;                   // 视觉副本(Anim 子树副本)
        public Animator animator;                  // 视觉副本上的 Animator(同一个 runtimeAnimatorController)
        public SpriteRenderer sprite;              // 视觉副本主 SpriteRenderer(残影取帧来源)
        public BackstabCloneRelay relay;           // 动画事件转发组件(挂在视觉副本 = Animator 同物体)
        public SpriteRenderer[] renderers;         // 视觉副本下全部渲染器(构建时缓存,禁每帧 Find)
        public bool[] rendererEnabledAtBuild;      // 各渲染器构建时的 enabled(恢复显示用,不改到原本关掉的)
        public bool active;                        // 在播
        public bool renderersVisible;              // 已显示(进 Backstab 状态后才显,防闪静态帧)
        public bool idleHold;                      // 动画已播出但视觉保留中(停在末帧当玩家替身,等下一刀替换)
        public long serial;                        // 启动序号(池满时按序号找最早那只)
        public float startTime;                    // 本刀开始时间(缩放时间)
        public float recycleAt = -1f;              // >=0 = 已收到结束事件,到点回收
        public int alignStep = -1;                 // [对位 debug] 本槽待补报的刀序(-1 = 不用报)
        public float alignPoint = -1f;             // [对位 debug] 本槽待补报的标点
        public float clockStart = -1f;             // 本刀起播的音乐时刻(<0 = 不按音乐时钟驱动,交给 Animator 自己走)
    }

    /// <summary>
    /// 本刀动画的「打击帧」时刻(秒)= Assets/Anim/Player/Backstab.anim 里动画事件 OnBackstabHitFrame 的时间
    /// (m_SampleRate 12、m_StopTime 0.5 → 1/12 = 0.0833s)。起播提前量、刀光延迟、诊断都以它为准:
    /// 起播 = 标点 − 本值 → 打击帧(视觉重音)与命中结算正好落在标点上。
    /// 改动画片段的打击帧时刻时这里同步改。
    /// </summary>
    public const float HitFrameSeconds = 1f / 12f;

    // ============================================================
    // [2026-09-21 对位 debug] 只记「这一刀的动画起始在音乐轴上的时刻」,退出时打一次汇总;诊断完整块删
    // ============================================================
    private struct AlignRow { public int step; public float point; public float diffMs; }
    private readonly List<AlignRow> _alignRows = new List<AlignRow>();
    private int _alignNeverShown;   // 没进 Backstab 状态、动画没演的刀数

    /// <summary>[2026-09-21 对位 debug] 一行:这一刀的动画起始(音乐轴)与标点的差。诊断完删。</summary>
    private void LogAlignAnim(int step, float point, bool sameFrame)
    {
        var mgr = MusicPointManager.Instance;
        if (mgr == null) return;
        float music = mgr.TrackTime;
        // 2026-09-21 档 B:起播已提前一个打击帧(StrikeLeadSeconds),所以真正要对标点的是「打击帧」时刻;
        // 差 = 打击帧 − 标点(这才是耳朵/眼睛感知的重音偏差),起播单列出来便于核对提前量。
        float hitAt = music + HitFrameSeconds;
        string pointLabel = point >= 0f ? $"{point:F3}" : "无";
        string diffLabel = point >= 0f ? $"差={(hitAt - point) * 1000f:+0.0;-0.0}ms" : "差=-";
        Debug.Log($"[对位] 动画 刀序={step} 标点={pointLabel} 起播={music:F3} 打击帧={hitAt:F3} {diffLabel} 进状态={(sameFrame ? "同帧" : "迟了")}");
        if (point >= 0f) _alignRows.Add(new AlignRow { step = step, point = point, diffMs = (hitAt - point) * 1000f });
    }

    /// <summary>[2026-09-21 对位 debug] 打一次动画侧汇总(PlayerBackstabState.OnExit 调);诊断完删。</summary>
    public void FlushBackstabAlignSummary()
    {
        if (_alignRows.Count == 0 && _alignNeverShown == 0) return;
        int n = _alignRows.Count, inRange = 0;
        float min = float.MaxValue, max = float.MinValue, sum = 0f;
        var sb = new System.Text.StringBuilder();
        foreach (var r in _alignRows)
        {
            if (Mathf.Abs(r.diffMs) <= 20f) inRange++;
            if (r.diffMs < min) min = r.diffMs;
            if (r.diffMs > max) max = r.diffMs;
            sum += r.diffMs;
            sb.Append($"{r.step}:{r.diffMs:+0;-0} ");
        }
        string stat = n > 0
            ? $"落在±20ms内={inRange}/{n} 最小差={min:+0.0;-0.0}ms 最大差={max:+0.0;-0.0}ms 平均差={(sum / n):+0.0;-0.0}ms 未演={_alignNeverShown} 逐刀(ms)={sb}"
            : $"没有演出动画的刀 未演={_alignNeverShown}";
        Debug.Log($"[对位] 汇总动画(差 = 打击帧 − 标点) 刀数={n} {stat}");
        _alignRows.Clear();
        _alignNeverShown = 0;
    }

    private readonly List<CloneSlot> _pool = new List<CloneSlot>();
    private bool _poolBuilt;
    private long _serial;                          // 启动序号自增(PlayAt 顺序)
    private CloneSlot _latest;                     // 最新活跃 clone(残影取帧来源)

    private PlayerCombat _combat;                  // 事件回到链路用(懒解析一次,不每帧 Find)
    private bool _baseSpriteHidden;                // 本体 SR 已隐藏(幂等:反复 PlayAt 不覆盖原始 enabled)
    private bool _baseSpriteOriginalEnabled = true;
    private bool _baseFrozen;                      // 本体 Animator 已冻结(speed=0)
    private float _baseAnimatorSpeed = 1f;
    private float _baseHiddenSince;                // 首次隐藏时刻(隐影兜底计时)
    private bool _ghostTrailResolved;              // 残影组件已找过(只找一次)
    private bool _warned;                          // 一次性配置告警(避免永久日志刷屏)

    /// <summary>是否还有在播的 clone(供状态判断:最后一刀是否还在演)</summary>
    public bool HasActive
    {
        get
        {
            for (int i = 0; i < _pool.Count; i++)
                if (_pool[i].active) return true;
            return false;
        }
    }

    // ============================================================
    // 生命周期
    // ============================================================

    private void Awake()
    {
        if (visualRoot != null)
        {
            if (baseSprite == null) baseSprite = visualRoot.GetComponent<SpriteRenderer>();
            if (baseAnimator == null) baseAnimator = visualRoot.GetComponent<Animator>();
        }
    }

    /// <summary>预建池:把 Instantiate 成本从"第一刀当帧"挪到场景启动(副本很轻:单 SpriteRenderer + Animator)</summary>
    private void Start()
    {
        if (prewarmOnStart && visualRoot != null) EnsurePool();
    }

    private void Update()
    {
        MusicPointManager clockMgr = MusicPointManager.Instance;   // 时基调取一次,循环内不再查

        for (int i = 0; i < _pool.Count; i++)
        {
            CloneSlot s = _pool[i];
            if (!s.active) continue;

            // ① 还没显示的活跃 clone:动画器确认真进了 Backstab 再显示。
            //    clone 刚 Instantiate 时渲染器上还是 prefab 的静态帧(clone 根激活那一帧),直接显示会闪一下旧姿态;
            //    确认前不发事件(动画器没进 Backstab 就不会触发任何 clip 事件),也没人看得见它。
            if (!s.renderersVisible)
            {
                if (IsPlayingBackstab(s.animator))
                {
                    SetSlotRenderersVisible(s, true);
                    if (s.alignStep >= 0)   // [2026-09-21 对位 debug] 迟了若干帧才显示的动画:这里补一行
                    {
                        LogAlignAnim(s.alignStep, s.alignPoint, false);
                        s.alignStep = -1;
                    }
                }
                else if (s.animator != null)
                {
                    // 自愈:状态机需要多跳(默认态 → Exit → Entry → Backstab)时补一次同步评估,
                    // deltaTime=0 不推进动画,所以不会产生额外帧或事件
                    s.animator.SetBool(AnimParams.IsBackstabbing, true);
                    s.animator.Update(0f);
                }
            }

            // ①.5 动画位置按音乐时钟锁定(2026-09-21 档 B / S3):
            //   目标位置 = 当前音乐时刻 − 本刀起播时刻 − 视觉延迟补偿,每帧把差值补进 Animator。
            //   只补正、不回退:动画贴不上时钟(掉帧、状态机迟一帧、瞬移那帧开销大)会被下一帧补回来,
            //   不靠自增 deltaTime 累;也不产生反向跨越,所以动画事件(命中帧/结束)不会被补发。
            //   clockStart < 0(拿不到音乐管理器的降级路径)= 不驱动,交回 Animator 自己走。
            if (s.clockStart >= 0f && clockMgr != null && s.animator != null && IsPlayingBackstab(s.animator))
            {
                float posTarget = Mathf.Max(0f, clockMgr.TrackTime - s.clockStart - visualLatencyOffset);
                AnimatorStateInfo info = s.animator.GetCurrentAnimatorStateInfo(0);
                float fix = posTarget - info.normalizedTime * info.length;
                if (fix > 0.0005f) s.animator.Update(fix);
            }

            // ② 回收:动画结束事件已收到 → 立即收(与本体原行为一致:结束事件即动画结束);
            //    事件丢失 → recycleTimeout 兜底,防 clone 卡在落点
            float deadline = s.recycleAt >= 0f ? s.recycleAt : s.startTime + recycleTimeout;
            if (Time.time >= deadline)
            {
                if (s.alignStep >= 0)   // [2026-09-21 对位 debug] 到回收都没进 Backstab 状态 = 这一刀没动画
                {
                    Debug.Log($"[对位] 动画 刀序={s.alignStep} 标点={(s.alignPoint >= 0f ? s.alignPoint.ToString("F3") : "无")} 起播=未演(到回收都没进 Backstab 状态) 打击帧=- 差=-");
                    _alignNeverShown++;
                    s.alignStep = -1;
                }
                RecycleSlot(s, holdVisible: true);   // 事件丢失兜底;本体还在等下一刀则保留末帧
            }
        }

        // ③ 收尾:画面上还有 clone(在播 或 停在末帧当替身)时本体继续隐藏,禁止双影;
        //    没有在播的 clone 时,状态已退 / 兜底超时就收掉替身,再恢复本体
        //    (本体 IsBackstabbing 条件已清才解冻,否则会把冻结期间积压的动画事件补发出来,见 RestoreBaseIfIdle)
        if (HasActive) return;

        if (HasIdleHold && (!IsBaseBackstabPending() || Time.time - _baseHiddenSince >= StuckBaseRecoverSeconds))
            ReleaseAllHolds();

        if (!HasVisibleClone()) RestoreBaseIfIdle();
    }

    private void OnDisable()
    {
        StopAll();   // 组件失活后没有 Update 兜底 → StopAll 无条件恢复显隐/冻结
    }

    private void OnDestroy()
    {
        // clone 在世界根(不随玩家销毁)→ 必须显式销毁,否则泄漏到场景(DashGhostTrail 同处理)
        for (int i = 0; i < _pool.Count; i++)
        {
            CloneSlot s = _pool[i];
            s.active = false;
            if (s.root != null) Destroy(s.root);
        }
        _pool.Clear();
        _latest = null;
        UnhideBaseSprite();
        UnfreezeBaseAnimator();
    }

    // ============================================================
    // 对外接口(供 PlayerBackstabState / PlayerController 调用)
    // ============================================================

    /// <summary>
    /// 在指定位置播一刀 Backstab:取一个空闲 clone(池满则复用最早启动的那只)放到 position 并朝 faceLeft,
    /// 从 0 播一遍完整 Backstab,播完自动回收;同时隐藏本体、把残影取帧来源切到该 clone。
    /// 空引用/未接线时安全跳过(本体不隐藏,状态照常走)。
    /// </summary>
    /// <param name="position">落点(玩家根的世界坐标:clone 视觉根带自身局部偏移,这里传玩家根位置即可对齐)</param>
    /// <param name="faceLeft">true = 朝左(与 CharacterBase.UpdateFacing:根 scale.x 取负)</param>
    /// <param name="clockStartMusic">本刀动画「位置 0」对应的音乐时刻(&lt; 0 = 拿当前音乐时刻当起播,即按键那刀)</param>
    public void PlayAt(Vector3 position, bool faceLeft, int diagStep = -1, float diagPoint = -1f, float clockStartMusic = -1f)
    {
        if (visualRoot == null)
        {
            WarnOnce("[BackstabClone] visualRoot 未接线(拖 Player 下的 Anim 物体)→ PlayAt 跳过。");
            return;
        }

        EnsurePool();
        if (_pool.Count == 0) return;

        CloneSlot slot = TakeSlot();

        // 2026-09-21 一刀一个:新一刀起播时,把其它还亮着的替身在播的收掉、停末帧的也一并释放 ——
        // 连音密集点(0.1s 间隔)下不再几只替身叠着演。空档里没有下一刀,所以"最后一刀"的末帧替身照旧保留
        // (防玩家在整个空档里消失)。
        for (int i = 0; i < _pool.Count; i++)
        {
            CloneSlot other = _pool[i];
            if (other != slot && other.active) RecycleSlot(other);
        }
        ReleaseAllHolds();

        slot.active = true;
        slot.idleHold = false;   // 该槽若正停在末帧当替身:本次接管,重新播这一刀
        slot.alignStep = -1;     // [2026-09-21 对位 debug] 清掉上一位残留
        slot.alignPoint = -1f;
        MusicPointManager mgrClock = MusicPointManager.Instance;
        slot.clockStart = clockStartMusic >= 0f ? clockStartMusic
                       : (mgrClock != null ? mgrClock.TrackTime : -1f);   // 按键那刀:以当前音乐时刻为起点
        slot.serial = ++_serial;
        slot.startTime = Time.time;
        slot.recycleAt = -1f;
        slot.renderersVisible = false;
        if (slot.root != null) slot.root.SetActive(true);
        slot.root.transform.SetPositionAndRotation(position, ResolveRootRotation());
        slot.root.transform.localScale = ComputeRootScale(faceLeft);

        // 先藏渲染器:确认进 Backstab 后才显(见 Update ①),防 clone 出生帧闪静态/默认姿态
        SetSlotRenderersVisible(slot, false);
        StartBackstab(slot);
        bool shownNow = IsPlayingBackstab(slot.animator);
        if (shownNow) SetSlotRenderersVisible(slot, true);

        // [2026-09-21 对位 debug] 这一帧就进 Backstab 状态 = 动画起始就是这一帧;没进则挂在本槽上,等 Update 真显示时补报
        if (diagStep >= 0)
        {
            if (shownNow) LogAlignAnim(diagStep, diagPoint, true);
            else { slot.alignStep = diagStep; slot.alignPoint = diagPoint; }
        }

        HideBase();                     // 本体隐藏 + 冻结(与 clone 不同时显示,也不重复触发事件)
        _latest = slot;                 // 残影取"最新活跃 clone"
        SetGhostSource(slot.sprite);
    }

    /// <summary>收起全部 clone 并恢复本体显示(状态退出 / 打断 / 死亡等全路径兜底调用,幂等)。</summary>
    public void StopAll()
    {
        for (int i = 0; i < _pool.Count; i++)
        {
            CloneSlot s = _pool[i];
            if (s.active) RecycleSlot(s);
        }
        ReleaseAllHolds();   // 末帧保留的替身一并收掉(状态退出/打断/死亡路径)
        _latest = null;
        SetGhostSource(null);

        // StopAll = 明确收尾语义:显隐与冻结都无条件恢复(规格:任何退出路径都不许留隐影)。
        // 调用方应在状态退出/打断/死亡路径调用(那时本体已清 IsBackstabbing,解冻不会补发动画事件)。
        UnfreezeBaseAnimator();
        UnhideBaseSprite();
    }

    // ============================================================
    // clone 动画事件回调(BackstabCloneRelay → 这里 → PlayerCombat)
    // ============================================================

    /// <summary>clone 命中帧 → 转回 PlayerCombat(与本体 AnimationRelay 同一入口:伤害/硬直/追击窗口都在状态里,clone 不碰业务)。</summary>
    public void NotifyHitFrame(int slotIndex)
    {
        ResolveCombat()?.OnBackstabHitFrame();
    }

    /// <summary>clone 动画结束 → ① 先转回 PlayerCombat 让状态退出(状态靠它结束,超时兜底更晚,不能被吞);
    /// ② 再回收该 clone(结束事件即动画结束,与本体原行为一致:不额外停留,避免本体已可见时 clone 还在演 → 双影)</summary>
    public void NotifyAnimEnd(int slotIndex)
    {
        ResolveCombat()?.OnBackstabEnd();

        if (slotIndex >= 0 && slotIndex < _pool.Count)
        {
            CloneSlot slot = _pool[slotIndex];
            if (slot.active) RecycleSlot(slot, holdVisible: true);   // 本体还在等下一刀 → 保留末帧当替身
        }
        if (!HasVisibleClone()) RestoreBaseIfIdle();
    }

    // ============================================================
    // 池
    // ============================================================

    /// <summary>首次调用时一次性预生成 poolSize 个 clone(之后全部复用,禁止每次 Instantiate)</summary>
    private void EnsurePool()
    {
        if (_poolBuilt) return;
        _poolBuilt = true;
        if (visualRoot == null) return;

        int size = Mathf.Clamp(poolSize, 2, MaxClonePool);
        for (int i = 0; i < size; i++)
        {
            // clone 根放世界根(不 SetParent):clone 必须留在自己的落点,不能随玩家移动
            var root = new GameObject($"BackstabClone_{i}");

            // 视觉副本:整棵 Anim 子树(Animator + SpriteRenderer + 同一个 controller)
            GameObject visual = Instantiate(visualRoot.gameObject, root.transform, false);
            visual.name = visualRoot.gameObject.name;

            // 视觉根自身缩放的 x 符号统一交给 clone 根(ComputeRootScale 按 faceLeft 重算),这里归一化到正
            Vector3 ls = visual.transform.localScale;
            visual.transform.localScale = new Vector3(Mathf.Abs(ls.x), ls.y, ls.z);

            // 只留视觉:剥离业务/驱动脚本与物理组件(见 StripNonVisual)
            StripNonVisual(visual);

            var relay = visual.AddComponent<BackstabCloneRelay>();
            relay.Bind(this, i);

            var slot = new CloneSlot
            {
                root = root,
                visual = visual.transform,
                animator = visual.GetComponent<Animator>(),
                relay = relay,
                renderers = visual.GetComponentsInChildren<SpriteRenderer>(true),
            };
            slot.sprite = visual.GetComponent<SpriteRenderer>();
            if (slot.sprite == null && slot.renderers.Length > 0) slot.sprite = slot.renderers[0];

            slot.rendererEnabledAtBuild = new bool[slot.renderers.Length];
            for (int r = 0; r < slot.renderers.Length; r++)
                slot.rendererEnabledAtBuild[r] = slot.renderers[r].enabled;

            if (slot.animator == null || slot.animator.runtimeAnimatorController == null)
                WarnOnce("[BackstabClone] visualRoot 上取不到 Animator/AnimatorController → clone 不会有动画表现。");

            ApplyCloneAlpha(slot);   // 替身整体压半透明(乘,不覆盖原素材 alpha)

            _pool.Add(slot);

            SetSlotRenderersVisible(slot, false);
            root.SetActive(false);   // 预生成即挂起,复用时再激活
        }
    }

    /// <summary>
    /// clone 只做视觉:剥离视觉副本上的全部 MonoBehaviour(Animator/SpriteRenderer/Transform 不是 MonoBehaviour,保留)。
    /// 视觉是"完整副本"的同时不把玩家的业务/驱动脚本带进来:AnimationRelay 留着会在 clone 事件转发之外再找一次
    /// PlayerCombat(clone 在世界根时取不到,但层级一变就双发),PlayerAnimation 会按玩家的 rb 速度改 clone 的 Speed 参数。
    /// 另剥离 Collider2D/Rigidbody2D:clone 不与物理/伤害交互。
    /// </summary>
    private static void StripNonVisual(GameObject visual)
    {
        MonoBehaviour[] scripts = visual.GetComponentsInChildren<MonoBehaviour>(true);
        for (int i = 0; i < scripts.Length; i++)
            if (scripts[i] != null) Destroy(scripts[i]);

        Collider2D[] colliders = visual.GetComponentsInChildren<Collider2D>(true);
        for (int i = 0; i < colliders.Length; i++)
            if (colliders[i] != null) Destroy(colliders[i]);

        Rigidbody2D[] bodies = visual.GetComponentsInChildren<Rigidbody2D>(true);
        for (int i = 0; i < bodies.Length; i++)
            if (bodies[i] != null) Destroy(bodies[i]);
    }

    /// <summary>取一个槽:优先空闲;全忙 → 复用最早启动的那只(与 BackstabAimIndicator 环池同口径,不新建)</summary>
    private CloneSlot TakeSlot()
    {
        for (int i = 0; i < _pool.Count; i++)
            if (!_pool[i].active) return _pool[i];

        CloneSlot oldest = _pool[0];
        for (int i = 1; i < _pool.Count; i++)
            if (_pool[i].serial < oldest.serial) oldest = _pool[i];

        RecycleSlot(oldest);   // 复用前先收干净(渲染器/动画器复位),再交给本次使用
        return oldest;
    }

    /// <summary>
    /// 回收:渲染器收起 → clone 根挂起 → 残影来源/本体显隐跟着更新(不销毁,池化复用)。
    /// holdVisible = true 且本体仍在背刺状态(正等下一刀)时不收视觉:clone 停在最后一帧继续当玩家替身 ——
    /// 防"两刀之间的空档里既没有 clone、本体又是隐藏的"(玩家会整个消失):本体在等下一刀期间的替身由它承担。
    /// 该槽下一次被 PlayAt 复用时自然被替换成新一刀。
    /// </summary>
    private void RecycleSlot(CloneSlot slot, bool holdVisible = false)
    {
        if (slot == null || !slot.active) return;
        slot.active = false;
        slot.recycleAt = -1f;

        if (holdVisible && slot.renderersVisible && IsBaseBackstabPending())
        {
            // 末帧保留:不隐藏渲染器、不挂起根、不复位动画器(动画停在最后一帧,继续显示)
            slot.idleHold = true;
        }
        else
        {
            slot.idleHold = false;
            SetSlotRenderersVisible(slot, false);

            if (slot.animator != null)
            {
                // 复位:清条件让状态机离开 Backstab,下次复用从干净状态 Rebind 起(本例先于挂起执行)
                slot.animator.SetBool(AnimParams.IsBackstabbing, false);
                slot.animator.Update(0f);
            }
            if (slot.root != null) slot.root.SetActive(false);
        }

        if (ReferenceEquals(_latest, slot))
        {
            _latest = FindLatestActive();
            SetGhostSource(_latest != null ? _latest.sprite : null);
        }
        if (!HasVisibleClone()) RestoreBaseIfIdle();
    }

    /// <summary>是否还有 clone 停在末帧继续显示(等下一刀来替换)</summary>
    private bool HasIdleHold
    {
        get
        {
            for (int i = 0; i < _pool.Count; i++)
                if (_pool[i].idleHold) return true;
            return false;
        }
    }

    /// <summary>是否还有 clone 在画面上(在播 或 停在末帧);true 时本体必须继续隐藏(禁止双影)</summary>
    private bool HasVisibleClone()
    {
        for (int i = 0; i < _pool.Count; i++)
        {
            CloneSlot s = _pool[i];
            if (s.active || s.idleHold) return true;
        }
        return false;
    }

    /// <summary>收掉一只"末帧保留"的 clone(状态已退 / 兜底超时 / StopAll)</summary>
    private void ReleaseHold(CloneSlot slot)
    {
        if (slot == null || !slot.idleHold) return;
        slot.idleHold = false;
        SetSlotRenderersVisible(slot, false);
        if (slot.animator != null)
        {
            slot.animator.SetBool(AnimParams.IsBackstabbing, false);
            slot.animator.Update(0f);
        }
        if (slot.root != null) slot.root.SetActive(false);
    }

    /// <summary>收掉全部"末帧保留"的 clone</summary>
    private void ReleaseAllHolds()
    {
        for (int i = 0; i < _pool.Count; i++) ReleaseHold(_pool[i]);
    }

    private CloneSlot FindLatestActive()
    {
        CloneSlot latest = null;
        for (int i = 0; i < _pool.Count; i++)
        {
            CloneSlot s = _pool[i];
            if (!s.active) continue;
            if (latest == null || s.serial > latest.serial) latest = s;
        }
        return latest;
    }

    // ============================================================
    // clone 动画驱动
    // ============================================================

    /// <summary>
    /// clone 从 0 播一遍 Backstab。走与本体同一套 Entry 路由(IsBackstabbing=true → Backstab 状态),
    /// 不用 anim.Play 直切(项目规则):Rebind 回控制器起点 → 置条件 → 同步评估。
    /// Update(0f) 循环最多 3 次,让状态机需要多跳(默认态 → Exit → Entry → Backstab)时在同帧内走完;
    /// 真进不去则渲染器不显示,由 recycleTimeout 兜底回收(不会出现半截/错帧的可见 clone)。
    /// </summary>
    private void StartBackstab(CloneSlot slot)
    {
        Animator anim = slot.animator;
        if (slot.relay != null) slot.relay.PrepareForPlay();   // 每刀事件复位(同槽复用第二刀要能再发一次)
        if (anim == null) return;

        anim.enabled = true;
        anim.speed = 1f;
        // 触发 Animator 懒初始化(物体刚实例化/刚激活时状态机句柄还没建,先读一次让它就绪,后面的 Rebind 才可靠)
        if (!anim.isInitialized) anim.GetCurrentAnimatorStateInfo(0);
        anim.Rebind();
        anim.SetBool(AnimParams.IsBackstabbing, true);
        for (int i = 0; i < 3 && !IsPlayingBackstab(anim); i++)
            anim.Update(0f);
    }

    /// <summary>当前是否停在/播着 Backstab 状态(按状态名判定;clone 与本体共用 Player.controller,状态名同源)</summary>
    private static bool IsPlayingBackstab(Animator anim)
    {
        if (anim == null || anim.runtimeAnimatorController == null) return false;
        return anim.GetCurrentAnimatorStateInfo(0).IsName(BackstabStateName);
    }

    // ============================================================
    // 位置/缩放/渲染器
    // ============================================================

    /// <summary>clone 根朝向 = 视觉根父级(玩家根)的世界旋转;2D 下恒为 identity,保留以兼容父级带旋转的摆法</summary>
    private Quaternion ResolveRootRotation()
    {
        Transform parent = visualRoot != null ? visualRoot.parent : null;
        return parent != null ? parent.rotation : Quaternion.identity;
    }

    /// <summary>
    /// clone 根缩放 = 视觉根世界缩放 ÷ 视觉根局部缩放(把玩家根的缩放层级补回来,y/z 连符号一起还原),
    /// x 的符号只由 faceLeft 决定 —— 与 CharacterBase.UpdateFacing 一致:朝向靠根 scale.x 正负(Animator 不认 flipX)。
    /// 视觉副本自身的局部缩放已在建池时归一化,所以这里算出的根缩放乘上去恰好等于视觉根的世界缩放。
    /// </summary>
    private Vector3 ComputeRootScale(bool faceLeft)
    {
        Vector3 ls = visualRoot.localScale;
        Vector3 ws = visualRoot.lossyScale;
        float rx = Mathf.Abs(ls.x) > ScaleEpsilon ? ws.x / ls.x : ws.x;
        float ry = Mathf.Abs(ls.y) > ScaleEpsilon ? ws.y / ls.y : ws.y;
        float rz = Mathf.Abs(ls.z) > ScaleEpsilon ? ws.z / ls.z : ws.z;
        return new Vector3(faceLeft ? -Mathf.Abs(rx) : Mathf.Abs(rx), ry, rz);
    }

    /// <summary>建池时一次性把 cloneAlpha 乘到各渲染器的 alpha 上(乘,不覆盖原本就半透明的部件)。
    /// 之后显示/回收只改 enabled,不动 color,系数不会被清掉。</summary>
    private void ApplyCloneAlpha(CloneSlot slot)
    {
        if (slot.renderers == null || Mathf.Approximately(cloneAlpha, 1f)) return;
        for (int i = 0; i < slot.renderers.Length; i++)
        {
            SpriteRenderer sr = slot.renderers[i];
            if (sr == null) continue;
            Color c = sr.color;
            c.a *= cloneAlpha;
            sr.color = c;
        }
    }

    /// <summary>按构建时的 enabled 恢复每个渲染器的可见性(显示时不强行打开原本关掉的渲染器)</summary>
    private static void SetSlotRenderersVisible(CloneSlot slot, bool visible)
    {
        slot.renderersVisible = visible;
        if (slot.renderers == null) return;
        for (int i = 0; i < slot.renderers.Length; i++)
        {
            SpriteRenderer sr = slot.renderers[i];
            if (sr == null) continue;
            sr.enabled = visible && (slot.rendererEnabledAtBuild == null || slot.rendererEnabledAtBuild[i]);
        }
    }

    // ============================================================
    // 本体显隐 / 冻结
    // ============================================================

    /// <summary>隐藏本体 SpriteRenderer 并冻结本体 Animator(幂等:反复 PlayAt 只记一次原始状态)</summary>
    private void HideBase()
    {
        if (!_baseSpriteHidden)
        {
            if (baseSprite != null)
            {
                _baseSpriteOriginalEnabled = baseSprite.enabled;
                baseSprite.enabled = false;
            }
            _baseSpriteHidden = true;
            _baseHiddenSince = Time.time;
        }
        FreezeBaseAnimator();
    }

    private void UnhideBaseSprite()
    {
        if (!_baseSpriteHidden) return;
        if (baseSprite != null) baseSprite.enabled = _baseSpriteOriginalEnabled;
        _baseSpriteHidden = false;
    }

    /// <summary>
    /// clone 播期内冻结本体 Animator(speed=0)。理由:本体若按 Entry 路由进了 Backstab 状态,会自己触发
    /// OnBackstabHitFrame/OnBackstabEnd,与 clone 转发形成"同一刀两次伤害结算";选①(本体不进 Backstab 状态)
    /// 需要改 PlayerBackstabState,不在本任务范围。冻结紧跟在 clone 启动之后(PlayAt 内)。
    /// </summary>
    private void FreezeBaseAnimator()
    {
        if (_baseFrozen || baseAnimator == null) return;
        _baseAnimatorSpeed = baseAnimator.speed;
        baseAnimator.speed = 0f;
        _baseFrozen = true;
    }

    /// <summary>恢复本体 Animator 速度(解冻资格由调用方判定:见 RestoreBaseIfIdle / StopAll)</summary>
    private void UnfreezeBaseAnimator()
    {
        if (!_baseFrozen) return;
        if (baseAnimator != null) baseAnimator.speed = _baseAnimatorSpeed;
        _baseFrozen = false;
    }

    /// <summary>本体是否还处在背刺条件里(IsBackstabbing 参数仍为 true = 状态没退出):
    /// 此时解冻会让本体 Backstab clip 继续推进 → 把冻结期间积压的命中帧/结束事件补发出来(同一刀两次结算)。
    /// 判参数而不判"动画器当前状态名":状态退出会立刻清参数,但状态机转移要到动画器下一次 update 才生效,
    /// 判参数能让本体在状态退出的当帧就恢复显示,不必白等 1~2 帧隐影。</summary>
    private bool IsBaseBackstabPending()
    {
        return baseAnimator != null && baseAnimator.GetBool(AnimParams.IsBackstabbing);
    }

    /// <summary>
    /// 无活跃 clone 时的自动收尾:本体已退出背刺条件 → 解冻 + 恢复显示(同帧完成)。
    /// 本体条件还在(状态未退,例如正等下一刀)→ 继续隐藏 + 冻结,这是连音间隔期的正常态;
    /// 异常卡住超过 StuckBaseRecoverSeconds 则强制恢复,防"玩家永久隐形"(本体状态自身 MaxBackstabDuration 2.5s
    /// 更短,正常永远走不到这条兜底)。
    /// </summary>
    private void RestoreBaseIfIdle()
    {
        if (HasVisibleClone()) return;                            // 画面上还有 clone(在播或停在末帧)→ 本体继续隐藏(禁止双影)
        if (IsBaseBackstabPending() && Time.time - _baseHiddenSince < StuckBaseRecoverSeconds)
            return;                                               // 状态未退:继续隐藏 + 冻结

        UnfreezeBaseAnimator();
        UnhideBaseSprite();
    }

    // ============================================================
    // 依赖解析
    // ============================================================

    /// <summary>事件回到 PlayerCombat(与本体 AnimationRelay 同一入口);懒解析一次,不做每帧 Find</summary>
    private PlayerCombat ResolveCombat()
    {
        if (_combat == null) _combat = GetComponentInParent<PlayerCombat>();
        return _combat;
    }

    /// <summary>残影取帧来源切到 clone(背刺期本体隐藏,残影必须从 clone 取帧,否则残影是空/旧帧);
    /// 只在 PlayAt / 回收时写一次值,不做每帧查找</summary>
    private void SetGhostSource(SpriteRenderer sr)
    {
        if (!_ghostTrailResolved)
        {
            _ghostTrailResolved = true;
            if (ghostTrail == null) ghostTrail = GetComponentInChildren<DashGhostTrail>(true);
        }
        if (ghostTrail != null) ghostTrail.SetCloneSource(sr);
    }

    private void WarnOnce(string message)
    {
        if (_warned) return;
        _warned = true;
        Debug.LogWarning(message, this);
    }
}

/// <summary>
/// clone 侧动画事件转发组件 —— 挂在 clone 视觉副本根(= Animator 同物体;动画事件只派发到 Animator 所在物体的组件)。
/// 由 BackstabClone 运行时 AddComponent(与 BackstabClone 同文件:Unity 只要求"能在 Inspector 挂"的类名与文件名一致,
/// 运行时 AddComponent 不受此限),因此不需要编辑器挂载。
/// Backstab clip 的 3 个事件全部实现:缺任何一个 Unity 会打印 "AnimationEvent ... has no receiver" 警告。
/// 每次开播前 PrepareForPlay 复位,保证"每刀只触发一次"(防 clip 循环/状态重进造成重复结算)。
/// </summary>
public class BackstabCloneRelay : MonoBehaviour
{
    private BackstabClone _owner;
    private int _slotIndex = -1;
    private bool _hitForwarded;   // 本刀命中帧已转(每刀只转一次)
    private bool _endForwarded;   // 本刀结束事件已转

    /// <summary>绑定宿主与池槽下标(下标用于结束时回收对应 clone)</summary>
    public void Bind(BackstabClone owner, int slotIndex)
    {
        _owner = owner;
        _slotIndex = slotIndex;
    }

    /// <summary>每次开播前复位(同一槽复用第二刀时事件要能再发一次)</summary>
    public void PrepareForPlay()
    {
        _hitForwarded = false;
        _endForwarded = false;
    }

    /// <summary>命中帧 → 宿主 → PlayerCombat(伤害结算仍在 PlayerBackstabState)</summary>
    public void OnBackstabHitFrame()
    {
        if (_hitForwarded) return;
        _hitForwarded = true;
        if (_owner != null) _owner.NotifyHitFrame(_slotIndex);
    }

    /// <summary>动画结束 → 宿主 → PlayerCombat(状态退出)并回收本 clone</summary>
    public void OnBackstabEnd()
    {
        if (_endForwarded) return;
        _endForwarded = true;
        if (_owner != null) _owner.NotifyAnimEnd(_slotIndex);
    }

    /// <summary>Backstab clip 的输入门事件(0.25s)。clone 没有战斗输入语义,显式空实现 ——
    /// 不实现会报 "AnimationEvent 'OnAttackInputOpen' ... has no receiver" 警告;也不能转给 PlayerCombat
    /// (那会让 clone 去打开/消费本体的攻击输入门,违反"clone 不承载业务逻辑")。</summary>
    public void OnAttackInputOpen() { }
}
