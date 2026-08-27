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

    // ============================================================
    // 运行时状态
    // ============================================================

    private bool _inSkillPhase = true;
    private int _skillCastLimit;
    private int _skillCastCount;
    private int _meleeLimit;
    private int _meleeCount;

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

        // 技能阶段:随机选技能释放
        if (_inSkillPhase)
        {
            int[] available = skillSlots.GetAvailableSkills();
            if (available.Length > 0)
            {
                int chosen = available[Random.Range(0, available.Length)];
                skillSlots.Execute(chosen);
                _skillCastCount++;
                if (_skillCastCount >= _skillCastLimit)
                    EnterMeleePhase();
                return true;
            }
            // 池空:直接转普攻
            EnterMeleePhase();
        }

        // 普攻阶段:普攻间隔中不普攻
        if (boss.IsMeleeIntervalActive) return false;

        boss.PerformDefaultMelee();          // 普攻伤害(即时判定)
        boss.Fsm.ChangeState(boss.CreateAttackState());   // 普攻动画(播完回追击)
        _meleeCount++;
        if (_meleeCount >= _meleeLimit)
            EnterSkillPhase();
        return true;
    }
}
