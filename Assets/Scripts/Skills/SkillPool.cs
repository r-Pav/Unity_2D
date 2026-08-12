using UnityEngine;
using System;
using System.Collections.Generic;

/// <summary>
/// [P7] 技能池管理器 — 挂在 Player GameObject 上。
/// 职责：
///   1. 管理所有"已拥有"技能（ownedSkills 列表）
///   2. 管理 4 个 HUD 槽位的绑定（hudSlotAssignments）
///   3. 是技能拥有状态的唯一真相源（source of truth）
///
/// 核心原则：
///   - SkillPool 管"拥有"：记录玩家获得了哪些技能
///   - SkillManager 管"装备"：4 个 HUD 槽位指向 SkillPool 中的技能
///   - 所有技能获取渠道（初始解锁、合成产出、商店购买等）统一汇入 SkillPool
/// </summary>
public class SkillPool : MonoBehaviour
{
    // ============================================================
    // 序列化配置
    // ============================================================

    [Header("初始技能")]
    [Tooltip("游戏开始时自动拥有的技能列表")]
    [SerializeField] private List<SkillData> initialSkills;

    [Header("运行时状态")]
    [SerializeField] private List<OwnedSkillEntry> ownedSkills = new();

    // ============================================================
    // HUD 槽位配置
    // ============================================================

    /// <summary>4 个 HUD 槽位，每个槽位存储 OwnedSkillEntry 的 id，-1 或空字符串 = 空</summary>
    private string[] hudSlotAssignments = new string[4] { "", "", "", "" };

    // ============================================================
    // 事件（C# Action，非 EventBus，保持自包含）
    // ============================================================

    /// <summary>技能池内容变化（增/删/升级）</summary>
    public event Action OnPoolChanged;

    /// <summary>指定 HUD 槽位绑定变化（装备/卸下），参数为 hudIndex</summary>
    public event Action<int> OnHudSlotChanged;

    // ============================================================
    // 生命周期
    // ============================================================

    private void Awake()
    {
        // 如果没有通过 SaveSystem 恢复存档，则初始化初始技能
        InitializeInitialSkills();
    }

    private void OnEnable()
    {
        EventBus.Subscribe<SkillLevelChangedEvent>(OnSkillLevelChanged);
    }

    private void OnDisable()
    {
        EventBus.Unsubscribe<SkillLevelChangedEvent>(OnSkillLevelChanged);
    }

    /// <summary>技能升级时同步 Pool 中对应条目的等级</summary>
    private void OnSkillLevelChanged(SkillLevelChangedEvent e)
    {
        if (string.IsNullOrEmpty(e.skillName)) return;
        var entry = FindEntryById(e.skillName);
        if (entry != null && e.newLevel > entry.level)
        {
            entry.level = e.newLevel;
            OnPoolChanged?.Invoke();
        }
    }

    /// <summary>
    /// 将 Inspector 中配置的 initialSkills 添加到技能池，
    /// 并自动装备到前 N 个 HUD 槽位。
    /// 仅在 ownedSkills 为空时执行（有存档时跳过）。
    /// </summary>
    private void InitializeInitialSkills()
    {
        if (initialSkills == null || initialSkills.Count == 0) return;

        foreach (var skillData in initialSkills)
        {
            if (skillData == null) continue;
            AddSkillInternal(skillData, skillData.skillLevel, "initial", skipEvent: true);
        }

        // 自动装备初始技能到 HUD 的前 N 个槽位（按顺序 Q/E/R/F）
        int slotIndex = 0;
        foreach (var entry in ownedSkills)
        {
            if (slotIndex >= 4) break;
            // 只自动装备主动/切换类型技能到 HUD
            if (entry.skillData != null &&
                (entry.skillData.type == SkillType.Active || entry.skillData.type == SkillType.Toggle))
            {
                hudSlotAssignments[slotIndex] = entry.id;
                slotIndex++;
            }
        }

        // 初始化完成后触发一次完整刷新
        OnPoolChanged?.Invoke();
    }

    // ============================================================
    // 公共接口 — 技能池管理
    // ============================================================

    /// <summary>
    /// 添加技能到池子。如果已存在同名技能则升级（覆盖等级）。
    /// </summary>
    /// <param name="skillData">技能 SO 数据</param>
    /// <param name="level">技能等级（默认 1）</param>
    /// <param name="source">获得来源：initial / craft / unlock / quest / shop</param>
    /// <returns>是否成功添加（新增或升级）</returns>
    public bool AddSkill(SkillData skillData, int level = 1, string source = "unknown")
    {
        return AddSkillInternal(skillData, level, source, skipEvent: false);
    }

    private bool AddSkillInternal(SkillData skillData, int level, string source, bool skipEvent)
    {
        if (skillData == null) return false;

        string skillId = skillData.skillName;

        // 检查是否已拥有：如果已有则升级（取较高等级）
        var existing = FindEntryById(skillId);
        if (existing != null)
        {
            if (level > existing.level)
                existing.level = level;
            existing.source = source; // 更新来源
            if (!skipEvent) OnPoolChanged?.Invoke();
            return true;
        }

        // 新增条目
        var entry = new OwnedSkillEntry
        {
            id = skillId,
            skillData = skillData,
            level = level,
            source = source,
            acquiredAt = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")
        };
        ownedSkills.Add(entry);

        if (!skipEvent) OnPoolChanged?.Invoke();
        return true;
    }

    /// <summary>
    /// 移除技能（不可逆，如合成消耗）。
    /// 如果该技能已装备到 HUD 槽位，会自动清空对应槽位。
    /// </summary>
    /// <param name="skillId">技能 ID（即 skillName）</param>
    /// <returns>是否成功移除</returns>
    public bool RemoveSkill(string skillId)
    {
        if (string.IsNullOrEmpty(skillId)) return false;

        // 先从 HUD 槽位中清除（如果已装备）
        for (int i = 0; i < hudSlotAssignments.Length; i++)
        {
            if (hudSlotAssignments[i] == skillId)
            {
                hudSlotAssignments[i] = "";
                OnHudSlotChanged?.Invoke(i);
            }
        }

        // 再从池中移除
        int removed = ownedSkills.RemoveAll(e => e.id == skillId);
        if (removed > 0)
        {
            OnPoolChanged?.Invoke();
            return true;
        }
        return false;
    }

    /// <summary>获取所有已拥有技能的只读列表</summary>
    public List<OwnedSkillEntry> GetOwnedSkills()
    {
        return ownedSkills;
    }

    /// <summary>按 skillId 查找技能条目</summary>
    public OwnedSkillEntry FindSkill(string skillId)
    {
        return FindEntryById(skillId);
    }

    // ============================================================
    // 公共接口 — HUD 槽位绑定
    // ============================================================

    /// <summary>获取指定 HUD 槽位装备的技能（null=空）</summary>
    public OwnedSkillEntry GetHudSkill(int hudIndex)
    {
        if (hudIndex < 0 || hudIndex >= hudSlotAssignments.Length) return null;
        string skillId = hudSlotAssignments[hudIndex];
        if (string.IsNullOrEmpty(skillId)) return null;
        return FindEntryById(skillId);
    }

    /// <summary>
    /// 将技能装备到 HUD 槽位。
    /// 如果该技能已在其他 HUD 槽位装备，会自动从旧槽位移除。
    /// </summary>
    /// <param name="hudIndex">HUD 槽位索引 0~3</param>
    /// <param name="skillId">技能 ID（即 skillName）</param>
    /// <returns>是否装备成功</returns>
    public bool EquipToHud(int hudIndex, string skillId)
    {
        if (hudIndex < 0 || hudIndex >= hudSlotAssignments.Length) return false;
        if (string.IsNullOrEmpty(skillId)) return false;

        // 检查技能是否存在于池中
        var entry = FindEntryById(skillId);
        if (entry == null)
        {
            Debug.LogWarning($"[SkillPool] 技能 [{skillId}] 不在池中，无法装备");
            return false;
        }

        // 如果已在其他 HUD 槽位装备，先移除（避免重复装备）
        for (int i = 0; i < hudSlotAssignments.Length; i++)
        {
            if (i != hudIndex && hudSlotAssignments[i] == skillId)
            {
                hudSlotAssignments[i] = "";
                OnHudSlotChanged?.Invoke(i);
            }
        }

        hudSlotAssignments[hudIndex] = skillId;
        OnHudSlotChanged?.Invoke(hudIndex);
        return true;
    }

    /// <summary>清空指定 HUD 槽位</summary>
    public void ClearHudSlot(int hudIndex)
    {
        if (hudIndex < 0 || hudIndex >= hudSlotAssignments.Length) return;
        if (string.IsNullOrEmpty(hudSlotAssignments[hudIndex])) return;

        hudSlotAssignments[hudIndex] = "";
        OnHudSlotChanged?.Invoke(hudIndex);
    }

    /// <summary>获取所有 HUD 槽位分配情况（返回 skillId 数组，空字符串=空）</summary>
    public string[] GetHudAssignments()
    {
        return (string[])hudSlotAssignments.Clone();
    }

    /// <summary>获取 HUD 槽位数量</summary>
    public const int HudSlotCount = 4;

    // ============================================================
    // 公共接口 — 查询
    // ============================================================

    /// <summary>
    /// 按 skillName 查找初始技能 SO（读档恢复技能池用：初始技能在 initialSkills 配置，
    /// 不在 SkillManager 槽位中，SaveSystem.FindSkillDataByName 找不到时兜底到这里）
    /// </summary>
    public bool TryGetInitialSkill(string skillName, out SkillData data)
    {
        if (initialSkills != null)
        {
            for (int i = 0; i < initialSkills.Count; i++)
            {
                if (initialSkills[i] != null && initialSkills[i].skillName == skillName)
                {
                    data = initialSkills[i];
                    return true;
                }
            }
        }
        data = null;
        return false;
    }

    /// <summary>是否拥有某技能（按 skillName 查找）</summary>
    public bool HasSkill(string skillId)
    {
        return FindEntryById(skillId) != null;
    }

    /// <summary>技能池中技能总数</summary>
    public int OwnedCount => ownedSkills.Count;

    // ============================================================
    // 公共接口 — HUD 查询辅助方法（供 SkillManager 使用）
    // ============================================================

    /// <summary>获取指定 HUD 槽位的 SkillData SO 引用（null=空）</summary>
    public SkillData GetHudSkillData(int hudIndex)
    {
        return GetHudSkill(hudIndex)?.skillData;
    }

    /// <summary>获取指定 HUD 槽位的技能等级（空槽返回 0）</summary>
    public int GetHudSkillLevel(int hudIndex)
    {
        return GetHudSkill(hudIndex)?.level ?? 0;
    }

    // ============================================================
    // 内部方法
    // ============================================================

    private OwnedSkillEntry FindEntryById(string skillId)
    {
        if (string.IsNullOrEmpty(skillId)) return null;
        for (int i = 0; i < ownedSkills.Count; i++)
        {
            if (ownedSkills[i].id == skillId)
                return ownedSkills[i];
        }
        return null;
    }
}
