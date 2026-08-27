using UnityEngine;

/// <summary>
/// 玩家卡点判定 + 自动连打。
/// 重击音判定:BossHeavySound 标点窗口内按 攻击键(左键)或 F 键 → 判定成功,进入自动连打模式。
/// 自动连打:PlayerCombo 标点到达时,玩家自动触发攻击(attack1/attack2 由连击系统自然交替)。
/// 判定失败:无惩罚,正常走流程。
/// 挂玩家根物体上。
/// </summary>
public class PlayerBeatJudge : MonoBehaviour
{
    [Header("标点组")]
    [Tooltip("重击音判定组名(窗口内按攻击/F 键 = 判定成功)")]
    public string judgeGroup = "BossHeavySound";
    [Tooltip("自动连打组名(判定成功后,此组标点到达自动攻击)")]
    public string comboGroup = "PlayerCombo";

    [Header("判定窗口标识")]
    [Tooltip("标识物体(初始关闭):进入判定窗口时显示,窗口结束隐藏")]
    public GameObject judgeIndicator;

    private PlayerController _pc;
    private bool _autoComboActive;
    private bool _subscribed;

    public bool AutoComboActive => _autoComboActive;

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
            Debug.Log($"[PlayerBeat] 监听启动 判定组[{judgeGroup}] 连打组[{comboGroup}] 当前曲={(mgr.CurrentTrack != null ? mgr.CurrentTrack.name : "null")}");
        }
        else
        {
            Debug.LogWarning("[PlayerBeat] MusicPointManager 不存在,判定/连打不生效");
        }
    }

    private void OnDestroy()
    {
        var mgr = MusicPointManager.Instance;
        if (mgr != null && _subscribed)
        {
            mgr.OnWindowEnter -= OnWindowEnter;
            mgr.OnWindowPassed -= OnWindowPassed;
        }
    }

    private void Update()
    {
        // 判定输入:攻击键(左键)或 F 键;窗口内 = 成功(失败无惩罚)
        if (Input.GetMouseButtonDown(0) || Input.GetKeyDown(KeyCode.F))
        {
            var mgr = MusicPointManager.Instance;
            if (mgr != null)
            {
                bool inWindow = mgr.IsInGroupWindow(judgeGroup);
                Debug.Log($"[PlayerBeat] 按键 组[{judgeGroup}]窗口={inWindow} 当前组={mgr.CurrentWindowGroup} 连打已激活={_autoComboActive}");
                if (inWindow)
                {
                    _autoComboActive = true;
                    Debug.Log("[PlayerBeat] 重击音判定成功,进入自动连打");
                }
            }
        }
    }

    /// <summary>音乐窗口事件:进入判定窗口 → 显示标识;PlayerCombo 标点 → 自动攻击</summary>
    private void OnWindowEnter(float pointTime)
    {
        var mgr = MusicPointManager.Instance;
        string currentGroup = mgr != null ? mgr.CurrentWindowGroup : "无";
        Debug.Log($"[PlayerBeat] 窗口事件 组={currentGroup} 连打激活={_autoComboActive}");

        // 判定窗口:显示标识(提示玩家该按键)
        if (mgr != null && mgr.IsInGroupWindow(judgeGroup) && judgeIndicator != null)
            judgeIndicator.SetActive(true);

        if (!_autoComboActive) return;
        if (mgr == null || !mgr.IsInGroupWindow(comboGroup)) return;

        if (_pc == null)
        {
            Debug.Log("[PlayerBeat] 自动攻击拦截:PlayerController 为空");
            return;
        }
        if (_pc.IsAttacking)
        {
            Debug.Log("[PlayerBeat] 自动攻击拦截:玩家正在攻击中");
            return;
        }
        if (_pc.Combat == null || !_pc.Combat.AttackCooldownReady)
        {
            Debug.Log("[PlayerBeat] 自动攻击拦截:攻击冷却未就绪");
            return;
        }
        Debug.Log("[PlayerBeat] 自动攻击触发(PlayerCombo 标点)");
        _pc.PlayerFsm.ChangeState(_pc.AttackState);
    }

    /// <summary>窗口结束:判定窗口的标点过了 → 隐藏标识</summary>
    private void OnWindowPassed(float pointTime)
    {
        var mgr = MusicPointManager.Instance;
        if (mgr == null) return;
        if (judgeIndicator != null && PointInGroup(mgr, pointTime, judgeGroup))
            judgeIndicator.SetActive(false);
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
