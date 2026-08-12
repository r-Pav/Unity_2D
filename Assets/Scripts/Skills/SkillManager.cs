using UnityEngine;

/// <summary>
/// 技能管理器 — 挂在 Player GameObject 上
/// 负责：技能槽管理、冷却计时、法力管理、输入检测、事件触发
/// Phase 1：只做基础框架，具体技能的施放逻辑留 Phase 2
/// </summary>
public class SkillManager : MonoBehaviour
{
    // ============================================================
    // Singleton 注册表（Player 子组件；调用方统一走 Instance）
    // ============================================================

    private static SkillManager _instance;

    public static SkillManager Instance
    {
        get
        {
            if (_instance == null)
                _instance = FindObjectOfType<SkillManager>();
            return _instance;
        }
    }

    // ============================================================
    // 序列化配置
    // ============================================================

    [Header("技能槽")]
    [Tooltip("最多 4 个技能槽，Inspector 拖入 SkillData ScriptableObject 即可")]
    [SerializeField] private SkillSlot[] skillSlots = new SkillSlot[4];

    [Header("法力")]
    [Tooltip("已移至 PlayerAttrConfigSO.initialMana 统一配置；此处仅作运行时显示")]
    [SerializeField] private float maxMana = 100f;

    [Tooltip("每秒法力回复量")]
    [SerializeField] private float manaRegenPerSec = 5f;

    [Header("技能等级")]
    [SerializeField] private SynergyConfig synergyConfig;
    [SerializeField] private int initialSkillPoints = 10;

    [Header("P3 分支升级")]
    [SerializeField] private BranchUpgradeSystem branchSystem = new();

    // ============================================================
    // 运行时状态
    // ============================================================

    private SkillPool skillPool;                  // [P7] 技能池引用
    private float currentMana;                   // 当前法力
    private float lastManaEventValue;            // 上次发送事件的法力值（避免重复发送）
    private PlayerController owner;              // 所属玩家控制器
    private float[] cooldownTimers;              // 每个槽位独立冷却计时
    private int[] slotLevels;                    // 每个槽位技能等级
    private SynergyBonus activeSynergy;          // 当前激活的联动 Bonus
    private StatModifierManager statModManager;  // [P3] 属性修饰器（CD/法耗查询）
    private SkillPointManager skillPointManager; // [P3] 技能点管理器（替代自管池）
    private PlayerAttributeSystem attrSystem;     // 读取 SO 统一初始值

    // ============================================================
    // 公共属性
    // ============================================================

    public float CurrentMana => currentMana;
    /// <summary>基础最大法力值（未经过修饰器管线，SO 配置值）</summary>
    public float BaseMaxMana => maxMana;
    /// <summary>当前最大法力值（如有 StatModifierManager 则走修饰器管线）</summary>
    public float MaxMana => statModManager != null
        ? statModManager.GetFinalValue(maxMana, StatId.MaxMana)
        : maxMana;

    /// <summary>获取指定槽位的冷却剩余（秒）</summary>
    public float GetCooldownTimer(int index)
    {
        if (index < 0 || index >= cooldownTimers.Length) return 0f;
        return cooldownTimers[index];
    }

    /// <summary>获取指定槽位的冷却比例（0=冷却完毕，1=刚触发）</summary>
    public float GetCooldownRatio(int index)
    {
        if (index < 0 || index >= skillSlots.Length) return 0f;
        var data = skillSlots[index]?.data;
        if (data == null) return 0f;

        // [P3] 对 ActiveSkillData 使用分支等级对应的冷却时间
        float cd = data.cooldown;
        if (data is ActiveSkillData activeData)
        {
            int level = slotLevels[index];
            var branchData = activeData.GetBranchData(level);
            if (branchData != null) cd = branchData.cooldown;
        }

        if (cd <= 0f) return 0f;
        return Mathf.Clamp01(cooldownTimers[index] / cd);
    }

    // ============================================================
    // 生命周期
    // ============================================================

    private void Awake()
    {
        owner = GetComponent<PlayerController>();
        skillPool = GetComponent<SkillPool>();
        lastManaEventValue = currentMana;
        cooldownTimers = new float[skillSlots.Length];
        slotLevels = new int[skillSlots.Length];

        // [P7] 订阅 SkillPool 事件，同步 skillSlots 缓存
        if (skillPool != null)
        {
            skillPool.OnHudSlotChanged += SyncSlotFromPool;
            skillPool.OnPoolChanged += SyncAllSlotsFromPool;
        }

        // 主动技能从运行时未解锁状态开始；SO 的 skillLevel 只描述资产默认值。
        for (int i = 0; i < skillSlots.Length; i++)
        {
            SkillData data = skillSlots[i]?.data;
            slotLevels[i] = data is ActiveSkillData ? 0 : data?.skillLevel ?? 0;
            if (data is ActiveSkillData activeData)
                activeData.chosenBranch = null;
        }

        // [P3] 获取属性修饰器管理器（CD/法耗查询）
        statModManager = GetComponent<StatModifierManager>();

        // 从 PlayerAttrConfigSO 统一切换初始法力值
        attrSystem = GetComponent<PlayerAttributeSystem>();
        if (attrSystem != null && attrSystem.AttrConfig != null)
            maxMana = attrSystem.AttrConfig.initialMana;
        currentMana = maxMana;
        lastManaEventValue = currentMana;

        // [P3] 初始化分支升级系统
        var spm = GetComponent<SkillPointManager>();
        skillPointManager = spm;
        branchSystem.Initialize(this, spm, slotLevels, skillSlots);
    }

    private void Start()
    {
        // Start() 在所有 OnEnable() 之后执行，确保 HUD 已订阅事件
        EventBus.Trigger(new PlayerManaChangedEvent(currentMana, MaxMana));
    }

    // ============================================================
    // 每帧更新（由 PlayerController.OnUpdate 调用）
    // ============================================================

    /// <summary>
    /// 每帧由 PlayerController 调用
    /// 处理：法力回复、冷却计时、快捷键检测
    /// </summary>
    public void OnPlayerUpdate(PlayerController pc)
    {
        UpdateMana();
        UpdateCooldowns();
        CheckHotkeys();
    }

    private void UpdateMana()
    {
        float regenBonus = activeSynergy != null ? activeSynergy.manaRegenBonus : 0f;
        float effectiveRegen = statModManager != null
            ? statModManager.GetFinalValue(manaRegenPerSec, StatId.ManaRegen)
            : manaRegenPerSec;
        if (currentMana < MaxMana)
        {
            currentMana = Mathf.Min(MaxMana, currentMana + (effectiveRegen + regenBonus) * Time.deltaTime);
            NotifyManaChanged();
        }
    }

    private void UpdateCooldowns()
    {
        float cdScale = activeSynergy != null ? activeSynergy.cooldownMultiplier : 1f;
        for (int i = 0; i < cooldownTimers.Length; i++)
            UpdateCooldownTimer(i, cdScale);
    }

    private void UpdateCooldownTimer(int i, float cdScale)
    {
        if (cooldownTimers[i] <= 0f) return;
        cooldownTimers[i] -= Time.deltaTime / cdScale;
        if (cooldownTimers[i] <= 0f)
            OnCooldownExpired(i);
    }

    private void OnCooldownExpired(int i)
    {
        cooldownTimers[i] = 0f;
        EventBus.Trigger(new SkillCooldownEndEvent(
            skillSlots[i]?.data?.skillName ?? "",
            i
        ));
    }

    private void CheckHotkeys()
    {
        if (owner != null && !owner.InputEnabled) return;

        // [P7] 硬编码按键映射：Q=0, E=1, R=2, F=3
        KeyCode[] hudKeys = { KeyCode.Q, KeyCode.E, KeyCode.R, KeyCode.F };
        for (int i = 0; i < skillSlots.Length; i++)
        {
            var slot = skillSlots[i];
            if (slot.data == null) continue;
            if (!IsActivatableType(slot.data.type)) continue;
            if (Input.GetKeyDown(hudKeys[i]))
                TryActivate(i);
        }
    }

    private static bool IsActivatableType(SkillType type) =>
        type == SkillType.Active || type == SkillType.Toggle;

    // ============================================================
    // 技能激活
    // ============================================================

    /// <summary>尝试激活指定槽位的技能</summary>
    public void TryActivate(int index)
    {
        // 边界检查
        if (index < 0 || index >= skillSlots.Length) return;

        if (skillSlots[index] == null) return;
        var data = skillSlots[index].data;
        if (data == null) return;

        // 冷却检查
        if (cooldownTimers[index] > 0f) return;

        // [P3] 对 ActiveSkillData 使用分支等级对应的基础值
        float baseManaCost = data.manaCost;
        float baseCooldown = data.cooldown;
        if (data is ActiveSkillData activeData)
        {
            int level = slotLevels[index];
            var branchData = activeData.GetBranchData(level);
            if (branchData != null)
            {
                baseManaCost = branchData.manaCost;
                baseCooldown = branchData.cooldown;
            }
        }

        // [P3] 法力消耗受 StatModifierManager 修饰
        float effectiveManaCost = GetEffectiveManaCost(baseManaCost);

        // 法力检查
        if (!HasMana(effectiveManaCost)) return;

        // 消耗法力
        SpendMana(effectiveManaCost);

        // [P3] 冷却时间受 StatModifierManager 修饰
        cooldownTimers[index] = GetEffectiveCooldown(baseCooldown);

        // 发射技能激活事件（Phase 2 的具体技能逻辑会订阅此事件）
        EventBus.Trigger(new SkillActivatedEvent(
            data.skillName,
            index,
            slotLevels[index],
            gameObject
        ));

        // P3b:技能激活成功 → 切入技能释放状态(行为层表现 + 输入锁定,时长由状态类管理;
        // 技能冷却/法力逻辑保留在 SkillManager 数据层;快捷键(CheckHotkeys)/UI 按钮调用均触发)
        if (owner != null && owner.PlayerFsm != null && owner.SkillCastState != null)
            owner.PlayerFsm.ChangeState(owner.SkillCastState);

        Debug.Log($"[Skill] {data.skillName} activated (slot {index})");
    }

    // ============================================================
    // 法力管理
    // ============================================================

    /// <summary>消耗法力</summary>
    public void SpendMana(float amount)
    {
        currentMana = Mathf.Max(0f, currentMana - amount);
        NotifyManaChanged();
    }

    /// <summary>检查法力是否足够</summary>
    public bool HasMana(float amount)
    {
        return currentMana >= amount;
    }

    /// <summary>法力值变化时通知 UI（仅在实际值变化时发送事件）</summary>
    private void NotifyManaChanged()
    {
        if (currentMana != lastManaEventValue)
        {
            lastManaEventValue = currentMana;
            EventBus.Trigger(new PlayerManaChangedEvent(currentMana, MaxMana));
        }
    }

    // ============================================================
    // 技能等级管理
    // ============================================================

    /// <summary>获取指定槽位技能等级</summary>
    public int GetSkillLevel(int slotIndex) =>
        (slotIndex >= 0 && slotIndex < slotLevels.Length) ? slotLevels[slotIndex] : 0;

    /// <summary>[P5] 技能槽总数</summary>
    public int SlotCount => skillSlots.Length;

    /// <summary>[P5] 获取指定槽位的技能数据（null = 空槽）</summary>
    public SkillData GetSlotData(int slotIndex) =>
        (slotIndex >= 0 && slotIndex < skillSlots.Length) ? skillSlots[slotIndex]?.data : null;

    /// <summary>[P5] 检查指定槽位是否为空</summary>
    public bool IsSlotEmpty(int slotIndex) => GetSlotData(slotIndex) == null;

    /// <summary>[P5] 清空指定槽位（合成消耗材料时调用）</summary>
    public void ClearSlot(int slotIndex)
    {
        if (slotIndex < 0 || slotIndex >= skillSlots.Length) return;
        skillSlots[slotIndex].data = null;
        slotLevels[slotIndex] = 0;
        // [P7] 同步清空 SkillPool HUD 槽位
        skillPool?.ClearHudSlot(slotIndex);
        RefreshSynergy();
    }

    /// <summary>[P5] 设置指定槽位（合成产出分配时调用）</summary>
    public void SetSlot(int slotIndex, SkillData data, int level)
    {
        if (slotIndex < 0 || slotIndex >= skillSlots.Length) return;
        skillSlots[slotIndex].data = data;
        slotLevels[slotIndex] = level;
        // [P7] 同步装备到 SkillPool HUD 槽位
        if (data != null)
            skillPool?.EquipToHud(slotIndex, data.skillName);
        else
            skillPool?.ClearHudSlot(slotIndex);
        RefreshSynergy();
    }

    // ============================================================
    // [P7] SkillPool 同步方法
    // ============================================================

    /// <summary>SkillPool HUD 槽位变化时同步更新 skillSlots 缓存</summary>
    private void SyncSlotFromPool(int hudIndex)
    {
        if (hudIndex < 0 || hudIndex >= skillSlots.Length) return;
        var entry = skillPool?.GetHudSkill(hudIndex);
        skillSlots[hudIndex].data = entry?.skillData;
        slotLevels[hudIndex] = entry?.level ?? 0;
    }

    /// <summary>SkillPool 池内容变化时刷新所有 slot 缓存</summary>
    private void SyncAllSlotsFromPool()
    {
        for (int i = 0; i < skillSlots.Length; i++)
            SyncSlotFromPool(i);
        RefreshSynergy();
    }

    /// <summary>可用技能点数（委托 SkillPointManager）</summary>
    public int AvailableSkillPoints => skillPointManager != null ? skillPointManager.CurrentSkillPoints : 0;

    /// <summary>升级指定槽位技能</summary>
    public bool LevelUp(int slotIndex)
    {
        if (slotIndex < 0 || slotIndex >= skillSlots.Length) return false;
        var data = skillSlots[slotIndex]?.data;
        if (data == null || slotLevels[slotIndex] >= data.maxLevel) return false;

        // [P3] 分支技能：委托给 BranchUpgradeSystem 处理
        if (data is ActiveSkillData)
        {
            return branchSystem.TryUpgrade(slotIndex);
        }

        // 原有逻辑：非分支技能
        if (skillPointManager == null || !skillPointManager.CanSpend(1)) return false;

        skillPointManager.SpendPoints(1);
        slotLevels[slotIndex]++;
        EventBus.Trigger(new SkillLevelChangedEvent(data.skillName, slotIndex, slotLevels[slotIndex]));
        RefreshSynergy();
        Debug.Log($"[SkillManager] {data.skillName} 升级到 Lv{slotLevels[slotIndex]}");
        return true;
    }

    /// <summary>检查协同联动：所有已装备技能的最低等级匹配哪个 Bonus</summary>
    public void RefreshSynergy()
    {
        int minLevel = int.MaxValue;
        for (int i = 0; i < skillSlots.Length; i++)
        {
            if (skillSlots[i]?.data == null) continue;
            minLevel = Mathf.Min(minLevel, slotLevels[i]);
        }

        activeSynergy = null;
        if (synergyConfig != null && synergyConfig.bonuses != null)
        {
            for (int i = synergyConfig.bonuses.Length - 1; i >= 0; i--)
            {
                if (minLevel >= synergyConfig.bonuses[i].requiredLevel)
                {
                    activeSynergy = synergyConfig.bonuses[i];
                    break;
                }
            }
        }

        if (activeSynergy != null)
        {
            EventBus.Trigger(new SynergyActivatedEvent(
                activeSynergy.requiredLevel, activeSynergy.bonusName,
                activeSynergy.cooldownMultiplier, activeSynergy.manaRegenBonus,
                activeSynergy.effectMultiplier
            ));
            Debug.Log($"[SkillManager] 联动激活: {activeSynergy.bonusName}");
        }
    }

    /// <summary>[P3] 暴露分支升级系统（供 UI 查询 IsWaitingForBranchChoice 等状态）</summary>
    public BranchUpgradeSystem BranchSystem => branchSystem;

    // ============================================================
    // [P3] CD/法耗修饰器查询
    // ============================================================

    /// <summary>
    /// [P3] 获取受 StatModifierManager 修饰后的冷却时间
    /// 公式：baseCooldown × CooldownMultiplier 最终值
    /// </summary>
    private float GetEffectiveCooldown(float baseCooldown)
    {
        if (statModManager == null) return baseCooldown;
        float cdMult = statModManager.GetFinalValue(1f, StatId.CooldownMultiplier);
        return baseCooldown * cdMult;
    }

    /// <summary>
    /// [P3] 获取受 StatModifierManager 修饰后的法力消耗
    /// 公式：baseManaCost × ManaCostMultiplier 最终值
    /// </summary>
    private float GetEffectiveManaCost(float baseManaCost)
    {
        if (statModManager == null) return baseManaCost;
        float manaMult = statModManager.GetFinalValue(1f, StatId.ManaCostMultiplier);
        return baseManaCost * manaMult;
    }
}
