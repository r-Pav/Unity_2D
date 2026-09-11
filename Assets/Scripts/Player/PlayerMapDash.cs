using System.Collections;
using UnityEngine;

/// <summary>
/// 地图元素冲刺执行器 —— S3/5:玩家在重音窗口按 F、且没有可背刺敌人时,
/// 瞬移到「地图元素」处的一个**无伤害位移**(赶路 + 填空档)。
///
/// 【无伤害位移 / 不进状态】本类**不新建任何状态类**,也不改 FSM:
///   位移纯粹是「瞬移 + 残影 + 特效 + 元素进 CD」,不造成伤害、不打断普攻语义。
///   F 分流(何时该冲元素、何时该背刺)是 S5(PlayerController)的职责,本类不碰输入。
///
/// 【复用既有链路,不重造】
///   · 位移复用 PlayerTeleport.TeleportTo(自带贴墙钳制 + 清速度 + 0.5s 无敌帧 + 传送事件);
///   · 残影复用 DashGhostTrail.StartTail(冲刺尾部延续那套,单例协程、重入安全);
///   · 特效复用 AttackVFXAnchor.PlayMapDash;
///   · 元素表现复用 MapDashPoint.HideJudgeRing / Consume(判定圈与 CD 淡色都在元素自己身上;
///     判定环的**预告**由 S6 MapDashIndicator 提前播,本类只负责触发时把它收掉);
///
/// 【缓落自实现一份】空中触发时给一小段小重力(飘落感),**不动 PlayerBackstabState 那份**:
///   单例协程 + 原始重力只记第一次 + OnDisable 兜底还原 —— 防「组件被禁/重入后重力永久变小」。
///
/// 挂点:玩家根物体(与 PlayerTeleport / PlayerController 同物体)。
/// </summary>
public class PlayerMapDash : MonoBehaviour
{
    // ============================================================
    // 序列化参数(全部 Inspector 可调,默认值即可用)
    // ============================================================

    [Header("落点与冲力")]
    [Tooltip("到达后给的冲刺方向水平冲力(米/秒):落点 = 元素位置本身,到达后把 x 速度设成 冲刺方向.x × 此值(一次性,之后交给玩家输入接管)")]
    [SerializeField] private float dashSpeed = 6f;

    [Header("表现")]
    [Tooltip("残影间隔(秒) — 直接喂 DashGhostTrail.StartTail")]
    [SerializeField] private float ghostInterval = 0.05f;

    [Header("空中缓落")]
    [Tooltip("缓落时长(秒):空中触发后小重力持续这么久,然后还原原重力")]
    [SerializeField] private float hoverDuration = 0.35f;

    [Tooltip("缓落期间的重力倍率(1 = 不变;0.3 = 明显飘落感)")]
    [SerializeField] private float hoverGravityScale = 0.3f;

    // ============================================================
    // 懒缓存(只在首次用到时取一次;未挂则 null 并判空跳过 —— 不影响其它逻辑)
    // ============================================================

    private PlayerController _pc;                 // 玩家根:取朝向 / 位置 / 刚体 / 是否着地
    private PlayerTeleport _teleport;             // 玩家根:真正执行瞬移(+贴墙钳制/无敌帧)
    private DashGhostTrail _ghostTrail;           // 可能在子物体(Anim):残影尾部延续
    private AttackVFXAnchor _vfx;                 // 可能在子物体(attack_VFX):冲刺特效/音效
    private bool _refsResolved;                   // 首次解析过就不再查(禁止每帧查找/轮询)

    /// <summary>主相机(Awake 一次性兜底 Camera.main)。供 S5 调
    /// MapDashPoint.TryFindNearest 时传入 —— 那时是**按键帧**,不能再查 Camera.main。
    /// 未取到(场景里没 MainCamera 标签)时为 null,调用方自行判空。</summary>
    private Camera _cam;

    // ============================================================
    // 缓落状态(单例协程 + 原始重力只记一次)
    // ============================================================

    private Coroutine _hoverRoutine;              // 当前缓落协程(重入时先停旧的再起新的)
    private float _savedGravity = -1f;            // <0 = 未记录;缓落结束/兜底还原后复位为 -1

    // ============================================================
    // 公开 API
    // ============================================================

    /// <summary>缓存的相机(可能为 null = 场景里没有 MainCamera)。S5 用,键帧调用即可,不要再查 Camera.main。</summary>
    public Camera CachedCamera => _cam;

    /// <summary>
    /// 对指定元素执行一次地图元素冲刺。成功返回 true,任何守卫不通过返回 false(调用方据此走原背刺/空挥路径)。
    /// 流程:空引用/就绪守卫 → 算推进方向(元素在玩家哪一侧,长度≈0 时按朝向兜底)
    ///      → 落点 = 元素(特效)位置本身 → TeleportTo → 给一次水平冲力(dashSpeed) → 残影 → 特效 → 元素收判定环 + 进 CD
    ///      → 空中触发则起缓落。全程无伤害、不进状态。
    /// </summary>
    /// <param name="point">目标地图元素(由 MapDashPoint.TryFindNearest 选出);null 或 CD 中直接失败</param>
    public bool TryExecute(MapDashPoint point)
    {
        // 1. 空引用守卫:没元素 / 元素还在 CD → 失败(不产生任何副作用)
        if (point == null || !point.IsReady)
            return false;

        EnsureRefs();

        // 2. 推进方向 = 玩家 → 元素;长度≈0(玩家与元素几乎重合)时用朝向的水平方向兜底,避免 normalized 出 NaN
        Vector2 origin = _pc != null ? (Vector2)_pc.transform.position : (Vector2)transform.position;
        Vector2 target = point.transform.position;
        Vector2 delta = target - origin;

        Vector2 dir;
        if (delta.sqrMagnitude < 0.0001f * 0.0001f)   // 长度 < 0.0001
        {
            int facing = _pc != null ? _pc.GetFacing() : 1;
            dir = Vector2.right * facing;
        }
        else
        {
            dir = delta.normalized;
        }

        // 3. 落点 = 元素(特效)位置本身,不做任何偏移 ——
        //    早先用「元素位置 + 推进方向 × 1.5」会把贴地图边缘的元素顶到界外(边界外没有 collider 可钳制),
        //    现在落在元素上,再用一次性水平冲力把玩家继续往前带(saika 2026-09-11 拍板)。
        Vector2 landing = target;

        Rigidbody2D rb = _pc != null ? _pc.GetRigidbody() : null;

        // 4. 位移:优先走 PlayerTeleport(内含贴墙钳制 + 清速度 + 无敌帧 + 传送事件);
        //    未挂该组件时直接写刚体位置兜底,保证「执行器可用」
        if (_teleport != null)
            _teleport.TeleportTo(landing);
        else if (rb != null)
            rb.position = landing;

        // 4b. 到达后给一次水平冲力(一次性、不衰减):方向 = 冲刺方向(玩家→元素)的 x 分量,大小 = dashSpeed。
        //     之后交给玩家输入接管 —— 按方向键就继续跑,不按就被移动逻辑收住。
        //     TeleportTo 已把速度清零,故这里直接赋值;只写 x,垂直分量保持不动。
        if (rb != null)
            rb.velocity = new Vector2(dir.x * dashSpeed, rb.velocity.y);

        // 5. 残影(重入安全:StartTail 内部先停旧协程)
        if (_ghostTrail != null)
            _ghostTrail.StartTail(ghostInterval);

        // 6. 玩家侧冲刺特效 + 音效
        if (_vfx != null)
            _vfx.PlayMapDash();

        // 7. 元素表现:判定环在预告期(S6 MapDashIndicator)就已经播出来了,触发瞬间 = 这个环被"用掉",
        //    所以这里**收环**(语义同背刺命中帧的 BackstabAimIndicator.HideRing:只收金色环,
        //    内外圈与常显粒子不动),再进 CD。顺序保持"先表现后 CD":Consume 会把内外圈一起变淡,
        //    收环要在它之前完成,免得表现与 CD 淡色叠在同一帧上互相解释不清。
        point.HideJudgeRing();
        point.Consume();

        // 8. 缓落判定:瞬移后同帧 grounded 还是旧值(HandleGroundCheck 在 Update 里刷),不能直接读 ——
        //    先 Physics2D.SyncTransforms() 把 collider 同步到刚体新位置(rb.position 赋值后 bounds 同帧未更新,
        //    否则接地射线还在用旧起点),再手动刷新一次接地检测(复用 CharacterBase.HandleGroundCheck,不另开射线),
        //    悬空才起缓落;落点贴地则正常落地。
        //    注意 IsGrounded() 是方法:PlayerController 用 new 隐藏了基类属性,必须带括号。
        if (_pc != null)
        {
            Physics2D.SyncTransforms();
            _pc.RefreshGroundCheck();
            if (!_pc.IsGrounded())
                BeginHover();
        }

        return true;
    }

    // ============================================================
    // 懒缓存
    // ============================================================

    /// <summary>首次用到时解析全部引用;没有再命中过第二次(避免每帧 GetComponent)。</summary>
    private void EnsureRefs()
    {
        if (_refsResolved)
            return;
        _refsResolved = true;

        _pc = GetComponent<PlayerController>();
        _teleport = GetComponent<PlayerTeleport>();
        _ghostTrail = GetComponentInChildren<DashGhostTrail>(true);   // 含 inactive:残影源可能在未激活子物体上
        _vfx = GetComponentInChildren<AttackVFXAnchor>(true);
    }

    // ============================================================
    // 生命周期
    // ============================================================

    /// <summary>只在这里兜底一次 Camera.main(按键路径里绝不再查)</summary>
    private void Awake()
    {
        if (_cam == null)
            _cam = Camera.main;
    }

    /// <summary>组件被禁用(或物体销毁)时兜底:协程会被 Unity 直接掐断,不还原会让重力永久停在 hoverGravityScale</summary>
    private void OnDisable()
    {
        RestoreHoverGravity();
    }

    // ============================================================
    // 空中缓落(自实现一份,与 PlayerBackstabState 那份互不干扰)
    // ============================================================

    /// <summary>起缓落:原始重力**只记第一次**(第二次重入若再记,会把 0.3 当成原始值记下来 → 恢复后重力永久变小);
    /// 单例协程,重入先停旧协程,防多个协程各自写重力互相覆盖。</summary>
    private void BeginHover()
    {
        Rigidbody2D rb = _pc != null ? _pc.GetRigidbody() : null;
        if (rb == null)
            return;
        if (hoverDuration <= 0f)
            return;

        if (_savedGravity < 0f)
            _savedGravity = rb.gravityScale;

        if (_hoverRoutine != null)
            StopCoroutine(_hoverRoutine);

        _hoverRoutine = StartCoroutine(HoverRoutine(rb));
    }

    /// <summary>缓落协程:切小重力 → 等 hoverDuration → 还原原重力并复位记录值。
    /// 用 unscaledDeltaTime:即使处于命中顿帧(timeScale=0)也能按时收尾,不会把重力留在小值上。</summary>
    private IEnumerator HoverRoutine(Rigidbody2D rb)
    {
        float restore = _savedGravity >= 0f ? _savedGravity : 1f;   // 兜底:记录缺失时按 Unity 默认重力还原
        rb.gravityScale = hoverGravityScale;

        float t = 0f;
        while (t < hoverDuration)
        {
            t += Time.unscaledDeltaTime;
            yield return null;
        }

        if (rb != null)
            rb.gravityScale = restore;

        _hoverRoutine = null;
        _savedGravity = -1f;
    }

    /// <summary>恢复原始重力并停掉缓落协程(OnDisable 兜底路径)。幂等:没有记录值(没缓落过/已收尾)时什么都不做。</summary>
    private void RestoreHoverGravity()
    {
        if (_hoverRoutine != null)
        {
            StopCoroutine(_hoverRoutine);
            _hoverRoutine = null;
        }

        if (_savedGravity >= 0f)
        {
            Rigidbody2D rb = _pc != null ? _pc.GetRigidbody() : null;
            if (rb != null)
                rb.gravityScale = _savedGravity;
            _savedGravity = -1f;
        }
    }
}
