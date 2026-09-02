using UnityEngine;

/// <summary>
/// 石碑触发器 — 挂在石碑 GameObject 上(Area 根下的子物体),每块石碑一个实例。
/// 
/// 职责(单一):把玩家进出本 trigger 的事实上报给 WaypointSystem,不做激活判定、不做存档。
/// - 玩家进入范围 → 上报 SetNearby(this, true) + NotifyEntered(this)(首进由 System 统一激活);
/// - 玩家离开范围 → 上报 SetNearby(this, false);
/// - 激活(Activated 置位/激活动画/首激活日志)全部收口在 WaypointSystem.NotifyEntered。
/// 
/// 身份:AreaId 在 Awake 从父级 AreaIdentity 读取(免每块石碑手动拖引用),
/// 取不到(Area 根没挂 AreaIdentity)时回退父 GameObject 名,保证 WaypointId 恒非空。
/// index=0 约定为该 Area 的传送落点锚点(每 Area 唯一,编辑器约定)。
/// </summary>
[RequireComponent(typeof(Collider2D))]
public class WaypointTrigger : MonoBehaviour
{
    [Header("石碑")]
    [Tooltip("本石碑在 Area 内的序号;0 = 该 Area 传送落点(每 Area 唯一,编辑器约定)")]
    [SerializeField] private int index;

    [Tooltip("激活动画挂点(Animator);本期只留接口,不配动画内容,可空")]
    [SerializeField] private Animator activateAnimator;

    /// <summary>激活动画 trigger 参数名(AnimatorController 需含同名 trigger 才会真的触发)</summary>
    private const string ACTIVATE_TRIGGER = "Activated";

    /// <summary>所属 AreaId:Awake 从父级 AreaIdentity 读;取不到回退父 GameObject 名</summary>
    public string AreaId { get; private set; }

    /// <summary>本石碑在 Area 内的序号(只读)</summary>
    public int Index => index;

    /// <summary>石碑唯一 id(area#index),存档/激活集合的 key</summary>
    public string WaypointId => AreaId + "#" + index;

    /// <summary>是否已激活(运行时;读档恢复后由 WaypointSystem.RestoreActivated 置 true)</summary>
    public bool Activated { get; private set; }

    private void Awake()
    {
        ResolveAreaId();

        // 自注册到 WaypointSystem(GetAnchor 查锚点用);场景根没挂 WaypointSystem 时静默跳过
        if (WaypointSystem.Instance != null)
            WaypointSystem.Instance.Register(this);
    }

    private void OnDestroy()
    {
        if (WaypointSystem.Instance != null)
            WaypointSystem.Instance.Unregister(this);
    }

    /// <summary>
    /// 解析 AreaId:从父级链找 AreaIdentity 读 areaId;
    /// 找不到(Area 根没挂)或为空时回退父 GameObject 名,保证 WaypointId 恒有值。
    /// </summary>
    private void ResolveAreaId()
    {
        var areaIdentity = GetComponentInParent<AreaIdentity>();
        if (areaIdentity != null && !string.IsNullOrEmpty(areaIdentity.AreaId))
        {
            AreaId = areaIdentity.AreaId;
            return;
        }
        AreaId = transform.parent != null ? transform.parent.name : gameObject.name;
    }

    private void OnTriggerEnter2D(Collider2D other)
    {
        if (!other.CompareTag("Player")) return;
        // 只上报,激活判定/存档由 System 统一处理(单写)
        var system = WaypointSystem.Instance;
        if (system == null) return;
        system.SetNearby(this, true);
        system.NotifyEntered(this);
    }

    private void OnTriggerExit2D(Collider2D other)
    {
        if (!other.CompareTag("Player")) return;
        var system = WaypointSystem.Instance;
        if (system == null) return;
        system.SetNearby(this, false);
    }

    /// <summary>
    /// 激活动作(由 WaypointSystem 在首激活时调用):置 Activated + 播放激活动画(预留)。
    /// 幂等:已激活的石碑重复调用直接返回,不重复播动画。
    /// </summary>
    public void Activate()
    {
        if (Activated) return;
        Activated = true;

        // 激活动画挂点:仅当 AnimatorController 实际含 ACTIVATE_TRIGGER 参数才触发,
        // 避免本期动画内容未配(无该参数)时 Animator.SetTrigger 运行时告警。
        if (activateAnimator != null && AnimatorHasTrigger(activateAnimator))
            activateAnimator.SetTrigger(ACTIVATE_TRIGGER);
    }

    /// <summary>AnimatorController 是否含激活动画 trigger 参数(空实现期动画师接入后自动生效)</summary>
    private static bool AnimatorHasTrigger(Animator animator)
    {
        var parameters = animator.parameters;
        for (int i = 0; i < parameters.Length; i++)
        {
            if (parameters[i].name == ACTIVATE_TRIGGER)
                return true;
        }
        return false;
    }
}
