using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// [P2] 被动装备管理器 — 挂 Player GameObject
/// 职责：管理 5 层被动槽位（每层 3 槽，5 线选 3）、装备/卸下被动、
///       自动将修饰器同步到 StatModifierManager、暴露 UI 布局数据
/// 约束：非战斗可操作、同层不可重复选同线、多层同线效果在 StatModifierManager 层叠加
/// </summary>
public class PassiveEquipManager : MonoBehaviour
{
    // ============================================================
    // Singleton 注册表（Player 子组件；调用方统一走 Instance）
    // ============================================================

    private static PassiveEquipManager _instance;

    public static PassiveEquipManager Instance
    {
        get
        {
            if (_instance == null)
                _instance = FindObjectOfType<PassiveEquipManager>();
            return _instance;
        }
    }

    // ============================================================
    // 常量
    // ============================================================

    public const int LayerCount = 5;
    public const int SlotPerLayer = 3;
    public const int LineCount = 5;

    /// <summary>低血阈值（"低血加防"触发条件：HP/MaxHP ≤ 此值）</summary>
    private const float LowHpThreshold = 0.3f;
    /// <summary>"低血加防"额外减伤值</summary>
    private const float LowHpDefenseBonus = 0.15f;

    /// <summary>修饰器 source 前缀</summary>
    private const string SourcePrefix = "Passive";

    // ============================================================
    // 配置
    // ============================================================

    [Header("被动数据资产")]
    [Tooltip("所有被动节点 SO（25个：5层×5线）。Inspector 拖入全部 PassiveSkillData")]
    [SerializeField] private PassiveSkillData[] allPassiveData;

    // ============================================================
    // 运行时状态
    // ============================================================

    /// <summary>拍平一维槽位数组，索引 = layer * SlotPerLayer + slotIndex，-1=空。
    /// 不直接访问——统一走 Slots 属性，保证懒初始化。</summary>
    [SerializeField] private int[] slots;

    /// <summary>懒初始化包装器：无论 Awake 是否执行，首次访问时自动创建槽位数组</summary>
    private int[] Slots
    {
        get
        {
            if (slots == null || slots.Length == 0)
            {
                slots = new int[LayerCount * SlotPerLayer];
                for (int i = 0; i < slots.Length; i++)
                    slots[i] = -1;
            }
            return slots;
        }
    }

    /// <summary>是否处于战斗中（战斗时锁定所有 Equip/Unequip 操作）</summary>
    private bool inCombat;

    /// <summary>战斗引用计数（多敌人同时进入/退出战斗时，归零才退出战斗态）</summary>
    private int combatRefCount;

    /// <summary>当前章节进度（1~5），持久化到存档。替代旧版 playerLevel 解锁机制。</summary>
    [SerializeField] private int currentChapter = 1;

    /// <summary>低血加防修饰器的 source（TV 减伤+控制线的特殊条件效果）。
    /// 使用独立唯一前缀，避免与 SO 被动层 source（Passive_L5_L3）冲突。</summary>
    private const string LowHpSource = "Passive_LowHpDefense";

    /// <summary>低血加防修饰器实例引用（用于血量变化时刷新条件）</summary>
    private Modifier lowHpModifier;

    // ============================================================
    // 依赖引用
    // ============================================================

    private StatModifierManager statModManager;
    private PlayerHealth playerHealth;

    // ============================================================
    // 数据索引 — 字典替代 if-else 查找
    // ============================================================

    /// <summary>按 (layer, lineId) 快速查找 PassiveSkillData</summary>
    private readonly Dictionary<(int layer, int lineId), PassiveSkillData> dataIndex = new Dictionary<(int layer, int lineId), PassiveSkillData>();

    /// <summary>线 ID → 显示名称 映射</summary>
    private static readonly Dictionary<int, string> LineNames = new Dictionary<int, string>()
    {
        { 0, "HP恢复" },
        { 1, "伤害+攻速" },
        { 2, "移速+闪避" },
        { 3, "减伤+控制" },
        { 4, "法力+CD" },
    };

    // ============================================================
    // Unity 生命周期
    // ============================================================

    private void Awake()
    {
        // 初始化槽位数组：5层 × 3槽，拍平为一维
        slots = new int[LayerCount * SlotPerLayer];
        for (int i = 0; i < slots.Length; i++)
            slots[i] = -1;

        statModManager = GetComponent<StatModifierManager>();
        playerHealth = GetComponent<PlayerHealth>();
        BuildDataIndex();
    }

    private void OnEnable()
    {
        EventBus.Subscribe<PlayerHealthChangedEvent>(OnHealthChanged);
    }

    private void OnDisable()
    {
        EventBus.Unsubscribe<PlayerHealthChangedEvent>(OnHealthChanged);
    }

    // ============================================================
    // 公共接口 — 装备 / 卸下
    // ============================================================

    /// <summary>装备被动技能到指定层指定槽位。如槽位已有旧装备自动先卸下</summary>
    /// <param name="layer">层级 0~4（对应 TI~TV）</param>
    /// <param name="lineId">线 ID 0~4</param>
    /// <param name="slotIndex">该层内槽位 0~2</param>
    /// <returns>装备是否成功</returns>
    public bool EquipPassive(int layer, int lineId, int slotIndex)
    {
        if (InCombat)
        {
            Debug.LogWarning($"[PassiveEquip] 战斗中不可装备被动");
            return false;
        }
        if (!IsLayerValid(layer) || !IsLineValid(lineId) || !IsSlotValid(slotIndex))
            return false;
        if (!IsLayerUnlocked(layer))
        {
            Debug.LogWarning($"[PassiveEquip] 层级 {LayerLabel(layer)} 未解锁（需到达第{UnlockChapterOf(layer)}章，当前第{currentChapter}章）");
            return false;
        }
        if (IsLineEquippedInLayer(layer, lineId))
        {
            Debug.LogWarning($"[PassiveEquip] 线 {LineName(lineId)} 已在层 {LayerLabel(layer)} 装备，不可重复");
            return false;
        }

        // 目标槽位已有旧装备 → 先卸下
        int oldLineId = Slots[SlotIndex(layer, slotIndex)];
        if (oldLineId >= 0)
            RemoveModifiersForNode(layer, oldLineId);

        // 装备新被动
        Slots[SlotIndex(layer, slotIndex)] = lineId;
        AddModifiersForNode(layer, lineId);

        EventBus.Trigger(new PassiveSlotsChangedEvent(layer, lineId, slotIndex, "equip"));
        return true;
    }

    /// <summary>卸下指定层指定线的被动技能</summary>
    /// <param name="layer">层级 0~4</param>
    /// <param name="lineId">线 ID 0~4</param>
    /// <returns>卸下是否成功</returns>
    public bool UnequipPassive(int layer, int lineId)
    {
        if (InCombat)
        {
            Debug.LogWarning($"[PassiveEquip] 战斗中不可卸下被动");
            return false;
        }
        if (!IsLayerValid(layer) || !IsLineValid(lineId))
            return false;

        int slotIndex = GetSlotIndexForLine(layer, lineId);
        if (slotIndex < 0) return false;

        RemoveModifiersForNode(layer, lineId);
        Slots[SlotIndex(layer, slotIndex)] = -1;

        EventBus.Trigger(new PassiveSlotsChangedEvent(layer, lineId, slotIndex, "unequip"));
        return true;
    }

    /// <summary>批量卸下指定层所有已装备的被动</summary>
    public void UnequipAllInLayer(int layer)
    {
        if (!IsLayerValid(layer)) return;
        for (int i = 0; i < SlotPerLayer; i++)
        {
            int lineId = Slots[SlotIndex(layer, i)];
            if (lineId >= 0)
                UnequipPassive(layer, lineId);
        }
    }

    /// <summary>卸下所有层级所有被动</summary>
    public void UnequipAll()
    {
        for (int l = 0; l < LayerCount; l++)
            UnequipAllInLayer(l);
    }

    // ============================================================
    // 公共接口 — 状态查询
    // ============================================================

    /// <summary>获取指定层指定槽位当前装备的线 ID，-1=空</summary>
    public int GetEquippedLineId(int layer, int slotIndex)
    {
        // Slots 属性自带懒初始化，所以这里只需检查边界
        if (!IsLayerValid(layer) || !IsSlotValid(slotIndex)) return -1;
        return Slots[SlotIndex(layer, slotIndex)];
    }

    /// <summary>每层对应具体解锁章节，具名映射:
    ///   T1 → 第1章, T2 → 第2章, T3 → 第3章, T4 → 第4章, T5 → 第5章</summary>
    public static int UnlockChapterOf(int layer) => layer switch
    {
        0 => 1, // T1 第1章解锁
        1 => 2, // T2 第2章解锁
        2 => 3, // T3 第3章解锁
        3 => 4, // T4 第4章解锁
        4 => 5, // T5 第5章解锁
        _ => 1,
    };

    /// <summary>指定层是否已解锁（当前章节 >= 该层解锁章节）</summary>
    public bool IsLayerUnlocked(int layer) =>
        IsLayerValid(layer) && currentChapter >= UnlockChapterOf(layer);

    /// <summary>指定线 ID 是否已在指定层装备（任一槽位）</summary>
    public bool IsLineEquippedInLayer(int layer, int lineId)
    {
        if (!IsLayerValid(layer)) return false;
        for (int i = 0; i < SlotPerLayer; i++)
            if (Slots[SlotIndex(layer, i)] == lineId) return true;
        return false;
    }

    /// <summary>指定线 ID 在指定层装备的槽位索引，-1=未装备</summary>
    public int GetSlotIndexForLine(int layer, int lineId)
    {
        if (!IsLayerValid(layer)) return -1;
        for (int i = 0; i < SlotPerLayer; i++)
            if (Slots[SlotIndex(layer, i)] == lineId) return i;
        return -1;
    }

    /// <summary>当前是否处于战斗中（面板打开豁免后，见下方 InCombat 属性）</summary>
    // 注: InCombat 属性定义在 SetUIPauseOverride 附近，统一带 uiPauseOverride 豁免

    /// <summary>获取/设置当前章节</summary>
    public int CurrentChapter
    {
        get => currentChapter;
        private set => currentChapter = Mathf.Clamp(value, 1, LayerCount);
    }

    /// <summary>推进章节（由地图/剧情系统调用）。返回新章节号。</summary>
    public int AdvanceChapter(int delta = 1)
    {
        int newChapter = Mathf.Min(currentChapter + delta, LayerCount);
        if (newChapter != currentChapter)
        {
            currentChapter = newChapter;
            EventBus.Trigger(new ChapterChangedEvent(currentChapter));
        }
        return currentChapter;
    }

    /// <summary>直接设置章节（读档时用，不经过 Advance 的 delta 逻辑）</summary>
    public void SetChapter(int chapter)
    {
        currentChapter = Mathf.Clamp(chapter, 1, LayerCount);
        EventBus.Trigger(new ChapterChangedEvent(currentChapter));
    }

    /// <summary>获取指定层对应的解锁章节号（供 UI 查询）</summary>
    public int GetUnlockChapter(int layer) => UnlockChapterOf(layer);

    // ============================================================
    // 公共接口 — 外部状态同步
    // ============================================================

    /// <summary>设置战斗状态（由敌人系统通过 ref-count 调用）。
    /// 多个敌人同时战斗时，只有最后一个退出才设 inCombat=false。
    /// 战斗中锁定 Equip/Unequip。</summary>
    /// <param name="enterCombat">true=进入战斗(+1)；false=退出战斗(-1，归零才退出)</param>
    public void SetCombatState(bool enterCombat)
    {
        if (enterCombat)
        {
            combatRefCount++;
            if (!inCombat)
            {
                inCombat = true;
                RefreshLowHpCondition();
            }
        }
        else
        {
            combatRefCount = Mathf.Max(0, combatRefCount - 1);
            if (combatRefCount == 0)
                inCombat = false;
        }
    }

    /// <summary>暂停期间（被动面板打开时）强制视为非战斗。
    /// 面板是 FullScreen+PauseGame，打开时 timeScale=0 游戏已暂停，
    /// 此时敌人 AI 冻结在 Chase 无法退出战斗，若不豁免则面板永远锁死。</summary>
    private bool uiPauseOverride;

    /// <summary>当前是否处于战斗中（面板打开豁免后，仅真实运行中的战斗算战斗）</summary>
    public bool InCombat => inCombat && !uiPauseOverride;

    /// <summary>由 PassiveUI 在 OnEnable/OnDisable 调用：面板打开时豁免战斗锁定</summary>
    public void SetUIPauseOverride(bool active)
    {
        uiPauseOverride = active;
    }

    // ============================================================
    // 公共接口 — UI 数据
    // ============================================================

    /// <summary>获取完整布局数据供 UI 消费</summary>
    public PassiveLayoutData GetLayoutData()
    {
        var layerData = new PassiveLayerData[LayerCount];
        for (int l = 0; l < LayerCount; l++)
        {
            var slotLines = new int[SlotPerLayer];
            for (int s = 0; s < SlotPerLayer; s++)
                slotLines[s] = Slots[SlotIndex(l, s)];

            layerData[l] = new PassiveLayerData
            {
                layer = l,
                isUnlocked = IsLayerUnlocked(l),
                unlockChapter = UnlockChapterOf(l),
                equippedLineIds = slotLines,
            };
        }
        return new PassiveLayoutData { layers = layerData, inCombat = InCombat };
    }

    /// <summary>获取指定层已装备的线 ID 列表（忽略顺序，跳过空槽）</summary>
    public int[] GetEquippedLinesInLayer(int layer)
    {
        if (!IsLayerValid(layer)) return System.Array.Empty<int>();
        var list = new List<int>(SlotPerLayer);
        for (int i = 0; i < SlotPerLayer; i++)
            if (Slots[SlotIndex(layer, i)] >= 0) list.Add(Slots[SlotIndex(layer, i)]);
        return list.ToArray();
    }

    /// <summary>查询指定线的累计效果汇总（含多层同线叠加，用于 UI 悬停提示）</summary>
    public List<Modifier> GetCumulativeModifiers(int lineId)
    {
        var result = new List<Modifier>();
        // 遍历所有层，收集已装备该线的节点的 effects，合并同 statId 的数值
        var merged = new Dictionary<string, (float value, ModifierType type)>();
        for (int l = 0; l < LayerCount; l++)
        {
            int slotIdx = GetSlotIndexForLine(l, lineId);
            if (slotIdx < 0) continue;

            var data = GetPassiveData(l, lineId);
            if (data?.effects == null) continue;

            foreach (var eff in data.effects)
            {
                if (merged.TryGetValue(eff.targetStat, out var acc))
                    merged[eff.targetStat] = (acc.value + eff.value, eff.type);
                else
                    merged[eff.targetStat] = (eff.value, eff.type);
            }
        }

        foreach (var kv in merged)
            result.Add(new Modifier(kv.Key, kv.Value.value, kv.Value.type, $"Passive_Line{lineId}"));
        return result;
    }

    /// <summary>[P6] 所有被动数据资产引用（供 UI 被动网格消费，如技能图标/名称/描述）</summary>
    public PassiveSkillData[] AllPassiveData => allPassiveData;

    // ============================================================
    // 内部方法 — 修饰器同步
    // ============================================================

    /// <summary>将某个被动节点的所有效果转为修饰器，送入 StatModifierManager</summary>
    private void AddModifiersForNode(int layer, int lineId)
    {
        if (statModManager == null) return;

        var data = GetPassiveData(layer, lineId);
        if (data == null || data.effects == null) return;

        string source = BuildSource(layer, lineId);

        foreach (var effect in data.effects)
        {
            var mod = new Modifier(effect.targetStat, effect.value, effect.type, source);
            statModManager.AddModifier(mod);
        }

        // TV 减伤+控制线（lineId=3）额外添加低血加防条件修饰器
        if (layer == 4 && lineId == 3)
        {
            lowHpModifier = new Modifier(
                StatId.DamageReduction, LowHpDefenseBonus, ModifierType.Flat, LowHpSource,
                condition: () => EvaluateLowHpCondition());
            statModManager.AddModifier(lowHpModifier);
        }

        // 触发式 debug（上线前改为条件编译或注释）
        // Debug.Log($"[PassiveEquip] 装备: {LayerLabel(layer)} {LineName(lineId)} → {data.effects.Length} modifiers");
    }

    /// <summary>移除某个被动节点对应的所有修饰器</summary>
    private void RemoveModifiersForNode(int layer, int lineId)
    {
        if (statModManager == null) return;

        string source = BuildSource(layer, lineId);
        statModManager.RemoveModifier(source);

        // 同时移除低血加防条件修饰器
        if (layer == 4 && lineId == 3)
        {
            statModManager.RemoveModifier(LowHpSource);
            lowHpModifier = null;
        }

        // 触发式 debug
        // Debug.Log($"[PassiveEquip] 卸下: {LayerLabel(layer)} {LineName(lineId)}");
    }

    /// <summary>刷新低血加防修饰器的条件绑定（血量变化后调用）</summary>
    private void RefreshLowHpCondition()
    {
        if (lowHpModifier != null)
            lowHpModifier.condition = () => EvaluateLowHpCondition();
    }

    /// <summary>[P2] HP 变化后重评估低血条件，并触发 DamageReduction 重算</summary>
    private void OnHealthChanged(PlayerHealthChangedEvent e)
    {
        if (lowHpModifier != null)
        {
            RefreshLowHpCondition();
            statModManager?.ForceRefreshStat(StatId.DamageReduction);
        }
    }

    /// <summary>计算低血条件：currentHealth / maxHealth ≤ 30%</summary>
    private bool EvaluateLowHpCondition()
    {
        if (playerHealth == null) return false;
        float maxHp = playerHealth.MaxHealth;
        if (maxHp <= 0f) return false;
        return playerHealth.CurrentHealth / maxHp <= LowHpThreshold;
    }

    // ============================================================
    // 内部方法 — 数据索引
    // ============================================================

    /// <summary>构建 (layer, lineId) → PassiveSkillData 快速查找字典</summary>
    private void BuildDataIndex()
    {
        dataIndex.Clear();
        if (allPassiveData == null) return;

        foreach (var data in allPassiveData)
        {
            if (data == null) continue;
            var key = (data.layer - 1, data.lineId); // SO 中 layer 是 1~5，转为内部 0~4
            dataIndex[key] = data;
        }
    }

    /// <summary>根据层+线 ID 查找 PassiveSkillData</summary>
    private PassiveSkillData GetPassiveData(int layer, int lineId)
    {
        dataIndex.TryGetValue((layer, lineId), out var data);
        return data;
    }

    // ============================================================
    // 内部方法 — 校验
    // ============================================================

    private static bool IsLayerValid(int layer) => layer >= 0 && layer < LayerCount;
    private static bool IsLineValid(int lineId) => lineId >= 0 && lineId < LineCount;
    private static bool IsSlotValid(int slotIndex) => slotIndex >= 0 && slotIndex < SlotPerLayer;

    /// <summary>将 (layer, slotIndex) 映射到拍平一维数组索引</summary>
    private static int SlotIndex(int layer, int slotIndex) => layer * SlotPerLayer + slotIndex;

    // ============================================================
    // 内部方法 — 工具
    // ============================================================

    /// <summary>构造修饰器 source 标识符</summary>
    private static string BuildSource(int layer, int lineId) =>
        $"{SourcePrefix}_L{layer + 1}_L{lineId}";

    /// <summary>层级显示标签（T1~T5 用罗马数字，避免 char 运算溢出为 TJ/TK）</summary>
    private static string LayerLabel(int layer)
    {
        string[] labels = { "I", "II", "III", "IV", "V" };
        return layer >= 0 && layer < labels.Length ? $"T{labels[layer]}" : $"T{layer + 1}";
    }

    /// <summary>线显示名称（字典查找）</summary>
    private static string LineName(int lineId) =>
        LineNames.TryGetValue(lineId, out var name) ? name : $"Line{lineId}";

    // ============================================================
    // 内部方法 — 读档恢复（跳过解锁检查）
    // ============================================================

    /// <summary>内部装备方法（跳过解锁检查，读档专用）</summary>
    private bool EquipPassiveInternal(int layer, int lineId, int slotIndex)
    {
        if (inCombat) return false;
        if (!IsLayerValid(layer) || !IsLineValid(lineId) || !IsSlotValid(slotIndex))
            return false;
        if (IsLineEquippedInLayer(layer, lineId))
        {
            Debug.LogWarning($"[PassiveEquip] 线 {LineName(lineId)} 已在层 {LayerLabel(layer)} 装备");
            return false;
        }

        int oldLineId = Slots[SlotIndex(layer, slotIndex)];
        if (oldLineId >= 0)
            RemoveModifiersForNode(layer, oldLineId);

        Slots[SlotIndex(layer, slotIndex)] = lineId;
        AddModifiersForNode(layer, lineId);
        EventBus.Trigger(new PassiveSlotsChangedEvent(layer, lineId, slotIndex, "equip"));
        return true;
    }

    /// <summary>读档恢复被动槽位（跳过解锁检查，供 SaveSystem 调用）</summary>
    public void RestorePassiveSlots(int[][] layerLineIds)
    {
        int layerCount = Mathf.Min(layerLineIds.Length, LayerCount);
        for (int l = 0; l < layerCount; l++)
        {
            if (layerLineIds[l] == null) continue;
            int slotCount = Mathf.Min(layerLineIds[l].Length, SlotPerLayer);
            for (int s = 0; s < slotCount; s++)
            {
                int lineId = layerLineIds[l][s];
                if (lineId >= 0)
                    EquipPassiveInternal(l, lineId, s);
            }
        }
    }

    // ============================================================
    // 内部类型 — UI 数据载体
    // ============================================================

    /// <summary>被动布局数据 — 完整 5 层状态快照，供 UI 消费</summary>
    [System.Serializable]
    public struct PassiveLayoutData
    {
        public PassiveLayerData[] layers;
        public bool inCombat;
    }

    /// <summary>单层被动槽位数据</summary>
    [System.Serializable]
    public struct PassiveLayerData
    {
        public int layer;
        public bool isUnlocked;
        public int unlockChapter;    // 替代旧版 unlockLevel，该层解锁所需章节号
        public int[] equippedLineIds;
    }
}
