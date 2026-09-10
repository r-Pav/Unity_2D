using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 背刺判定环控制器 — 挂标识 prefab 根(由 BeatFlashPoint aimPrefab 槽生成)。
/// 结构(P4):1 组内外圈(rangeOuter / rangeInner,基准,**整组共用不叠加**)+ N 个金色收缩环(rings 环池,2~6)。
/// 几何(判定窗口 = 内圈~外圈带;参数固定,其余自动):
///   判定外环 outerRadius:金色环缩到此尺寸时刻 = 重音拍 = 窗口起点(判定锚,默认 1.5)
///   视觉外环(RangeOuter 图片) = outerRadius - visualOuterInset(默认画小 0.5):
///     玩家看到金色环碰到"视觉小环"时,窗口实际已开 visualOuterInset/shrinkSpeed 秒 → 手感更松,不碰判定
///   内环 = outerRadius - shrinkSpeed×窗口时长(mgr 喂,自动;窗口 1s/速度 4 → 钳到 0.05)
///   收缩环起点 ringStartRadius(默认 3):环从此尺寸匀速缩,lead = (起点-外环)/速度 秒后到外环 = 窗口起点
///   出现提前(lead)= (ringStartRadius - outerRadius)/shrinkSpeed,由 Show / ShowChain 传入的 secondsToPoints 对齐:
///     某点剩余 ≥ lead → 该环先隐(scale 0)、等 (剩余-lead) 再以固定起点开缩;
///     剩余 < lead(晚触发兜底)→ 从当前剩余反推起点立即缩,保证仍精确到该点。
/// 连音(P4):第 i 个点各占一个环(rings[i % 池大小]),各自独立协程、互不干扰;池不够时按轮转复用最早那只,
///   并发协程数恒 ≤ 池大小;内外圈整个 ShowChain 只播一组,多点不叠加。
/// 粒子都是基准单环(startSize=1.0),实际尺寸 = transform.localScale(同心缩放)。
/// 外部驱动:BeatFlashPoint 在窗口前调 Show(secondsToWindowStart, windowSeconds)(P5 起连音走 ShowChain),
///   窗口结束 / 背刺命中调 Hide。
/// </summary>
public class BackstabAimIndicator : MonoBehaviour
{
    [Header("粒子引用(拖 prefab 内子物体,禁 Find 按名)")]
    [SerializeField] private ParticleSystem rangeOuter;   // 视觉外环(显示 = 判定外环 - visualOuterInset)
    [SerializeField] private ParticleSystem rangeInner;   // 内环(Show 时按窗口时长算出 scale)
    [Tooltip("金色判定环环池(2~6 个,按顺序拖 prefab 内 Ring 子物体);整组留空则退回下方单环 ring")]
    [SerializeField] private ParticleSystem[] rings;      // 金色判定环池(每个点各占一只)
    [Tooltip("单环兜底(prefab 未扩环池时使用;rings 有引用则被忽略)")]
    [SerializeField] private ParticleSystem ring;         // 金色判定环(单环兼容字段)

    [Header("固定参数(其余自动计算;定稿 2026-09-09)")]
    [Tooltip("判定外环尺寸:金色环缩到此尺寸 = 重音拍 = 窗口起点(判定锚;定稿 1.5)")]
    [SerializeField] private float outerRadius = 1.5f;

    [Tooltip("收缩环起点尺寸:环从多大开始缩(定稿 3 = 1.5 + 4×0.375)")]
    [SerializeField] private float ringStartRadius = 3f;

    [Tooltip("收缩速度(单位/秒,匀速;定稿 4)")]
    [SerializeField] private float shrinkSpeed = 4f;

    [Tooltip("视觉外环内缩量:图片比判定外环小此值(玩家碰视觉环时窗口已开 inset/速度 秒,判定更松;定稿 0.5 → 视觉外环 1.0)")]
    [SerializeField] private float visualOuterInset = 0.5f;

    private readonly List<ParticleSystem> _pool = new List<ParticleSystem>();          // 本轮可用环(rings 过滤空引用,全空退回 ring)
    private readonly List<Coroutine> _poolRoutines = new List<Coroutine>();            // 与 _pool 同下标的在跑协程(每槽至多 1 个)

    /// <summary>派生:环从起点缩到判定外环的用时 = 出现提前量(秒)</summary>
    private float LeadSeconds => Mathf.Max(0f, (ringStartRadius - outerRadius) / shrinkSpeed);

    /// <summary>单点(兼容旧调用方,行为与 P4 前一致):等价 ShowChain(new[]{ secondsToWindowStart }, windowSeconds)。</summary>
    public void Show(float secondsToWindowStart, float windowSeconds)
    {
        ShowChain(new[] { secondsToWindowStart }, windowSeconds);
    }

    /// <summary>显示标识并驱动金色环池:第 i 个环在 secondsToPoints[i] 秒后到达判定外环(= 该点窗口起点)。
    /// 内外圈(共用一组)只设置+播放一次;每个点启一个独立协程驱动自己的环。
    /// 点数为 0 / 环池为空时不报错(仅显示内外圈 / 只跑内外圈 + 无金环)。</summary>
    public void ShowChain(float[] secondsToPoints, float windowSeconds)
    {
        if (!gameObject.activeSelf) gameObject.SetActive(true);

        float window = Mathf.Max(0.01f, windowSeconds);
        float inner = Mathf.Max(0.05f, outerRadius - shrinkSpeed * window);

        // ── 内外圈:整组只设置 + 播放一次(连音多点共用,不叠加)──
        SetScale(rangeOuter, Mathf.Max(0.05f, outerRadius - visualOuterInset));   // 图片小一圈,判定不动
        SetScale(rangeInner, inner);
        ResetAndPlay(rangeOuter);
        ResetAndPlay(rangeInner);

        // ── 金色环池:每轮 Show 都从干净状态起(停掉上一轮所有协程),再按点分配 ──
        StopRingRoutines();
        BuildPool();
        int poolCount = _pool.Count;
        if (poolCount == 0) return;                       // 环引用全空:只有内外圈,不报错

        int count = secondsToPoints != null ? secondsToPoints.Length : 0;
        for (int i = 0; i < count; i++)
        {
            int slot = i % poolCount;                     // 超出池大小 → 轮转回"最早分配的那只"复用(并发恒 ≤ 池大小)
            if (_poolRoutines[slot] != null)              // 被复用的环:先打断它的旧协程,再重新收缩
            {
                StopCoroutine(_poolRoutines[slot]);
                _poolRoutines[slot] = null;
            }
            ParticleSystem ps = _pool[slot];
            ResetAndPlay(ps);
            _poolRoutines[slot] = StartCoroutine(
                ShrinkRoutine(ps, slot, Mathf.Max(0f, secondsToPoints[i]), inner));
        }
    }

    /// <summary>匀速收缩对齐该点窗口起点:剩余 ≥ lead 时该环先隐着,到 (点-lead) 再以固定起点开缩;
    /// 晚触发(剩余 &lt; lead)则从当前剩余反推起点立即缩。环到判定外环(outerRadius)的时刻恒 = Show 时刻 + secondsToPoint;
    /// 窗口期内缩到内环,之后继续缩到 0 收尾(结束显式置 0,防残留)。</summary>
    private IEnumerator ShrinkRoutine(ParticleSystem ps, int slot, float secondsToPoint, float innerRadius)
    {
        if (ps == null)
        {
            ClearSlot(slot);
            yield break;
        }

        float speed = shrinkSpeed;
        float lead = LeadSeconds;

        float wait = secondsToPoint - lead;
        if (wait > 0f)
        {
            // 提前量充足:金色环先不显示,到 next-lead 再以固定起点开始(环带此时已可见)
            SetScale(ps, 0f);
            float waited = 0f;
            while (waited < wait)
            {
                waited += Time.deltaTime;
                yield return null;
            }
        }

        // 起点:正常 = ringStartRadius;晚触发(剩余<lead)反推起点保证仍精确到点
        float startRadius = wait > 0f ? ringStartRadius : outerRadius + speed * secondsToPoint;
        startRadius = Mathf.Max(startRadius, innerRadius);   // 极端晚触发兜底:不从内环以内起(否则 total<0 会卡住不缩)
        float total = (startRadius - innerRadius) / speed;   // 缩到内环的时间(内环之后继续缩到 0)
        float t = 0f;
        while (t < total)
        {
            t += Time.deltaTime;   // 与音乐窗口同用缩放时间:暂停(timeScale=0)时环停,窗口也停,不错位
            float r = startRadius - speed * t;
            SetScale(ps, Mathf.Max(0f, r));
            yield return null;
        }
        SetScale(ps, 0f);
        ClearSlot(slot);
    }

    /// <summary>只收「第 index 个金色环」(P7 连音背刺命中帧按序收环:命中哪一刀就只收哪一只,
    /// 同一敌人身上其它点的环继续缩 —— 修 P5 报的「命中即收掉整只标识实例」隐患)。
    /// 只停该环自己的协程与粒子,内外圈与其它环一律不动(组结束/状态退出仍走 Hide() 收全部,现状不变)。
    /// 索引口径 = ShowChain 传入数组的下标(池不够时按 i % 池大小 轮转,同槽多环一并收起);
    /// 越界/空引用安全跳过,幂等。</summary>
    public void HideRing(int index)
    {
        BuildPool();
        if (index < 0 || index >= _pool.Count) return;
        if (_poolRoutines[index] != null)
        {
            StopCoroutine(_poolRoutines[index]);
            _poolRoutines[index] = null;
        }
        SetScale(_pool[index], 0f);      // 缩到 0 收干净(与 ShrinkRoutine 收尾同口径),再停粒子
        StopAndClear(_pool[index]);
    }

    /// <summary>隐藏(窗口正常结束 / 背刺命中帧调用)。幂等:连按两轮再 Hide 也不会留环。</summary>
    public void Hide()
    {
        StopRingRoutines();
        StopAndClear(rangeOuter);
        StopAndClear(rangeInner);
        StopRingParticles();
        gameObject.SetActive(false);
    }

    /// <summary>物体失活(含 Hide 内 SetActive(false)):环协程全停,防残留。幂等。</summary>
    private void OnDisable()
    {
        StopRingRoutines();
    }

    /// <summary>解析环池:rings 有引用时用它(跳过空槽);全空则退回单环 ring(防 prefab 未扩时数组为空)。</summary>
    private void BuildPool()
    {
        _pool.Clear();
        if (rings != null)
        {
            for (int i = 0; i < rings.Length; i++)
                if (rings[i] != null) _pool.Add(rings[i]);
        }
        if (_pool.Count == 0 && ring != null)
            _pool.Add(ring);

        // 槽位与池对齐:池变小(编辑器改字段)时多出来的槽先停协程再截断
        for (int i = _pool.Count; i < _poolRoutines.Count; i++)
        {
            if (_poolRoutines[i] != null)
            {
                StopCoroutine(_poolRoutines[i]);
                _poolRoutines[i] = null;
            }
        }
        if (_poolRoutines.Count > _pool.Count)
            _poolRoutines.RemoveRange(_pool.Count, _poolRoutines.Count - _pool.Count);
        while (_poolRoutines.Count < _pool.Count)
            _poolRoutines.Add(null);
    }

    /// <summary>停掉所有环协程并清空槽(不动粒子视觉,Hide 负责清粒子)</summary>
    private void StopRingRoutines()
    {
        for (int i = 0; i < _poolRoutines.Count; i++)
        {
            if (_poolRoutines[i] != null) StopCoroutine(_poolRoutines[i]);
            _poolRoutines[i] = null;
        }
    }

    private void ClearSlot(int slot)
    {
        if (slot >= 0 && slot < _poolRoutines.Count) _poolRoutines[slot] = null;
    }

    /// <summary>停全部金色环粒子(rings 与单环兜底都停一遍,引用去重由粒子自身 Stop 幂等保证)</summary>
    private void StopRingParticles()
    {
        if (rings != null)
        {
            for (int i = 0; i < rings.Length; i++)
                StopAndClear(rings[i]);
        }
        StopAndClear(ring);
    }

    private static void SetScale(ParticleSystem ps, float size)
    {
        if (ps == null) return;
        ps.transform.localScale = new Vector3(size, size, 1f);
    }

    private static void ResetAndPlay(ParticleSystem ps)
    {
        if (ps == null) return;
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        ps.time = 0f;
        ps.Play();
    }

    private static void StopAndClear(ParticleSystem ps)
    {
        if (ps == null) return;
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
    }

#if UNITY_EDITOR
    /// <summary>非 Play 预览:字段一改立即按当前值刷新各环 scale(编辑器可直接拖参数看环大小变化)。
    /// 内环按假设窗口 0.5s 展示(Play 按真实窗口);金色环按起点展示;环池为空退回单环字段。</summary>
    private void OnValidate()
    {
        if (Application.isPlaying) return;   // 运行时由 Show/协程接管
        SetScale(rangeOuter, Mathf.Max(0.05f, outerRadius - visualOuterInset));
        SetScale(rangeInner, Mathf.Max(0.05f, outerRadius - shrinkSpeed * 0.5f));
        if (rings != null && rings.Length > 0)
        {
            for (int i = 0; i < rings.Length; i++)
                SetScale(rings[i], ringStartRadius);
        }
        else
        {
            SetScale(ring, ringStartRadius);
        }
    }
#endif
}
