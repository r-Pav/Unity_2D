using UnityEngine;

/// <summary>
/// 玩家卡点判定(Boss 重击音)。
/// 判定:BossHeavy 标点窗口内按 攻击键(左键)或 F 键 → 判定成功。
///   · 判定组 = BossHeavy(即重击点本身;2026-09-15 前用 BossHeavySound,两点错位导致抵消永不生效);
///   · 判定成功:先给本次重击打抵消标记(经 BossControllerBase.NotifyHeavyHit → BossHeavyAttack.NotifyHit)保证重击不出伤,
///     再走一次背刺(PlayerController.TryEnterBackstab → PlayerBackstabState),落点解析/贴墙钳制/无敌帧/单次伤害全在背刺状态里;
///   · 判定有效期截止到出伤那一帧:Boss 侧 HeavyDamageSettled 为 true 时本次判定不生效(不闪、不算成功);
///   · 判定失败:无惩罚,正常走流程。
/// [2026-09-15 改造] 原「判定成功 → 进入自动连打 + 每帧吸附 Boss 身后」已移除:
///   吸附会每帧硬改玩家位置(含 y 跟随 Boss)→ 玩家被吸在 Boss 身上悬空、操作被夺;
///   连打会在 PlayerCombo 标点上自动打出三段攻击,不符合「背刺只一段」。现在判定成功只打一段背刺,结束即结束。
/// 挂玩家根物体上。
/// </summary>
public class PlayerBeatJudge : MonoBehaviour
{
    [Header("标点组")]
    [Tooltip("重击音判定组名(窗口内按攻击/F 键 = 判定成功);与 Boss 重击点同组 BossHeavy")]
    public string judgeGroup = "BossHeavy";
    [Tooltip("[已停用 2026-09-15] 原自动连打组名;连打已移除,字段保留防序列化丢失")]
    public string comboGroup = "PlayerCombo";

    [Header("判定窗口标识")]
    [Tooltip("标识物体(初始关闭):进入判定窗口时显示,窗口结束隐藏;Boss 侧收缩圈接管提示后此槽可以留空")]
    public GameObject judgeIndicator;

    [Header("判定窗口")]
    [Tooltip("判定窗口前段长度(秒):有效区间起点 = 重音 - 该值;必须与 Boss 侧 BossHeavyAttack.judgeWindowBefore 一致")]
    public float windowBefore = 0.4f;
    [Tooltip("判定窗口后段长度(秒):重音之后仍有效的秒数。出伤在重音后约 0.36 秒,所以后段给 0.3;出伤一过由 HeavyDamageSettled 自动拒绝")]
    public float windowAfter = 0.3f;

    [Header("瞬移敌后")]
    [Tooltip("[已停用 2026-09-15] 原自实现的瞬移距离;瞬移与落点现在由 PlayerBackstabState 负责")]
    public float behindDistance = 1.5f;

    private PlayerController _pc;
    private bool _subscribed;
    private BossControllerBase _bossCache;   // Boss 缓存:首次查找后复用,销毁后 Unity == null 自动重找

    /// <summary>[临时调试] 日志标签(验收后删)</summary>
    private const string DbgTag = "[HeavyDbg]";

    // ── [临时调试] 单窗口汇总记录:进窗口一条日志、窗口结束一条日志,中间不刷屏 ──
    private bool _dbgInWindow;
    private float _dbgPoint = -1f;
    private float _dbgEnterTime = -1f;
    private bool _dbgPressed;
    private float _dbgPressTime = -1f;
    private float _dbgPressDelta;
    private string _dbgPressResult = "未按";

    // [2026-09-15 移除] 连打与吸附相关运行时状态,留档备查:
    //   private bool _autoComboActive;                      // 连打模式开关
    //   public bool AutoComboActive => _autoComboActive;     // 外部只读(无调用方)
    //   private int _lockedFacing = 1;                       // 吸附用的朝向快照
    //   private BossControllerBase _lockedEnemy;             // 被锁朝向的 boss

    private void Awake()
    {
        _pc = GetComponent<PlayerController>();
    }

    private void Start()
    {
        var mgr = MusicPointManager.Instance;
        if (mgr != null)
        {
            mgr.OnWindowEnter += OnWindowEnter;
            mgr.OnWindowPassed += OnWindowPassed;
            _subscribed = true;
        }
        else
        {
            Debug.LogWarning("[PlayerBeat] MusicPointManager 不存在,判定不生效");
        }
    }

    private void OnDestroy()
    {
        // [2026-09-15] 原 ReleaseFacingLock() 兜底已移除:玩家侧不再锁 Boss 朝向,朝向锁由重击/背刺状态各自接管
        var mgr = MusicPointManager.Instance;
        if (mgr != null && _subscribed)
        {
            mgr.OnWindowEnter -= OnWindowEnter;
            mgr.OnWindowPassed -= OnWindowPassed;
        }
    }

    private void Update()
    {
        // [2026-09-15 移除] 原「连打中吸附 Boss 身后 + 玩家主动位移结束连打」整段,留档:
        //   if (_autoComboActive)
        //   {
        //       if (水平输入/空格/Shift) { _autoComboActive = false; ReleaseFacingLock(); }
        //       else SnapBehindEnemy();
        //   }

        // [临时调试] 判定窗口汇总(进窗口/窗口结束各一条)
        TickWindowLog();

        // 判定输入:只认 F 键(2026-09-15 saika 定:左键不再触发重击判定,左键只走普通攻击)
        if (!Input.GetKeyDown(KeyCode.F)) return;

        var mgr = MusicPointManager.Instance;
        if (mgr == null) return;

        // [v2] 判定窗口 = [重音 - windowBefore, 重音 + windowAfter]:
        //   重音(环缩到判定外环的那一帧)是完美帧;后半段留出人的反应误差,
        //   一直到出伤之前都有效(出伤在重音后约 0.36 秒) → 出伤后由 HeavyDamageSettled 自动拒绝。
        // 取「最近的点」而不是 NextPointInGroup:标点一过 NextPointInGroup 就跳到下一个点,
        // 会把「点之后 windowAfter 秒内的按键」误算成下一个点太早而拒掉(实测就是这个坑)。
        float[] pts = GetGroupPoints(mgr, judgeGroup);
        float point = -1f;
        float bestDist = float.MaxValue;
        for (int i = 0; i < pts.Length; i++)
        {
            float d = Mathf.Abs(mgr.TrackTime - pts[i]);
            if (d < bestDist) { bestDist = d; point = pts[i]; }
        }
        float delta = point >= 0f ? mgr.TrackTime - point : float.NaN;
        bool inWindow = point >= 0f && delta >= -windowBefore && delta <= windowAfter;

        // [临时调试] 记录本窗口内的第一次按键(结果统一由「窗口结束」日志输出)
        if (_dbgInWindow && !_dbgPressed)
        {
            _dbgPressed = true;
            _dbgPressTime = mgr.TrackTime;
            _dbgPressDelta = delta;
            _dbgPressResult = inWindow ? "窗口内" : "窗口外" + ReasonText(point, delta);
        }

        if (!inWindow) return;

        var enemy = ResolveBoss();
        if (enemy != null && enemy.HeavyDamageSettled)
        {
            _dbgPressResult = "通过但本次重击已出伤";
            return;
        }
        if (enemy != null) enemy.NotifyHeavyHit();
        if (_pc != null)
        {
            _pc.TryEnterBackstab(false);
            _dbgPressResult = "通过 → 进背刺";
        }
        else _dbgPressResult = "通过但 PlayerController 为空";
    }

    /// <summary>[临时调试] 判定窗口汇总:进窗口一条、窗口结束一条</summary>
    private void TickWindowLog()
    {
        var mgr = MusicPointManager.Instance;
        if (mgr == null) return;

        float[] pts = GetGroupPoints(mgr, judgeGroup);
        float point = -1f;
        float bestDist = float.MaxValue;
        for (int i = 0; i < pts.Length; i++)
        {
            float d = Mathf.Abs(mgr.TrackTime - pts[i]);
            if (d < bestDist) { bestDist = d; point = pts[i]; }
        }
        float delta = point >= 0f ? mgr.TrackTime - point : float.NaN;
        bool inside = point >= 0f && delta >= -windowBefore && delta <= windowAfter;

        if (inside)
        {
            if (!_dbgInWindow)
            {
                _dbgInWindow = true;
                _dbgPoint = point;
                _dbgEnterTime = mgr.TrackTime;
                _dbgPressed = false;
                _dbgPressTime = -1f;
                _dbgPressDelta = 0f;
                _dbgPressResult = "未按";
                Debug.Log($"{DbgTag} 进窗口:点={point:0.###} 当前TrackTime={_dbgEnterTime:0.###} 窗口=[-{windowBefore:0.###}, +{windowAfter:0.###}] 组={judgeGroup}");
            }
            return;
        }

        if (_dbgInWindow)
        {
            _dbgInWindow = false;
            Debug.Log($"{DbgTag} 窗口结束:点={_dbgPoint:0.###} 进窗口于={_dbgEnterTime:0.###} 窗口内按F={(_dbgPressed ? "有" : "无")}"
                + (_dbgPressed ? $" 首次按于={_dbgPressTime:0.###}(距点 {_dbgPressDelta:+0.###;-0.###}) 结果={_dbgPressResult}" : ""));
        }
    }

    /// <summary>[临时调试] 窗口被拒的原因文案</summary>
    private string ReasonText(float point, float delta)
    {
        if (point < 0f) return "(该组没有标点)";
        if (delta > windowAfter) return $"(太晚:已过标点 {delta:0.###} 秒,超过后段 {windowAfter:0.###})";
        if (delta < -windowBefore) return $"(太早:窗口还没开,还差 {-windowBefore - delta:0.###} 秒)";
        return "";
    }

    /// <summary>[临时调试] 取某组全部标点</summary>
    private static float[] GetGroupPoints(MusicPointManager mgr, string groupName)
    {
        var track = mgr != null ? mgr.CurrentTrack : null;
        var g = track != null ? track.GetGroup(groupName) : null;
        return g != null && g.points != null ? g.points : System.Array.Empty<float>();
    }

    /// <summary>[临时调试] 标点数组转文本</summary>
    private static string PointsText(float[] pts)
    {
        if (pts == null || pts.Length == 0) return "无";
        var sb = new System.Text.StringBuilder();
        for (int i = 0; i < pts.Length; i++)
        {
            if (i > 0) sb.Append(", ");
            sb.Append(pts[i].ToString("0.###"));
        }
        return sb.ToString();
    }

    /// <summary>音乐窗口事件:进入判定窗口 → 显示标识</summary>
    private void OnWindowEnter(float pointTime)
    {
        var mgr = MusicPointManager.Instance;

        // [2026-09-15] 旧判定标识(judgeIndicator)不再显示:重击的判定提示统一由 Boss 挂点的背刺圈担任。
        //   留档:
        //   if (mgr != null && mgr.IsInGroupWindow(judgeGroup) && judgeIndicator != null)
        //       judgeIndicator.SetActive(true);
        _ = mgr;

        // [2026-09-15 移除] 原「PlayerCombo 标点到达 → 自动攻击(连打)」整段,留档:
        //   if (!_autoComboActive) return;
        //   if (mgr == null || !mgr.IsInGroupWindow(comboGroup)) return;
        //   ... 攻击冷却/状态拦截 ...
        //   _pc.PlayerFsm.ChangeState(_pc.AttackState);
    }

    /// <summary>窗口结束:判定窗口的标点过了 → 隐藏标识</summary>
    private void OnWindowPassed(float pointTime)
    {
        var mgr = MusicPointManager.Instance;
        if (mgr == null) return;
        // [2026-09-15] 旧判定标识不再显示,隐藏逻辑一并停用;留档:
        //   if (judgeIndicator != null && PointInGroup(mgr, pointTime, judgeGroup))
        //       judgeIndicator.SetActive(false);
    }

    // [2026-09-15 移除] 原 TeleportBehindEnemy()(自实现瞬移到 Boss 身后)与 SnapBehindEnemy()(连打吸附),
    // 落点/朝向/瞬移统一交给 PlayerBackstabState(它自带贴墙钳制与无敌帧),避免两套瞬移口径不一致。

    /// <summary>
    /// 获取当前 Boss:缓存为空(Boss 未找到或已被销毁,Unity 销毁对象 == null)才 Find 一次并缓存。
    /// 替代全场景重复扫描,只针对 Boss 不影响普通 enemy。
    /// </summary>
    private BossControllerBase ResolveBoss()
    {
        if (_bossCache == null)
            _bossCache = FindObjectOfType<BossControllerBase>();
        return _bossCache;
    }

    /// <summary>该标点时刻是否属于某组</summary>
    private bool PointInGroup(MusicPointManager mgr, float point, string groupName)
    {
        var track = mgr.CurrentTrack;
        if (track == null) return false;
        var g = track.GetGroup(groupName);
        if (g == null || g.points == null) return false;
        foreach (float p in g.points)
        {
            if (Mathf.Abs(p - point) < 0.001f) return true;
        }
        return false;
    }
}
