using UnityEngine;

/// <summary>
/// Boss 攻击编排 — 技能阶段 ↔ 普攻阶段循环(替代旧 ExecuteBossSkillCycle)。
/// 随机技能释放 1~2 次 → 普攻 2~4 次 → 循环;EnterSkillPhase/EnterMeleePhase 都是公开方法,
/// 任何阶段/外部可直接调用切入(如某技能释放完强制进普攻)。
/// 技能不受普攻间隔约束;普攻受 boss 的 meleeInterval(5 秒)约束。
/// 由 ChaseState 在玩家进攻击范围且 CanAttack 时调用 TryAttack() 触发一次攻击。
/// </summary>
public class BossAttackDirector : MonoBehaviour
{
    [Header("引用")]
    [Tooltip("Boss 控制器")]
    [SerializeField] private BossControllerBase boss;
    [Tooltip("技能池组件")]
    [SerializeField] private BossSkillSlots skillSlots;
    [Tooltip("普攻组件(普攻阶段伤害用)")]
    [SerializeField] private EnemyMeleeAttack defaultMelee;

    [Header("技能阶段")]
    [Tooltip("每次进入技能阶段随机释放次数下限")]
    [SerializeField] private int minSkillCasts = 1;
    [Tooltip("每次进入技能阶段随机释放次数上限")]
    [SerializeField] private int maxSkillCasts = 2;

    [Header("普攻阶段")]
    [Tooltip("每次进入普攻阶段随机次数下限")]
    [SerializeField] private int minMeleeCount = 2;
    [Tooltip("每次进入普攻阶段随机次数上限")]
    [SerializeField] private int maxMeleeCount = 4;

    [Header("法球预约")]
    [Tooltip("法球技能在技能池的索引(启用法球预约; -1 = 不启用)")]
    [SerializeField] private int orbSkillIndex = -1;
    [Tooltip("预约窗口:标点在此时长(秒)内才预约;无标点/太远则放弃法球回普通")]
    [SerializeField] private float orbReserveWindow = 10f;
    [Tooltip("法球标点前提前释放秒数(保证飞行到标点)")]
    [SerializeField] private float orbReserveLead = 4f;

    // ============================================================
    // 运行时状态
    // ============================================================

    private bool _inSkillPhase = true;
    private int _skillCastLimit;
    private int _skillCastCount;
    private int _meleeLimit;
    private int _meleeCount;
    private int _lastSkillIndex = -1;   // 上次释放的技能(随机时排除,避免连续重复)

    // 法球预约状态:预约后到 releaseAt 前只普攻,到点用预约组释放
    private bool _orbReserved;
    private float _orbReleaseAt;
    private string _orbGroup;
    private static int _orbUseCount;    // 法球组轮换计数(与执行器分开,预约路径用)

    public bool InSkillPhase => _inSkillPhase;

    private void Awake()
    {
        if (boss == null) boss = GetComponentInParent<BossControllerBase>();
        if (skillSlots == null) skillSlots = GetComponentInParent<BossSkillSlots>();
        if (defaultMelee == null) defaultMelee = GetComponentInParent<EnemyMeleeAttack>();
    }

    private void Start()
    {
        EnterSkillPhase();
    }

    // ============================================================
    // 阶段入口(公开,外部可直接切入)
    // ============================================================

    /// <summary>进入技能阶段:重掷技能释放次数(1~2),计数清零</summary>
    public void EnterSkillPhase()
    {
        _inSkillPhase = true;
        _skillCastLimit = Random.Range(minSkillCasts, maxSkillCasts + 1);
        _skillCastCount = 0;
    }

    /// <summary>进入普攻阶段:重掷普攻次数(2~4),计数清零</summary>
    public void EnterMeleePhase()
    {
        _inSkillPhase = false;
        _meleeLimit = Random.Range(minMeleeCount, maxMeleeCount + 1);
        _meleeCount = 0;
    }

    /// <summary>技能随机选择:排除上次释放的技能(池内多于 1 个时),避免连续重复</summary>
    private int SelectSkillAvoidRepeat(int[] available)
    {
        if (available.Length <= 1) return available[0];
        var pool = new System.Collections.Generic.List<int>();
        foreach (int idx in available)
        {
            if (idx != _lastSkillIndex)
                pool.Add(idx);
        }
        if (pool.Count == 0) return available[Random.Range(0, available.Length)];
        return pool[Random.Range(0, pool.Count)];
    }

    // ============================================================
    // 攻击请求(ChaseState 调用)
    // ============================================================

    /// <summary>
    /// 请求一次攻击:按当前阶段执行技能或普攻。返回 true = 已触发(技能执行中/普攻状态切换)。
    /// 技能优先(不受普攻间隔约束);无技能可用直接转普攻;普攻被 5 秒间隔拦 → 不触发。
    /// </summary>
    public bool TryAttack()
    {
        if (boss == null || boss.IsDead) return false;
        if (skillSlots == null) return false;
        if (skillSlots.IsExecuting) return false;

        // 法球预约中:releaseAt 前只普攻(保证标点前 Boss 非技能状态),到点释放法球(用预约组)
        if (_orbReserved)
        {
            var mgr = MusicPointManager.Instance;
            if (mgr != null && mgr.TrackTime >= _orbReleaseAt)
            {
                _orbReserved = false;
                skillSlots.Execute(orbSkillIndex, _orbGroup);
                _skillCastCount++;
                if (_skillCastCount >= _skillCastLimit)
                    EnterMeleePhase();
                return true;
            }
            return TryMeleeOnly();
        }

        // 技能阶段:随机选技能释放(排除上次,避免连续重复)
        if (_inSkillPhase)
        {
            int[] available = skillSlots.GetAvailableSkills();
            if (available.Length > 0)
            {
                int chosen = SelectSkillAvoidRepeat(available);

                // 法球:预约检查(无标点/标点太远 → 放弃法球,回普通循环,不卡)
                if (chosen == orbSkillIndex)
                {
                    var mgr = MusicPointManager.Instance;
                    if (mgr != null)
                    {
                        string group = NextOrbGroup();
                        float next = mgr.NextPointInGroup(group);
                        float toNext = next >= 0f ? next - mgr.TrackTime : -1f;
                        if (toNext >= 0f && toNext < orbReserveWindow)
                        {
                            // 预约成功:标点前 orbReserveLead 秒释放;期间只普攻
                            _orbReserved = true;
                            _orbReleaseAt = next - orbReserveLead;
                            _orbGroup = group;
                            _lastSkillIndex = -1;   // 法球未真正释放,下次可再选
                            EnterMeleePhase();
                            return TryMeleeOnly();
                        }
                    }
                    // 无标点/太远/无音乐管理器:放弃法球,回普通
                    _lastSkillIndex = -1;
                    EnterMeleePhase();
                    return TryMeleeOnly();
                }

                _lastSkillIndex = chosen;
                skillSlots.Execute(chosen);
                _skillCastCount++;
                if (_skillCastCount >= _skillCastLimit)
                    EnterMeleePhase();
                return true;
            }
            // 池空:直接转普攻
            EnterMeleePhase();
        }

        return TryMeleeOnly();
    }

    /// <summary>只普攻(普攻间隔中不普攻;次数到 limit 转技能阶段)</summary>
    private bool TryMeleeOnly()
    {
        if (boss.IsMeleeIntervalActive) return false;

        boss.Fsm.ChangeState(boss.CreateAttackState());   // 普攻动画;伤害在动画命中帧由 BossAttackState 结算
        _meleeCount++;
        if (_meleeCount >= _meleeLimit)
            EnterSkillPhase();
        return true;
    }

    /// <summary>法球组轮换(与 BossSkill_Orb 轮换计数分开;预约路径由 Director 定组,执行器不重复轮换)</summary>
    private static string NextOrbGroup()
    {
        int idx = _orbUseCount % 5 + 1;
        _orbUseCount++;
        return "BossOrb" + idx;
    }
}
