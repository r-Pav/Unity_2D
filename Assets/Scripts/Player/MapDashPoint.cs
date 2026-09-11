using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 地图元素(常驻点位)组件 —— S2/5:元素自身(注册表 + CD + 表现 + 最近元素查询)。
///
/// 【无 collider】元素是场景里常驻的、**不带任何 collider** 的点位,
///   所以不走物理查询(不靠 Trigger / Overlap),而是:Awake 进静态注册表、OnDestroy 出表,
///   查询用静态方法 TryFindNearest 直接遍历注册表(只在"按键那一帧"调用,不是每帧)。
///
/// 【CD 表现 + 惰性还原】被冲刺消费过的元素各自独立进 cdSeconds 秒 CD:
///   · 期间「常显粒子 idleParticles」**和「判定圈 judgeRing 的内外圈」用同一套口径一起变淡**
///     (降 alpha + 去饱和),表示这个点暂时不可用;金色判定环不参与变淡、CD 期间也不播
///     (未就绪的元素根本不会被选中,ShowJudgeRing 另有一层 IsReady 双保险);
///   · 到点**不写 Update/FixedUpdate 轮询**,而是惰性还原 —— 任一公开入口
///     (Consume / ShowJudgeRing / TryFindNearest / OnEnable)判到"时间已过 + 当前仍是淡色",
///     才把 Awake 缓存的原 startColor 写回(三个目标一起)。零协程、零每帧开销。
///
/// 【判定圈与敌人同款】judgeRing 就是敌人身上那个 BackstabAimIndicator。
///   S6 起改为**预告驱动**:MapDashIndicator 在重音点前 leadSeconds 调 ShowJudgeRing(剩余秒数)提前播环
///   (环缩到判定外环的时刻 = 该重音点),冲刺触发时 HideJudgeRing 按「命中帧收环」口径只收金色环;
///   元素自己不复刻这套动画。CD 淡色只借它的内外圈 ParticleSystem 引用(通过 RangeOuter / RangeInner 只读属性拿),
///   不动它的任何行为与状态。
///
/// 本类只负责"元素自身":冲刺执行是 S3(PlayerMapDash)、F 分流是 S5(PlayerController)。
/// </summary>
public class MapDashPoint : MonoBehaviour
{
    // ============================================================
    // 序列化引用 / 参数(全部 Inspector 拖好,禁止运行时按名查找)
    // ============================================================

    /// <summary>常显粒子(元素默认外观的一部分;CD 期间与判定圈内外圈一起变淡)。引用型字段显式 = null:
    /// 与默认值等价、不影响序列化,但能让新文件不引入 CS0649「从未赋值」噪音(保持 warning 基线干净)。</summary>
    [Tooltip("常显粒子(元素默认外观;被冲刺后与判定圈内外圈一起变淡,CD 结束自动还原)")]
    [SerializeField] private ParticleSystem idleParticles = null;

    /// <summary>判定圈(与敌人身上同款 BackstabAimIndicator);触发瞬间播一次收缩环。
    /// CD 期间它的内外圈粒子(rangeOuter / rangeInner)也跟着一起变淡;金色判定环不参与。</summary>
    [Tooltip("判定圈(与敌人身上同款 BackstabAimIndicator);触发时 Show(0, ringWindowSeconds) 播一次收缩环;CD 期间其内外圈一起变淡")]
    [SerializeField] private BackstabAimIndicator judgeRing = null;

    /// <summary>CD 秒数:被冲刺后要等这么久才能再次被选中(各元素独立计时)</summary>
    [Tooltip("CD 秒数(被冲刺后各自独立计时)")]
    [SerializeField] private float cdSeconds = 8f;

    /// <summary>判定圈收缩时长(秒)= Show 的 windowSeconds(触发时播一次)</summary>
    [Tooltip("判定圈收缩时长(秒)")]
    [SerializeField] private float ringWindowSeconds = 0.3f;

    /// <summary>元素本体精灵(CD 期间整块变灰)。留空时 Awake 从自身取一次 SpriteRenderer;
    /// 都取不到 = 这一层表现跳过,不影响 CD 逻辑。</summary>
    [Tooltip("元素本体精灵(CD 期间变灰);留空时自动取自身 SpriteRenderer")]
    [SerializeField] private SpriteRenderer bodySprite = null;

    // ============================================================
    // 注册表(元素无 collider → 不做物理查询,只维护这张表)
    // ============================================================

    /// <summary>全部存活元素;只增删于 Awake / OnDestroy,查询时按需遍历</summary>
    private static readonly List<MapDashPoint> All = new List<MapDashPoint>();

    // ============================================================
    // 常量 / 运行时状态
    // ============================================================

    /// <summary>CD 变灰的 alpha 缩放比例(1 = 不降透明;0.35 = 明显变淡)——
    /// 现取 1:CD 表现已从「变淡」改成「整块变灰」(saika 2026-09-11)。</summary>
    private const float CdAlphaScale = 1f;

    /// <summary>CD 变灰的向灰度混合比例(0 = 保持原色;1 = 全灰)——
    /// 现取 1:直接设成灰色,一眼看出该元素当前不可用。</summary>
    private const float CdGrayBlend = 1f;

    /// <summary>CD 期间元素本体精灵的灰阶(0 = 全黑,1 = 原色;0.45 = 明显变灰但还看得见轮廓)</summary>
    private const float CdSpriteGrayScale = 0.45f;

    private float _readyAt;                       // 可再次被选中的最早时刻(初始 0 = 默认就绪)

    private Color _spriteOriginal;                // 本体精灵原色(首次变灰时缓存,不在 Awake 读:引用可能靠 Awake 兜底才拿到)
    private bool _spriteOriginalCached;

    /// <summary>淡变目标 = idleParticles + judgeRing 的内外圈(空引用在收集时跳过)。
    /// 三个目标共用同一套淡色口径(CdAlphaScale / CdGrayBlend),不做任何差异化。</summary>
    private ParticleSystem[] _dimTargets;

    /// <summary>与 _dimTargets 同下标的原 startColor(Awake 缓存,CD 期间只读缓存;
    /// 类型是嵌套的 UnityEngine.ParticleSystem.MinMaxGradient,不是顶层类型)</summary>
    private ParticleSystem.MinMaxGradient[] _dimOriginals;

    private bool _dimmed;                         // 当前是否处于 CD 淡色态(惰性还原的判据)

    // ============================================================
    // 公开 API
    // ============================================================

    /// <summary>是否就绪(未被 CD 锁住);_readyAt 初始为 0,即默认就绪</summary>
    public bool IsReady => Time.time >= _readyAt;

    /// <summary>被冲刺消费:进入 cdSeconds 秒 CD + 常显粒子与判定圈内外圈一起变淡。
    /// 到点不轮询,由任意入口的惰性还原恢复原色(见 RestoreVisualIfRecovered)。</summary>
    public void Consume()
    {
        RestoreVisualIfRecovered();      // 惰性还原入口之一:上一轮 CD 恰好在本次访问前结束 → 先还原再重新计时
        _readyAt = Time.time + cdSeconds;
        ApplyDim();
    }

    /// <summary>[S6] 预告播环:在「距重音点还有 secondsToPoint 秒」时就把判定圈收缩环播出来 ——
    /// 环缩到判定外环的时刻正好 = 该重音点(BackstabAimIndicator 内部按 lead 反推起点/隐藏时长)。
    /// 只负责"播一下",环自己的收尾由 BackstabAimIndicator 协程处理,被用掉时由 HideJudgeRing 收。
    /// CD 中不播(双保险:S6 的预告器只在元素就绪时才可能被选中,这里再挡一层,
    /// 保证「CD 表现 = 常显粒子 + 内外圈变淡 + 判定圈不播」在任何调用顺序下都成立)。</summary>
    /// <param name="secondsToPoint">距重音点的剩余秒数(0 = 就是现在,&gt;0 = 提前预告;由 MapDashIndicator 喂 mgr 的只读时间查询)</param>
    public void ShowJudgeRing(float secondsToPoint)
    {
        RestoreVisualIfRecovered();      // 惰性还原入口之一
        if (!IsReady) return;            // 未就绪不播环
        // 第一个参数 = 提前量(不再是固定 0):环从 ringStartRadius 缩到判定外环用时 = lead,
        //   secondsToPoint >= lead → 环先隐着,到 (点 - lead) 才以固定起点开缩;剩余不足则从当前剩余反推起点立即缩,
        //   两种情况环到判定外环的时刻恒 = 该重音点。
        // 第二个参数仍是本元素的 ringWindowSeconds(窗口时长):环缩过判定外环后继续缩到内环、
        //   再到 0 收尾。注意它应与当前曲的 mgr.WindowSeconds 一致(元素判定窗口口径),默认值已对齐。
        judgeRing?.Show(secondsToPoint, ringWindowSeconds);
    }

    /// <summary>[S6] 收环:冲刺触发瞬间把这个元素的金色判定环收掉 —— 环在预告期已经播过,触发 = 它被"用掉"了
    /// (语义同背刺命中帧的 BackstabAimIndicator.HideRing)。**只收金色环(池下标 0 = Show 分配的那只)**,
    /// 内外圈与常显粒子一律不动:那两个是元素的常显外观,由 CD 淡色负责,收了会让元素看起来凭空消失。
    /// 幂等(没预告过 / 环已自然缩完时调用无副作用),CD 判定无关(收环不是播环,不受 IsReady 限制)。</summary>
    public void HideJudgeRing()
    {
        judgeRing?.HideRing(0);
    }

    /// <summary>在注册表里找「就绪 + 在视口内(含 viewportMargin)」的最近元素 —— 口径与背刺目标分配一致:
    /// **不判朝向**(背刺的目标也可以在玩家身后),只按视口 + 距离取最近。
    /// 只在按键那一帧 / 预告那一帧被调用(调用方只在 Awake 缓存一次主相机,不在这些路径里查 Camera),
    /// 所以直接遍历注册表、不维护额外缓存表。没找到 → 返回 false 且 point = null。</summary>
    /// <param name="origin">查询原点(玩家位置)</param>
    /// <param name="cam">用于视口判定的摄像机(调用方传入)</param>
    /// <param name="viewportMargin">视口外扩余量(viewport 坐标 0~1,允许 -margin ~ 1+margin)</param>
    /// <param name="point">命中的最近元素;无候选时为 null</param>
    public static bool TryFindNearest(Vector2 origin, Camera cam, float viewportMargin, out MapDashPoint point)
    {
        point = null;
        if (cam == null) return false;

        float bestSqr = float.MaxValue;
        for (int i = 0; i < All.Count; i++)
        {
            MapDashPoint p = All[i];
            if (p == null) continue;                 // 防御:已销毁但尚未从表里摘掉
            if (!p.IsReady) continue;                // CD 中的元素不参与候选

            p.RestoreVisualIfRecovered();            // 惰性还原入口之一:CD 刚过的元素在这里把粒子色写回

            Vector3 pos = p.transform.position;

            // 视口内:z < 0 = 摄像机背后,xy 出 [-margin, 1+margin] = 屏幕外
            Vector3 vp = cam.WorldToViewportPoint(pos);
            if (vp.z < 0f) continue;
            if (vp.x < -viewportMargin || vp.x > 1f + viewportMargin) continue;
            if (vp.y < -viewportMargin || vp.y > 1f + viewportMargin) continue;

            // 取距离平方最小(免开方)
            Vector2 delta = (Vector2)pos - origin;
            float sqr = delta.sqrMagnitude;
            if (sqr < bestSqr)
            {
                bestSqr = sqr;
                point = p;
            }
        }

        return point != null;
    }

    // ============================================================
    // 生命周期(注册表进出 + 惰性还原入口)
    // ============================================================

    /// <summary>进注册表 + 缓存各变灰目标的原色(元素无 collider,不注册任何物理查询/触发)</summary>
    private void Awake()
    {
        All.Add(this);
        if (bodySprite == null) bodySprite = GetComponent<SpriteRenderer>();   // 兜底:精灵通常就在元素根上
        CacheOriginalColors();
    }

    /// <summary>出注册表(销毁后静态查询不再命中该元素)</summary>
    private void OnDestroy()
    {
        All.Remove(this);
    }

    /// <summary>重新激活时顺手惰性还原一次(激活是事件、不是每帧轮询)</summary>
    private void OnEnable()
    {
        RestoreVisualIfRecovered();
    }

    // ============================================================
    // 表现(idleParticles + 判定圈内外圈一起变淡 / 还原)
    // ============================================================

    /// <summary>收集淡变目标并缓存各自的原 startColor(整个生命周期只读一次,CD 期间只读缓存)。
    /// 目标 = idleParticles + judgeRing 的内外圈,**任一为空都跳过**:元素 prefab 可能没拖判定圈,
    /// 或判定圈上没拖内外圈(那时只有常显粒子参与变淡,不报错)。
    /// 收集只在 Awake 做一次 —— 原色必须在"还没被变淡"时读,若每次 Consume 重读会把淡色当原色存下来。</summary>
    private void CacheOriginalColors()
    {
        List<ParticleSystem> targets = new List<ParticleSystem>(3);   // 最多 3 个:idle + 外圈 + 内圈

        if (idleParticles != null) targets.Add(idleParticles);

        if (judgeRing != null)                                        // 判定圈可为空(Unity 的 == 会把已销毁对象判为 null)
        {
            if (judgeRing.RangeOuter != null) targets.Add(judgeRing.RangeOuter);
            if (judgeRing.RangeInner != null) targets.Add(judgeRing.RangeInner);
        }

        _dimTargets = targets.ToArray();
        _dimOriginals = new ParticleSystem.MinMaxGradient[_dimTargets.Length];
        for (int i = 0; i < _dimTargets.Length; i++)
            _dimOriginals[i] = _dimTargets[i].main.startColor;
    }

    /// <summary>惰性还原(本类唯一判"CD 到点"的地方):时间已过 + 当前仍是变灰态 → 粒子与精灵原色一起写回。
    /// 由各公开入口调用,所以既不轮询,也不会长期停在灰态上。</summary>
    private void RestoreVisualIfRecovered()
    {
        if (!_dimmed) return;
        if (Time.time < _readyAt) return;
        _dimmed = false;
        for (int i = 0; i < _dimTargets.Length; i++)
            ApplyStartColor(_dimTargets[i], _dimOriginals[i]);
        ApplySpriteGray(false);
    }

    /// <summary>CD 期间把 idleParticles、判定圈内外圈与本体精灵一起改成灰
    /// (粒子:alpha 缩 CdAlphaScale + 向灰度混 CdGrayBlend;精灵:整块 CdSpriteGrayScale 灰)。
    /// 每份灰都由缓存的原色算出 → 重复调用结果恒等(不会越叠越灰);
    /// 金色判定环不在目标里,不受影响。</summary>
    private void ApplyDim()
    {
        _dimmed = _dimTargets.Length > 0 || bodySprite != null;   // 全空 = 没有任何表现,也就不用还原
        for (int i = 0; i < _dimTargets.Length; i++)
            ApplyStartColor(_dimTargets[i], BuildDimmed(_dimOriginals[i]));
        ApplySpriteGray(true);
    }

    /// <summary>本体精灵变灰 / 还原(整块设成灰阶,保留原 alpha)。原色在首次调用时才缓存 ——
    /// 元素 prefab 可能把 sprite 放子物体、或引用靠 Awake 兜底才拿到,那时不能提前读色。</summary>
    private void ApplySpriteGray(bool gray)
    {
        if (bodySprite == null) return;
        if (!_spriteOriginalCached)
        {
            _spriteOriginal = bodySprite.color;
            _spriteOriginalCached = true;
        }
        bodySprite.color = gray
            ? new Color(CdSpriteGrayScale, CdSpriteGrayScale, CdSpriteGrayScale, _spriteOriginal.a)
            : _spriteOriginal;
    }

    /// <summary>把颜色写回某个粒子:MainModule 是指向粒子的结构体,取本地变量改属性即写穿到粒子本身
    /// (startColor 的类型是 UnityEngine.ParticleSystem.MinMaxGradient,嵌套在 ParticleSystem 里)。空引用安全。</summary>
    private static void ApplyStartColor(ParticleSystem ps, ParticleSystem.MinMaxGradient color)
    {
        if (ps == null) return;                // 目标中途被销毁也安全(Unity 的 == 把已销毁对象判为 null)
        ParticleSystem.MainModule main = ps.main;
        main.startColor = color;
    }

    /// <summary>按原 startColor 的取色模式构造"淡色"版本(4 种模式都保留,还原时整份写回原值)</summary>
    private static ParticleSystem.MinMaxGradient BuildDimmed(ParticleSystem.MinMaxGradient src)
    {
        switch (src.mode)
        {
            case ParticleSystemGradientMode.Color:
                return new ParticleSystem.MinMaxGradient(Dim(src.color, true));
            case ParticleSystemGradientMode.TwoColors:
                return new ParticleSystem.MinMaxGradient(Dim(src.colorMin, true), Dim(src.colorMax, true));
            case ParticleSystemGradientMode.Gradient:
            case ParticleSystemGradientMode.RandomColor:   // 随机色同样走 gradient 字段,能一并去饱和降透
                return new ParticleSystem.MinMaxGradient(DimGradient(src.gradient));
            case ParticleSystemGradientMode.TwoGradients:
                return new ParticleSystem.MinMaxGradient(DimGradient(src.gradientMin), DimGradient(src.gradientMax));
            default:
                return src;                                 // 未知模式:保守不动
        }
    }

    /// <summary>淡色 = 向灰度混 CdGrayBlend + (可选)alpha × CdAlphaScale;rgb 与 a 分开处理,透明度只降一次</summary>
    private static Color Dim(Color c, bool scaleAlpha)
    {
        float gray = c.r * 0.2126f + c.g * 0.7152f + c.b * 0.0722f;   // 亮度权重(Rec.709)
        return new Color(
            Mathf.Lerp(c.r, gray, CdGrayBlend),
            Mathf.Lerp(c.g, gray, CdGrayBlend),
            Mathf.Lerp(c.b, gray, CdGrayBlend),
            scaleAlpha ? c.a * CdAlphaScale : c.a);
    }

    /// <summary>渐变淡色:colorKeys 只去饱和(透明度由 alphaKeys 单独控制,避免重复降透)。
    /// 两个数组都先 Clone 再改 —— 不依赖引擎 getter 是否已返回副本,绝不改到缓存的原始 Gradient 上
    /// (否则元素的外观会随 CD 被永久改掉)。</summary>
    private static Gradient DimGradient(Gradient src)
    {
        if (src == null) return null;

        GradientColorKey[] srcColors = src.colorKeys;
        GradientColorKey[] colorKeys = srcColors != null
            ? (GradientColorKey[])srcColors.Clone()
            : new GradientColorKey[0];
        for (int i = 0; i < colorKeys.Length; i++)
            colorKeys[i].color = Dim(colorKeys[i].color, false);

        GradientAlphaKey[] srcAlphas = src.alphaKeys;
        GradientAlphaKey[] alphaKeys = srcAlphas != null
            ? (GradientAlphaKey[])srcAlphas.Clone()
            : new GradientAlphaKey[0];
        for (int i = 0; i < alphaKeys.Length; i++)
            alphaKeys[i].alpha *= CdAlphaScale;

        Gradient g = new Gradient();
        g.mode = src.mode;
        g.colorKeys = colorKeys;
        g.alphaKeys = alphaKeys;
        return g;
    }
}
