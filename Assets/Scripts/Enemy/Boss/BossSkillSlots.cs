using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

// ============================================================
// BossSkillSlots — 挂载在 Boss 身上，管理 N 个 BossAttackSO 的选择和调用
// ============================================================

/// <summary>
/// Boss 特殊技能管理器。在 Inspector 中填入 allSkills（BossAttackSO 数组）
/// 和 defaultMelee（冷却时 fallback 普攻），运行时由 BossControllerBase 调用。
/// </summary>
public class BossSkillSlots : MonoBehaviour
{
    // ============================================================
    // Inspector 配置
    // ============================================================

    [Header("技能列表")]
    [Tooltip("Boss 拥有的全部特殊技能 SO")]
    [SerializeField] private BossAttackSO[] allSkills;

    [Header("普攻 fallback")]
    [Tooltip("冷却时 fallback 的普攻组件")]
    [SerializeField] private EnemyMeleeAttack defaultMelee;

    // ============================================================
    // Debug
    // ============================================================

    [Header("VFX")]
    [Tooltip("冲撞拖尾 VFX — 冲撞期间每帧在 Boss 脚底生成")]
    [SerializeField] private GameObject chargeTrailVFXPrefab;
    [Tooltip("冲撞击墙 VFX — 冲撞撞墙时生成")]
    [SerializeField] private GameObject chargeImpactVFXPrefab;

    [Header("Debug")]
    [SerializeField] private bool showCooldownGizmos;
    [SerializeField] private bool disableCooldowns;
    [SerializeField] private bool logSkillExecutions;

    // ============================================================
    // 运行时状态
    // ============================================================

    private Dictionary<int, float> lastUseTime = new Dictionary<int, float>();
    private Coroutine currentCoroutine;
    private int currentExecutingIndex = -1;
    private EnemyControllerBase owner;
    private int currentPhase;
    private Transform player;
    private bool isQuitting;

    // ============================================================
    // 属性
    // ============================================================

    public int SkillCount => allSkills != null ? allSkills.Length : 0;

    /// <summary>是否有技能正在执行中</summary>
    public bool IsExecuting => currentCoroutine != null;

    /// <summary>当前正在执行的技能 index（-1 表示无）</summary>
    public int CurrentSkillIndex => currentExecutingIndex;

    // ============================================================
    // 事件（供 FSM / UI 订阅）
    // ============================================================

    /// <summary>技能开始执行（参数=技能 index）</summary>
    public event Action<int> OnSkillStarted;

    /// <summary>技能执行完毕（参数=技能 index）</summary>
    public event Action<int> OnSkillFinished;

    /// <summary>判定帧开始</summary>
    public event Action OnActiveFrameStart;

    /// <summary>判定帧结束</summary>
    public event Action OnActiveFrameEnd;

    /// <summary>技能被打断（受击/死亡，参数=技能 index）</summary>
    public event Action<int> OnSkillInterrupted;

    // ============================================================
    // 生命周期
    // ============================================================

    private void Awake()
    {
        owner = GetComponent<EnemyControllerBase>();
    }

    private void Start()
    {
        player = PlayerController.Instance?.transform;
    }

    private void OnDestroy()
    {
        isQuitting = true;
    }

    // ============================================================
    // 公开方法
    // ============================================================

    /// <summary>获取指定 index 的技能 SO</summary>
    public BossAttackSO GetSkill(int index)
    {
        if (allSkills == null || index < 0 || index >= allSkills.Length)
            return null;
        return allSkills[index];
    }

    /// <summary>执行指定 index 的技能（由 BossControllerBase 调用）</summary>
    public void Execute(int index)
    {
        if (allSkills == null || index < 0 || index >= allSkills.Length)
        {
            Debug.LogWarning($"[BossSkillSlots] 无效的技能 index: {index}");
            return;
        }

        var so = allSkills[index];
        if (so == null) return;

        // 阻止重复执行
        if (currentCoroutine != null)
        {
            Debug.LogWarning($"[BossSkillSlots] 已有技能执行中，跳过 Execute({index})");
            return;
        }

        // 记录冷却
        if (!disableCooldowns)
            lastUseTime[index] = Time.time;

        currentExecutingIndex = index;

        if (logSkillExecutions)
            Debug.Log($"[BossSkillSlots] 执行技能 [{index}] {so.skillName} (Type={so.skillType})");

        OnSkillStarted?.Invoke(index);

        // 按类型分发协程
        currentCoroutine = StartCoroutine(WrapSkill(so, index));
    }

    /// <summary>检查指定 index 是否在冷却中</summary>
    public bool IsOnCooldown(int index)
    {
        if (disableCooldowns) return false;
        if (!lastUseTime.TryGetValue(index, out float last)) return false;
        var so = GetSkill(index);
        if (so == null) return false;
        return Time.time - last < so.GetCooldownForPhase(currentPhase);
    }

    /// <summary>当前阶段是否已解锁指定 index 的技能</summary>
    public bool IsUnlocked(int index)
    {
        var so = GetSkill(index);
        if (so == null) return false;
        return so.IsUnlockedInPhase(currentPhase);
    }

    /// <summary>获取当前阶段已解锁 + 冷却完毕的技能 index 数组</summary>
    public int[] GetAvailableSkills()
    {
        if (allSkills == null) return Array.Empty<int>();
        var list = new List<int>();
        for (int i = 0; i < allSkills.Length; i++)
        {
            if (IsUnlocked(i) && !IsOnCooldown(i))
                list.Add(i);
        }
        return list.ToArray();
    }

    /// <summary>获取指定 index 的剩余冷却秒数（0=冷却完毕）</summary>
    public float GetCooldownRemaining(int index)
    {
        if (!lastUseTime.TryGetValue(index, out float last)) return 0f;
        var so = GetSkill(index);
        if (so == null) return 0f;
        float elapsed = Time.time - last;
        float cd = so.GetCooldownForPhase(currentPhase);
        return Mathf.Max(0f, cd - elapsed);
    }

    /// <summary>设置当前阶段（由 BossControllerBase.OnPhaseChanged 调用）</summary>
    public void SetPhase(int phase)
    {
        currentPhase = phase;
        if (logSkillExecutions)
            Debug.Log($"[BossSkillSlots] 阶段切换 → P{phase + 1}");
    }

    /// <summary>强制中断当前技能（受击/死亡时调用）</summary>
    public void Interrupt()
    {
        if (currentCoroutine != null)
        {
            if (logSkillExecutions)
                Debug.Log($"[BossSkillSlots] 中断技能 [{currentExecutingIndex}]");

            StopCoroutine(currentCoroutine);
            currentCoroutine = null;

            DisableAllHitboxes();
            OnActiveFrameEnd?.Invoke();

            int interruptedIndex = currentExecutingIndex;
            currentExecutingIndex = -1;
            OnSkillInterrupted?.Invoke(interruptedIndex);
        }
    }

    /// <summary>获取 fallback 普攻组件</summary>
    public EnemyMeleeAttack GetDefaultMelee() => defaultMelee;

    // ============================================================
    // 技能包装协程（统一处理前摇→判定→后摇生命周期 + 事件）
    // ============================================================

    private IEnumerator WrapSkill(BossAttackSO so, int index)
    {
        // 播放动画
        if (owner != null && !string.IsNullOrEmpty(so.animTrigger))
            owner.GetComponent<Animator>()?.SetTrigger(so.animTrigger);

        // 按类型分发
        yield return so.skillType switch
        {
            BossSkillType.Charge => ExecuteCharge(so),
            BossSkillType.Slam => ExecuteSlam(so),
            BossSkillType.Shockwave => ExecuteShockwave(so),
            BossSkillType.MeleeWrap => ExecuteMeleeWrap(so),
            BossSkillType.RangedWrap => ExecuteRangedWrap(so),
            BossSkillType.Combo => ExecuteCombo(so),
            _ => null
        };

        currentCoroutine = null;
        currentExecutingIndex = -1;
        OnSkillFinished?.Invoke(index);

        if (logSkillExecutions)
            Debug.Log($"[BossSkillSlots] 技能 [{index}] {so.skillName} 执行完毕");
    }

    // ============================================================
    // Charge（冲撞）协程
    // ============================================================

    private IEnumerator ExecuteCharge(BossAttackSO so)
    {
        // 1. 前摇
        float windup = so.windupTime;
        if (player != null)
            owner.UpdateFacing(player.position.x > transform.position.x ? 1 : -1);
        yield return new WaitForSeconds(windup);

        // 2. 冲撞阶段
        OnActiveFrameStart?.Invoke();
        Vector2 chargeDir = player != null
            ? ((Vector2)(player.position - transform.position)).normalized
            : Vector2.right * owner.Facing;

        float traveled = 0f;
        float chargeSpeed = GetBossBaseSpeed() * so.chargeSpeedMultiplier;

        while (traveled < so.chargeMaxDistance)
        {
            float step = chargeSpeed * Time.deltaTime;

            // 撞墙检测
            if (so.chargeStopOnWall && CheckWall(chargeDir))
            {
                // 撞墙 VFX
                if (chargeImpactVFXPrefab != null)
                {
                    Vector2 hitPoint = (Vector2)transform.position + chargeDir * 0.5f;
                    VFXSpawner.SpawnOnBoss(chargeImpactVFXPrefab, hitPoint);
                }
                break;
            }

            transform.Translate(chargeDir * step);
            traveled += step;

            // 冲撞拖尾 VFX — 每帧在脚底位置生成
            if (chargeTrailVFXPrefab != null)
            {
                Vector2 feetPos = (Vector2)transform.position + Vector2.down * 0.5f;
                VFXSpawner.SpawnOnBoss(chargeTrailVFXPrefab, feetPos);
            }

            // 持续判定：矩形检测
            CheckChargeHitbox(so, chargeDir);

            yield return null;
        }
        OnActiveFrameEnd?.Invoke();

        // 3. 后摇
        yield return new WaitForSeconds(so.recoveryTime);
    }

    private void CheckChargeHitbox(BossAttackSO so, Vector2 dir)
    {
        if (player == null) return;
        Vector2 center = (Vector2)transform.position + dir * so.chargeMaxDistance * 0.5f;
        Vector2 size = new Vector2(so.chargeHitboxWidth, so.chargeHitboxHeight);

        var hits = Physics2D.OverlapBoxAll(center, size, 0f);
        foreach (var hit in hits)
        {
            if (!hit.CompareTag("Player")) continue;
            PlayerController pc = hit.GetComponent<PlayerController>();
            if (pc != null)
            {
                Vector2 knockDir = dir;
                knockDir.y = 0f;
                // P4c:统一走 CombatResolver 结算(原 pc.TakeDamageWithKnockback;攻击标签按原无标签,击退按原硬编码 10f/0.2s)
                var ph = pc.GetComponent<PlayerHealth>();
                if (ph != null)
                {
                    CombatResolver.Resolve(owner, ph, new DamageInfo
                    {
                        amount = so.damage,
                        source = owner,
                        sourcePosition = (Vector2)transform.position,
                        attackLabel = "",
                        knockback = new Knockback
                        {
                            direction = knockDir,
                            force = 10f,     // 原 TakeDamageWithKnockback 硬编码击退力度
                            duration = 0.2f, // 原 KnockbackRoutine 硬编码硬直时长
                            ignoreResistance = false
                        }
                    });
                }
                if (so.hitVFXPrefab != null)
                    VFXSpawner.SpawnOnPlayer(so.hitVFXPrefab, pc.transform.position);
            }
        }
    }

    // ============================================================
    // Slam（砸地 AOE）协程
    // ============================================================

    private IEnumerator ExecuteSlam(BossAttackSO so)
    {
        // 1. 前摇
        yield return new WaitForSeconds(so.windupTime);

        // 2. 判定帧
        OnActiveFrameStart?.Invoke();
        Vector2 center = (Vector2)transform.position + so.slamOffset;
        var hits = Physics2D.OverlapCircleAll(center, so.slamRadius);
        foreach (var hit in hits)
        {
            if (!hit.CompareTag("Player")) continue;
            PlayerController pc = hit.GetComponent<PlayerController>();
            if (pc != null)
            {
                float kb = so.slamKnockbackOverride > 0f ? so.slamKnockbackOverride : so.knockbackForce;
                Vector2 knockDir = ((Vector2)(pc.transform.position - (Vector3)center)).normalized;
                knockDir.y = 0f;
                if (knockDir.magnitude < 0.01f) knockDir = Vector2.right;
                // P4c:统一走 CombatResolver 结算(原 pc.TakeDamageWithKnockback;攻击标签按原无标签,击退按原硬编码 10f/0.2s)
                var ph = pc.GetComponent<PlayerHealth>();
                if (ph != null)
                {
                    CombatResolver.Resolve(owner, ph, new DamageInfo
                    {
                        amount = so.damage,
                        source = owner,
                        sourcePosition = (Vector2)transform.position,
                        attackLabel = "",
                        knockback = new Knockback
                        {
                            direction = knockDir,
                            force = 10f,     // 原 TakeDamageWithKnockback 硬编码击退力度
                            duration = 0.2f, // 原 KnockbackRoutine 硬编码硬直时长
                            ignoreResistance = false
                        }
                    });
                }
                if (so.hitVFXPrefab != null)
                    VFXSpawner.SpawnOnPlayer(so.hitVFXPrefab, pc.transform.position);
            }
        }

        // 屏幕震动
        if (so.slamScreenShakeIntensity > 0f)
            CameraShake(so.slamScreenShakeIntensity, so.slamScreenShakeDuration);

        // 地面特效
        if (so.slamGroundVFXPrefab != null)
            VFXSpawner.SpawnOnBoss(so.slamGroundVFXPrefab, center);

        yield return new WaitForSeconds(so.activeTime);
        OnActiveFrameEnd?.Invoke();

        // 3. 后摇
        yield return new WaitForSeconds(so.recoveryTime);
    }

    // ============================================================
    // Shockwave（地面波）协程
    // ============================================================

    private IEnumerator ExecuteShockwave(BossAttackSO so)
    {
        // 1. 前摇
        yield return new WaitForSeconds(so.windupTime);

        // 2. 发射地面波
        OnActiveFrameStart?.Invoke();
        Vector2 spawnPos = (Vector2)transform.position + so.waveSpawnOffset;
        Vector2 dir = player != null
            ? ((Vector2)(player.position - transform.position)).normalized
            : Vector2.right * owner.Facing;

        for (int i = 0; i < so.waveCount; i++)
        {
            float angle = 0f;
            if (so.waveCount > 1)
                angle = -so.waveSpreadAngle / 2f + so.waveSpreadAngle * i / (so.waveCount - 1);
            Vector2 waveDir = Quaternion.Euler(0f, 0f, angle) * dir;

            GameObject wavePrefab = so.wavePrefab;
            if (wavePrefab == null)
            {
                // 没有 prefab 时：创建默认圆形投射物
                wavePrefab = CreateDefaultWave();
            }
            var wave = Instantiate(wavePrefab, spawnPos, Quaternion.identity);
            var proj = wave.GetComponent<ShockwaveProjectile>();
            if (proj == null)
                proj = wave.AddComponent<ShockwaveProjectile>();
            proj.Initialize(waveDir, so.waveSpeed, so.waveMaxDistance,
                            so.waveHeight, so.damage, so.knockbackForce);
            proj.SetSource(owner); // P1a:携带发射者，命中玩家时作为 DamageInfo.source 触发弹反等结算
        }

        yield return new WaitForSeconds(so.activeTime);
        OnActiveFrameEnd?.Invoke();

        // 3. 后摇
        yield return new WaitForSeconds(so.recoveryTime);
    }

    /// <summary>创建一个默认的冲击波 GameObject（当没有 wavePrefab 时）</summary>
    private GameObject CreateDefaultWave()
    {
        var go = new GameObject("Shockwave_Default");
        // 添加碰撞体（Trigger）
        var col = go.AddComponent<BoxCollider2D>();
        col.isTrigger = true;
        col.size = new Vector2(1f, 0.5f);
        // 添加 SpriteRenderer 作可视化
        var sr = go.AddComponent<SpriteRenderer>();
        sr.sprite = CreateSquareSprite();
        sr.color = new Color(0.3f, 0.5f, 1f, 0.6f);
        sr.drawMode = SpriteDrawMode.Sliced;
        sr.size = new Vector2(1f, 0.3f);
        return go;
    }

    private static Sprite CreateSquareSprite()
    {
        // 1x1 白色纹理，Unity 会将 SpriteRenderer 颜色叠加
        var tex = new Texture2D(4, 4);
        var colors = new Color[16];
        for (int i = 0; i < 16; i++) colors[i] = Color.white;
        tex.SetPixels(colors);
        tex.Apply();
        return Sprite.Create(tex, new Rect(0, 0, 4, 4), new Vector2(0.5f, 0.5f), 4);
    }

    // ============================================================
    // MeleeWrap（近战包装）协程
    // ============================================================

    private IEnumerator ExecuteMeleeWrap(BossAttackSO so)
    {
        var melee = so.wrappedAttack;
        if (melee == null)
        {
            Debug.LogWarning($"[BossSkillSlots] MeleeWrap 缺少 wrappedAttack 引用");
            yield break;
        }

        // 前摇
        yield return new WaitForSeconds(so.windupTime);

        // 判定
        OnActiveFrameStart?.Invoke();
        melee.PerformAttack(owner);
        yield return new WaitForSeconds(so.activeTime);
        OnActiveFrameEnd?.Invoke();

        // 后摇
        yield return new WaitForSeconds(so.recoveryTime);
    }

    // ============================================================
    // RangedWrap（远程包装）协程
    // ============================================================

    private IEnumerator ExecuteRangedWrap(BossAttackSO so)
    {
        var ranged = so.wrappedRangedAttack;
        if (ranged == null)
        {
            Debug.LogWarning($"[BossSkillSlots] RangedWrap 缺少 wrappedRangedAttack 引用");
            yield break;
        }

        // 前摇
        yield return new WaitForSeconds(so.windupTime);

        // 判定
        OnActiveFrameStart?.Invoke();
        ranged.PerformAttack(owner);
        yield return new WaitForSeconds(so.activeTime);
        OnActiveFrameEnd?.Invoke();

        // 后摇
        yield return new WaitForSeconds(so.recoveryTime);
    }

    // ============================================================
    // Combo（连击）协程
    // ============================================================

    private IEnumerator ExecuteCombo(BossAttackSO so)
    {
        if (so.comboAttacks == null || so.comboAttacks.Length == 0)
        {
            Debug.LogWarning($"[BossSkillSlots] Combo 缺少 comboAttacks 子技能");
            yield break;
        }

        // 前摇
        yield return new WaitForSeconds(so.windupTime);

        for (int i = 0; i < so.comboAttacks.Length; i++)
        {
            var sub = so.comboAttacks[i];
            if (sub == null) continue;

            bool isLast = (i == so.comboAttacks.Length - 1);

            // 最后一击覆写格挡/弹反属性
            bool origBlock = sub.canBeBlocked;
            bool origParry = sub.canBeParried;
            float origDamage = sub.damage;

            if (isLast && so.finalHitUnblockable) sub.canBeBlocked = false;
            if (isLast && so.finalHitUnparriable) sub.canBeParried = false;
            if (isLast && so.finalHitExtraDamage > 0f) sub.damage += so.finalHitExtraDamage;

            // 执行子技能
            yield return ExecuteSubSkill(sub);

            // 恢复
            sub.canBeBlocked = origBlock;
            sub.canBeParried = origParry;
            sub.damage = origDamage;

            if (!isLast)
                yield return new WaitForSeconds(so.comboInterval);
        }

        // 后摇
        yield return new WaitForSeconds(so.recoveryTime);
    }

    /// <summary>执行单个子技能（Combo 内部使用）</summary>
    private IEnumerator ExecuteSubSkill(BossAttackSO subSo)
    {
        OnActiveFrameStart?.Invoke();
        yield return subSo.skillType switch
        {
            BossSkillType.Charge => ExecuteCharge(subSo),
            BossSkillType.Slam => ExecuteSlam(subSo),
            BossSkillType.Shockwave => ExecuteShockwave(subSo),
            BossSkillType.MeleeWrap => ExecuteMeleeWrap(subSo),
            BossSkillType.RangedWrap => ExecuteRangedWrap(subSo),
            _ => null
        };
        OnActiveFrameEnd?.Invoke();
    }

    // ============================================================
    // 辅助方法
    // ============================================================

    /// <summary>获取 Boss 基础移动速度（从 owner 推断）</summary>
    private float GetBossBaseSpeed()
    {
        // 优先使用 FirstBoss 的 CurrentMoveSpeed
        if (owner is FirstBoss fb)
            return fb.CurrentMoveSpeed;
        // 回退：默认速度
        return 3f;
    }

    /// <summary>前方墙壁检测</summary>
    private bool CheckWall(Vector2 dir)
    {
        float checkDist = 0.5f;
        RaycastHit2D hit = Physics2D.Raycast(transform.position, dir, checkDist,
            LayerMask.GetMask("Ground", "Wall"));
        return hit.collider != null;
    }

    /// <summary>关闭所有判定框（Interrupt 时调用）</summary>
    private void DisableAllHitboxes()
    {
        // 子类的具体判定框由各自协程的 yield break 自然终止
        // 这里只做事件通知
    }

    /// <summary>屏幕震动辅助</summary>
    private static void CameraShake(float intensity, float duration)
    {
        // 如果项目有 CameraShake 组件，在这里调用
        // CameraShake.Instance?.Shake(intensity, duration);
        // 没有则静默跳过
    }

    // ============================================================
    // Gizmos (Debug)
    // ============================================================

#if UNITY_EDITOR
    private void OnDrawGizmosSelected()
    {
        if (!showCooldownGizmos || allSkills == null) return;

        Vector3 basePos = transform.position + Vector3.up * 0.5f;
        for (int i = 0; i < allSkills.Length; i++)
        {
            var so = allSkills[i];
            if (so == null) continue;

            float remaining = Application.isPlaying ? GetCooldownRemaining(i) : 0f;
            float total = so.GetCooldownForPhase(currentPhase);
            float ratio = total > 0f ? 1f - (remaining / total) : 1f;

            Vector3 pos = basePos + Vector3.right * i * 1.2f;

            Gizmos.color = Color.gray;
            Gizmos.DrawWireCube(pos, new Vector3(1f, 0.3f, 0f));

            Gizmos.color = remaining > 0f ? Color.red : Color.green;
            Gizmos.DrawCube(pos, new Vector3(1f * ratio, 0.25f, 0f));

#if UNITY_EDITOR
            UnityEditor.Handles.Label(pos + Vector3.up * 0.3f,
                $"[{i}] {so.skillName}",
                new GUIStyle() { normal = new GUIStyle().normal, fontSize = 10 });
#endif
        }
    }
#endif
}
