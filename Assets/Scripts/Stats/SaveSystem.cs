using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// [P6] 存档系统 — 挂 Player GameObject
/// 职责：序列化/反序列化所有技能相关状态（技能点/槽位/分支/被动/武器/组合技能）。
/// 提供 SaveGame() / LoadGame() 接口，数据以 JSON 存入 PlayerPrefs。
///
/// 序列化内容：
///   - 技能点（SkillPointManager）
///   - 4 槽位技能名+等级+分支选择（SkillManager + ActiveSkillData）
///   - 5 层被动槽位装备（PassiveEquipManager）
///   - 武器技能+消耗标记（WeaponSkillLink）
///   - 组合技能（通过槽位技能名隐式保存）
///
/// P4 边界：组合消耗武器技能后 _skillConsumed=true，存档正确记录 consumed 标记，
///         读档时不会错误恢复已被消耗的武器技能。
/// </summary>
public class SaveSystem : MonoBehaviour
{
    // ============================================================
    // 常量
    // ============================================================

    private const string SaveKey = "PlayerSkillSave";
    private const string InventorySaveKey = "PlayerInventorySave";
    private const int MaxSlots = 4;

    // ============================================================
    // 运行时引用
    // ============================================================

    private SkillManager skillManager;
    private SkillPool skillPool;
    private SkillPointManager skillPointManager;
    private PassiveEquipManager passiveEquipManager;
    private WeaponSkillLink weaponSkillLink;

    // Debug 触发计数器（≤3 帧限制）
    private int _debugCount;
    private int _debugFrame;

    // ============================================================
    // 生命周期
    // ============================================================

    private void Awake()
    {
        skillManager = GetComponent<SkillManager>();
        skillPool = GetComponent<SkillPool>();
        skillPointManager = GetComponent<SkillPointManager>();
        passiveEquipManager = GetComponent<PassiveEquipManager>();
        weaponSkillLink = GetComponent<WeaponSkillLink>();
    }

    // ============================================================
    // 公开接口 — Save / Load
    // ============================================================

    /// <summary>
    /// 保存当前所有技能状态到 PlayerPrefs。
    /// 返回 true 表示保存成功。
    /// </summary>
    public bool SaveGame()
    {
        var data = new SaveData();
        CollectSkillPoints(data);
        CollectSkillSlots(data);
        CollectSkillPool(data);
        CollectHudAssignments(data);
        CollectPassiveSlots(data);
        CollectWeapon(data);

        // [Phase5] 保存属性分配点
        CollectAttributePoints(data);

        // 保存章节进度（被动解锁改造）
        data.currentChapter = passiveEquipManager != null ? passiveEquipManager.CurrentChapter : 1;

        string json = JsonUtility.ToJson(data, prettyPrint: false);
        PlayerPrefs.SetString(SaveKey, json);

        // [Phase5] 保存背包/仓库/装备数据（独立 key，方便单独重置背包）
        SaveInventory();

        PlayerPrefs.Save();

        DebugOnce("[SaveSystem] 存档完成");
        return true;
    }

    /// <summary>
    /// 从 PlayerPrefs 读取存档并恢复到所有管理器。
    /// 返回 true 表示有存档数据并成功加载；false = 无存档或部分失败。
    /// </summary>
    public bool LoadGame()
    {
        if (!PlayerPrefs.HasKey(SaveKey))
        {
            DebugOnce("[SaveSystem] 无存档数据，跳过加载");
            return false;
        }

        string json = PlayerPrefs.GetString(SaveKey, "");
        if (string.IsNullOrEmpty(json))
            return false;

        SaveData data;
        try
        {
            data = JsonUtility.FromJson<SaveData>(json);
        }
        catch (System.Exception e)
        {
            Debug.LogWarning($"[SaveSystem] JSON 解析失败: {e.Message}");
            return false;
        }

        if (data == null) return false;

        RestoreSkillPoints(data);
        RestoreSkillSlots(data);
        RestoreSkillPool(data);
        RestoreHudAssignments(data);
        RestorePassiveSlots(data);
        RestoreWeapon(data);

        // [Phase5] 恢复属性分配点和背包数据
        RestoreAttributePoints(data);
        LoadInventory();

        DebugOnce("[SaveSystem] 读档完成");
        return true;
    }

    /// <summary>删除存档（调试/重置用）</summary>
    public void DeleteSave()
    {
        PlayerPrefs.DeleteKey(SaveKey);
        PlayerPrefs.DeleteKey(InventorySaveKey);
        PlayerPrefs.Save();
    }

    // ============================================================
    // 收集 — 技能点
    // ============================================================

    private void CollectSkillPoints(SaveData data)
    {
        if (skillPointManager != null)
            data.skillPoints = skillPointManager.CurrentSkillPoints;
    }

    // ============================================================
    // 收集 — 技能槽位（4 槽：技能名+等级+分支选择）
    // ============================================================

    private void CollectSkillSlots(SaveData data)
    {
        data.slotData = new SlotSaveData[MaxSlots];
        if (skillManager == null) return;

        int count = skillManager.SlotCount;
        for (int i = 0; i < count && i < MaxSlots; i++)
        {
            var skillData = skillManager.GetSlotData(i);
            int level = skillManager.GetSkillLevel(i);

            data.slotData[i] = new SlotSaveData
            {
                skillName = skillData != null ? skillData.skillName : "",
                level = level,
            };

            // 主动分支技能：保存 chosenBranch
            if (skillData is ActiveSkillData activeData)
            {
                data.slotData[i].chosenBranch = activeData.chosenBranch;
            }
        }
    }

    // ============================================================
    // [P7] 收集 — 技能池
    // ============================================================

    private void CollectSkillPool(SaveData data)
    {
        if (skillPool == null)
        {
            data.poolSkills = new PoolSaveData[0];
            return;
        }

        var owned = skillPool.GetOwnedSkills();
        data.poolSkills = new PoolSaveData[owned.Count];
        for (int i = 0; i < owned.Count; i++)
        {
            data.poolSkills[i] = new PoolSaveData
            {
                skillName = owned[i].skillData?.skillName ?? owned[i].id,
                level = owned[i].level,
                source = owned[i].source
            };
        }
    }

    // ============================================================
    // [P7] 收集 — HUD 槽位绑定
    // ============================================================

    private void CollectHudAssignments(SaveData data)
    {
        if (skillPool == null)
        {
            data.hudSlots = new string[0];
            return;
        }
        data.hudSlots = skillPool.GetHudAssignments();
    }

    // ============================================================
    // 收集 — 被动槽位（5 层 × 3 槽）
    // ============================================================

    private void CollectPassiveSlots(SaveData data)
    {
        if (passiveEquipManager == null)
        {
            data.passiveLayers = new PassiveLayerSave[0];
            return;
        }

        int layerCount = PassiveEquipManager.LayerCount;
        data.passiveLayers = new PassiveLayerSave[layerCount];

        for (int l = 0; l < layerCount; l++)
        {
            int[] lines = new int[PassiveEquipManager.SlotPerLayer];
            for (int s = 0; s < PassiveEquipManager.SlotPerLayer; s++)
            {
                lines[s] = passiveEquipManager.GetEquippedLineId(l, s);
            }
            data.passiveLayers[l] = new PassiveLayerSave { lineIds = lines };
        }
    }

    // ============================================================
    // 收集 — 武器技能
    // ============================================================

    private void CollectWeapon(SaveData data)
    {
        if (weaponSkillLink == null)
        {
            data.weapon = new WeaponSaveData { exists = false, weaponType = -1 };
            return;
        }

        var wsData = weaponSkillLink.CurrentWeaponSkill;
        var wt = weaponSkillLink.CurrentWeaponType;
        data.weapon = new WeaponSaveData
        {
            exists = wsData != null || weaponSkillLink.HasWeaponSkill,
            skillName = wsData != null ? wsData.skillName : "",
            weaponType = wt.HasValue ? (int)wt.Value : -1,
            consumed = !weaponSkillLink.HasWeaponSkill,
        };
    }

    // ============================================================
    // 恢复 — 技能点
    // ============================================================

    private void RestoreSkillPoints(SaveData data)
    {
        if (skillPointManager != null)
            skillPointManager.SetPoints(data.skillPoints);
    }

    // ============================================================
    // 恢复 — 技能槽位
    // ============================================================

    private void RestoreSkillSlots(SaveData data)
    {
        if (skillManager == null || data.slotData == null) return;

        int count = Mathf.Min(data.slotData.Length, skillManager.SlotCount);
        for (int i = 0; i < count; i++)
        {
            var slotInfo = data.slotData[i];
            if (slotInfo == null || string.IsNullOrEmpty(slotInfo.skillName))
            {
                // 空槽 — 保持现状（初始化为空即可）
                continue;
            }

            // 通过技能名查找 SkillData SO（在已挂载的 skillSlots 中遍历）
            SkillData skillData = FindSkillDataByName(slotInfo.skillName);
            if (skillData == null) continue;

            // 恢复分支选择（ActiveSkillData）
            if (skillData is ActiveSkillData activeData && !string.IsNullOrEmpty(slotInfo.chosenBranch))
            {
                activeData.chosenBranch = slotInfo.chosenBranch;
            }

            // 设置槽位和等级
            skillManager.SetSlot(i, skillData, slotInfo.level);
        }
    }

    // ============================================================
    // [P7] 恢复 — 技能池
    // ============================================================

    /// <summary>
    /// 恢复技能池（在 RestoreSkillSlots 之后调用，
    /// 因为恢复 HUD 时需要技能已在池中）
    /// </summary>
    private void RestoreSkillPool(SaveData data)
    {
        if (skillPool == null || data.poolSkills == null) return;

        // 清空当前池子（初始化时的初始技能被存档覆盖）
        var currentOwned = skillPool.GetOwnedSkills();
        for (int i = currentOwned.Count - 1; i >= 0; i--)
        {
            skillPool.RemoveSkill(currentOwned[i].id);
        }

        // 从存档恢复技能池
        for (int i = 0; i < data.poolSkills.Length; i++)
        {
            var poolInfo = data.poolSkills[i];
            if (poolInfo == null || string.IsNullOrEmpty(poolInfo.skillName))
                continue;

            // 通过技能名查找 SO
            SkillData skillData = FindSkillDataByName(poolInfo.skillName);
            if (skillData == null) continue;

            // 恢复分支选择（ActiveSkillData）
            // chosenBranch 已保存在 slotData 中，在 RestoreSkillSlots 已经恢复
            // 这里只恢复池中技能

            skillPool.AddSkill(skillData, poolInfo.level, poolInfo.source ?? "save");
        }
    }

    // ============================================================
    // [P7] 恢复 — HUD 槽位绑定
    // ============================================================

    /// <summary>
    /// 恢复 HUD 槽位绑定（在 RestoreSkillPool 之后调用）
    /// </summary>
    private void RestoreHudAssignments(SaveData data)
    {
        if (skillPool == null || data.hudSlots == null) return;

        int count = Mathf.Min(data.hudSlots.Length, SkillPool.HudSlotCount);
        for (int i = 0; i < count; i++)
        {
            string skillId = data.hudSlots[i];
            if (!string.IsNullOrEmpty(skillId))
                skillPool.EquipToHud(i, skillId);
        }
    }

    // ============================================================
    // 恢复 — 被动槽位（先恢复章节，再恢复被动装备，跳过解锁检查）
    // ============================================================
    private void RestorePassiveSlots(SaveData data)
    {
        if (passiveEquipManager == null || data.passiveLayers == null) return;

        // 先恢复章节（兼容旧存档：currentChapter=0 时默认 1）
        int chapter = data.currentChapter > 0 ? data.currentChapter : 1;
        passiveEquipManager.SetChapter(chapter);

        // 构建 int[][] 传给 PassiveEquipManager.RestorePassiveSlots（跳过解锁检查）
        int layerCount = Mathf.Min(data.passiveLayers.Length, PassiveEquipManager.LayerCount);
        var slots = new int[layerCount][];
        for (int l = 0; l < layerCount; l++)
            slots[l] = data.passiveLayers[l]?.lineIds ?? new int[0];

        passiveEquipManager.RestorePassiveSlots(slots);
    }

    // ============================================================
    // 恢复 — 武器技能
    // ============================================================

    private void RestoreWeapon(SaveData data)
    {
        if (weaponSkillLink == null || data.weapon == null || !data.weapon.exists)
            return;

        // 武器技能的恢复依赖 WeaponEquippedEvent 事件流。
        // 存档本身只记录 consumed 标记；实际装备通过武器系统事件触发 WeaponSkillLink 恢复。
        // 如 consumed=true 则 WeaponSkillLink 在装备事件触发后标记为已消耗。
        if (data.weapon.consumed)
        {
            weaponSkillLink.ConsumeWeaponSkill();
        }
    }

    // ============================================================
    // 辅助 — 技能名查找
    // ============================================================

    /// <summary>
    /// 通过技能名在当前 skillSlots 或其他已装载的 SO 中查找 SkillData 引用。
    /// 策略：优先查当前槽位已有的 SO（保留引用），兜底遍历所有槽位数据。
    /// </summary>
    private SkillData FindSkillDataByName(string skillName)
    {
        if (skillManager == null) return null;

        int count = skillManager.SlotCount;
        for (int i = 0; i < count; i++)
        {
            var data = skillManager.GetSlotData(i);
            if (data != null && data.skillName == skillName)
                return data;
        }
        return null;
    }

    // ============================================================
    // [Phase5] 属性点收集 / 恢复
    // ============================================================

    private PlayerAttributeSystem _attrSystem;
    private PlayerAttributeSystem AttrSystem
    {
        get
        {
            if (_attrSystem == null)
                _attrSystem = GetComponent<PlayerAttributeSystem>();
            return _attrSystem;
        }
    }

    private void CollectAttributePoints(SaveData data)
    {
        if (AttrSystem != null)
        {
            data.assignedStr = AttrSystem.AssignedStr;
            data.assignedInt = AttrSystem.AssignedInt;
            data.assignedAgi = AttrSystem.AssignedAgi;
        }
    }

    private void RestoreAttributePoints(SaveData data)
    {
        AttrSystem?.SetAssignedPoints(data.assignedStr, data.assignedInt, data.assignedAgi);
    }

    // ============================================================
    // [Phase5] 背包/仓库/装备存档（独立 PlayerPrefs key）
    // ============================================================

    /// <summary>
    /// 保存背包/仓库/装备数据
    /// 延迟查找 InventoryManager（可能在 SaveSystem 之后初始化）
    /// </summary>
    private void SaveInventory()
    {
        InventoryManager inv = InventoryManager.Instance;
        if (inv == null)
        {
            Debug.LogWarning("[SaveSystem] InventoryManager 未找到，跳过背包存档");
            return;
        }

        InventorySaveData invData = inv.SaveToData();
        if (invData != null)
        {
            string json = JsonUtility.ToJson(invData, prettyPrint: false);
            PlayerPrefs.SetString(InventorySaveKey, json);
        }
    }

    /// <summary>
    /// 加载背包/仓库/装备数据
    /// </summary>
    private void LoadInventory()
    {
        if (!PlayerPrefs.HasKey(InventorySaveKey)) return;

        string json = PlayerPrefs.GetString(InventorySaveKey, "");
        if (string.IsNullOrEmpty(json)) return;

        InventorySaveData invData;
        try
        {
            invData = JsonUtility.FromJson<InventorySaveData>(json);
        }
        catch (System.Exception e)
        {
            Debug.LogWarning($"[SaveSystem] 背包存档 JSON 解析失败: {e.Message}");
            return;
        }

        if (invData == null) return;

        InventoryManager inv = InventoryManager.Instance;
        if (inv != null)
        {
            inv.LoadFromData(invData);
        }
        else
        {
            Debug.LogWarning("[SaveSystem] InventoryManager 未找到，背包数据将在 InventoryManager 初始化后加载");
            // 保存到静态变量，由 InventoryManager.Start 时检查并加载
            _pendingInventoryData = invData;
        }
    }

    /// <summary>待加载的背包数据（用于 InventoryManager 晚于 SaveSystem 初始化的情况）</summary>
    private static InventorySaveData _pendingInventoryData;

    /// <summary>
    /// [Phase5] 检查是否有待加载的背包存档（由 InventoryManager.Start 调用）
    /// </summary>
    public static bool TryConsumePendingInventoryData(out InventorySaveData data)
    {
        data = _pendingInventoryData;
        bool has = _pendingInventoryData != null;
        _pendingInventoryData = null;
        return has;
    }

    // ============================================================
    // Debug — 触发式一行 + 递减少量帧
    // ============================================================

    private void DebugOnce(string msg)
    {
        int frame = Time.frameCount;
        if (frame != _debugFrame)
        {
            _debugFrame = frame;
            _debugCount = 0;
        }
        if (_debugCount < 3)
        {
            _debugCount++;
            // Debug.Log(msg);
        }
    }

    // ============================================================
    // 内部类型 — 存档数据结构
    // ============================================================

    [System.Serializable]
    private class SaveData
    {
        public int skillPoints;
        public SlotSaveData[] slotData;
        public PoolSaveData[] poolSkills;
        public string[] hudSlots;
        public PassiveLayerSave[] passiveLayers;
        public WeaponSaveData weapon;
        // [Phase5] 属性分配点
        public int assignedStr;
        public int assignedInt;
        public int assignedAgi;
        // 被动解锁改造：章节进度
        public int currentChapter = 1;
    }

    [System.Serializable]
    private class SlotSaveData
    {
        public string skillName;
        public int level;
        public string chosenBranch; // null/"" = 无分支 / "Left" / "Right"
    }

    [System.Serializable]
    private class PoolSaveData
    {
        public string skillName;
        public int level;
        public string source;
    }

    [System.Serializable]
    private class PassiveLayerSave
    {
        public int[] lineIds; // 3 个槽位，-2=显式空，-1=未选，0~4=技能线
    }

    [System.Serializable]
    private class WeaponSaveData
    {
        public bool exists;
        public string skillName;
        public int weaponType; // (int)WeaponType 转换，-1=无
        public bool consumed; // P4 边界：组合消耗后为 true
    }
}
