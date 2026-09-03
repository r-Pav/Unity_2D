using UnityEngine;

/// <summary>
/// 重音背刺 Combo 计数头顶指示器 — 挂 Player 根下的头顶子物体(建议名 BeatComboSlot)。
/// 每次成功背刺(PlayerBackstabState 命中帧 → NotifyBeatHit)计数 +1(上限 3),
/// 实例化对应档位 prefab(图片+特效一体)到自身(挂点)下 localPosition=0,
/// 跟随玩家(挂在 Player 子物体上自动跟随)。显示 displayDuration 秒;
/// 期间再次触发刷新计时并切对应档;3 秒无新触发 → 隐藏并清零。
/// 挥空(无目标/未命中)不计数(调用方只在命中目标分支通知)。
/// 计数按 Area:进入新 Area(AreaEnterEvent,管道到达/传送完成广播)清零并隐藏。
/// 所有 prefab 槽位允许为空:null/未拖 = 空安全跳过(只计数计时不显示),不报错。
/// </summary>
public class BeatComboIndicator : MonoBehaviour
{
    [Header("档位")]
    [Tooltip("档位数组:index 0=第1次,1=第2次,2=第3次;允许 null 元素/长度不足(空档跳过显示只刷新计时)")]
    [SerializeField] private GameObject[] comboPrefabs;

    [Tooltip("显示时长(秒),默认 3,脚本可调")]
    [SerializeField] private float displayDuration = 3f;

    // ============================================================
    // 运行时状态
    // ============================================================

    private int _count;                 // 当前 combo 计数(上限 3)
    private float _hideTimer;           // 剩余显示时长(<=0 = 隐藏并清零)
    private GameObject _activeInstance; // 当前档位实例(无实例 = null)

    // ============================================================
    // 公开方法
    // ============================================================

    /// <summary>重音背刺成功:计数 +1(上限 3,达到 3 后保持 3 并刷新),切对应档位并刷新显示计时</summary>
    public void NotifyBeatHit()
    {
        _count = Mathf.Min(3, _count + 1);
        ShowCurrent();
        _hideTimer = displayDuration;
    }

    /// <summary>进入新 Area:清零并隐藏(AreaEnterEvent 订阅回调;任何广播都清零)</summary>
    public void ResetForNewArea()
    {
        _count = 0;
        _hideTimer = 0f;
        DestroyActive();
    }

    // ============================================================
    // 生命周期 — EventBus 订阅配对(Subscribe 是 Delegate.Combine 不幂等,必须 OnEnable/OnDisable 配对防重复回调)
    // ============================================================

    private void OnEnable()
    {
        EventBus.Subscribe<AreaEnterEvent>(OnAreaEnter);
    }

    private void OnDisable()
    {
        EventBus.Unsubscribe<AreaEnterEvent>(OnAreaEnter);
        // 组件禁用/离开场景:清理计时与实例
        _hideTimer = 0f;
        DestroyActive();
        _count = 0;
    }

    private void OnAreaEnter(AreaEnterEvent _)
    {
        ResetForNewArea();
    }

    private void Update()
    {
        if (_hideTimer <= 0f) return;
        _hideTimer -= Time.deltaTime;
        if (_hideTimer <= 0f)
        {
            _count = 0;
            DestroyActive();
        }
    }

    // ============================================================
    // 私有
    // ============================================================

    /// <summary>销毁旧实例并实例化当前计数档位到自身下;空档(数组为 null/长度不足/元素 null)静默跳过只刷新计时</summary>
    private void ShowCurrent()
    {
        DestroyActive();
        int idx = _count - 1;
        if (idx < 0 || comboPrefabs == null || idx >= comboPrefabs.Length || comboPrefabs[idx] == null) return;

        GameObject go = Instantiate(comboPrefabs[idx], transform);
        go.transform.localPosition = Vector3.zero;
        go.SetActive(true);   // 团结引擎 Instantiate 复制激活状态,强制激活
        _activeInstance = go;
    }

    /// <summary>销毁当前档位实例(空实例 null 安全)</summary>
    private void DestroyActive()
    {
        if (_activeInstance != null)
            Destroy(_activeInstance);
        _activeInstance = null;
    }
}
