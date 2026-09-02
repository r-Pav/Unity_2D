using UnityEngine;
using System.Collections;
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

    // [存档UI] 槽位化：0-4 手动槽，5 = 自动存档槽
    private const int SaveSlotCount = 6;
    /// <summary>自动存档槽索引（固定最底部，覆盖式）</summary>
    public const int AutoSlotIndex = SaveSlotCount - 1;

    // ============================================================
    // 运行时引用
    // ============================================================

    private SkillManager skillManager;
    private SkillPool skillPool;
    private SkillPointManager skillPointManager;
    private PassiveEquipManager passiveEquipManager;
    private WeaponSkillLink weaponSkillLink;
    // [阶段8] 元素模块引用（可空：Player 未挂 ElementModule 时存档/读档跳过元素数据，不报错）
    private ElementModule elementModule;
    // [石碑系统 T2] 石碑系统引用（可空：方案 A — Inspector 拖场景根 WaypointSystem GO；
    // SaveSystem 挂 Player 上、WaypointSystem 挂场景根，GetComponent 拿不到，故用拖引用；
    // 未拖引用时存档/读档跳过石碑数据，不报错）
    [SerializeField] private WaypointSystem waypointSystem;

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
        // [阶段8] 元素模块（可空：未挂时存档/读档跳过元素数据）
        elementModule = GetComponent<ElementModule>();
        // [石碑系统 T2] 石碑系统:优先 Inspector 拖引用(方案 A);未拖(编辑器接线前)回退单例懒查(方案 B)。
        // 场景内确无 WaypointSystem 时保持 null → Collect/Restore 跳过不报错
        if (waypointSystem == null)
            waypointSystem = WaypointSystem.Instance;
    }

    private void OnEnable()
    {
        // 进入新地区（过管道）→ 自动存档到自动槽
        EventBus.Subscribe<AreaEnterEvent>(OnAreaEnter);
    }

    private void OnDisable()
    {
        EventBus.Unsubscribe<AreaEnterEvent>(OnAreaEnter);
    }

    private void OnAreaEnter(AreaEnterEvent e)
    {
        AutoSave();
    }

    // ============================================================
    // 公开接口 — Save / Load
    // ============================================================

    /// <summary>旧无参接口保留 — 槽 0 别名（兼容现有调用）</summary>
    public bool SaveGame()
    {
        return SaveGame(0);
    }

    /// <summary>
    /// 保存当前所有技能状态到 PlayerPrefs 指定槽位。
    /// 槽 0 沿用旧 key 兼容老存档；槽 1-5 用 SaveKey+"_N"。
    /// 返回 true 表示保存成功。
    /// </summary>
    public bool SaveGame(int slot)
    {
        if (slot < 0 || slot >= SaveSlotCount)
        {
            Debug.LogWarning($"[SaveSystem] 无效存档槽位: {slot}");
            return false;
        }

        var data = new SaveData();
        data.saveTime = System.DateTime.Now.ToString("MM-dd HH:mm");
        // [石碑系统 T3] 当前所在地区 = ZoneManager.CurrentAreaId(运行时状态源;复用了预留的 areaName 字段)。
        // ZoneManager 未挂(纯技能/无区域场景)时留空,不报错
        data.areaName = ZoneManager.Instance != null ? ZoneManager.Instance.CurrentAreaId : "";
        Transform playerT = PlayerController.Instance != null ? PlayerController.Instance.transform : null;
        if (playerT != null)
        {
            // 管道移动中:存对侧出口位置(管道外落点),不存管道内当前位置——
            // 否则管道内 ESC 存档后读档会卡在管道中间(移动协程已结束,玩家被困)
            Vector3? pending = AreaChannelTrigger.PendingSavePosition;
            if (pending.HasValue)
            {
                data.posX = pending.Value.x;
                data.posY = pending.Value.y;
                data.posZ = pending.Value.z;
            }
            else
            {
                data.posX = playerT.position.x;
                data.posY = playerT.position.y;
                data.posZ = playerT.position.z;
            }
        }

        CollectSkillPoints(data);
        CollectSkillSlots(data);
        CollectSkillPool(data);
        CollectHudAssignments(data);
        CollectPassiveSlots(data);
        CollectWeapon(data);
        // [阶段8] 元素状态（决策 D17：走 ElementModule 导出接口，SaveSystem 不直接管字段语义）
        CollectElement(data);
        // [石碑系统 T2] 已激活石碑（WaypointSystem 导出；未拖引用 → 跳过，字段保持 null）
        CollectWaypoints(data);

        // [Phase5] 保存属性分配点
        CollectAttributePoints(data);

        // 保存章节进度（被动解锁改造）
        data.currentChapter = passiveEquipManager != null ? passiveEquipManager.CurrentChapter : 1;

        string key = slot == 0 ? SaveKey : SaveKey + "_" + slot;
        string json = JsonUtility.ToJson(data, prettyPrint: false);
        PlayerPrefs.SetString(key, json);

        // [Phase5] 保存背包/仓库/装备数据（独立 key，方便单独重置背包）
        SaveInventory(slot);

        PlayerPrefs.Save();

        DebugOnce("[SaveSystem] 存档完成 槽位=" + slot);
        return true;
    }

    /// <summary>旧无参接口保留 — 槽 0 别名（兼容现有调用）</summary>
    public bool LoadGame()
    {
        return LoadGame(0);
    }

    /// <summary>
    /// 从 PlayerPrefs 指定槽位读取存档并恢复到所有管理器。
    /// 返回 true 表示有存档数据并成功加载；false = 无存档或部分失败。
    /// </summary>
    public bool LoadGame(int slot)
    {
        if (slot < 0 || slot >= SaveSlotCount) return false;

        string key = slot == 0 ? SaveKey : SaveKey + "_" + slot;
        if (!PlayerPrefs.HasKey(key))
        {
            DebugOnce("[SaveSystem] 无存档数据，跳过加载 槽位=" + slot);
            return false;
        }

        string json = PlayerPrefs.GetString(key, "");
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
        // [阶段8] 元素状态（决策 D17：走 ElementModule 导入接口；先解锁列表后当前元素）
        RestoreElement(data);

        // [Phase5] 恢复属性分配点和背包数据
        RestoreAttributePoints(data);
        LoadInventory(slot);

        // 管道移动中读档:先取消移动协程——否则恢复位置后协程继续推玩家 → 自动 walk 被接管。
        // 必须在恢复位置前调用(移动协程挂 VCam,菜单暂停只是 timeScale=0 空转,协程还活着)
        AreaChannelTrigger.CancelMove();

        // [石碑系统 T2] 恢复已激活石碑（旧档 null → 空列表；需在恢复位置前调，
        // 石碑 Activated 状态恢复与场景内注册表就绪——LoadGame 在场景内运行，注册已发生）
        RestoreWaypoints(data);

        // [石碑系统 T3] 恢复当前地区:走静默版 SetCurrentAreaSilent(只写不广播)。
        // 不能用 NotifyAreaEntered(广播版)——会触发 AreaEnterEvent → SaveSystem.AutoSave,
        // 反向覆盖刚读的档(方案风险 R5)。areaName 空(旧档)→ 不动,保持 ZoneManager.initialAreaId。
        RestoreCurrentArea(data);

        // 恢复位置：延迟一帧等地区显隐稳定后再设置，防止与地区显隐冲突
        StartCoroutine(RestorePositionNextFrame(data));

        DebugOnce("[SaveSystem] 读档完成 槽位=" + slot);
        return true;
    }

    /// <summary>旧无参接口保留 — 槽 0 别名（兼容现有调用）</summary>
    public void DeleteSave()
    {
        DeleteSave(0);
    }

    /// <summary>删除指定槽位存档（调试/重置用）</summary>
    public void DeleteSave(int slot)
    {
        if (slot < 0 || slot >= SaveSlotCount) return;

        string key = slot == 0 ? SaveKey : SaveKey + "_" + slot;
        string invKey = slot == 0 ? InventorySaveKey : InventorySaveKey + "_" + slot;
        PlayerPrefs.DeleteKey(key);
        PlayerPrefs.DeleteKey(invKey);
        PlayerPrefs.Save();
    }

    /// <summary>指定槽位是否有存档数据</summary>
    public bool HasSave(int slot)
    {
        if (slot < 0 || slot >= SaveSlotCount) return false;
        string key = slot == 0 ? SaveKey : SaveKey + "_" + slot;
        return PlayerPrefs.HasKey(key);
    }

    /// <summary>读取指定槽位元数据（时间/章节/技能点/三属性摘要），用于存档槽 UI</summary>
    public SlotMeta GetSlotMeta(int slot)
    {
        if (slot < 0 || slot >= SaveSlotCount || !HasSave(slot))
            return new SlotMeta { hasData = false };

        string key = slot == 0 ? SaveKey : SaveKey + "_" + slot;
        string json = PlayerPrefs.GetString(key, "");
        SaveData data;
        try
        {
            data = JsonUtility.FromJson<SaveData>(json);
        }
        catch (System.Exception)
        {
            return new SlotMeta { hasData = false };
        }
        if (data == null) return new SlotMeta { hasData = false };

        // 三属性摘要 = 存档的分配点数 + 基础值（无配置时默认 5，与 PlayerAttributeSystem 默认一致）
        int baseStr = 5, baseInt = 5, baseAgi = 5;
        PlayerAttrConfigSO cfg = AttrSystem != null ? AttrSystem.AttrConfig : null;
        if (cfg != null)
        {
            baseStr = cfg.baseStr;
            baseInt = cfg.baseInt;
            baseAgi = cfg.baseAgi;
        }

        return new SlotMeta
        {
            hasData = true,
            saveTime = string.IsNullOrEmpty(data.saveTime) ? "-" : data.saveTime,
            chapter = data.currentChapter,
            skillPoints = data.skillPoints,
            str = data.assignedStr + baseStr,
            @int = data.assignedInt + baseInt,
            agi = data.assignedAgi + baseAgi,
        };
    }

    /// <summary>自动存档 → 固定槽 5（覆盖式，不占手动槽）</summary>
    public void AutoSave()
    {
        SaveGame(AutoSlotIndex);
    }

    /// <summary>恢复玩家位置 — 延迟一帧，等地区显隐稳定后再设置 Transform</summary>
    private IEnumerator RestorePositionNextFrame(SaveData data)
    {
        yield return null;
        PlayerController player = PlayerController.Instance;
        if (player == null) yield break;
        player.transform.position = new Vector3(data.posX, data.posY, data.posZ);
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
    // [阶段8] 收集 — 元素状态（决策 D17：SaveSystem 不直接管字段语义，只搬运 ElementModule 导出接口）
    // ============================================================

    private void CollectElement(SaveData data)
    {
        if (elementModule == null) return; // 元素模块未挂（Player 未配置）→ 跳过，字段保持默认（None + 空列表）
        data.currentElement = elementModule.CurrentElement;
        data.unlockedElements = new List<ElementType>(elementModule.UnlockedElements);
    }

    // ============================================================
    // [石碑系统 T2] 收集 — 已激活石碑（决策：WaypointSystem 导出 ActivatedWaypoints 只读列表，
    // SaveSystem 只搬运复制，不解释 id 语义；可空：Inspector 未拖引用 → 跳过）
    // ============================================================

    private void CollectWaypoints(SaveData data)
    {
        if (waypointSystem == null) return; // 未拖场景根 WaypointSystem 引用 → 跳过，字段保持 null（视为空）
        // 复制构造，防引用别名——否则下次激活改动 _activationOrder 会污染已序列化的存档数据
        data.activatedWaypoints = new List<string>(waypointSystem.ActivatedWaypoints);
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
    // [阶段8] 恢复 — 元素状态（决策 D17：走 ElementModule 导入接口）
    // 恢复顺序（手册 8.1）：先恢复解锁列表，再恢复当前元素——
    // SetElement 校验 IsUnlocked，当前元素必须先出现在解锁列表里才能切换成功。
    // ============================================================

    private void RestoreElement(SaveData data)
    {
        if (elementModule == null) return; // 元素模块未挂（Player 未配置）→ 跳过，不报错

        // 1. 恢复解锁列表（旧档无此字段 → unlockedElements=null，跳过 = 空解锁列表）
        if (data.unlockedElements != null)
        {
            for (int i = 0; i < data.unlockedElements.Count; i++)
            {
                elementModule.UnlockElement(data.unlockedElements[i]);
            }
        }

        // 2. 恢复当前元素（None 恒可用；旧档默认 None；异常档当前元素未解锁由 SetElement 内部校验忽略）
        elementModule.SetElement(data.currentElement);
    }

    // ============================================================
    // [石碑系统 T2] 恢复 — 已激活石碑（决策：走 WaypointSystem.RestoreActivated 导入接口，
    // SaveSystem 不直接管集合语义；旧档 activatedWaypoints==null → RestoreActivated 内部视为空，不报错）
    // ============================================================

    private void RestoreWaypoints(SaveData data)
    {
        if (waypointSystem == null) return; // 未拖引用 → 跳过，不报错
        waypointSystem.RestoreActivated(data.activatedWaypoints);
    }

    // ============================================================
    // [石碑系统 T3] 恢复 — 当前地区(CurrentAreaId)
    // 决策:走 ZoneManager.SetCurrentAreaSilent 静默版(只写不广播)——广播版会触发
    // AreaEnterEvent → AutoSave → 反向覆盖刚读的档(方案风险 R5)。areaName 空(旧档)→ 不动,
    // CurrentAreaId 保持 ZoneManager.Awake 兜底的 initialAreaId(兼容矩阵 R6)。
    // ============================================================

    private void RestoreCurrentArea(SaveData data)
    {
        var zm = ZoneManager.Instance;
        if (zm == null) return; // ZoneManager 未挂(纯技能场景) → 跳过,不报错
        if (string.IsNullOrEmpty(data.areaName)) return; // 旧档无地区名 → 保持 initialAreaId
        zm.SetCurrentAreaSilent(data.areaName);
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

        // 兜底：初始技能配置在 SkillPool.initialSkills（不在技能槽位中），
        // 不查这里会导致读档后技能池恢复为空、HUD 装备报"不在池中"
        if (skillPool != null && skillPool.TryGetInitialSkill(skillName, out SkillData initial))
            return initial;

        // 兜底2：合成技能（CombinationSkillData 在 Resources/Skills/Combo，不在槽位/初始技能中）——
        // 2026-08-19 修复：不查这里合成产物读档后从池中消失
        var combos = Resources.LoadAll<CombinationSkillData>("Skills/Combo");
        for (int i = 0; i < combos.Length; i++)
        {
            if (combos[i] != null && combos[i].skillName == skillName)
                return combos[i];
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
    /// 保存背包/仓库/装备数据到指定槽位对应的 key
    /// 延迟查找 InventoryManager（可能在 SaveSystem 之后初始化）
    /// </summary>
    private void SaveInventory(int slot)
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
            string key = slot == 0 ? InventorySaveKey : InventorySaveKey + "_" + slot;
            string json = JsonUtility.ToJson(invData, prettyPrint: false);
            PlayerPrefs.SetString(key, json);
        }
    }

    /// <summary>
    /// 从指定槽位加载背包/仓库/装备数据
    /// </summary>
    private void LoadInventory(int slot)
    {
        string key = slot == 0 ? InventorySaveKey : InventorySaveKey + "_" + slot;
        if (!PlayerPrefs.HasKey(key)) return;

        string json = PlayerPrefs.GetString(key, "");
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
        // [阶段8] 元素状态（决策 D17：SaveSystem 只搬运，不解释字段语义——写入读 CurrentElement/UnlockedElements，
        // 恢复走 UnlockElement/SetElement；旧档无此字段 → currentElement=None、unlockedElements=null 视为空）
        public ElementType currentElement;
        public List<ElementType> unlockedElements;
        // [存档UI] 槽位元数据
        public string saveTime;  // DateTime.Now.ToString("MM-dd HH:mm")
        public string areaName;  // 玩家所在地区名（地区名追踪后续优化，字段预留）
        public float posX, posY, posZ; // 玩家位置（读档恢复）
        // [石碑系统 T2] 已激活石碑 id（"area#index" 形式，按激活顺序）；
        // 旧档无此字段 → null → RestoreActivated 内部视为空列表，不报错
        public List<string> activatedWaypoints;
    }

    /// <summary>存档槽元数据 — 供存档槽 UI 显示（时间/章节/技能点/三属性摘要）</summary>
    public struct SlotMeta
    {
        public bool hasData;
        public string saveTime;
        public int chapter;
        public int skillPoints;
        public int str;
        public int @int;  // 智力（字段名与方案一致，int 为关键字需 @ 转义）
        public int agi;
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
