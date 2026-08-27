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
            _subscribed = true;
        }
    }

    private void OnDestroy()
    {
        var mgr = MusicPointManager.Instance;
        if (mgr != null && _subscribed)
            mgr.OnWindowEnter -= OnWindowEnter;
    }

    private void Update()
    {
        // 判定输入:攻击键(左键)或 F 键;窗口内 = 成功(失败无惩罚)
        if (Input.GetMouseButtonDown(0) || Input.GetKeyDown(KeyCode.F))
        {
            var mgr = MusicPointManager.Instance;
            if (mgr != null && mgr.IsInGroupWindow(judgeGroup))
            {
                _autoComboActive = true;
                Debug.Log("[PlayerBeat] 重击音判定成功,进入自动连打");
            }
        }
    }

    /// <summary>音乐窗口事件:PlayerCombo 标点到达 → 自动攻击(玩家不在攻击中且冷却就绪)</summary>
    private void OnWindowEnter(float pointTime)
    {
        if (!_autoComboActive) return;
        var mgr = MusicPointManager.Instance;
        if (mgr == null || !mgr.IsInGroupWindow(comboGroup)) return;

        if (_pc == null || _pc.IsAttacking) return;
        if (_pc.Combat == null || !_pc.Combat.AttackCooldownReady) return;
        _pc.PlayerFsm.ChangeState(_pc.AttackState);
    }
}
