using System.Collections;
using UnityEngine;

/// <summary>
/// 背刺判定环控制器 — 挂标识 prefab 根(由 BeatFlashPoint aimPrefab 槽生成)。
/// 几何(判定窗口 = 内圈~外圈带;参数固定,其余自动):
///   判定外环 outerRadius:金色环缩到此尺寸时刻 = 重音拍 = 窗口起点(判定锚,默认 2.5)
///   视觉外环(RangeOuter 图片) = outerRadius - visualOuterInset(默认画小 0.3):
///     玩家看到金色环碰到"视觉小环"时,窗口实际已开 visualOuterInset/shrinkSpeed 秒 → 手感更松,不碰判定
///   内环 = outerRadius - shrinkSpeed×窗口时长(mgr 喂,自动;窗口 1s/速度 1.5 → 1.0)
///   收缩环起点 ringStartRadius(默认 3.7):环从此尺寸匀速缩,lead = (起点-外环)/速度 秒后到外环 = 窗口起点
///   出现提前(lead)= (ringStartRadius - outerRadius)/shrinkSpeed,由 Show 传入的 secondsToWindowStart 对齐:
///     若传入剩余 ≥ lead → 先等 (剩余-lead) 再以固定起点开缩;若 < lead(晚触发兜底)→ 从当前剩余反推起点立即缩。
/// 三个粒子都是基准单环(startSize=1.0),实际尺寸 = transform.localScale(同心缩放)。
/// 外部驱动:EnemyBeatIndicator 在窗口前调 Show(secondsToWindowStart, windowSeconds),窗口结束/命中调 Hide。
/// </summary>
public class BackstabAimIndicator : MonoBehaviour
{
    [Header("粒子引用(拖 prefab 内子物体,禁 Find 按名)")]
    [SerializeField] private ParticleSystem rangeOuter;   // 视觉外环(显示 = 判定外环 - visualOuterInset)
    [SerializeField] private ParticleSystem rangeInner;   // 内环(Show 时按窗口时长算出 scale)
    [SerializeField] private ParticleSystem ring;         // 金色判定环(协程驱动 scale)

    [Header("固定参数(其余自动计算;定稿 2026-09-09)")]
    [Tooltip("判定外环尺寸:金色环缩到此尺寸 = 重音拍 = 窗口起点(判定锚;定稿 1.5)")]
    [SerializeField] private float outerRadius = 1.5f;

    [Tooltip("收缩环起点尺寸:环从多大开始缩(定稿 3 = 1.5 + 4×0.375)")]
    [SerializeField] private float ringStartRadius = 3f;

    [Tooltip("收缩速度(单位/秒,匀速;定稿 4)")]
    [SerializeField] private float shrinkSpeed = 4f;

    [Tooltip("视觉外环内缩量:图片比判定外环小此值(玩家碰视觉环时窗口已开 inset/速度 秒,判定更松;定稿 0.5 → 视觉外环 1.0)")]
    [SerializeField] private float visualOuterInset = 0.5f;

    private Coroutine _shrinkRoutine;

    /// <summary>派生:环从起点缩到判定外环的用时 = 出现提前量(秒)</summary>
    private float LeadSeconds => Mathf.Max(0f, (ringStartRadius - outerRadius) / shrinkSpeed);

    /// <summary>显示标识并驱动金色环,保证环在 secondsToWindowStart 秒后到达判定外环(= 窗口起点)。</summary>
    public void Show(float secondsToWindowStart, float windowSeconds)
    {
        if (!gameObject.activeSelf) gameObject.SetActive(true);
        float window = Mathf.Max(0.01f, windowSeconds);
        float inner = Mathf.Max(0.05f, outerRadius - shrinkSpeed * window);
        SetScale(rangeOuter, Mathf.Max(0.05f, outerRadius - visualOuterInset));   // 图片小一圈,判定不动
        SetScale(rangeInner, inner);
        ResetAndPlay(rangeOuter);
        ResetAndPlay(rangeInner);
        ResetAndPlay(ring);

        if (_shrinkRoutine != null) StopCoroutine(_shrinkRoutine);
        _shrinkRoutine = StartCoroutine(ShrinkRoutine(Mathf.Max(0f, secondsToWindowStart), window, inner));
    }

    /// <summary>匀速收缩对齐窗口起点:剩余时间 ≥ lead 时保持固定起点(先等差值);晚触发则从当前剩余反推起点。
    /// 环到判定外环(outerRadius)时刻恒 = Show + secondsToWindowStart = 窗口起点;窗口期内缩到内环,之后缩到 0 收尾。</summary>
    private IEnumerator ShrinkRoutine(float secondsToWindowStart, float windowSeconds, float innerRadius)
    {
        float speed = shrinkSpeed;
        float lead = LeadSeconds;

        float wait = secondsToWindowStart - lead;
        if (wait > 0f)
        {
            // 提前量充足:金色环先不显示,到 next-lead 再以固定起点开始(环带此时已可见)
            SetScale(ring, 0f);
            float waited = 0f;
            while (waited < wait)
            {
                waited += Time.deltaTime;
                yield return null;
            }
        }

        // 起点:正常 = ringStartRadius;晚触发(剩余<lead)反推起点保证仍精确到窗口起点
        float startRadius = wait > 0f ? ringStartRadius : outerRadius + speed * secondsToWindowStart;
        float total = (startRadius - innerRadius) / speed;   // 缩到内环的时间(内环之后继续缩到 0)
        float t = 0f;
        while (t < total)
        {
            t += Time.deltaTime;   // 与音乐窗口同用缩放时间:暂停(timeScale=0)时环停,窗口也停,不错位
            float r = startRadius - speed * t;
            SetScale(ring, Mathf.Max(0f, r));
            yield return null;
        }
        _shrinkRoutine = null;
    }

    /// <summary>隐藏(窗口正常结束 / 背刺命中帧调用)</summary>
    public void Hide()
    {
        if (_shrinkRoutine != null)
        {
            StopCoroutine(_shrinkRoutine);
            _shrinkRoutine = null;
        }
        StopAndClear(rangeOuter);
        StopAndClear(rangeInner);
        StopAndClear(ring);
        gameObject.SetActive(false);
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
    /// <summary>非 Play 预览:字段一改立即按当前值刷新三环 scale(编辑器可直接拖参数看环大小变化)。
    /// 内环按假设窗口 0.5s 展示(Play 按真实窗口);金色环按起点展示。</summary>
    private void OnValidate()
    {
        if (Application.isPlaying) return;   // 运行时由 Show/协程接管
        SetScale(rangeOuter, Mathf.Max(0.05f, outerRadius - visualOuterInset));
        SetScale(rangeInner, Mathf.Max(0.05f, outerRadius - shrinkSpeed * 0.5f));
        SetScale(ring, ringStartRadius);
    }
#endif
}
