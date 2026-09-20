using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 冲刺执行器 — 纯逻辑执行器(IsDashing 状态迁至 PlayerDashState)
/// 充能制冲刺(决策 D3/D16):默认 1 充能,maxCharges 序列化可配;树 B lv1 解锁后 2 充能、各自独立恢复。
/// 对外提供:DoDash(消耗充能+清速度+设冲刺速度+开恢复计时) / CooldownReady(充能查询) /
/// TickCooldown(充能恢复) / UnlockExtraCharge / EnableDashDamage / SetDashDamage(树 B lv1 解锁入口)。
/// dashDistance/dashDuration 保留序列化配置(未注入 SO 时的回退);速度 = 距离 ÷ 时长 推导。
/// 冲刺伤害判定在 PlayerDashState(DashDamageEnabled 开启后每帧 OverlapBox),检测参数在本组件序列化配置。
/// 冲刺拖尾光:DoDash 生效瞬间按 dashTrailVFXPrefab 在身后生成(走 VFXSpawner,无引用 = 不生成)。
/// 冲刺拖尾光线:dashTrailRenderer 引用玩家身上挂的 TrailRenderer,由状态类 OnEnter/OnExit 开关发射
/// (BeginDashTrail / EndDashTrail;无引用 = 不启用,零影响)。
/// </summary>
public class PlayerDash : MonoBehaviour
{
    [Header("冲刺")]
    [Tooltip("冲刺距离(米);速度 = 距离 ÷ 总时长 自动推导;树B SO 未注入时回退此值")]
    [SerializeField] private float dashDistance = 1.8f;
    [Tooltip("冲刺总持续时间(秒);树B SO 未注入时回退此值")]
    [SerializeField] private float dashDuration = 0.15f;

    // [2026-09-19 saika:数值统一收口到技能树 SO] 充能数与恢复时间不再由本组件配置,
    // 改由 DashUpgradeExecutor 在 SkillLevelChangedEvent 时按等级注入(SetChargeConfig):
    //   充能数 = Lv1 1 / Lv2 2 / Lv3 2(左右分支一致);恢复时间读 Skill_Active_E 各等级分支的 cooldown 字段。
    // 原三级配置字段注释留档(禁止再新增调用方):
    //[Header("充能(按树 B 等级;未升级不给冲刺)")]
    //[SerializeField] private int lv1MaxCharges = 1;
    //[SerializeField] private float lv1RechargeTime = 1f;
    //[SerializeField] private int lv2MaxCharges = 2;
    //[SerializeField] private float lv2RechargeTime = 3f;
    //[SerializeField] private int lv3MaxCharges = 2;
    //[SerializeField] private float lv3RechargeTime = 2f;

    /// <summary>SO 分支未配恢复时间时的兜底(秒)</summary>
    private const float DefaultRechargeFallback = 1f;

    [Header("冲刺伤害(树 B lv1 启用后生效)")]
    [Tooltip("冲刺伤害检测 Layer(默认 Enemy,与 PlayerCombat.enemyLayer 一致)")]
    [SerializeField] private LayerMask dashHitLayers; // 默认值在 Awake 赋值(NameToLayer 禁止在字段初始化器调用)
    [Tooltip("冲刺伤害检测矩形尺寸(宽沿冲刺方向)")]
    [SerializeField] private Vector2 dashHitBoxSize = new Vector2(1.2f, 1.0f);
    [Tooltip("检测矩形中心相对玩家的前方偏移")]
    [SerializeField] private float dashHitForwardOffset = 0.6f;
    [Tooltip("冲刺击退力度(沿冲刺方向;冲刺只伤害+击退,不进敌人硬直分流)")]
    [SerializeField] private float dashKnockbackForce = 3f;

    [Header("冲刺特效")]
    [Tooltip("冲刺拖尾光 prefab(留空则不生成);冲刺生效瞬间在角色身后甩一道光,朝左自动整体翻转")]
    [SerializeField] private GameObject dashTrailVFXPrefab;

    /// <summary>拖尾光生成点相对角色的后撤距离(防光从身体正中冒出来)</summary>
    private const float DashTrailBackOffset = 0.2f;

    [Tooltip("冲刺拖尾光线(玩家身上的 TrailRenderer,留空则不启用);冲刺期间持续发射,沿冲刺路径拉出一条光带,冲刺结束按自身 Time 淡出")]
    [SerializeField] private TrailRenderer dashTrailRenderer;

    // ── 运行时状态(不序列化:充能恢复直接补满,不持久化半恢复状态,与 CD 处理一致)──
    [System.NonSerialized] private bool dashUnlocked;                        // [2026-09-19] 树 B Lv1 起解锁冲刺(未升级不给冲刺)
    [System.NonSerialized] private int maxCharges = 1;                       // 当前等级的最大充能数(SetChargeConfig 注入)
    [System.NonSerialized] private float rechargeTime = 1f;                  // 当前等级的每充能恢复时间(SetChargeConfig 注入)
    [System.NonSerialized] private int charges;                              // 当前可用充能数
    [System.NonSerialized] private readonly List<float> chargeTimers = new(); // 每消耗 1 充能 = 1 个独立恢复计时(决策 D3)
    [System.NonSerialized] private bool extraChargeUnlocked;                 // 树 B lv1 已解锁标记(幂等,防 E 键重复激活 maxCharges 无限增长)
    [System.NonSerialized] private bool dashDamageEnabled;                   // 树 B lv1 解锁后冲刺带伤害
    [System.NonSerialized] private float dashDamage;                         // 冲刺伤害值(由 DashUpgradeExecutor 按 lv1Data.damage 注入;0 = 无伤害)
    [System.NonSerialized] private float dashDistanceMultiplier = 1f;        // 冲刺距离修饰(阶段 6 lv3B-02 右分支"距离增加";运行时注入,默认 1 = 原距离)

    // [2026-08-21] 树B SO 注入的冲刺参数(0 = 未注入,回退序列化 dashDistance/dashDuration):
    // 冲刺距离/总时长从 ActiveBranchData 读取,速度 = 距离 ÷ 时长 推导(见 DashUpgradeExecutor)
    [System.NonSerialized] private float dashSpeedOverride;
    [System.NonSerialized] private float dashDurationOverride;

    private void Awake()
    {
        if (dashHitLayers == 0)
            dashHitLayers = LayerMask.GetMask("Enemy"); // 默认值兜底(NameToLayer 仅允许在 Awake/Start 调用)
        // [2026-09-19 saika] 未升级不给冲刺:启动不补满,等树 B Lv1 的 SetChargeConfig 解锁并补满
        rechargeTime = DefaultRechargeFallback;
        charges = 0;
    }

    /// <summary>是否可冲刺(已解锁 且 充能 > 0;5 个状态类的 Shift 检测沿用此属性,零代码改动)</summary>
    public bool CooldownReady => dashUnlocked && charges > 0;

    /// <summary>冲刺是否已解锁(树 B Lv1 起;未升级不给冲刺)</summary>
    public bool DashUnlocked => dashUnlocked;

    /// <summary>冲刺时长(秒),注入 PlayerDashState 做超时退出;SO 注入值优先,0 回退序列化</summary>
    public float DashDuration => dashDurationOverride > 0f ? dashDurationOverride : dashDuration;

    /// <summary>冲刺速度(米/秒),DoDash 设速用;SO 注入值优先,未注入用 距离 ÷ 时长 推导</summary>
    public float DashSpeed => dashSpeedOverride > 0f ? dashSpeedOverride
        : (dashDistance > 0f && dashDuration > 0f ? dashDistance / dashDuration : 0f);

    /// <summary>当前可用充能数(HUD 充能显示预留)</summary>
    public int Charges => charges;

    /// <summary>最大充能数</summary>
    public int MaxCharges => maxCharges;

    /// <summary>冲刺伤害开关(树 B lv1 解锁后 true;未解锁冲刺无伤害,保持现状)</summary>
    public bool DashDamageEnabled => dashDamageEnabled;

    /// <summary>冲刺伤害值(由执行器按分支数据注入)</summary>
    public float DashDamage => dashDamage;

    /// <summary>冲刺距离修饰倍率(阶段 6 lv3B-02"距离增加";默认 1 = 原距离)</summary>
    public float DashDistanceMultiplier => dashDistanceMultiplier;

    // ── 冲刺伤害检测参数(PlayerDashState 每帧 OverlapBox 读取)──
    public LayerMask DashHitLayers => dashHitLayers;
    public Vector2 DashHitBoxSize => dashHitBoxSize;
    public float DashHitForwardOffset => dashHitForwardOffset;
    public float DashKnockbackForce => dashKnockbackForce;

    /// <summary>执行冲刺:消耗 1 充能 + 开启该充能独立恢复计时 + 清速度 + 设冲刺速度(facing × DashSpeed)。由 PlayerDashState.OnEnter 调用。</summary>
    public void DoDash(PlayerController owner)
    {
        if (charges <= 0) return; // 充能耗尽即不可冲刺,无保底(决策 D16;调用方已按 CooldownReady 拦截,此处双保险)

        charges--;
        chargeTimers.Add(rechargeTime); // 每消耗 1 充能新增 1 个独立恢复槽(决策 D3);恢复时长 = 当前等级配置

        Rigidbody2D rb = owner.GetRigidbody();
        rb.velocity = Vector2.zero;
        // 冲刺距离修饰(阶段 6 lv3B-02):速度 × 倍率,dashDuration 不变 → 冲刺距离变长
        // 速度优先用 SO 注入值(DashSpeed),未注入用序列化;距离 = DashSpeed × DashDuration(时长由状态类计时)
        rb.velocity = new Vector2(owner.GetFacing() * DashSpeed * dashDistanceMultiplier, 0);

        // 冲刺拖尾光:冲刺真正生效的这一瞬间,在角色身后甩一道光(burst 3 条光条,约 0.5s 收完)。
        // prefab 是世界空间的(光甩在原地,角色继续跑不会把光拖走);朝左时整体绕 Y 转 180° 让光甩向 -X。
        // 走 VFXSpawner 统一入口:实例挂到 PlayerVFX 容器,并自动挂 VFXAutoDestruct 播完自毁,不用手动 Destroy。
        if (dashTrailVFXPrefab != null)
        {
            float facing = owner.GetFacing();
            Vector2 trailPos = (Vector2)owner.transform.position - new Vector2(facing * DashTrailBackOffset, 0f);
            VFXSpawner.Spawn(VFXCategory.PlayerVFX, dashTrailVFXPrefab, trailPos,
                facing < 0f ? Quaternion.Euler(0f, 180f, 0f) : Quaternion.identity);
        }
    }

    /// <summary>开始冲刺拖尾光线(PlayerDashState.OnEnter 调用):清掉上一段残留轨迹再开发射。
    /// 未拖引用 = 空操作。发射开关只由冲刺状态控制,平时不发射(站着跑动不留光)。</summary>
    public void BeginDashTrail()
    {
        if (dashTrailRenderer == null) return;
        dashTrailRenderer.Clear();          // 清掉上一次冲刺的残留顶点,防两段光带连成一条
        dashTrailRenderer.emitting = true;
    }

    /// <summary>结束冲刺拖尾光线(PlayerDashState.OnExit 调用,覆盖自然结束/受击打断/切场景全部出口)。
    /// 只关发射,已有顶点按 TrailRenderer 的 Time 自然淡出,不会硬切出断口。</summary>
    public void EndDashTrail()
    {
        if (dashTrailRenderer == null) return;
        dashTrailRenderer.emitting = false;
    }

    /// <summary>充能恢复(PlayerController.UpdateCooldowns 每帧调用):遍历所有恢复中的充能槽,恢复满则 charges++(上限 maxCharges)。
    /// 用 unscaledDeltaTime:卡帧(timeScale=0)期间充能照常恢复,卡帧只冻视觉不冻数值(2026-08-19 saika 确认)</summary>
    public void TickCooldown()
    {
        for (int i = chargeTimers.Count - 1; i >= 0; i--)
        {
            chargeTimers[i] -= Time.unscaledDeltaTime;
            if (chargeTimers[i] <= 0f)
            {
                chargeTimers.RemoveAt(i);
                if (charges < maxCharges) charges++; // 恢复满 1 充能(上限 maxCharges)
            }
        }
    }

    /// <summary>
    /// [2026-09-19 已停用] 原树 B lv1 解锁入口(最大充能 +1 并补满)。充能与恢复节奏改由 SetChargeConfig(level) 按等级设置,
    /// 本方法不再有调用方,保留留档(extraChargeUnlocked 标记一并停用)。禁止再新增调用方。
    /// </summary>
    public void UnlockExtraCharge()
    {
        if (extraChargeUnlocked) return;
        extraChargeUnlocked = true;
        maxCharges++;
        if (charges < maxCharges) charges = maxCharges; // 解锁即补满
    }

    /// <summary>
    /// [2026-09-19 saika] 按树 B 等级设置冲刺充能与恢复节奏(由 DashUpgradeExecutor 在 SkillLevelChangedEvent 注入)。
    /// 数值统一收口到技能树 SO:恢复时间 = Skill_Active_E 当前等级分支的 cooldown;充能数由执行器按等级规则传入
    ///   Lv1 → 1 充能(每格 1 秒);Lv2(左右一致)/ Lv3 → 2 充能(每格 3 秒 / 2 秒)。
    /// level ≥ 1 才解锁冲刺(未升级不给冲刺:CooldownReady 恒假);切等级即按新配置补满并清空旧恢复计时。
    /// </summary>
    public void SetChargeConfig(int level, int maxCharges, float rechargeTime)
    {
        if (level <= 0) return;

        this.maxCharges = maxCharges >= 1 ? maxCharges : 1;
        this.rechargeTime = rechargeTime > 0.01f ? rechargeTime : DefaultRechargeFallback;

        dashUnlocked = true;
        chargeTimers.Clear();           // 切等级:丢弃旧的恢复计时
        charges = this.maxCharges;      // 按新配置补满(不保留旧充能进度)
    }

    /// <summary>树 B lv1 解锁:启用冲刺伤害(幂等,重复调用安全;未解锁时冲刺无伤害,保持现状)</summary>
    public void EnableDashDamage()
    {
        dashDamageEnabled = true;
    }

    /// <summary>设置冲刺伤害值(由 DashUpgradeExecutor 按分支 lv1Data.damage 注入;重复激活覆盖为同值,幂等)</summary>
    public void SetDashDamage(float damage)
    {
        dashDamage = damage;
    }

    /// <summary>
    /// [2026-08-21] 设置冲刺距离/总时长(由 DashUpgradeExecutor 按树B 当前等级分支数据注入;
    /// 速度 = 距离 ÷ 时长 自动推导,不单独配置;≤0 的值视为未配置,回退序列化 dashDistance/dashDuration;幂等)
    /// </summary>
    public void SetDashParams(float distance, float duration)
    {
        dashDurationOverride = duration > 0f ? duration : 0f;
        if (distance > 0f && duration > 0f)
            dashSpeedOverride = distance / duration; // 冲刺速度 = 距离 ÷ 总时长
        else
            dashSpeedOverride = 0f;
    }

    /// <summary>
    /// 设置冲刺距离修饰倍率(阶段 6 lv3B-02"距离增加";DashComboExecutor 注入,幂等)。
    /// multiplier ≤ 0 视为恢复默认(1)。速度 × 倍率 → 冲刺距离变长。
    /// </summary>
    public void SetDashDistanceMultiplier(float multiplier)
    {
        dashDistanceMultiplier = multiplier > 0f ? multiplier : 1f;
    }
}
