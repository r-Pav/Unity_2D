using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 冲刺执行器 — 纯逻辑执行器(IsDashing 状态迁至 PlayerDashState)
/// 充能制冲刺(决策 D3/D16):默认 1 充能,maxCharges 序列化可配;树 B lv1 解锁后 2 充能、各自独立恢复。
/// 对外提供:DoDash(消耗充能+清速度+设冲刺速度+开恢复计时) / CooldownReady(充能查询) /
/// TickCooldown(充能恢复) / UnlockExtraCharge / EnableDashDamage / SetDashDamage(树 B lv1 解锁入口)。
/// dashSpeed/dashDuration 保留序列化配置;dashDuration 由 PlayerController 注入状态类。
/// 冲刺伤害判定在 PlayerDashState(DashDamageEnabled 开启后每帧 OverlapBox),检测参数在本组件序列化配置。
/// </summary>
public class PlayerDash : MonoBehaviour
{
    [Header("冲刺")]
    [SerializeField] private float dashSpeed = 10f;
    [SerializeField] private float dashDuration = 0.15f;

    [Header("充能")]
    [Tooltip("最大充能数(默认 1;树 B lv1 解锁后运行时 +1 到 2)")]
    [SerializeField] private int maxCharges = 1;
    [Tooltip("每充能恢复时间(秒,沿用原 0.6f 冷却语义;决策 D3 各充能独立计时)")]
    [SerializeField] private float chargeCooldown = 0.6f;

    [Header("冲刺伤害(树 B lv1 启用后生效)")]
    [Tooltip("冲刺伤害检测 Layer(默认 Enemy,与 PlayerCombat.enemyLayer 一致)")]
    [SerializeField] private LayerMask dashHitLayers; // 默认值在 Awake 赋值(NameToLayer 禁止在字段初始化器调用)
    [Tooltip("冲刺伤害检测矩形尺寸(宽沿冲刺方向)")]
    [SerializeField] private Vector2 dashHitBoxSize = new Vector2(1.2f, 1.0f);
    [Tooltip("检测矩形中心相对玩家的前方偏移")]
    [SerializeField] private float dashHitForwardOffset = 0.6f;
    [Tooltip("冲刺击退力度(沿冲刺方向;冲刺只伤害+击退,不进敌人硬直分流)")]
    [SerializeField] private float dashKnockbackForce = 3f;

    // ── 运行时状态(不序列化:充能恢复直接补满,不持久化半恢复状态,与 CD 处理一致)──
    [System.NonSerialized] private int charges;                              // 当前可用充能数
    [System.NonSerialized] private readonly List<float> chargeTimers = new(); // 每消耗 1 充能 = 1 个独立恢复计时(决策 D3)
    [System.NonSerialized] private bool extraChargeUnlocked;                 // 树 B lv1 已解锁标记(幂等,防 E 键重复激活 maxCharges 无限增长)
    [System.NonSerialized] private bool dashDamageEnabled;                   // 树 B lv1 解锁后冲刺带伤害
    [System.NonSerialized] private float dashDamage;                         // 冲刺伤害值(由 DashUpgradeExecutor 按 lv1Data.damage 注入;0 = 无伤害)
    [System.NonSerialized] private float dashDistanceMultiplier = 1f;        // 冲刺距离修饰(阶段 6 lv3B-02 右分支"距离增加";运行时注入,默认 1 = 原距离)

    // [2026-08-21] 树B SO 注入的冲刺参数(0 = 未注入,回退序列化 dashSpeed/dashDuration):
    // 冲刺距离 = 冲刺速度 × 冲刺时长,两值随树B 等级从 ActiveBranchData 读取(见 DashUpgradeExecutor)
    [System.NonSerialized] private float dashSpeedOverride;
    [System.NonSerialized] private float dashDurationOverride;

    private void Awake()
    {
        if (dashHitLayers == 0)
            dashHitLayers = LayerMask.GetMask("Enemy"); // 默认值兜底(NameToLayer 仅允许在 Awake/Start 调用)
        charges = maxCharges; // 启动补满
    }

    /// <summary>是否可冲刺(充能 > 0;5 个状态类的 Shift 检测沿用此属性,语义自动变为"有充能",零代码改动)</summary>
    public bool CooldownReady => charges > 0;

    /// <summary>冲刺时长(秒),注入 PlayerDashState 做超时退出;SO 注入值优先,0 回退序列化</summary>
    public float DashDuration => dashDurationOverride > 0f ? dashDurationOverride : dashDuration;

    /// <summary>冲刺速度(米/秒),DoDash 设速用;SO 注入值优先,0 回退序列化</summary>
    public float DashSpeed => dashSpeedOverride > 0f ? dashSpeedOverride : dashSpeed;

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

    /// <summary>执行冲刺:消耗 1 充能 + 开启该充能独立恢复计时 + 清速度 + 设冲刺速度(facing × dashSpeed)。由 PlayerDashState.OnEnter 调用。</summary>
    public void DoDash(PlayerController owner)
    {
        if (charges <= 0) return; // 充能耗尽即不可冲刺,无保底(决策 D16;调用方已按 CooldownReady 拦截,此处双保险)

        charges--;
        chargeTimers.Add(chargeCooldown); // 每消耗 1 充能新增 1 个独立恢复槽(决策 D3)

        Rigidbody2D rb = owner.GetRigidbody();
        rb.velocity = Vector2.zero;
        // 冲刺距离修饰(阶段 6 lv3B-02):速度 × 倍率,dashDuration 不变 → 冲刺距离变长
        // 速度优先用 SO 注入值(DashSpeed),未注入用序列化;距离 = DashSpeed × DashDuration(时长由状态类计时)
        rb.velocity = new Vector2(owner.GetFacing() * DashSpeed * dashDistanceMultiplier, 0);
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

    /// <summary>树 B lv1 解锁:最大充能 +1 并补满。幂等:已解锁过直接返回(内部标记),防 E 键重复激活导致 maxCharges 无限增长。</summary>
    public void UnlockExtraCharge()
    {
        if (extraChargeUnlocked) return;
        extraChargeUnlocked = true;
        maxCharges++;
        if (charges < maxCharges) charges = maxCharges; // 解锁即补满
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
    /// [2026-08-21] 设置冲刺速度/时长(由 DashUpgradeExecutor 按树B 当前等级分支数据注入;
    /// 冲刺距离 = 速度 × 时长,两值随技能升级变化;≤0 的值视为未配置,回退序列化 dashSpeed/dashDuration;幂等)
    /// </summary>
    public void SetDashParams(float speed, float duration)
    {
        dashSpeedOverride = speed > 0f ? speed : 0f;
        dashDurationOverride = duration > 0f ? duration : 0f;
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
