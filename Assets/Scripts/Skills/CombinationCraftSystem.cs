using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// [P5] 组合技能合成系统 — 挂 Player GameObject
/// 职责：材料池收集、配方校验、消耗产出、暴露数据接口供 UI 消费
///
/// 合成流程：选 2 材料 → 校验配方 → 等级判定 → 消耗产出
/// 材料池：主动技能（含分支）+ 武器技能；被动/组合技能不可用作材料
/// 等级判定：取两材料较低等级，匹配 3 个配方
/// 消耗：永久消失，不可逆
/// 产出：分配到空闲主动技能槽
/// </summary>
public class CombinationCraftSystem : MonoBehaviour
{
    // ============================================================
    // 配方表 — 运行时从 Resources/Skills/Combo 加载所有组合技能 SO
    // 每个 CombinationSkillData 自带 materialSkillA / materialSkillB 配方字段
    // ============================================================

    private CombinationSkillData[] allCombos;

    // ============================================================
    // 运行时引用
    // ============================================================

    private SkillManager skillManager;
    private SkillPool skillPool;
    private WeaponSkillLink weaponSkillLink;
    private PassiveEquipManager passiveEquipManager;

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
        weaponSkillLink = GetComponent<WeaponSkillLink>();
        passiveEquipManager = GetComponent<PassiveEquipManager>();

        // 加载所有组合技能配方
        allCombos = Resources.LoadAll<CombinationSkillData>("Skills/Combo");
        if (allCombos == null || allCombos.Length == 0)
        {
            allCombos = new CombinationSkillData[0];
            Debug.LogWarning("[ComboCraft] Resources/Skills/Combo 下未找到任何组合技能 SO");
        }
    }

    // ============================================================
    // 材料信息结构（供 UI 消费）
    // ============================================================

    /// <summary>单个合成材料描述符</summary>
    public struct MaterialInfo
    {
        /// <summary>技能显示名称</summary>
        public string skillName;
        /// <summary>技能 ID（用于从 SkillPool 移除）</summary>
        public string skillId;
        /// <summary>技能等级</summary>
        public int level;
        /// <summary>所属技能树根名称（用于同树过滤）</summary>
        public string rootSkillName;
        /// <summary>是否为武器技能</summary>
        public bool isWeaponSkill;
        /// <summary>主动技能槽位索引（武器技能时为 -1）</summary>
        public int slotIndex;
        /// <summary>原始 SkillData SO 引用</summary>
        public SkillData skillData;
    }

    // ============================================================
    // 公共接口 — 材料池（供 UI 消费）
    // ============================================================

    /// <summary>
    /// 获取所有可作合成材料的技能列表。每个技能树按已解锁等级展开多行（Lv1~当前等级）。
    /// </summary>
    /// <param name="excludeRootName">可选：排除指定技能树的所有等级（用于第二槽过滤）</param>
    public List<MaterialInfo> GetAvailableMaterials(string excludeRootName = null)
    {
        var materials = new List<MaterialInfo>();

        // 1. [P7] 从 SkillPool 获取所有已拥有技能，按等级展开
        if (skillPool != null)
        {
            var owned = skillPool.GetOwnedSkills();
            foreach (var entry in owned)
            {
                if (entry.skillData == null) continue;
                // 组合技能（合成产物）不可再作合成材料
                if (entry.skillData is CombinationSkillData) continue;
                if (entry.skillData.type != SkillType.Active &&
                    entry.skillData.type != SkillType.Toggle)
                    continue;

                string rootName = entry.skillData.skillName;
                if (!string.IsNullOrEmpty(excludeRootName) && rootName == excludeRootName)
                    continue;

                // 展开 Lv1 到当前等级，每级一行
                for (int lv = 1; lv <= entry.level; lv++)
                {
                    materials.Add(new MaterialInfo
                    {
                        skillName = rootName,
                        skillId = entry.id,
                        level = lv,
                        rootSkillName = rootName,
                        isWeaponSkill = false,
                        slotIndex = -1,
                        skillData = entry.skillData
                    });
                }
            }
        }

        // 2. 武器技能（视为 Lv1，不展开）
        if (weaponSkillLink != null && weaponSkillLink.HasWeaponSkill)
        {
            var wsData = weaponSkillLink.CurrentWeaponSkill;
            if (wsData != null && (string.IsNullOrEmpty(excludeRootName) || wsData.skillName != excludeRootName))
            {
                materials.Add(new MaterialInfo
                {
                    skillName = wsData.skillName,
                    skillId = wsData.skillName,
                    level = 1,
                    rootSkillName = wsData.skillName,
                    isWeaponSkill = true,
                    slotIndex = -1,
                    skillData = wsData
                });
            }
        }

        return materials;
    }

    // ============================================================
    // 公共接口 — 配方校验（供 UI 消费）
    // ============================================================

    /// <summary>
    /// 校验两个材料是否可合成，返回产出技能预览。
    /// 供 UI 在玩家选材料时实时显示预览。
    /// </summary>
    /// <param name="m1">第 1 个材料</param>
    /// <param name="m2">第 2 个材料</param>
    /// <param name="result">产出技能 SO（校验通过时非 null）</param>
    /// <param name="failReason">校验失败原因</param>
    /// <returns>是否合法配方</returns>
    public bool ValidateRecipe(MaterialInfo m1, MaterialInfo m2, out CombinationSkillData result, out string failReason)
    {
        result = null;
        failReason = null;

        // 同一技能实例不可自合成
        if (m1.skillData == m2.skillData)
        {
            failReason = "不能使用同一个技能自合成";
            return false;
        }

        // 必须同级
        if (m1.level != m2.level)
        {
            failReason = $"材料等级不同（{m1.level} ≠ {m2.level}），只能合成同级技能";
            return false;
        }

        // 遍历所有组合技能配方，匹配材料（SO + 等级）
        result = FindMatchingCombo(m1.skillData, m2.skillData, m1.level, m2.level);
        if (result == null)
        {
            failReason = $"技能组合 [{m1.skillName} + {m2.skillName}] 无可匹配配方";
            return false;
        }

        // 已拥有该配方产出 → 禁止重复合成
        if (HasOwnedCombo(result))
        {
            failReason = "已有技能，不可重复合成";
            result = null;
            return false;
        }

        return true;
    }

    /// <summary>技能池是否已拥有指定组合技能（按 SO 引用或 skillName 匹配）</summary>
    private bool HasOwnedCombo(CombinationSkillData combo)
    {
        if (skillPool == null || combo == null) return false;
        var owned = skillPool.GetOwnedSkills();
        foreach (var entry in owned)
        {
            if (entry.skillData != null && entry.skillData == combo) return true;
            if (!string.IsNullOrEmpty(entry.id) && entry.id == combo.skillName) return true;
        }
        return false;
    }

    /// <summary>
    /// 在所有组合技能中查找匹配两个材料（SO + 等级）的配方。
    /// 匹配规则：A+B 或 B+A，SO 引用相等且等级相等，配方材料必须非 null。
    /// 返回 null 表示无匹配。
    /// </summary>
    public CombinationSkillData FindMatchingCombo(SkillData skillA, SkillData skillB, int levelA, int levelB)
    {
        if (skillA == null || skillB == null) return null;

        foreach (var combo in allCombos)
        {
            if (combo == null) continue;
            if (combo.materialSkillA == null || combo.materialSkillB == null) continue;

            if (combo.materialSkillA == skillA && combo.materialSkillB == skillB
                && combo.materialLevelA == levelA && combo.materialLevelB == levelB)
                return combo;

            if (combo.materialSkillA == skillB && combo.materialSkillB == skillA
                && combo.materialLevelA == levelB && combo.materialLevelB == levelA)
                return combo;
        }

        return null;
    }

    // ============================================================
    // 公共接口 — 合成执行
    // ============================================================

    /// <summary>
    /// [P7] 执行合成：产出组合技能进入 SkillPool。
    /// 合成不消耗材料技能，产物不自动装备到 HUD。
    /// </summary>
    /// <param name="m1">第 1 个材料</param>
    /// <param name="m2">第 2 个材料</param>
    /// <returns>是否合成成功</returns>
    public bool Craft(MaterialInfo m1, MaterialInfo m2)
    {
        // 配方校验
        if (!ValidateRecipe(m1, m2, out var resultData, out string failReason))
        {
            DebugOnce($"[ComboCraft] 合成失败: {failReason}");
            return false;
        }

        if (skillPool == null)
        {
            DebugOnce("[ComboCraft] 合成失败: SkillPool 未找到");
            return false;
        }

        // [P7] 产出进入 SkillPool（不自动装备到 HUD）
        skillPool.AddSkill(resultData, resultData.combinationLevel, "craft");

        // 触发事件
        EventBus.Trigger(new CombinationCraftedEvent(
            new[] { m1.skillName, m2.skillName },
            resultData.skillName,
            resultData.skillName
        ));

        Debug.Log($"[ComboCraft] 合成成功: {m1.skillName} + {m2.skillName} → {resultData.skillName} (进入技能池)");
        return true;
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
            Debug.Log(msg);
        }
    }
}
