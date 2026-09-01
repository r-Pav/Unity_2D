using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

// ============================================================
// BossSkillSlots — Boss 技能池管理器(重构,替代旧 BossAttackSO 分发)
// 技能 = BossSkillData(SO) + skillPrefab(执行器),Execute 时实例化 prefab 挂 Boss 下,
// 由 BossAttackDirector 决定放哪个技能(随机),本组件只负责执行/中断/事件路由。
// ============================================================

/// <summary>
/// Boss 技能池。Inspector 填入 allSkills(BossSkillData 数组),
/// 由 BossAttackDirector 调用 Execute(index) 执行;动画事件经 BossAnimationRelay 转发回当前执行器。
/// 无冷却(节奏由攻击循环 + 动画时长控制)。
/// </summary>
public class BossSkillSlots : MonoBehaviour
{
    [Header("技能池")]
    [Tooltip("Boss 技能数据列表(归一化 SO;skillPrefab 上的 BossSkillExecutor 读 data 执行)")]
    [SerializeField] private BossSkillData[] allSkills;

    [Header("Debug")]
    [SerializeField] private bool logSkillExecutions;

    [Tooltip("测试用:打开后运行中按数字键 1~9 强制释放对应索引技能(先打断当前技能/普攻)")]
    [SerializeField] private bool debugForceKeys;

    // ============================================================
    // 运行时状态
    // ============================================================

    private EnemyControllerBase owner;
    private Animator animator;
    private Transform player;
    private int currentPhase;
    private bool isQuitting;

    private Coroutine currentCoroutine;
    private BossSkillData currentSkill;
    private BossSkillExecutor currentExecutor;
    private GameObject currentInstance;

    /// <summary>攻击持续 VFX 锚点(attack_VFX 子物体上的 AttackVFXAnchor;未配置时为 null,空安全)</summary>
    private AttackVFXAnchor _vfx;

    // ============================================================
    // 属性
    // ============================================================

    public int SkillCount => allSkills != null ? allSkills.Length : 0;

    /// <summary>是否有技能正在执行中</summary>
    public bool IsExecuting => currentCoroutine != null;

    /// <summary>当前执行中的技能 data(动画事件路由用)</summary>
    public BossSkillData CurrentSkill => currentSkill;

    /// <summary>当前执行器(动画事件路由用)</summary>
    public BossSkillExecutor CurrentExecutor => currentExecutor;

    // ============================================================
    // 事件(供 FSM / UI 订阅)
    // ============================================================

    public event Action<int> OnSkillStarted;
    public event Action<int> OnSkillFinished;
    public event Action<int> OnSkillInterrupted;

    // ============================================================
    // 生命周期
    // ============================================================

    private void Awake()
    {
        owner = GetComponent<EnemyControllerBase>();
        animator = owner != null ? owner.GetComponentInChildren<Animator>() : GetComponentInChildren<Animator>();
    }

    private void Start()
    {
        player = PlayerController.Instance?.transform;
    }

    // ============================================================
    // 调试:数字键强制释放技能(测试用,debugForceKeys 打开生效)
    // ============================================================

    private void Update()
    {
        if (!debugForceKeys || allSkills == null) return;
        for (int i = 1; i <= Mathf.Min(9, allSkills.Length); i++)
        {
            if (Input.GetKeyDown(KeyCode.Alpha0 + i))
            {
                Interrupt();
                Execute(i - 1);
                break;
            }
        }
    }

    private void OnDestroy()
    {
        isQuitting = true;
    }

    // ============================================================
    // 公开方法
    // ============================================================

    public BossSkillData GetSkill(int index)
    {
        if (allSkills == null || index < 0 || index >= allSkills.Length)
            return null;
        return allSkills[index];
    }

    /// <summary>可用技能 index 数组(新框架无冷却/阶段解锁过滤,池内全部可用)</summary>
    public int[] GetAvailableSkills()
    {
        if (allSkills == null) return Array.Empty<int>();
        var list = new List<int>();
        for (int i = 0; i < allSkills.Length; i++)
        {
            if (allSkills[i] != null)
                list.Add(i);
        }
        return list.ToArray();
    }

    /// <summary>执行指定 index 技能(由 BossAttackDirector 调用;reservedOrbGroup 法球预约组可选)</summary>
    public void Execute(int index, string reservedOrbGroup = null)
    {
        if (allSkills == null || index < 0 || index >= allSkills.Length)
        {
            Debug.LogWarning($"[BossSkillSlots] 无效的技能 index: {index}");
            return;
        }

        var so = allSkills[index];
        if (so == null) return;

        if (currentCoroutine != null)
        {
            Debug.LogWarning($"[BossSkillSlots] 已有技能执行中,跳过 Execute({index})");
            return;
        }

        currentSkill = so;
        if (logSkillExecutions)
            Debug.Log($"[BossSkillSlots] 执行技能 [{index}] {so.skillName} (anim={so.animState})");

        OnSkillStarted?.Invoke(index);
        currentCoroutine = StartCoroutine(ExecuteRoutine(so, index, reservedOrbGroup));
    }

    /// <summary>强制中断当前技能(受击/死亡时调用)</summary>
    public void Interrupt()
    {
        if (currentCoroutine == null) return;

        if (logSkillExecutions)
            Debug.Log($"[BossSkillSlots] 中断技能 {currentSkill?.skillName}");

        StopCoroutine(currentCoroutine);
        currentCoroutine = null;

        // 先停特效,再杀技能体(顺序:防特效残留)
        if (_vfx == null) _vfx = GetComponentInChildren<AttackVFXAnchor>(true);
        _vfx?.Hide();

        if (currentInstance != null)
            Destroy(currentInstance);
        currentInstance = null;
        currentExecutor = null;

        int interruptedIndex = -1;
        var so = currentSkill;
        if (so != null)
        {
            for (int i = 0; i < allSkills.Length; i++)
            {
                if (allSkills[i] == so) { interruptedIndex = i; break; }
            }
        }
        currentSkill = null;
        OnSkillInterrupted?.Invoke(interruptedIndex);
    }

    /// <summary>设置当前阶段(由 BossControllerBase.OnPhaseChanged 调用,技能池预留)</summary>
    public void SetPhase(int phase)
    {
        currentPhase = phase;
    }

    /// <summary>动画事件入口(经 BossAnimationRelay 转发):技能命中帧</summary>
    public void OnSkillHitFrame()
    {
        currentExecutor?.OnHitFrame();
    }

    /// <summary>动画事件入口(经 BossAnimationRelay 转发):技能动画结束帧</summary>
    public void OnSkillAnimEnd()
    {
        currentExecutor?.OnAnimEnd();
    }

    // ============================================================
    // 执行协程
    // ============================================================

    private IEnumerator ExecuteRoutine(BossSkillData so, int index, string reservedOrbGroup = null)
    {
        // 实例化技能 prefab 挂 Boss 下(prefab 根上的执行器执行逻辑)
        if (so.skillPrefab != null)
        {
            currentInstance = Instantiate(so.skillPrefab, transform);
            currentExecutor = currentInstance.GetComponent<BossSkillExecutor>();
        }

        // 攻击持续 VFX:技能动画期间播对应槽(slot_<skillName>)
        if (_vfx == null) _vfx = GetComponentInChildren<AttackVFXAnchor>(true);
        _vfx?.Show("slot_" + so.skillName);

        if (currentExecutor != null)
        {
            currentExecutor.Data = so;
            var ctx = new BossSkillContext
            {
                boss = owner as BossControllerBase,
                player = player,
                slots = this,
                animator = animator,
                reservedOrbGroup = reservedOrbGroup
            };
            yield return currentExecutor.ExecuteSkill(ctx);
        }
        else
        {
            // 无执行器:只播动画,计时兜底(占位技能)
            if (animator != null && !string.IsNullOrEmpty(so.animState))
                animator.Play(so.animState);
            yield return new WaitForSeconds(1f);
        }

        if (currentInstance != null)
            Destroy(currentInstance);
        currentInstance = null;
        currentExecutor = null;
        currentSkill = null;
        currentCoroutine = null;

        // 技能正常结束:收起持续特效
        _vfx?.Hide();

        if (logSkillExecutions)
            Debug.Log($"[BossSkillSlots] 技能 [{index}] {so.skillName} 执行完毕");
        OnSkillFinished?.Invoke(index);
    }
}
