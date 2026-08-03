using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// [Phase2] 玩家属性系统 — 管理主属性(STR/INT/AGI)与派生属性计算
/// 挂 Player GameObject，应在 StatModifierManager 之前初始化
/// 
/// 计算管道（8步）：
///   基础值(SO) → 装备加成 → 升级点数 → 主属性终值
///   → 派生公式计算 → Modifier注入(priority=-100)
///   → 装备/技能/消耗品叠加(priority=0/100/200)
///   → Bonus加成(priority=999) → 钳制 → 终值
/// 
/// 公开 API：9 个方法 + 2 个事件
/// </summary>
public class PlayerAttributeSystem : MonoBehaviour
{
    // ============================================================
    // Singleton 注册表（Player 子组件；调用方统一走 Instance）
    // ============================================================

    private static PlayerAttributeSystem _instance;

    public static PlayerAttributeSystem Instance
    {
        get
        {
            if (_instance == null)
                _instance = FindObjectOfType<PlayerAttributeSystem>();
            return _instance;
        }
    }

    // ============================================================
    // 配置
    // ============================================================

    [Header("属性配置")]
    [Tooltip("玩家属性配置 SO（基础值 + 派生系数）")]
    [SerializeField] private PlayerAttrConfigSO attrConfig;

    /// <summary>公开只读访问配置 SO</summary>
    public PlayerAttrConfigSO AttrConfig => attrConfig;

    // ============================================================
    // 运行时状态 — 主属性
    // ============================================================

    /// <summary>升级分配的力量点数</summary>
    public int AssignedStr { get; private set; }

    /// <summary>升级分配的智力点数</summary>
    public int AssignedInt { get; private set; }

    /// <summary>升级分配的敏捷点数</summary>
    public int AssignedAgi { get; private set; }

    /// <summary>装备提供的力量加成</summary>
    private int equipStr;

    /// <summary>装备提供的智力加成</summary>
    private int equipInt;

    /// <summary>装备提供的敏捷加成</summary>
    private int equipAgi;

    // ============================================================
    // 运行时状态 — 派生值缓存
    // ============================================================

    /// <summary>派生属性裸值缓存（公式计算结果，未经过修饰器管线）</summary>
    private readonly Dictionary<string, float> derivedCache = new Dictionary<string, float>();

    // ============================================================
    // 缓存引用
    // ============================================================

    private StatModifierManager statModManager;

    // ============================================================
    // 派生公式中使用的 source 常量
    // ============================================================

    private const string SOURCE_DERIVED_PREFIX = "AttrSys_Derived_";
    private const string SOURCE_BONUS_PREFIX = "AttrSys_Bonus_";

    // ============================================================
    // 生命周期
    // ============================================================

    private void Awake()
    {
        statModManager = GetComponent<StatModifierManager>();

        if (attrConfig == null)
        {
            Debug.LogWarning("[PlayerAttributeSystem] attrConfig 未配置，使用默认值（全5）");
        }
    }

    private void Start()
    {
        // 首次启动时执行完整重算
        RecalculateAll();
    }

    // ============================================================
    // 公开读取 — 主属性
    // ============================================================

    /// <summary>获取力量终值（基础 + 装备 + 升级点数）</summary>
    public int GetStrength()
    {
        int baseVal = attrConfig != null ? attrConfig.baseStr : 5;
        return baseVal + equipStr + AssignedStr;
    }

    /// <summary>获取智力终值（基础 + 装备 + 升级点数）</summary>
    public int GetIntelligence()
    {
        int baseVal = attrConfig != null ? attrConfig.baseInt : 5;
        return baseVal + equipInt + AssignedInt;
    }

    /// <summary>获取敏捷终值（基础 + 装备 + 升级点数）</summary>
    public int GetAgility()
    {
        int baseVal = attrConfig != null ? attrConfig.baseAgi : 5;
        return baseVal + equipAgi + AssignedAgi;
    }

    /// <summary>获取力量裸值（仅基础 + 升级点数，不含装备）</summary>
    public int GetBaseStrength()
    {
        int baseVal = attrConfig != null ? attrConfig.baseStr : 5;
        return baseVal + AssignedStr;
    }

    // ============================================================
    // 公开读取 — 派生属性
    // ============================================================

    /// <summary>
    /// 获取派生属性裸值（公式计算结果，未经过修饰器管线）
    /// 支持的 statId: maxHealth, hpRegen, armor, maxMana, mpRegen,
    ///                skillEnhance, attackSpeed, dodgeChance, cooldownReduction
    /// </summary>
    public float GetDerivedValue(string statId)
    {
        if (derivedCache.TryGetValue(statId, out float value))
            return value;
        return 0f;
    }

    // ============================================================
    // 公开修改 — 升级加点
    // ============================================================

    /// <summary>增加升级属性点（str/int/agi 的分配值）</summary>
    public void AddAttributePoint(string attrId)
    {
        switch (attrId)
        {
            case "str": AssignedStr++; break;
            case "int": AssignedInt++; break;
            case "agi": AssignedAgi++; break;
            default:
                Debug.LogWarning($"[PlayerAttributeSystem] 未知属性 ID: {attrId}");
                return;
        }

        RecalculateAll();
    }

    /// <summary>
    /// [Phase5] 批量设置升级属性点数（用于存档加载）
    /// 一次性设置三个值，仅触发一次 RecalculateAll
    /// </summary>
    public void SetAssignedPoints(int str, int intelligence, int agi)
    {
        AssignedStr = str;
        AssignedInt = intelligence;
        AssignedAgi = agi;
        RecalculateAll();
    }

    // ============================================================
    // 公开修改 — 装备系统接口
    // ============================================================

    /// <summary>
    /// [Phase3预留] 装备系统调用：装备变化时更新主属性加成
    /// </summary>
    /// <param name="attrBonuses">装备提供的 str/int/agi 加成值</param>
    public void SetEquipmentBonus(Dictionary<string, int> attrBonuses)
    {
        equipStr = 0;
        equipInt = 0;
        equipAgi = 0;

        if (attrBonuses != null)
        {
            if (attrBonuses.TryGetValue("str", out int s)) equipStr = s;
            if (attrBonuses.TryGetValue("int", out int i)) equipInt = i;
            if (attrBonuses.TryGetValue("agi", out int a)) equipAgi = a;
        }

        RecalculateAll();
    }

    /// <summary>[Phase3预留] 装备卸下或清空时调用</summary>
    public void ClearEquipmentBonus()
    {
        equipStr = 0;
        equipInt = 0;
        equipAgi = 0;
        RecalculateAll();
    }

    /// <summary>
    /// [Phase3预留] 获取当前装备加成来源数据
    /// Phase3 EquipmentManager 调用此方法获取当前装备的主属性加成快照
    /// </summary>
    public Dictionary<string, int> GetBonusSource()
    {
        return new Dictionary<string, int>
        {
            { "str", equipStr },
            { "int", equipInt },
            { "agi", equipAgi }
        };
    }

    /// <summary>
    /// [Phase3预留] 移除装备加成来源并重算
    /// Phase3 EquipmentManager 在死亡掉落/卸下全装备时调用
    /// </summary>
    public void RemoveBonusSource()
    {
        ClearEquipmentBonus();
    }

    // ============================================================
    // 核心计算 — 重算管道
    // ============================================================

    /// <summary>
    /// 完全重算：主属性终值 → 派生公式 → Modifier 注入 → 事件通知
    /// 触发时机：Awake初始化、装备变化、升级加点
    /// </summary>
    public void RecalculateAll()
    {
        if (statModManager == null)
        {
            Debug.LogWarning("[PlayerAttributeSystem] StatModifierManager 未找到，跳过重算");
            return;
        }

        // ── Step 1-4: 主属性终值 ──
        int finalStr = GetStrength();
        int finalInt = GetIntelligence();
        int finalAgi = GetAgility();

        // ── Step 5: 派生公式计算（先移除旧 modifier，再计算并注入新值）──
        RemoveAllDerivedModifiers();

        // 清空派生缓存
        derivedCache.Clear();

        // 力量派生
        float baseHpPerPoint = attrConfig != null ? attrConfig.str_hpPerPoint : 10f;
        float baseHpRegenPerP = attrConfig != null ? attrConfig.str_hpRegenPerP : 0.5f;
        float baseArmorPer10 = attrConfig != null ? attrConfig.str_armorPer10 : 1f;

        float derivedMaxHealth = finalStr * baseHpPerPoint;
        float derivedHpRegen = finalStr * baseHpRegenPerP;
        float derivedArmor = (finalStr / 10) * baseArmorPer10; // Bonus

        // 智力派生
        float baseMpPerPoint = attrConfig != null ? attrConfig.int_mpPerPoint : 10f;
        float baseMpRegenPerP = attrConfig != null ? attrConfig.int_mpRegenPerP : 0.5f;
        float baseSkillEnhPer10 = attrConfig != null ? attrConfig.int_skillEnhPer10 : 0.05f;

        float derivedMaxMana = finalInt * baseMpPerPoint;
        float derivedMpRegen = finalInt * baseMpRegenPerP;
        float derivedSkillEnhance = (finalInt / 10) * baseSkillEnhPer10; // Bonus

        // 敏捷派生
        float baseAtkSpdPerP = attrConfig != null ? attrConfig.agi_atkSpdPerP : 2f;
        float baseDodgePerP = attrConfig != null ? attrConfig.agi_dodgePerP : 0.01f;
        float baseCdReducePer10 = attrConfig != null ? attrConfig.agi_cdReducePer10 : 0.05f;
        float atkSpeedBase = attrConfig != null ? attrConfig.atkSpeedBase : 100f;

        float derivedAttackSpeed = atkSpeedBase + finalAgi * baseAtkSpdPerP;
        float derivedDodgeChance = finalAgi * baseDodgePerP;
        float derivedCooldownReduction = (finalAgi / 10) * baseCdReducePer10; // Bonus

        // 缓存派生值（供 GetDerivedValue 查询）
        derivedCache[StatId.MaxHealth] = derivedMaxHealth;
        derivedCache[StatId.HpRegen] = derivedHpRegen;
        derivedCache[StatId.Armor] = derivedArmor;
        derivedCache[StatId.MaxMana] = derivedMaxMana;
        derivedCache[StatId.ManaRegen] = derivedMpRegen;
        derivedCache[StatId.SkillEnhance] = derivedSkillEnhance;
        derivedCache[StatId.AttackSpeed] = derivedAttackSpeed;
        derivedCache[StatId.DodgeChance] = derivedDodgeChance;
        derivedCache[StatId.CooldownReduction] = derivedCooldownReduction;

        // ── Step 6: 派生基础值注入修饰器管线（priority=-100，先于装备/技能）──
        // 这些是需要经过后续%修饰器叠加的基础值（如 maxHealth 会被装备+%HP 修饰）
        // 批量注入：一次性收集全部 Modifier，仅触发一次属性刷新（避免逐 AddModifier 事件风暴）
        var mods = new List<Modifier>(9)
        {
            new Modifier(StatId.MaxHealth, derivedMaxHealth, ModifierType.Flat, SOURCE_DERIVED_PREFIX + StatId.MaxHealth, priority: -100),
            new Modifier(StatId.HpRegen, derivedHpRegen, ModifierType.Flat, SOURCE_DERIVED_PREFIX + StatId.HpRegen, priority: -100),
            new Modifier(StatId.MaxMana, derivedMaxMana, ModifierType.Flat, SOURCE_DERIVED_PREFIX + StatId.MaxMana, priority: -100),
            new Modifier(StatId.ManaRegen, derivedMpRegen, ModifierType.Flat, SOURCE_DERIVED_PREFIX + StatId.ManaRegen, priority: -100),
            new Modifier(StatId.AttackSpeed, derivedAttackSpeed, ModifierType.Flat, SOURCE_DERIVED_PREFIX + StatId.AttackSpeed, priority: -100),
            new Modifier(StatId.DodgeChance, derivedDodgeChance, ModifierType.Flat, SOURCE_DERIVED_PREFIX + StatId.DodgeChance, priority: -100),

            // ── Step 6b: Bonus 加成注入（priority=999，独立 source，不与装备%混淆）──
            new Modifier(StatId.Armor, derivedArmor, ModifierType.Flat, SOURCE_BONUS_PREFIX + StatId.Armor, priority: 999),
            new Modifier(StatId.SkillEnhance, derivedSkillEnhance, ModifierType.Flat, SOURCE_BONUS_PREFIX + StatId.SkillEnhance, priority: 999),
            new Modifier(StatId.CooldownReduction, derivedCooldownReduction, ModifierType.Flat, SOURCE_BONUS_PREFIX + StatId.CooldownReduction, priority: 999),
        };
        statModManager.AddModifiers(mods);

        // ── 事件通知 ──
        EventBus.Trigger(new PlayerAttrChangedEvent(finalStr, finalInt, finalAgi));

        string[] changedStatIds = {
            StatId.MaxHealth, StatId.HpRegen, StatId.Armor,
            StatId.MaxMana, StatId.ManaRegen, StatId.SkillEnhance,
            StatId.AttackSpeed, StatId.DodgeChance, StatId.CooldownReduction
        };
        EventBus.Trigger(new PlayerAttrRecalculatedEvent(changedStatIds));

        // Debug.Log($"[PlayerAttributeSystem] 重算完成 STR={finalStr} INT={finalInt} AGI={finalAgi}");
    }

    // ============================================================
    // 内部方法 — 修饰器移除
    // ============================================================

    /// <summary>移除所有由属性系统注入的修饰器（批量，仅触发一次属性刷新）</summary>
    private void RemoveAllDerivedModifiers()
    {
        var sources = new List<string>(9);

        // 移除所有派生基础值 source
        string[] derivedStats = {
            StatId.MaxHealth, StatId.HpRegen, StatId.MaxMana,
            StatId.ManaRegen, StatId.AttackSpeed, StatId.DodgeChance
        };
        foreach (var statId in derivedStats)
            sources.Add(SOURCE_DERIVED_PREFIX + statId);

        // 移除所有 Bonus source
        string[] bonusStats = { StatId.Armor, StatId.SkillEnhance, StatId.CooldownReduction };
        foreach (var statId in bonusStats)
            sources.Add(SOURCE_BONUS_PREFIX + statId);

        statModManager.RemoveModifiers(sources);
    }
}
