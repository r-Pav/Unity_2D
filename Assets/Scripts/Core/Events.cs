using UnityEngine;

/// <summary>子弹命中事件 — 用于其他模块（特效、音效、成就）响应命中</summary>
public readonly struct ProjectileHitEvent
{
    public readonly GameObject target;
    public readonly float damage;
    public readonly Vector2 hitPoint;
    public readonly GameObject source; // 发射者

    public ProjectileHitEvent(GameObject target, float damage, Vector2 hitPoint, GameObject source = null)
    {
        this.target = target;
        this.damage = damage;
        this.hitPoint = hitPoint;
        this.source = source;
    }
}

/// <summary>敌人死亡事件 — 用于计分、掉落、任务系统</summary>
public readonly struct EnemyDeathEvent
{
    public readonly EnemyControllerBase enemy;
    public readonly Vector2 position;

    public EnemyDeathEvent(EnemyControllerBase enemy, Vector2 position)
    {
        this.enemy = enemy;
        this.position = position;
    }
}

/// <summary>砸地事件 — 全屏震屏、AOE 伤害等订阅</summary>
public readonly struct GroundPoundEvent
{
    public readonly Vector2 center;
    public readonly float radius;
    public readonly float damage;
    public readonly float knockbackForce;
    public readonly LayerMask targetLayers;

    public GroundPoundEvent(Vector2 center, float radius, float damage, float knockbackForce, LayerMask targetLayers)
    {
        this.center = center;
        this.radius = radius;
        this.damage = damage;
        this.knockbackForce = knockbackForce;
        this.targetLayers = targetLayers;
    }
}

// ============================================================
// 技能系统事件（Phase 1）
// ============================================================

/// <summary>技能激活事件 — 具体技能逻辑（Phase 2）订阅此事件执行</summary>
public readonly struct SkillActivatedEvent
{
    public readonly string skillName;
    public readonly int slotIndex;
    public readonly int skillLevel;
    public readonly GameObject source;

    public SkillActivatedEvent(string skillName, int slotIndex, int skillLevel, GameObject source = null)
    {
        this.skillName = skillName;
        this.slotIndex = slotIndex;
        this.skillLevel = skillLevel;
        this.source = source;
    }
}

/// <summary>技能冷却结束事件 — UI 冷却遮罩等订阅</summary>
public readonly struct SkillCooldownEndEvent
{
    public readonly string skillName;
    public readonly int slotIndex;

    public SkillCooldownEndEvent(string skillName, int slotIndex)
    {
        this.skillName = skillName;
        this.slotIndex = slotIndex;
    }
}

/// <summary>技能等级变化事件</summary>
public readonly struct SkillLevelChangedEvent
{
    public readonly string skillName;
    public readonly int slotIndex;
    public readonly int newLevel;

    public SkillLevelChangedEvent(string skillName, int slotIndex, int newLevel)
    {
        this.skillName = skillName;
        this.slotIndex = slotIndex;
        this.newLevel = newLevel;
    }
}

/// <summary>协同联动激活事件</summary>
public readonly struct SynergyActivatedEvent
{
    public readonly int requiredLevel;
    public readonly string bonusName;
    public readonly float cooldownMultiplier;
    public readonly float manaRegenBonus;
    public readonly float effectMultiplier;

    public SynergyActivatedEvent(int requiredLevel, string bonusName,
        float cooldownMultiplier, float manaRegenBonus, float effectMultiplier)
    {
        this.requiredLevel = requiredLevel;
        this.bonusName = bonusName;
        this.cooldownMultiplier = cooldownMultiplier;
        this.manaRegenBonus = manaRegenBonus;
        this.effectMultiplier = effectMultiplier;
    }
}

// ============================================================
// HUD / UI 事件
// ============================================================

/// <summary>玩家生命值变化事件 — HUD 血条等订阅</summary>
public readonly struct PlayerHealthChangedEvent
{
    public readonly float currentHealth;
    public readonly float maxHealth;
    public readonly float ratio;

    public PlayerHealthChangedEvent(float cur, float max)
    {
        currentHealth = cur;
        maxHealth = max;
        ratio = max > 0 ? cur / max : 0f;
    }
}

/// <summary>玩家法力值变化事件 — HUD 蓝条等订阅</summary>
public readonly struct PlayerManaChangedEvent
{
    public readonly float currentMana;
    public readonly float maxMana;
    public readonly float ratio;

    public PlayerManaChangedEvent(float cur, float max)
    {
        currentMana = cur;
        maxMana = max;
        ratio = max > 0 ? cur / max : 0f;
    }
}

// ============================================================
// P1 属性系统事件
// ============================================================

/// <summary>[P1] 修饰器列表变化事件 — 任何修饰器增删后触发，订阅方拉取最新属性值</summary>
public readonly struct StatModifiersChangedEvent
{
    public readonly string[] affectedStatIds;

    public StatModifiersChangedEvent(string[] affectedStatIds)
    {
        this.affectedStatIds = affectedStatIds;
    }
}

/// <summary>[P1] 玩家属性最终值重算事件 — 某属性最终值变化时触发，HUD 数值显示订阅</summary>
public readonly struct PlayerStatRecalculatedEvent
{
    public readonly string statId;
    public readonly float oldValue;
    public readonly float newValue;

    public PlayerStatRecalculatedEvent(string statId, float oldValue, float newValue)
    {
        this.statId = statId;
        this.oldValue = oldValue;
        this.newValue = newValue;
    }
}

/// <summary>[P1] 玩家技能点数变化事件 — SkillPointManager 触发，HUD 点数显示订阅</summary>
public readonly struct PlayerSkillPointsChangedEvent
{
    public readonly int currentPoints;
    public readonly int maxPoints;

    public PlayerSkillPointsChangedEvent(int current, int max)
    {
        currentPoints = current;
        maxPoints = max;
    }
}

// ============================================================
// P2 被动系统事件
// ============================================================

/// <summary>[P2] 被动槽位变化事件 — 装备/卸下/空选被动时触发，UI 被动面板订阅刷新</summary>
public readonly struct PassiveSlotsChangedEvent
{
    /// <summary>层级 0~4（对应 TI~TV）</summary>
    public readonly int layer;
    /// <summary>线 ID 0~4，或 -2(空)</summary>
    public readonly int lineId;
    /// <summary>该层内槽位索引 0~2</summary>
    public readonly int slotIndex;
    /// <summary>操作类型："equip" / "unequip" / "empty"</summary>
    public readonly string action;

    public PassiveSlotsChangedEvent(int layer, int lineId, int slotIndex, string action)
    {
        this.layer = layer;
        this.lineId = lineId;
        this.slotIndex = slotIndex;
        this.action = action;
    }
}

// ============================================================
// P3 主动技能分支事件
// ============================================================

/// <summary>[P3] 主动技能分支选择确认事件 — 玩家在弹窗中选择分支后触发</summary>
public readonly struct BranchChosenEvent
{
    /// <summary>技能名称</summary>
    public readonly string skillName;
    /// <summary>技能槽位索引</summary>
    public readonly int slotIndex;
    /// <summary>选择的分支："Left" 或 "Right"</summary>
    public readonly string branch;

    public BranchChosenEvent(string skillName, int slotIndex, string branch)
    {
        this.skillName = skillName;
        this.slotIndex = slotIndex;
        this.branch = branch;
    }
}

// ============================================================
// P4 武器系统事件
// ============================================================

/// <summary>[P4] 武器装备事件 — WeaponSystem 发出，WeaponSkillLink 订阅以激活武器技能</summary>
public readonly struct WeaponEquippedEvent
{
    /// <summary>装备的武器类型</summary>
    public readonly WeaponType weaponType;
    /// <summary>武器技能数据（SO 引用，由 WeaponSystem 或 WeaponSkillLink 自行查找）</summary>
    public readonly WeaponSkillData skillData;

    public WeaponEquippedEvent(WeaponType weaponType, WeaponSkillData skillData = null)
    {
        this.weaponType = weaponType;
        this.skillData = skillData;
    }
}

/// <summary>[P4] 武器卸下事件 — WeaponSystem 发出，WeaponSkillLink 订阅以移除武器技能</summary>
public readonly struct WeaponUnequippedEvent
{
    /// <summary>卸下的武器类型</summary>
    public readonly WeaponType weaponType;

    public WeaponUnequippedEvent(WeaponType weaponType)
    {
        this.weaponType = weaponType;
    }
}

// ============================================================
// P5 组合技能合成事件
// ============================================================

/// <summary>[P5] 组合技能合成完成事件 — 合成完成后触发，UI 面板、成就等订阅</summary>
public readonly struct CombinationCraftedEvent
{
    /// <summary>被消耗的材料技能名称列表</summary>
    public readonly string[] materialSkillIds;
    /// <summary>产出技能 ID（可用于查找 SO）</summary>
    public readonly string resultSkillId;
    /// <summary>产出技能显示名称</summary>
    public readonly string resultName;

    public CombinationCraftedEvent(string[] materialSkillIds, string resultSkillId, string resultName)
    {
        this.materialSkillIds = materialSkillIds;
        this.resultSkillId = resultSkillId;
        this.resultName = resultName;
    }
}

// ============================================================
// Phase2 属性系统事件
// ============================================================

/// <summary>[Phase2] 主属性变化事件 — 力量/智力/敏捷任一值变化时触发，HUD/装备系统订阅</summary>
public readonly struct PlayerAttrChangedEvent
{
    public readonly int strength;
    public readonly int intelligence;
    public readonly int agility;

    public PlayerAttrChangedEvent(int str, int i, int agi)
    {
        strength = str;
        intelligence = i;
        agility = agi;
    }
}

/// <summary>[Phase2] 派生属性重算完成事件 — 装备系统/UI 订阅刷新数值显示</summary>
public readonly struct PlayerAttrRecalculatedEvent
{
    /// <summary>本次重算涉及的属性 ID 列表</summary>
    public readonly string[] changedStatIds;

    public PlayerAttrRecalculatedEvent(string[] statIds)
    {
        changedStatIds = statIds;
    }
}

// ============================================================
// Phase3 装备系统事件
// ============================================================

/// <summary>[Phase3] 敌人拾取装备事件 — AI/音效/成就等模块订阅</summary>
public readonly struct EnemyEquipmentPickupEvent
{
    /// <summary>拾取装备的敌人</summary>
    public readonly EnemyControllerBase enemy;
    /// <summary>被拾取的物品实例</summary>
    public readonly ItemInstance item;
    /// <summary>装备等级</summary>
    public readonly int level;

    public EnemyEquipmentPickupEvent(EnemyControllerBase enemy, ItemInstance item, int level)
    {
        this.enemy = enemy;
        this.item = item;
        this.level = level;
    }
}

/// <summary>玩家死亡事件</summary>
public readonly struct PlayerDeathEvent { }

// ============================================================
// 格挡/弹反事件
// ============================================================

/// <summary>弹反成功事件 — HUD Buff 图标 + 音效订阅</summary>
public readonly struct ParrySuccessEvent { }

/// <summary>弹反 Buff 消耗事件 (重击已打出) — HUD 隐藏图标</summary>
public readonly struct ParryBuffConsumedEvent { }

// ============================================================
// 章节进度事件（被动解锁改造）
// ============================================================

// ============================================================
// 地区切换事件
// ============================================================

/// <summary>地区切换完成事件 — 离开来源地区、进入目标地区后触发</summary>
public readonly struct AreaSwitchEvent
{
    /// <summary>来源地区 key（null = 初始场景地区，非 Addressable 加载）</summary>
    public readonly string sourceKey;
    /// <summary>目标地区 key</summary>
    public readonly string targetKey;

    public AreaSwitchEvent(string sourceKey, string targetKey)
    {
        this.sourceKey = sourceKey;
        this.targetKey = targetKey;
    }
}

/// <summary>章节进度变化事件 — PassiveEquipManager.SetChapter/AdvanceChapter 触发</summary>
public readonly struct ChapterChangedEvent
{
    /// <summary>新章节号 (1~5)</summary>
    public readonly int chapter;

    public ChapterChangedEvent(int chapter)
    {
        this.chapter = chapter;
    }
}
