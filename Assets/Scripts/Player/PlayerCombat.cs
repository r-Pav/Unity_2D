using UnityEngine;
using System.Collections;

/// <summary>
/// 玩家战斗模块（子组件）— 由 PlayerController 自动查找并调用
/// 挂到 Player 对象上即可激活攻击功能，不挂则无
/// 遵循组件模式：主组件控制流程，子组件实现具体功能
/// P1 改造：引用 StatModifierManager，伤害倍率加成，公开闪避/减伤判定方法
/// 近战改造：引入 IAttackExecutor 接口，滚轮/Tab 切换近战/远程模式
/// 近战三连击：武器挥砍动画 + 中间帧 OverlapBox 判定 + 连击窗口链式输入
/// </summary>
public class PlayerCombat : MonoBehaviour
{
    // ============================================================
    // 攻击模式
    // ============================================================

    /// <summary>攻击模式枚举 — Events.cs 中的 AttackModeSwitchedEvent 依赖此定义</summary>
    public enum AttackMode { Ranged, Melee }

    // ============================================================
    // 攻击执行器接口（内部 — 装备系统可提取到独立文件）
    // ============================================================

    /// <summary>
    /// 攻击执行器接口 — 当前在 PlayerCombat 内部定义，
    /// 后续装备系统可提取到独立文件并外部注入。
    /// </summary>
    internal interface IAttackExecutor
    {
        void Execute(PlayerCombat combat, PlayerController owner);
    }

    /// <summary>远程攻击执行器 — 封装现有 BurstFire 逻辑</summary>
    private class RangedAttackExecutor : IAttackExecutor
    {
        public void Execute(PlayerCombat combat, PlayerController owner)
        {
            owner.StartCoroutine(combat.BurstFire());
        }
    }

    /// <summary>近战攻击执行器 — 封装三连击逻辑</summary>
    private class MeleeAttackExecutor : IAttackExecutor
    {
        public void Execute(PlayerCombat combat, PlayerController owner)
        {
            combat.ExecuteMeleeAttack();
        }
    }

    // ============================================================
    // 配置参数 —— 远程
    // ============================================================

    [Header("远程")]
    [Tooltip("每次攻击基础伤害（实际伤害 = 基础值 × 伤害倍率）")]
    [SerializeField] private float attackDamage = 1f;

    [Tooltip("攻击间隔（秒）— 两次单击之间的冷却")]
    [SerializeField] private float attackCooldown = 0.3f;

    [Tooltip("每次单击发射子弹数")]
    [SerializeField] private int shotsPerClick = 1;

    [Tooltip("连发间隔（秒）— 同一次单击内每颗子弹的间隔")]
    [SerializeField] private float burstInterval = 0.05f;

    [Tooltip("子弹散射角度（度）— 0 = 直线")]
    [SerializeField] private float bulletSpreadAngle = 5f;

    [Tooltip("子弹飞行速度（单位/秒）")]
    [SerializeField] private float bulletSpeed = 10f;

    [Tooltip("子弹颜色")]
    [SerializeField] private Color bulletColor = Color.cyan;

    [Tooltip("子弹球体半径")]
    [SerializeField] private float bulletRadius = 0.15f;

    [Header("远程 VFX")]
    [Tooltip("枪口闪光特效 Prefab — 在 BurstFire 发射子弹前生成")]
    [SerializeField] private GameObject muzzleFlashVFXPrefab;

    [Tooltip("远程攻击类型标签 — 传给 Enemy TakeDamage 用于匹配 VFX 变体")]
    [SerializeField] private string rangedAttackType = "Bullet";

    [Tooltip("敌人的 Layer")]
    [SerializeField] private LayerMask enemyLayer = ~0;

    [Tooltip("子弹不能穿过的墙 Layer")]
    [SerializeField] private LayerMask wallLayer = 0;

    // ============================================================
    // 配置参数 —— 近战
    // ============================================================

    [Header("近战")]
    [Tooltip("备用切换键（None = 不用）")]
    [SerializeField] private KeyCode meleeAltSwitchKey = KeyCode.Tab;

    [Tooltip("近战攻击类型标签 — 传给 Enemy TakeDamage 用于匹配 VFX 变体")]
    [SerializeField] private string meleeAttackType = "Sword";

    [Tooltip("近战第三段攻击类型标签 — 单独标记用于霸体计数")]
    [SerializeField] private string meleeFinisherAttackType = "Sword_Heavy";

    [Header("近战范围指示器")]
    [Tooltip("拖入 Player 下的攻击范围 Sprite（挂 MeleeRangeIndicator）")]
    [SerializeField] private MeleeRangeIndicator rangeIndicator;
    [Tooltip("攻击范围中心在 Player 前方的距离")]
    [SerializeField] private float meleeRangeOffset = 1.5f;

    [Header("近战 VFX")]
    [Tooltip("近战挥砍命中特效 Prefab — 在 OverlapBox 检测到敌人时生成")]
    [SerializeField] private GameObject slashVFXPrefab;

    [Header("攻击模式")]
    [Tooltip("初始攻击模式")]
    [SerializeField] private AttackMode startMode = AttackMode.Ranged;

    [Header("格挡/弹反")]
    [Tooltip("弹反判定最大时长(秒) — 短按松手 ≤ 此值判定为弹反")]
    [SerializeField] private float parryMaxWindow = 0.2f;
    [Tooltip("格挡减伤率 [0~1]")]
    [SerializeField] private float blockDamageReduction = 0.5f;
    [Tooltip("子弹 Layer（用于近战消除子弹的 OverlapBox 检测）")]
    [SerializeField] private LayerMask projectileLayer = 0;

    [Header("格挡/弹反 — 视觉（测试用，后续替换为 Animator）")]
    [Tooltip("玩家渲染器（测试用换色）")]
    [SerializeField] private SpriteRenderer playerRenderer;
    [Tooltip("格挡时颜色")]
    [SerializeField] private Color blockColor = new Color(0.3f, 0.5f, 1f, 1f);
    [Tooltip("弹反成功闪烁颜色")]
    [SerializeField] private Color parrySuccessColor = Color.yellow;
    [Tooltip("弹反闪烁时长（秒）")]
    [SerializeField] private float parryFlashDuration = 0.2f;
    [Tooltip("[预留] 弹反成功 Animator Trigger 名")]
    [SerializeField] private string parrySuccessAnimTrigger = "";
    [Tooltip("[预留] 格挡开始 Animator Trigger 名")]
    [SerializeField] private string blockStartAnimTrigger = "";
    [Tooltip("[预留] 格挡结束 Animator Trigger 名")]
    [SerializeField] private string blockEndAnimTrigger = "";

    // ============================================================
    // 配置参数 —— 近战
    // ============================================================

    [Header("近战")]
    [Tooltip("近战伤害")]
    [SerializeField] private float meleeDamage = 1f;

    [Tooltip("近战攻击冷却（秒）— 需短于 Attack1 动画时长，保证连击排队窗口存在")]
    [SerializeField] private float meleeAttackCooldown = 0.15f;

    [Tooltip("近战击退力度（直接施加到敌人 Rigidbody2D）")]
    [SerializeField] private float meleeKnockbackForce = 4f;

    [Tooltip("近战击退上挑力度（Y 轴最小值，保证敌人浮空）")]
    [SerializeField] private float meleeKnockbackUpForce = 0.3f;

    [Tooltip("近战命中卡肉时长（秒）")]
    [SerializeField] private float meleeHitStopDuration = 0.08f;

    // ============================================================
    // 运行时状态
    // ============================================================

    private float attackCooldownTimer;
    private bool isBursting;
    private PlayerController _owner;
    private PlayerAimLine aimLine;
    private StatModifierManager statModManager;
    private PassiveEquipManager passiveEquipManager;
    private CharacterBase _charBase;

    // 延迟初始化：CharacterBase 的 Awake 可能未跑完，在访问时懒加载
    private Animator Anim => _charBase != null ? _charBase.Animator : null;

    [Header("连击")]
    [Tooltip("连击重置时间（秒）— 超过此时间未连击则 comboIndex 重置为 1")]
    [SerializeField] private float comboResetTimer = 0.6f;

    private int comboIndex = 1;
    private const int comboLimit = 3;
    private float timeLastAttackExit;
    private bool _inAttackAnim;

    /// <summary>战斗超时计时器</summary>
    private float combatTimeoutTimer;
    private const float CombatTimeoutDuration = 5f;
    private bool playerCombatFlag; // 玩家侧 combat 标记（防止重复 +1）

    /// <summary>攻击时触发（供 PlayerController 订阅，用于战斗态锁定）</summary>
    public System.Action OnAttack;

    // ── 近战模式 ──
    private IAttackExecutor _currentExecutor;
    private readonly RangedAttackExecutor _rangedExec = new RangedAttackExecutor();
    private readonly MeleeAttackExecutor _meleeExec = new MeleeAttackExecutor();

    /// <summary>当前攻击模式（公开属性，供 HUD 读取）</summary>
    public AttackMode CurrentMode { get; private set; }

    /// <summary>基础发射数（只读，供 UI 面板读取）</summary>
    public int BaseShotsPerClick => shotsPerClick;

    /// <summary>基础攻击冷却（只读，供 UI 面板读取）</summary>
    public float BaseAttackCooldown => attackCooldown;

    public bool IsAttacking => _inAttackAnim;
    public bool IsInputLocked { get; private set; }

    // ── 格挡/弹反 ──
    /// <summary>是否正在格挡（按住右键）</summary>
    private bool isBlocking;

    /// <summary>是否正在格挡（C# 字段为状态源）</summary>
    public bool IsBlocking => isBlocking;

    /// <summary>是否持有弹反 Buff</summary>
    private bool hasParryBuff;
    /// <summary>格挡开始时间（用于区分长短按）</summary>
    private float blockStartTime;
    /// <summary>玩家原始颜色（格挡/弹反恢复用）</summary>
    private Color _playerOriginalColor;
    private Coroutine _parryFlashRoutine;

    /// <summary>格挡减伤修饰器 source 标识</summary>
    private const string BlockModSource = "Blocking";

    // ============================================================
    // 生命周期
    // ============================================================

    private void Awake()
    {
        aimLine = GetComponent<PlayerAimLine>();
        statModManager = GetComponent<StatModifierManager>();
        passiveEquipManager = GetComponent<PassiveEquipManager>();
        _charBase = GetComponent<CharacterBase>();

        if (playerRenderer != null)
            _playerOriginalColor = playerRenderer.color;

        // 初始化攻击执行器
        CurrentMode = startMode;
        _currentExecutor = CurrentMode == AttackMode.Melee
            ? (IAttackExecutor)_meleeExec : _rangedExec;

        // 初始状态：远程默认隐藏范围指示器
        if (rangeIndicator != null)
            rangeIndicator.gameObject.SetActive(CurrentMode == AttackMode.Melee);
    }

    private void OnEnable()
    {
        EventBus.Subscribe<PlayerDeathEvent>(OnPlayerDeath);
    }

    private void OnDisable()
    {
        EventBus.Unsubscribe<PlayerDeathEvent>(OnPlayerDeath);
        // 组件禁用时清理格挡状态
        CancelBlock();
    }

    // ============================================================
    // 父类调用接口
    // ============================================================

    /// <summary>每帧递减攻击冷却 / 战斗超时 / 连击窗口，处理输入</summary>
    public void OnPlayerUpdate(PlayerController owner)
    {
        _owner = owner;
        TickTimers();
        CheckModeSwitch(owner);
        HandleBlockParryInput(owner);
        TryAttack(owner);
    }

    private void TickTimers()
    {
        // 攻击冷却
        if (attackCooldownTimer > 0f)
        {
            float intervalMult = statModManager != null
                ? statModManager.GetFinalValue(1f, StatId.AttackInterval)
                : 1f;
            attackCooldownTimer -= Time.deltaTime * intervalMult;
        }

        // 战斗超时
        if (combatTimeoutTimer > 0f)
        {
            combatTimeoutTimer -= Time.deltaTime;
            if (combatTimeoutTimer <= 0f && playerCombatFlag)
            {
                passiveEquipManager?.SetCombatState(false);
                playerCombatFlag = false;
            }
        }

    }

    // ============================================================
    // 模式切换
    // ============================================================

    /// <summary>
    /// 检测滚轮 / Tab 键切换攻击模式
    /// 战斗中可切换、Dash 中可切换、不重置 CD
    /// </summary>
    private void CheckModeSwitch(PlayerController owner)
    {
        // UI 面板打开时阻止切换（如果实现了 ScrollBlocked）
        if (owner != null && owner.ScrollBlocked) return;

        // 滚轮主切换：上滚 → 近战，下滚 → 远程
        float scroll = Input.mouseScrollDelta.y;
        if (Mathf.Abs(scroll) > 0.01f)
        {
            CurrentMode = scroll > 0 ? AttackMode.Melee : AttackMode.Ranged;
            _currentExecutor = CurrentMode == AttackMode.Melee
                ? (IAttackExecutor)_meleeExec : _rangedExec;
            OnModeSwitched();
            return;
        }

        // Tab 备用切换
        if (meleeAltSwitchKey != KeyCode.None
            && Input.GetKeyDown(meleeAltSwitchKey))
        {
            CurrentMode = CurrentMode == AttackMode.Melee
                ? AttackMode.Ranged : AttackMode.Melee;
            _currentExecutor = CurrentMode == AttackMode.Melee
                ? (IAttackExecutor)_meleeExec : _rangedExec;
            OnModeSwitched();
        }
    }

    /// <summary>模式切换后触发事件，通知 HUD 更新图标</summary>
    private void OnModeSwitched()
    {
        // 近战隐藏瞄准线，远程恢复
        if (aimLine != null)
        {
            if (CurrentMode == AttackMode.Melee)
                aimLine.Hide();
            else
            {
                aimLine.enabled = true;
                aimLine.GetComponent<LineRenderer>().positionCount = 2;
            }
        }
        // 近战范围指示器：近战显示，远程隐藏
        if (rangeIndicator != null)
            rangeIndicator.gameObject.SetActive(CurrentMode == AttackMode.Melee);

        // 切换离开近战模式
        if (CurrentMode != AttackMode.Melee)

        EventBus.Trigger(new AttackModeSwitchedEvent(CurrentMode));
    }

    /// <summary>
    /// [预留] 装备系统入口 — 外部注入自定义攻击执行器。
    /// 调用后内部 exec 不再自动切换，由装备系统控制。
    /// </summary>
    internal void SetAttackExecutor(IAttackExecutor executor)
    {
        _currentExecutor = executor;
    }

    // ============================================================
    // 远程攻击
    // ============================================================

    private IEnumerator BurstFire()
    {
        isBursting = true;
        OnAttack?.Invoke();
        TriggerAttack();

        Vector2 gunMuzzlePos = (Vector2)transform.position + Vector2.right * AttackDir * 0.5f;
        VFXSpawner.SpawnOnPlayer(muzzleFlashVFXPrefab, gunMuzzlePos);

        Vector2 aimDir = aimLine != null ? aimLine.AimDirection : Vector2.right * AttackDir;

        int totalShots = GetEffectiveShotsPerClick();
        for (int i = 0; i < totalShots; i++)
        {
            // 方向散射：以 aimDir 为中心旋转 ±angle/2
            float angleOffset = (i - (totalShots - 1) * 0.5f) * bulletSpreadAngle;
            Vector2 dir = Quaternion.Euler(0f, 0f, -angleOffset) * aimDir;

            Vector3 spawnPos = transform.position
                             + Vector3.up * 0.3f
                             + (Vector3)dir * 0.5f;
            float finalDamage = GetEffectiveDamage();
            PlayerProjectile.Spawn(
                position: (Vector2)spawnPos,
                direction: (Vector2)dir,
                damage: finalDamage,
                speed: bulletSpeed,
                hitLayers: enemyLayer,
                wallLayers: wallLayer,
                radius: bulletRadius,
                color: bulletColor,
                parent: null,
                sourceLayer: 1 << gameObject.layer,
                attackType: rangedAttackType
            );

            if (i < totalShots - 1)
                yield return new WaitForSeconds(burstInterval);
        }

        isBursting = false;
    }

    // ============================================================
    // 近战攻击 — 三连击系统
    // ============================================================

    /// <summary>
    /// 近战攻击 — 触发动画，伤害判定由动画事件 OnMeleeHitFrame 驱动
    /// </summary>
    private void ExecuteMeleeAttack()
    {
        OnAttack?.Invoke();
        TriggerAttack();
    }

    /// <summary>Attack.anim 首帧触发</summary>
    public void OnAttackAnimationStart()
    {
        _inAttackAnim = true;  // 远程/近战统一：攻击动画播放期间 IsAttacking 为 true
        IsInputLocked = true;
        EnterAttack();
    }

    /// <summary>末帧触发：无排队则退子机回 Locomotion。连击不依赖此事件，由 TriggerAttack 的 Play 直切实现</summary>
    public void OnAttackAnimationEnd()
    {
        IsInputLocked = false;
        ExitAttack();
        _inAttackAnim = false;
        // IsAttacking=false 触发攻击子机 Exit（控制器条件），退回父层 Locomotion
        Anim?.SetBool(AnimParams.IsAttacking, false);
    }

    /// <summary>攻击键按下时调用</summary>
    private void TriggerAttack()
    {
        if (Anim == null) return;

        // comboReset：超时或越界重置
        if (Time.time > timeLastAttackExit + comboResetTimer)
            comboIndex = 1;
        if (comboIndex > comboLimit)
            comboIndex = 1;

        if (CurrentMode == AttackMode.Ranged)
        {
            Anim.SetBool(AnimParams.IsAttacking, true);
            Anim.SetInteger(AnimParams.AttackIndex, comboIndex);
            Anim.SetTrigger(AnimParams.Attack);
            return;
        }

        if (_inAttackAnim)
        {
            // 攻击中再次点击：直接切下一段（Play 强制切换，不经过子机 Exit，无 loc 间隙）
            if (comboIndex < comboLimit)
            {
                _inAttackAnim = true;
                Anim.SetBool(AnimParams.IsAttacking, true);
                Anim.Play("Attack" + (comboIndex + 1), 0, 0f);
            }
        }
        else
        {
            _inAttackAnim = true;
            Anim.SetBool(AnimParams.IsAttacking, true);
            Anim.SetInteger(AnimParams.AttackIndex, comboIndex);
        }
    }

    /// <summary>状态进入：设置 IsAttacking、同步攻速、更新朝向</summary>
    private void EnterAttack()
    {

        Anim?.SetBool(AnimParams.IsAttacking, true);

        if (_owner != null)
            _owner.UpdateFacing(AttackDir);

        if (Anim != null && statModManager != null)
        {
            float intervalMult = statModManager.GetFinalValue(1f, StatId.AttackInterval);
            Anim.speed = Mathf.Max(0.1f, intervalMult);
        }
    }

    /// <summary>状态退出：comboIndex++，溢出归1，记录时间</summary>
    private void ExitAttack()
    {
        comboIndex++;
        if (comboIndex > comboLimit) comboIndex = 1;
        timeLastAttackExit = Time.time;
    }

    private void TryAttack(PlayerController owner)
    {
        if (!Input.GetMouseButtonDown(0)) return;
        if (attackCooldownTimer > 0f) return;
        if (owner.IsDashing()) return;
        if (isBursting) return;

        attackCooldownTimer = GetEffectiveAttackCooldown(
            CurrentMode == AttackMode.Melee ? meleeAttackCooldown : attackCooldown);
        if (!playerCombatFlag)
        {
            passiveEquipManager?.SetCombatState(true);
            playerCombatFlag = true;
        }
        combatTimeoutTimer = CombatTimeoutDuration;
        _currentExecutor.Execute(this, owner);
    }

    private int AttackDir
    {
        get
        {
            float h = Input.GetAxisRaw("Horizontal");
            if (h > 0.1f) return 1;
            if (h < -0.1f) return -1;
            return _owner != null ? _owner.GetFacing() : 1;
        }
    }

    /// <summary>AnimationEvent 调用 — 在挥砍命中帧执行伤害判定 + 卡肉 + 闪烁</summary>
    public void OnMeleeHitFrame()
    {
        if (rangeIndicator == null) return;

        float damage = GetEffectiveDamage() * meleeDamage;

        if (hasParryBuff)
        {
            ExecuteHeavyMeleeAttack(damage);
            return;
        }

        LayerMask damageMask = enemyLayer;
        if (projectileLayer != 0)
            damageMask = enemyLayer | projectileLayer;

        Collider2D[] hits = MeleeHitDetector.Detect(rangeIndicator, damageMask);

        bool hitAnything = false;

        foreach (var col in hits)
        {
            var enemy = col.GetComponent<EnemyControllerBase>();
            if (enemy != null)
            {
                float dmg = RollCrit(damage);
                bool isFinisher = comboIndex >= comboLimit;

                // 第三段才施加击退
                if (isFinisher)
                {
                    Rigidbody2D enemyRb = enemy.GetComponent<Rigidbody2D>();
                    if (enemyRb != null)
                    {
                        Vector2 knockDir = ((Vector2)(enemy.transform.position - transform.position)).normalized;
                        if (knockDir.magnitude < 0.01f) knockDir = Vector2.right * AttackDir;
                        knockDir.y = Mathf.Max(knockDir.y, meleeKnockbackUpForce);
                        enemyRb.AddForce(knockDir * meleeKnockbackForce, ForceMode2D.Impulse);
                    }
                }

                string atkType = isFinisher ? meleeFinisherAttackType : meleeAttackType;
                enemy.TakeDamageFrom(dmg, (Vector2)transform.position, atkType);
                hitAnything = true;

                VFXSpawner.SpawnOnPlayer(slashVFXPrefab, col.transform.position);
            }
            else
            {
                var proj = col.GetComponent<Projectile>();
                if (proj != null && proj.CanBeDestroyedByMelee)
                {
                    proj.ReturnToPool();
                    hitAnything = true;
                }
            }
        }

        if (hitAnything)
            HitStopController.Instance?.Trigger(meleeHitStopDuration);

        rangeIndicator.Flash();
    }

    // ============================================================

    /// <summary>
    /// 弹反重击 — 高击退力 + 强制硬直，消耗弹反 Buff。
    /// 由 PerformMeleeHitDetection 在检测到 hasParryBuff 时调用。
    /// </summary>
    private void ExecuteHeavyMeleeAttack(float baseDamage)
    {
        hasParryBuff = false;
        EventBus.Trigger(new ParryBuffConsumedEvent());

        Collider2D[] hits = MeleeHitDetector.Detect(rangeIndicator, enemyLayer);

        bool hitAnything = false;

        foreach (var col in hits)
        {
            var enemy = col.GetComponent<EnemyControllerBase>();
            if (enemy != null)
            {
                float dmg = RollCrit(baseDamage);
                enemy.TakeDamage(dmg, meleeAttackType);
                hitAnything = true;

                // 强化击退：8f（原 3f）
                Vector2 knockDir = ((Vector2)(enemy.transform.position - transform.position)).normalized;
                knockDir.y = 0f;
                if (knockDir.magnitude < 0.01f) knockDir = Vector2.right;
                Rigidbody2D enemyRb = enemy.GetComponent<Rigidbody2D>();
                if (enemyRb != null)
                    enemyRb.AddForce(knockDir * 8f, ForceMode2D.Impulse);

                // 强制硬直
                enemy.EnterStunState();
            }
        }

        if (hitAnything)
            HitStopController.Instance?.Trigger(meleeHitStopDuration);

        rangeIndicator.Flash();
    }

    // ============================================================
    // 属性修饰器查询
    // ============================================================

    /// <summary>受 AttackInterval 修饰后的攻击冷却</summary>
    private float GetEffectiveAttackCooldown(float baseCD)
    {
        if (statModManager == null) return baseCD;
        float mult = statModManager.GetFinalValue(1f, StatId.AttackInterval);
        return baseCD / Mathf.Max(0.1f, mult);
    }

    /// <summary>受 ShotsPerClick 修饰后的单次发射子弹数</summary>
    private int GetEffectiveShotsPerClick()
    {
        if (statModManager == null) return shotsPerClick;
        int extra = Mathf.RoundToInt(statModManager.GetFinalValue(0f, StatId.ShotsPerClick));
        return Mathf.Max(1, shotsPerClick + extra);
    }

    /// <summary>暴击判定：受 CritRate/CritDamage 修饰</summary>
    private float RollCrit(float baseDamage)
    {
        if (statModManager == null) return baseDamage;
        float critChance = statModManager.GetFinalValue(0f, StatId.CritRate);
        if (critChance <= 0f || Random.value >= critChance)
            return baseDamage;
        float critDmg = statModManager.GetFinalValue(0f, StatId.CritDamage);
        return baseDamage * (1f + critDmg);
    }

    /// <summary>获取当前有效伤害（基础值 × 伤害倍率）</summary>
    private float GetEffectiveDamage()
    {
        if (statModManager == null) return attackDamage;
        float multiplier = statModManager.GetFinalValue(1f, StatId.DamageMultiplier);
        float dmg = attackDamage * multiplier;
        if (dmg < 0f) dmg = 0f;
        return dmg;
    }

    /// <summary>闪避判定：取 dodgeChance 做随机判定，成功返回 true</summary>
    public bool RollDodge()
    {
        if (statModManager == null) return false;
        float dodgeChance = statModManager.GetFinalValue(0f, StatId.DodgeChance);
        if (dodgeChance <= 0f) return false;
        return Random.value < dodgeChance;
    }

    /// <summary>减伤计算：actualDamage = incomingDamage × (1 - damageReduction)</summary>
    public float ApplyDamageReduction(float incomingDamage)
    {
        if (statModManager == null) return incomingDamage;
        float reduction = statModManager.GetFinalValue(0f, StatId.DamageReduction);
        reduction = Mathf.Clamp01(reduction);
        return incomingDamage * (1f - reduction);
    }

    // ============================================================
    // 格挡/弹反系统
    // ============================================================

    /// <summary>
    /// 格挡/弹反输入处理 — 由 OnPlayerUpdate 每帧调用。
    /// Mouse1 Down → 开始格挡；Mouse1 Up → 判定弹反/结束格挡。
    /// Dash 中自动取消格挡。
    /// </summary>
    private void HandleBlockParryInput(PlayerController owner)
    {
        // Dash 中取消格挡
        if (isBlocking && owner.IsDashing())
        {
            CancelBlock();
            return;
        }

        // 鼠标右键按下 → 开始格挡
        if (Input.GetMouseButtonDown(1))
        {
            StartBlocking();
            return;
        }

        // 鼠标右键松开 → 判定弹反/结束格挡
        if (Input.GetMouseButtonUp(1) && isBlocking)
        {
            float holdDuration = Time.time - blockStartTime;
            isBlocking = false;
            RemoveBlockModifier();

            // 短按判定为弹反
            if (holdDuration <= parryMaxWindow)
            {
                AttemptParry();
            }
            else
            {
                // 长按：正常格挡结束，恢复颜色
                // [预留] Animator.SetTrigger(blockEndAnimTrigger)
                RestorePlayerColor();
            }
        }
    }

    /// <summary>开始格挡：注入减伤修饰器 + 换色</summary>
    private void StartBlocking()
    {
        isBlocking = true;
        blockStartTime = Time.time;

        if (statModManager != null)
        {
            statModManager.AddModifier(new Modifier(
                targetStat: StatId.DamageReduction,
                value: blockDamageReduction,
                type: ModifierType.Percent,
                source: BlockModSource,
                priority: 500
            ));
        }

        // [测试] 格挡颜色 / [预留] Animator.SetTrigger(blockStartAnimTrigger)
        if (playerRenderer != null)
            playerRenderer.color = blockColor;
    }

    /// <summary>取消格挡：移除修饰器，重置状态，恢复颜色</summary>
    private void CancelBlock()
    {
        if (!isBlocking) return;
        isBlocking = false;
        RemoveBlockModifier();
        RestorePlayerColor();
    }

    /// <summary>移除格挡减伤修饰器</summary>
    private void RemoveBlockModifier()
    {
        if (statModManager != null)
            statModManager.RemoveModifier(BlockModSource);
    }

    /// <summary>
    /// 弹反判定：OverlapBox 检测范围内是否有正在攻击帧的敌人。
    /// 成功 → OnParrySuccess()；失败 → 无惩罚。
    /// </summary>
    private void AttemptParry()
    {
        if (rangeIndicator == null)
        {
            RestorePlayerColor();
            return;
        }

        Collider2D[] hits = MeleeHitDetector.Detect(rangeIndicator, enemyLayer);

        foreach (var col in hits)
        {
            var enemy = col.GetComponent<EnemyControllerBase>();
            if (enemy != null && enemy.IsInAttackFrame)
            {
                OnParrySuccess();
                return;
            }
        }

        // 弹反失败：恢复颜色
        RestorePlayerColor();
    }

    /// <summary>弹反成功：设置 Buff，闪烁，触发事件</summary>
    private void OnParrySuccess()
    {
        hasParryBuff = true;

        if (playerRenderer != null)
        {
            if (_parryFlashRoutine != null)
                StopCoroutine(_parryFlashRoutine);
            _parryFlashRoutine = StartCoroutine(ParryFlashRoutine());
        }

        EventBus.Trigger(new ParrySuccessEvent());
    }

    private System.Collections.IEnumerator ParryFlashRoutine()
    {
        playerRenderer.color = parrySuccessColor;
        yield return new WaitForSeconds(parryFlashDuration);
        // 弹反后若仍在格挡中则保持格挡色，否则恢复原色
        playerRenderer.color = isBlocking ? blockColor : _playerOriginalColor;
        _parryFlashRoutine = null;
    }

    /// <summary>恢复玩家原始颜色</summary>
    private void RestorePlayerColor()
    {
        if (playerRenderer != null)
            playerRenderer.color = _playerOriginalColor;
    }

    /// <summary>玩家死亡时取消格挡</summary>
    private void OnPlayerDeath(PlayerDeathEvent _)
    {
        CancelBlock();
    }

#if UNITY_EDITOR
    private void OnDrawGizmosSelected()
    {
        if (rangeIndicator == null) return;
        var pc = GetComponent<PlayerController>();
        if (pc == null) return;
        Vector2 center = (Vector2)transform.position + Vector2.right * pc.GetFacing() * meleeRangeOffset;
        Vector2 size = rangeIndicator.Size;
        Gizmos.color = new Color(1f, 0f, 0f, 0.4f);
        Gizmos.DrawCube(center, size);
        Gizmos.color = Color.red;
        Gizmos.DrawWireCube(center, size);
    }
#endif
}
