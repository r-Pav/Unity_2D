using UnityEngine;

/// <summary>
/// 战斗参与者接口 — 所有能造成/承受伤害的实体实现此接口
/// 交互层的唯一数据入口，禁止外部直接操作 Rigidbody2D 或 health 字段
/// </summary>
public interface ICombatant
{
    // ── 身份 ──
    GameObject GameObject { get; }
    Transform Transform { get; }

    // ── 攻击方 ──
    /// <summary>当前攻击标签（Sword/Sword_Heavy/Projectile/Fire 等），贯穿结算</summary>
    string CurrentAttackLabel { get; }

    // ── 受击方 ──
    /// <summary>韧性组件（null = 无韧性系统）</summary>
    PoiseComponent Poise { get; }
    /// <summary>承受伤害（含击退信息），返回实际造成伤害量</summary>
    float ApplyDamage(DamageInfo info);
    /// <summary>是否处于可被攻击的状态</summary>
    bool CanBeDamaged { get; }
    /// <summary>是否处于攻击判定帧（弹反查询用）</summary>
    bool IsInAttackFrame { get; }

    // ── 结算管线钩子（CombatResolver.Resolve 调用；P4a 空壳，P4b/P4c 由实现方接入）──

    /// <summary>闪避判定 — true 表示本次攻击被闪避，结算提前终止</summary>
    bool TryDodge(DamageInfo info);

    /// <summary>格挡/弹反判定 — true 表示本次攻击被弹反，结算提前终止</summary>
    bool TryParry(ICombatant attacker, DamageInfo info);

    /// <summary>护甲减免 — 返回减免后的伤害值</summary>
    float ApplyArmor(float amount);

    /// <summary>减伤 — 返回减伤后的伤害值</summary>
    float ApplyReduction(float amount);

    /// <summary>施加击退（内部使用 Knockback 参数，禁止散落硬编码）</summary>
    void ApplyKnockback(Knockback knockback);

    /// <summary>受击状态推送 — 状态机切换 Hurt/AirHurt/Stun</summary>
    void OnHitBy(DamageInfo info);
}

/// <summary>
/// 统一伤害信息 — 贯穿整个结算管线的数据结构
/// </summary>
public struct DamageInfo
{
    public float amount;
    public ICombatant source;       // 攻击者
    public Vector2 sourcePosition;  // 攻击来源位置（用于方向计算）
    public string attackLabel;      // 攻击标签（Sword/Sword_Heavy/Projectile...）
    public Knockback knockback;     // 击退参数

    /// <summary>元素标签（默认 None = 无元素；伤害触发时刻由攻击方写入，决策 N5 战斗中切换即时生效）</summary>
    public ElementType element;

    /// <summary>是否允许触发元素 proc（元素衍生伤害必须显式 false，防递归，决策 D14）。
    /// 注：C#9 结构体无参构造不可用，默认 true 由各 player 攻击构造点显式设置。</summary>
    public bool canTriggerElementProc;

    /// <summary>本次伤害实际采用的暴击倍率（0 = 未暴击；暴击仲裁结果透传，供 UI/统计读取）</summary>
    public float critMultiplier;

    /// <summary>true = 本次命中跳过敌人空中滞空吸附(_pullToPlayer)。背刺等终结技用:
    /// 敌人被击退正常飞出自然落地,不向玩家吸附、不被吊在空中。默认 false = 走原空中连段吸附。</summary>
    public bool suppressAirHang;
}

/// <summary>
/// 击退结构 — 统一击退参数，不再散落 rb.AddForce 硬编码数值
/// </summary>
public struct Knockback
{
    public Vector2 direction;       // 击退方向（单位向量）
    public float force;             // 击退力度
    public float duration;          // 击退硬直时间
    public bool ignoreResistance;   // 是否无视韧性/击退抵抗

    public static readonly Knockback None = new Knockback();  // force=0 时表示无击退
}
