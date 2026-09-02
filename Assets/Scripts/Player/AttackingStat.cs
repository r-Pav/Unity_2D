using UnityEngine;

/// <summary>
/// [框架二状态位] 玩家全局战斗状态标识 attackingStat — 任意敌人对玩家有仇恨 = 战斗中。
/// 由 EnemyControllerBase.OnEnter/OnExitCombatState 上报(per-enemy guard 已防重),管道只订阅它。
/// 职责单一:维护 refCount + 翻转时切管道实心(AreaChannelTrigger.SetAllSolid) + 广播事件。
/// 不含玩家攻击路径 → 挥空不置位。
/// </summary>
public class AttackingStat : MonoBehaviour
{
    private static AttackingStat _instance;

    public static AttackingStat Instance
    {
        get
        {
            if (_instance == null)
                _instance = PlayerController.Instance?.GetComponent<AttackingStat>();
            return _instance;
        }
    }

    /// <summary>战斗状态变化事件(true=进入战斗,false=脱离)。UI/被动等预留,当前可无订阅</summary>
    public event System.Action<bool> OnCombatChanged;

    /// <summary>是否处于战斗中(任意敌人仇恨)</summary>
    public bool InCombat => _combatRefCount > 0;

    /// <summary>仇恨敌人计数:多个敌人同时战斗时,最后一个退出才清 false(对齐 passiveEquipManager refCount 思想)</summary>
    private int _combatRefCount;

    /// <summary>
    /// 敌人仇恨上报(true=该敌人进入战斗;false=该敌人脱战/死亡)。
    /// 管道实心只在 0→1 翻转时切,恢复只在 →0 时切。
    /// </summary>
    public void Notify(bool enterCombat)
    {
        if (enterCombat)
        {
            _combatRefCount++;
            if (_combatRefCount == 1)
            {
                AreaChannelTrigger.SetAllSolid(true);   // 进入战斗:管道变空气墙(物理挡玩家+敌人)
                OnCombatChanged?.Invoke(true);
            }
        }
        else
        {
            _combatRefCount = Mathf.Max(0, _combatRefCount - 1);
            if (_combatRefCount == 0)
            {
                AreaChannelTrigger.SetAllSolid(false);  // 脱离战斗:管道恢复 trigger(可传送)
                OnCombatChanged?.Invoke(false);
            }
        }
    }
}
