using System.Collections;
using System.Collections.Generic;
using Cinemachine;
using UnityEngine;

/// <summary>
/// 石碑系统(单例)— 挂在场景根常驻 GO,作为石碑注册表/激活列表/当前站位/锚点查询的唯一收口。
/// 
/// 分工(单写原则):
/// - 激活判定与存档都走这里,WaypointTrigger 只上报事实(OnTriggerEnter/Exit),不做决策;
/// - NotifyEntered = 玩家踩到石碑 → 首激活置位 + 日志(存档桥 T2 接,本期不做);
/// - SetNearby = 维护 CurrentNearby(玩家当前所在石碑,F 键判定 T4 用);
/// - GetAnchor = 该 Area index==0 的落点石碑(传送执行 T5 用);
/// - ActivatedAreas = 去重保序 Area 列表(UI 列按钮用)。
/// 
/// 协程宿主:本组件所在 GO 是场景根常驻物,后续传送/黑场协程挂这里,
/// 不会因 ZoneManager.ShowArea/HideArea 的 SetActive(false) 被杀(R1 风险)。
/// </summary>
public class WaypointSystem : MonoBehaviour
{
    // ============================================================
    // Singleton(对齐 ZoneManager.Instance 模式:懒查找)
    // ============================================================

    private static WaypointSystem _instance;
    public static WaypointSystem Instance
    {
        get
        {
            if (_instance == null)
                _instance = FindObjectOfType<WaypointSystem>();
            return _instance;
        }
    }

    // ============================================================
    // 运行时状态(当前会话)
    // ============================================================

    /// <summary>玩家当前所在的石碑(trigger 内);null = 不在任何石碑旁</summary>
    private static WaypointTrigger _currentNearby;
    public static WaypointTrigger CurrentNearby => _currentNearby;

    /// <summary>已激活石碑 id 集合(area#index),O(1) 查重</summary>
    private readonly HashSet<string> _activatedIds = new HashSet<string>();

    /// <summary>激活顺序(与 _activatedIds 同步追加),供 ActivatedWaypoints/ActivatedAreas 保序</summary>
    private readonly List<string> _activationOrder = new List<string>();

    /// <summary>已注册石碑表(waypointId → trigger):GetAnchor 按 areaId#0 查落点锚点</summary>
    private readonly Dictionary<string, WaypointTrigger> _registered = new Dictionary<string, WaypointTrigger>();

    // ============================================================
    // 只读查询
    // ============================================================

    /// <summary>已激活石碑列表(存档原样,area#index,按激活顺序)</summary>
    public IReadOnlyList<string> ActivatedWaypoints => _activationOrder;

    /// <summary>已激活 Area 去重保序列表(UI 按钮用):从激活顺序里按 areaId 去重</summary>
    public IReadOnlyList<string> ActivatedAreas()
    {
        var result = new List<string>();
        var seen = new HashSet<string>();
        for (int i = 0; i < _activationOrder.Count; i++)
        {
            string waypointId = _activationOrder[i];
            int sep = waypointId.IndexOf('#');
            string areaId = sep >= 0 ? waypointId.Substring(0, sep) : waypointId;
            if (seen.Add(areaId))
                result.Add(areaId);
        }
        return result;
    }

    /// <summary>取该 Area 的 0 号石碑(传送落点锚点);未注册返回 null</summary>
    public WaypointTrigger GetAnchor(string areaId)
    {
        if (string.IsNullOrEmpty(areaId)) return null;
        _registered.TryGetValue(areaId + "#0", out var anchor);
        return anchor;
    }

    // ============================================================
    // 石碑上报(写入口,收敛到本类)
    // ============================================================

    /// <summary>
    /// 玩家进入石碑范围:激活(集合加 id + Activate + 首激活日志 + 自动存档)。
    /// 幂等:同一石碑重复进入不重复激活/不重复日志/不重复存档;
    /// 首激活 AutoSave(自动槽)把激活列表持久化——每块石碑一生一次,量小不高频写。
    /// </summary>
    public void NotifyEntered(WaypointTrigger t)
    {
        if (t == null) return;

        bool firstTime = _activatedIds.Add(t.WaypointId);
        if (firstTime)
        {
            _activationOrder.Add(t.WaypointId);
            Debug.Log($"[WaypointSystem] 石碑激活: {t.WaypointId}");

            // 首激活自动存档:激活列表落自动槽(SaveSystem 挂 Player 上,经 PlayerController 取)
            var pc = PlayerController.Instance;
            if (pc != null)
            {
                var save = pc.GetComponent<SaveSystem>();
                if (save != null) save.AutoSave();
            }
        }

        // 幂等:已激活石碑重复踩到不重复播动画(Activate 内部 if (Activated) return)
        t.Activate();
    }

    /// <summary>石碑 trigger Enter/Exit 上报:维护 CurrentNearby(玩家当前所在石碑)</summary>
    public void SetNearby(WaypointTrigger t, bool enter)
    {
        if (t == null) return;
        if (enter)
        {
            _currentNearby = t;   // 最近进入者胜出(石碑间距大,正常不会重叠)
        }
        else
        {
            // 只清自己:若玩家已进入另一块石碑(重叠场景),退出旧的不应误清新目标
            if (_currentNearby == t)
                _currentNearby = null;
        }
    }

    // ============================================================
    // 注册表(WaypointTrigger.Awake/OnDestroy 自注册)
    // ============================================================

    public void Register(WaypointTrigger t)
    {
        if (t == null || string.IsNullOrEmpty(t.WaypointId)) return;
        if (_registered.ContainsKey(t.WaypointId))
        {
            Debug.LogWarning($"[WaypointSystem] 重复注册石碑 {t.WaypointId}(同 Area 同 index 可能重复摆放),覆盖旧引用");
        }
        _registered[t.WaypointId] = t;
    }

    public void Unregister(WaypointTrigger t)
    {
        if (t == null || string.IsNullOrEmpty(t.WaypointId)) return;
        _registered.Remove(t.WaypointId);

        // 被销毁/卸载的石碑若正被玩家站着,清掉 CurrentNearby,避免悬空引用
        if (_currentNearby == t)
            _currentNearby = null;
    }

    // ============================================================
    // 传送页(石碑系统 T4)— 打开面板由 PlayerController.HandleWaypointInput 门控后调用
    // ============================================================

    /// <summary>传送页面板(Panels 下 TeleportPanel,Inspector 拖;未拖时打开仅告警不崩溃)</summary>
    [SerializeField] private GameObject teleportPanel;

    /// <summary>
    /// 打开传送页(TeleportPanel.OnEnable 自行刷新 Area 列表)。
    /// 未拖引用 → 告警并跳过;PanelManager 未挂(主菜单等场景)→ 空调用安全。
    /// </summary>
    public void OpenTeleportPanel()
    {
        if (teleportPanel == null)
        {
            Debug.LogWarning("[WaypointSystem] teleportPanel 未拖引用(检查本组件所在 GO 的 Inspector),无法打开传送页");
            return;
        }
        PanelManager.Instance?.OpenPanel(teleportPanel);
    }

    // ============================================================
    // 存档桥(石碑系统 T2 加)— SaveSystem 收集/恢复调用的唯二接口
    // ============================================================

    /// <summary>
    /// [T2 存档桥] 恢复已激活石碑(读档):清空当前运行时激活集合与顺序,按存档列表逐条重建;
    /// 并找到对应 WaypointTrigger 调 Activate() 置 Activated=true
    /// (需石碑已注册——LoadGame 在场景内运行,WaypointTrigger.Awake 注册已发生,可直接查 _registered)。
    /// 旧档 null → 视为空列表,不报错。幂等:同一 id 重复出现只入一次。
    /// </summary>
    public void RestoreActivated(List<string> list)
    {
        _activatedIds.Clear();
        _activationOrder.Clear();

        if (list == null) return; // 旧档无此字段 → 空激活列表

        for (int i = 0; i < list.Count; i++)
        {
            string id = list[i];
            if (string.IsNullOrEmpty(id)) continue;
            if (_activatedIds.Add(id)) // Add 返回 true = 首次;重复 id 不重复入序
            {
                _activationOrder.Add(id);
                // 置石碑 Activated=true;未注册(石碑不在当前场景/被删)只恢复列表,不报错
                if (_registered.TryGetValue(id, out var trigger) && trigger != null)
                    trigger.Activate();
            }
        }
    }

    // ============================================================
    // 传送执行(石碑系统 T5)— RequestTeleport 入口 + TeleportFlow 黑场协程
    // 协程宿主 = 本组件所在 GO(场景根常驻)。禁止挂石碑/Area 子物体:
    // 传送要 Show/Hide Area,协程若挂 Area 子物体会被 SetActive(false) 杀掉 → 输入永锁(风险 R1)。
    // ============================================================

    /// <summary>是否正在传送流程中(黑场淡出→全黑→淡入);true 期间拒绝新 RequestTeleport(防重入)</summary>
    public bool IsTeleporting { get; private set; }

    /// <summary>
    /// 黑场幕布(场景内 BlackoutCanvas 上的 TeleportBlackout)。
    /// 优先 Inspector 拖;未拖时 FindObjectOfType 懒找(与 ZoneManager.Instance 同款,方案 §7.8)。
    /// </summary>
    [SerializeField] private TeleportBlackout blackout;

    /// <summary>懒找缓存:未拖引用时缓存 FindObjectOfType 结果,避免每次传送全场景扫描</summary>
    private TeleportBlackout _foundBlackout;

    /// <summary>
    /// 请求传送到目标 Area 的 0 号石碑(传送页点 Area 按钮调用 = 活着入口 ignoreCombat=false)。
    /// - 防重入:IsTeleporting 期间直接忽略;
    /// - 战斗门(规则5):活着入口战斗中拒绝;死亡入口(复活页,T6)ignoreCombat=true 跳过尸体必可传;
    /// - 同区传送(targetAreaId==CurrentAreaId)允许,仍走黑场(规则3);
    /// - 黑场缺失(场景未搭 BlackoutCanvas)→ LogError 并复位,不锁输入。
    /// </summary>
    public void RequestTeleport(string targetAreaId, bool ignoreCombat = false)
    {
        if (IsTeleporting) return;

        // 战斗中拒绝(活着入口双保险再验;传送页 F 门控已查过,此处防面板开着时敌人进战斗,R3)
        if (!ignoreCombat && AttackingStat.Instance != null && AttackingStat.Instance.InCombat)
        {
            Debug.Log("[WaypointSystem] 战斗中无法传送(先脱离战斗再试)");
            return;
        }

        if (string.IsNullOrEmpty(targetAreaId))
        {
            Debug.LogError("[WaypointSystem] RequestTeleport 收到空 targetAreaId,已忽略");
            return;
        }

        TeleportBlackout bk = ResolveBlackout();
        if (bk == null)
        {
            Debug.LogError("[WaypointSystem] 场景中找不到 TeleportBlackout(需在黑场 Canvas 上挂 TeleportBlackout 组件),传送中止");
            return;
        }

        IsTeleporting = true;
        StartCoroutine(TeleportFlow(targetAreaId, ignoreCombat, bk));
    }

    /// <summary>
    /// 解析黑场引用:SerializeField 优先;未拖 → FindObjectOfType 懒找并缓存(ZoneManager.Instance 同款,免拖)。
    /// </summary>
    private TeleportBlackout ResolveBlackout()
    {
        if (blackout != null) return blackout;
        if (_foundBlackout == null)
            _foundBlackout = FindObjectOfType<TeleportBlackout>();
        return _foundBlackout;
    }

    /// <summary>
    /// 传送执行协程(宿主 = 本组件,场景根常驻):调用黑场幕布 Run(淡出→全黑回调→淡入)。
    /// 全黑回调内按方案 §3.6 顺序执行:锁输入 → (死亡入口 Revive 留 T6) → 取锚点(无则中止回滚)
    /// → PlayerTeleport 落点 → VCam warp → 区显隐 → NotifyAreaEntered(AutoSave) → 关面板。
    /// onDone(淡入完成)兜底恢复输入 + 复位 IsTeleporting。
    /// </summary>
    private IEnumerator TeleportFlow(string targetAreaId, bool ignoreCombat, TeleportBlackout bk)
    {
        PlayerController pc = PlayerController.Instance;
        if (pc == null)
        {
            Debug.LogError("[WaypointSystem] 找不到玩家(PlayerController.Instance=null),传送中止");
            IsTeleporting = false;
            yield break;
        }

        // 旧当前区:在 onFullyBlack 里改区前记录(NotifyAreaEntered 会覆盖 CurrentAreaId)
        string oldAreaId = ZoneManager.Instance != null ? ZoneManager.Instance.CurrentAreaId : null;

        bk.Run(
            onFullyBlack: () =>
            {
                // —— 全黑,画面不可见,可放心瞬移 ——
                pc.InputEnabled = false;   // 黑场内锁输入(防淡入结束前移动;onDone 兜底恢复)

                // 物理体引用(Revive 同步 + 落点瞬移 + 相机 warp 位移实测都用它)
                Rigidbody2D rb = pc.GetRigidbody();

                // 位移基准必须先于 Revive 记录:此刻 rb 位置 = 死亡处(相机锁定点)。
                // 死亡入口 Revive 会把玩家拉回 DefaultSpawnPoint,若在 Revive 后取 rbBefore,
                // warp delta 会漏掉「死亡处→出生点」这一段 → 淡入时相机视角错位(在别处扫虚空)。
                // 正确 delta = 实际落点 - 死亡处(玩家 transform 总位移,VCam warp 语义)。
                Vector2 rbBefore = rb != null ? rb.position : (Vector2)pc.transform.position;

                // 死亡入口(ignoreCombat=true,复活页 T6):先 Revive(清死+满血+FSM Idle)。
                // Revive 会把人拉回 DefaultSpawnPoint(场景 PlayerHealth 已拖)——此刻画面已全黑,出生点闪移被幕布盖住
                // (方案 §3.6 顺序:黑场内先 Revive 再落点瞬移;T6 实现)。活着入口(ignoreCombat=false)无此分支。
                if (ignoreCombat)
                {
                    PlayerHealth health = PlayerHealth.Instance;
                    if (health == null) health = pc.GetComponent<PlayerHealth>();
                    if (health != null)
                    {
                        health.Revive();
                        // Revive 直接写 transform.position;PlayerTeleport.TeleportTo 读 rb.position。
                        // Rigidbody2D 在 transform 直改后需手动同步 rb.position,否则后续落点解析用了过期的物理位
                        // (死亡处而非出生点),墙钳制/位移判定会错——瞬移前强制对齐一次。
                        if (rb != null) rb.position = pc.transform.position;
                    }
                    else
                    {
                        Debug.LogWarning("[WaypointSystem] 死亡入口未找到 PlayerHealth,跳过 Revive(按活体继续瞬移)");
                    }
                }

                // 落点锚点 = 目标 Area 的 0 号石碑;无 → 中止回滚(不瞬移不改区,黑场淡入恢复,onDone 恢复输入)
                WaypointTrigger anchor = GetAnchor(targetAreaId);
                if (anchor == null)
                {
                    Debug.LogError($"[WaypointSystem] 目标区 {targetAreaId} 无 0 号石碑(Waypoint_0 未摆或未注册),传送中止回滚");
                    // 死亡入口兜底:上面已 Revive(玩家满血站在出生点),死亡面板再开着就是幽灵面板——
                    // 自行关掉,等价「原地复活」;活着入口(玩家位置未变)则面板保持,可改选其他 Area。
                    if (ignoreCombat)
                        PanelManager.Instance?.CloseTopPanel();
                    return;
                }

                // 传送:复用 PlayerTeleport(墙钳制+清速度+无敌帧);组件缺失时运行时挂载(与技能执行器习惯一致)
                PlayerTeleport teleport = pc.GetComponent<PlayerTeleport>();
                if (teleport == null) teleport = pc.gameObject.AddComponent<PlayerTeleport>();

                teleport.TeleportTo(anchor.transform.position);   // 参数 = 石碑世界坐标

                // 相机瞬移:按实际位移 delta 修正(VCam 不滑行不扫虚空,抄 ChannelTeleportTrigger)
                // delta 用 rb 实测:TeleportTo 内含墙钳制,最终落点可能略偏移锚点,实测位移最准
                Vector2 rbAfter = rb != null ? rb.position : (Vector2)pc.transform.position;
                Vector3 delta = rbAfter - rbBefore;
                FindObjectOfType<CinemachineVirtualCamera>()?.OnTargetObjectWarped(pc.transform, delta);

                // 区显隐:Show 目标区;Hide 旧当前区(仅当不同区——同区传送不 Hide 自己脚下)
                ZoneManager zm = ZoneManager.Instance;
                if (zm != null)
                {
                    GameObject targetRoot = zm.GetAreaRoot(targetAreaId);
                    if (targetRoot != null) zm.ShowArea(targetRoot);

                    if (!string.IsNullOrEmpty(oldAreaId) && oldAreaId != targetAreaId)
                    {
                        GameObject oldRoot = zm.GetAreaRoot(oldAreaId);
                        if (oldRoot != null) zm.HideArea(oldRoot);
                    }

                    // 状态源写入口:CurrentAreaId=目标 + 广播 AreaEnterEvent → SaveSystem.AutoSave(自动槽)
                    zm.NotifyAreaEntered(targetAreaId);
                }

                // 传送页发起 → 流程内关面板(PanelManager 恢复 LockInput/Pause,onDone 再兜底输入)
                PanelManager.Instance?.CloseTopPanel();
            },
            onDone: () =>
            {
                // 兜底恢复输入(PanelManager 关面板时已恢复;此处双保险防异常路径锁死)
                if (pc != null) pc.InputEnabled = true;
                IsTeleporting = false;
            });

        // 黑场淡出→回调→淡入全程由 bk 内部协程驱动,本协程只做调度起始;结束由 onDone 复位。
        yield break;
    }
}
