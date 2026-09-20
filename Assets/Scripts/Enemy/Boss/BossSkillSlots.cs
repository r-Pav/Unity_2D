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

    [Header("起手瞬移(P4)")]
    [Tooltip("起手瞬移落点距离:落点 x = 玩家 x + 玩家面朝方向 × 本值(与重击 teleportDistance 同口径)。仅对勾了 BossSkillData.trackPlayerBeforeCast 的技能生效")]
    [SerializeField] private float castTeleportDistance = 1.5f;

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
    private PlayerController playerCtrl;   // [P4] 玩家控制器(取玩家朝向/位置用,与 player 同源)
    private int currentPhase;
    private bool isQuitting;

    private Coroutine currentCoroutine;
    private BossSkillData currentSkill;
    private BossSkillExecutor currentExecutor;
    private GameObject currentInstance;

    // [2026-09-07 AttackVFXAnchor 重构暂停:Boss 技能持续特效待玩家侧验收后按新结构迁移]
    ///// <summary>攻击持续 VFX 锚点(attack_VFX 子物体上的 AttackVFXAnchor;未配置时为 null,空安全)</summary>
    //private AttackVFXAnchor _vfx;

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
        playerCtrl = PlayerController.Instance;
        player = playerCtrl != null ? playerCtrl.transform : null;
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

    /// <summary>技能动画开关置位(参数名 = data.animState;名字为空时安全跳过)</summary>
    private void SetSkillAnimBool(string paramName, bool value)
    {
        if (string.IsNullOrEmpty(paramName)) return;
        if (animator == null)
            animator = owner != null ? owner.GetComponentInChildren<Animator>() : GetComponentInChildren<Animator>();
        if (animator != null)
            animator.SetBool(paramName, value);
    }

    /// <summary>强制中断当前技能(受击/死亡时调用)</summary>
    public void Interrupt()
    {
        if (currentCoroutine == null) return;

        if (logSkillExecutions)
            Debug.Log($"[BossSkillSlots] 中断技能 {currentSkill?.skillName}");

        StopCoroutine(currentCoroutine);
        currentCoroutine = null;

        // [2026-09-07 AttackVFXAnchor 重构暂停] 先停特效,再杀技能体(顺序:防特效残留)
        //if (_vfx == null) _vfx = GetComponentInChildren<AttackVFXAnchor>(true);
        //_vfx?.Hide();

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
        SetSkillAnimBool(so != null ? so.animState : null, false);   // 技能动画开关复位(参数名 = data.animState)
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

        // [2026-09-07 AttackVFXAnchor 重构暂停] 攻击持续 VFX:技能动画期间播对应槽(slot_<skillName>)
        //if (_vfx == null) _vfx = GetComponentInChildren<AttackVFXAnchor>(true);
        //_vfx?.Show("slot_" + so.skillName);

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

            // [P4] 起手瞬移(可选标记):释放前先瞬移到玩家身边再起手。
            //   此刻起手前霸体(P1)已由 Director 置真 → IsSkillCasting 为真,玩家普攻打不进 Hurt,无需额外霸体代码。
            TryTeleportToPlayerBeforeCast(so);

            yield return currentExecutor.ExecuteSkill(ctx);
        }
        else
        {
            // 无执行器:只开动画开关,计时兜底(占位技能)
            SetSkillAnimBool(so.animState, true);
            yield return new WaitForSeconds(1f);
        }

        if (currentInstance != null)
            Destroy(currentInstance);
        currentInstance = null;
        currentExecutor = null;

        // 技能动画开关复位(参数名 = data.animState):置假后动画器走 Exit 回 Entry,落回待机/追击
        SetSkillAnimBool(so.animState, false);

        currentSkill = null;
        currentCoroutine = null;

        // [2026-09-07 AttackVFXAnchor 重构暂停] 技能正常结束:收起持续特效
        //_vfx?.Hide();

        if (logSkillExecutions)
            Debug.Log($"[BossSkillSlots] 技能 [{index}] {so.skillName} 执行完毕");
        OnSkillFinished?.Invoke(index);
    }

    // ============================================================
    // [P4] 起手瞬移(可选标记)
    // ============================================================

    /// <summary>
    /// [P4] 起手瞬移:so.trackPlayerBeforeCast 为真时,释放前把 Boss 瞬移到玩家身边再起手。
    /// 顺序:原位生成消失表现 → 算落点(玩家位置/朝向快照 → 玩家面朝方向 × castTeleportDistance,
    ///      该侧被实心墙/管道挡住则翻到玩家另一侧)→ ForceSetPosition(内含贴墙钳制 + 清速度,必须用返回值)
    ///      → 转身朝玩家 → 落点生成出现表现。
    /// 取不到玩家 / 未勾标记 / 落点不可用 → 直接返回,原地起手(不阻塞技能)。
    /// 只走现成 API:EnemyControllerBase.ForceSetPosition / IsWallBlockedOnSide + VFXSpawner.SpawnOnBoss,
    /// 不新增射线/碰撞器/组件。
    /// 已知取舍(入代码评审用):IsWallBlockedOnSide 的检测带以 Boss 自身为中心(其固定口径),
    ///   Boss 离玩家较远时该判定偏向自身一侧 → 漏判时由 ForceSetPosition 内部的贴墙钳制兜底,
    ///   最坏结果 = Boss 落在墙外侧而非翻侧,不会穿墙。
    /// </summary>
    private void TryTeleportToPlayerBeforeCast(BossSkillData so)
    {
        if (so == null || !so.trackPlayerBeforeCast) return;
        if (owner == null) return;

        // 玩家引用:Start 时缓存;Start 早于玩家生成(Instance 为空)时在这里补取一次
        if (playerCtrl == null)
        {
            playerCtrl = PlayerController.Instance;
            player = playerCtrl != null ? playerCtrl.transform : null;
        }
        if (playerCtrl == null || player == null) return;   // 取不到玩家:跳过瞬移,直接起手

        // ── ① 原位:消失表现(先取快照位置,下一行 Boss 就瞬移走了) ──
        Vector3 origin = owner.transform.position;
        VFXSpawner.SpawnOnBoss(so.disappearVFXPrefab, origin);

        // ── ② 落点:玩家位置/朝向快照 → x = 玩家 x + 玩家面朝方向 × 距离(与 BossHeavyAttack.ExecuteHeavy 同口径) ──
        Vector2 playerSnap = player.position;
        float dirSign = playerCtrl.FacingDir >= 0 ? 1f : -1f;
        float distance = Mathf.Max(0.1f, castTeleportDistance);
        Vector2 landing = new Vector2(playerSnap.x + dirSign * distance, origin.y);

        // ── ③ 可达性:落点那侧被实心墙/管道挡住 → 翻到玩家另一侧 ──
        if (owner.IsWallBlockedOnSide(dirSign >= 0f ? 1 : -1))
            landing.x = playerSnap.x - dirSign * distance;

        // ── ④ 落点执行:ForceSetPosition 内含贴墙钳制 + 清速度,返回值才是实际落点 ──
        Vector2 actual = owner.ForceSetPosition(landing);

        // ── ⑤ 朝向:面向玩家(不锁朝向——瞬移只是起手前定位,施法朝向交给技能执行器) ──
        owner.UpdateFacing((player.position.x - actual.x) >= 0f ? 1f : -1f);

        // ── ⑥ 落点:出现表现 ──
        VFXSpawner.SpawnOnBoss(so.appearVFXPrefab, actual);

        if (logSkillExecutions)
            Debug.Log($"[BossSkillSlots] 起手瞬移 {origin} → {actual}(玩家快照={playerSnap} 朝向={dirSign})");
    }
}
