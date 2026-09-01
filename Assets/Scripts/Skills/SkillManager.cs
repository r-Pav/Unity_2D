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
    // [阶段7] 充能模型（7.1）：useCharges 技能走充能——每槽独立计数 + 已消耗充能各自独立恢复计时；
    // 未启用充能的技能（useCharges=false）完全走原单 CD 路径（零回归）。
    private int[] chargeCounts;                  // 每槽当前可用充能数
    private System.Collections.Generic.List<float>[] chargeTimers; // 每槽已消耗充能的独立恢复计时（1 消耗 = 1 槽）
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

    /// <summary>获取指定槽位的冷却剩余（秒）；充能技能返回「下一个充能恢复」剩余秒数（无恢复中 = 0）</summary>
    public float GetCooldownTimer(int index)
    {
        if (index < 0 || index >= cooldownTimers.Length) return 0f;
        if (IsChargeSkill(index)) return GetNextChargeTimer(index);
        return cooldownTimers[index];
    }

    /// <summary>获取指定槽位的冷却比例（0=冷却完毕，1=刚触发）；充能技能返回下一个充能的恢复进度</summary>
    public float GetCooldownRatio(int index)
    {
        if (index < 0 || index >= skillSlots.Length) return 0f;
        var data = skillSlots[index]?.data;
        if (data == null) return 0f;

        // [阶段7] 充能模型：充能满 = 无遮罩；否则按下一个充能的恢复进度显示
        if (data.useCharges)
        {
            if (chargeCounts[index] >= data.maxCharges) return 0f;
            float recharge = data.chargeRechargeTime > 0f ? data.chargeRechargeTime : data.cooldown;
            if (recharge <= 0f) return 0f;
            return Mathf.Clamp01(GetNextChargeTimer(index) / recharge);
        }

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
    // [阶段7] 充能查询（HUD 充能显示；PlayerDash 充能保持独立，不并入本管理器）
    // ============================================================

    /// <summary>该槽位是否启用充能模型（useCharges=true）</summary>
    public bool IsChargeSkill(int index)
    {
        if (index < 0 || index >= skillSlots.Length) return false;
        return skillSlots[index]?.data?.useCharges ?? false;
    }

    /// <summary>当前可用充能数（非充能技能返回 0；HUD 按 IsChargeSkill 判断显示）</summary>
    public int GetCharges(int index)
    {
        if (index < 0 || index >= chargeCounts.Length) return 0;
        return chargeCounts[index];
    }

    /// <summary>最大充能数（非充能技能返回 1）</summary>
    public int GetMaxCharges(int index)
    {
        if (index < 0 || index >= skillSlots.Length) return 1;
        return skillSlots[index]?.data?.maxCharges ?? 1;
    }

    /// <summary>下一个充能恢复的剩余秒数（无恢复中 = 0）</summary>
    private float GetNextChargeTimer(int index)
    {
        var timers = chargeTimers[index];
        if (timers == null || timers.Count == 0) return 0f;
        float min = float.MaxValue;
        for (int t = 0; t < timers.Count; t++)
            if (timers[t] < min) min = timers[t];
        return Mathf.Max(0f, min);
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

        // [阶段7] 充能模型：初始化计数与恢复槽（useCharges 技能启动补满）
        chargeCounts = new int[skillSlots.Length];
        chargeTimers = new System.Collections.Generic.List<float>[skillSlots.Length];
        for (int i = 0; i < skillSlots.Length; i++)
        {
            chargeTimers[i] = new System.Collections.Generic.List<float>();
            InitSlotCharges(i);
        }

        // [P7] 订阅 SkillPool 事件，同步 skillSlots 缓存
        if (skillPool != null)
        {
            skillPool.OnHudSlotChanged += SyncSlotFromPool;
            skillPool.OnPoolChanged += SyncAllSlotsFromPool;
        }

        // 主动技能从运行时未解锁状态开始；SO 的 skillLevel 只描述资产默认值。
        // defaultUnlocked 技能开局自动解锁到 Lv1（不消耗技能点；读档时以存档等级为准，见 Start 事件重放）
        for (int i = 0; i < skillSlots.Length; i++)
        {
            SkillData data = skillSlots[i]?.data;
            if (data is ActiveSkillData activeData)
            {
                slotLevels[i] = activeData.defaultUnlocked ? 1 : 0;
                activeData.chosenBranch = null;
            }
            else
            {
                slotLevels[i] = data?.skillLevel ?? 0;
            }
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

        // 默认解锁事件重放：defaultUnlocked 技能在 Awake 已置 Lv1，但事件不能在 Awake 发
        // （静态执行器 AfterSceneLoad 订阅、UI 在 OnEnable 订阅，均晚于 Awake）——此处补发驱动
        // SkillPool 等级同步 / HUD 与技能树刷新 / 执行器解锁效果。
        // 读档模式：SetSlot 已按存档等级重发事件（订阅方幂等）；slotLevels 被存档覆盖为 0 的槽位不重发，存档优先。
        for (int i = 0; i < skillSlots.Length; i++)
        {
            SkillData data = skillSlots[i]?.data;
            if (data is ActiveSkillData activeData && activeData.defaultUnlocked && slotLevels[i] >= 1)
                EventBus.Trigger(new SkillLevelChangedEvent(data.skillName, i, slotLevels[i]));
        }
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
        UpdateTimers();
        CheckHotkeys();
    }

    /// <summary>
    /// 数值层更新(法力回复 + CD/充能计时),不含按键检测。
    /// 由 PlayerController 在锁定判定前调用:攻击/受击/冲刺等 LocksInput 状态期间 CD 照常转,
    /// 卡帧(timeScale=0)也不停(unscaledDeltaTime),只冻视觉不冻数值。
    /// </summary>
    public void UpdateTimers()
    {
        UpdateMana();
        UpdateCooldowns();
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
        // [阶段7] 充能模型：useCharges 技能遍历恢复充能（每充能独立计时）
        if (skillSlots[i]?.data is SkillData chargeSkill && chargeSkill.useCharges)
        {
            UpdateChargeTimer(i, chargeSkill, cdScale);
            return;
        }

        if (cooldownTimers[i] <= 0f) return;
        // 卡帧(timeScale=0)期间 CD 照常转:卡帧只冻视觉,不冻技能数值(2026-08-19 saika 确认方案1)
        cooldownTimers[i] -= Time.unscaledDeltaTime / cdScale;
        if (cooldownTimers[i] <= 0f)
            OnCooldownExpired(i);
    }

    /// <summary>
    /// [阶段7] 充能恢复：遍历该槽所有恢复中的充能槽，恢复满则 chargeCounts++（上限 maxCharges）。
    /// 用 unscaledDeltaTime：卡帧(timeScale=0)期间充能照常恢复（与阶段 6 抗卡帧一致）。
    /// 最后一个充能恢复满（计数回满且无恢复中）时触发 SkillCooldownEndEvent（HUD 充能转好提示）。
    /// </summary>
    private void UpdateChargeTimer(int i, SkillData data, float cdScale)
    {
        System.Collections.Generic.List<float> timers = chargeTimers[i];
        if (timers == null || timers.Count == 0) return;

        for (int t = timers.Count - 1; t >= 0; t--)
        {
            timers[t] -= Time.unscaledDeltaTime / cdScale;
            if (timers[t] <= 0f)
            {
                timers.RemoveAt(t);
                if (chargeCounts[i] < data.maxCharges)
                    chargeCounts[i]++;
            }
        }

        // 计数回满且无恢复中 = 最后一个充能恢复满（本帧触发一次；下帧 timers 空提前 return 不会重复）
        if (chargeCounts[i] >= data.maxCharges && timers.Count == 0)
        {
            EventBus.Trigger(new SkillCooldownEndEvent(
                data.skillName ?? "",
                i
            ));
        }
    }

    private void OnCooldownExpired(int i)
    {
        cooldownTimers[i] = 0f;
        EventBus.Trigger(new SkillCooldownEndEvent(
            skillSlots[i]?.data?.skillName ?? "",
            i
        ));
    }

    public void CheckHotkeys()
    {
        if (owner != null && !owner.InputEnabled) return;

        // [重音背刺] 当前曲启用自动重音(barIntervalSeconds>0)时,F 归背刺/普攻挥空用,不再触发技能槽 3(背刺优先)
        bool fReserved = IsFReservedByBackstab();

        // [P7] 硬编码按键映射：Q=0, E=1, R=2, F=3
        KeyCode[] hudKeys = { KeyCode.Q, KeyCode.E, KeyCode.R, KeyCode.F };
        for (int i = 0; i < skillSlots.Length; i++)
        {
            var slot = skillSlots[i];
            if (slot.data == null) continue;
            if (!IsActivatableType(slot.data.type)) continue;
            if (fReserved && hudKeys[i] == KeyCode.F) continue;
            if (Input.GetKeyDown(hudKeys[i]))
                TryActivate(i);
        }
    }

    /// <summary>F 是否被重音背刺占用:当前曲 barIntervalSeconds>0(自动重音启用)时为 true</summary>
    private static bool IsFReservedByBackstab()
    {
        var mgr = MusicPointManager.Instance;
        return mgr != null && mgr.CurrentTrack != null && mgr.CurrentTrack.barIntervalSeconds > 0f;
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

        // 升级解锁被动型(树B 等):不响应激活——不进 CD、不切释放态、不发事件(UI 点击/热键双保险)
        if (data is ActiveSkillData passiveTree && passiveTree.unlockPassiveOnly) return;

        // 冷却检查（传送弹二次激活,阶段 5：执行器挂起未使用的传送弹时,CD 期间允许再按技能键触发传送）
        // [阶段7] 充能模型：useCharges 技能「有充能则消耗并激活」；否则走原单 CD 路径（零回归）
        // [2026-08-20 A02B02] 二次激活(技能键传送)不扣充能：pendingReactivate 时放行且不消耗
        bool pendingReactivate = SkillExecutorRegistry.HasPendingReactivation(data.skillName);
        if (data.useCharges)
        {
            if (!pendingReactivate && chargeCounts[index] <= 0) return; // 充能耗尽且非二次激活不可激活
        }
        else
        {
            if (!pendingReactivate && cooldownTimers[index] > 0f) return;
        }

        // [P3] 对 ActiveSkillData 使用分支等级对应的基础值
        // [MP-REMOVED 2026-08-17] 删除 MP 判定:不再检查/扣蓝,CD 为唯一限制。
        // 字段/UI/资产数据全部保留,防止后续恢复 MP。
        // float baseManaCost = data.manaCost;          // 恢复时取消注释
        float baseCooldown = data.cooldown;
        if (data is ActiveSkillData activeData)
        {
            int level = slotLevels[index];
            var branchData = activeData.GetBranchData(level);
            if (branchData != null)
            {
                // baseManaCost = branchData.manaCost;  // 恢复时取消注释
                baseCooldown = branchData.cooldown;
            }
        }

        // [MP-REMOVED] 不再查询 ManaCostMultiplier / 检查 HasMana / 扣除 SpendMana。
        // 恢复 MP 时取消注释下面 3 行:
        // float effectiveManaCost = GetEffectiveManaCost(baseManaCost);
        // if (!HasMana(effectiveManaCost)) return;
        // SpendMana(effectiveManaCost);

        // [P3] 冷却时间受 StatModifierManager 修饰
        // 扣充能段:二次激活(传送)不扣、不开新恢复计时
        if (data.useCharges)
        {
            if (!pendingReactivate)
            {
                chargeCounts[index]--;
                // 每充能恢复时间（未配置时回退到技能 cooldown；同样受 CooldownMultiplier 修饰，与 CD 一致）
                float recharge = data.chargeRechargeTime > 0f ? data.chargeRechargeTime : baseCooldown;
                chargeTimers[index].Add(GetEffectiveCooldown(Mathf.Max(0f, recharge)));
            }
        }
        else
        {
            cooldownTimers[index] = GetEffectiveCooldown(baseCooldown);
        }

        // 发射技能激活事件（Phase 2 的具体技能逻辑会订阅此事件）
        EventBus.Trigger(new SkillActivatedEvent(
            data.skillName,
            index,
            slotLevels[index],
            gameObject
        ));

        // P3b:技能激活成功 → 切入技能释放状态(行为层表现 + 输入锁定,时长由状态类管理;
        // 技能冷却/法力逻辑保留在 SkillManager 数据层;快捷键(CheckHotkeys)/UI 按钮调用均触发)
        // [阶段7 B9 出口]：interceptsStateAfterActivate 技能由执行器接管状态（瞄准选点等长时选点），
        // 不切 PlayerSkillCastState 固定 0.25s（否则瞄准态会被强制弹回 Idle/Move）
        if (data.interceptsStateAfterActivate)
        {
            Debug.Log($"[Skill] {data.skillName} activated (slot {index}) - state intercepted by executor");
        }
        else if (owner != null && owner.PlayerFsm != null && owner.SkillCastState != null)
        {
            owner.PlayerFsm.ChangeState(owner.SkillCastState);
        }

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
        // [阶段7] 槽位技能变更：充能状态重置（空槽 = 0 计数 + 清恢复槽）
        InitSlotCharges(slotIndex);
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
        // [阶段7] 槽位技能变更：充能状态重置为满（新技能新状态，防旧技能残留计数）
        InitSlotCharges(slotIndex);
        // [P7] 同步装备到 SkillPool HUD 槽位
        if (data != null)
            skillPool?.EquipToHud(slotIndex, data.skillName);
        else
            skillPool?.ClearHudSlot(slotIndex);

        // [阶段3] 补发等级变化事件：SaveSystem 读档恢复唯一走 SetSlot（LevelUp 已触发），
        // 升级类被动解锁（TreeB_Dash 充能+冲刺伤害）依赖此事件重放，否则读档后解锁丢失。
        // 订阅者幂等：DashUpgradeExecutor 重复应用安全 / SkillPool 有 newLevel>entry.level 守卫 / UI 仅重读状态。
        if (data != null && level >= 1)
            EventBus.Trigger(new SkillLevelChangedEvent(data.skillName, slotIndex, level));

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
        SkillData oldData = skillSlots[hudIndex].data;
        skillSlots[hudIndex].data = entry?.skillData;
        slotLevels[hudIndex] = entry?.level ?? 0;
        // [阶段7] HUD 槽位技能变更（换装备）：充能状态重置为满（新技能新状态）
        if (oldData != skillSlots[hudIndex].data)
            InitSlotCharges(hudIndex);
    }

    /// <summary>
    /// [阶段7] 初始化指定槽位的充能状态：useCharges 技能计数补满（上限 maxCharges），清空恢复槽；
    /// 非充能技能/空槽计数置 0。槽位技能变更（装备/清空/读档）时调用。
    /// </summary>
    private void InitSlotCharges(int index)
    {
        if (index < 0 || index >= skillSlots.Length) return;
        var data = skillSlots[index]?.data;
        chargeCounts[index] = data != null && data.useCharges ? Mathf.Max(1, data.maxCharges) : 0;
        if (chargeTimers[index] != null)
            chargeTimers[index].Clear();
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
