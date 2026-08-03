using UnityEngine;

/// <summary>
/// [P4] 武器技能联动组件 — 挂 Player GameObject
/// 职责：监听武器装备/卸下事件，自动管理武器技能引用。
/// 武器技能不走 SkillSlot 的 Q/E 槽位，以独立引用的方式存在。
///
/// 调用链：
///   WeaponSystem ──EventBus──→ WeaponSkillLink
///                               ├── 装备 → 记录技能引用（供 UI/战斗查询）
///                               └── 卸下 → 清除技能引用
///
/// 边界：组合合成消耗了武器技能后，不自动重新获得，
///       直到下次卸下并重新装备时才恢复。
/// </summary>
public class WeaponSkillLink : MonoBehaviour
{
    // ============================================================
    // 运行时状态
    // ============================================================

    private WeaponSkillData _currentSkill;
    private bool _skillConsumed; // 组合消耗标记：true 表示技能已被消耗

    // ============================================================
    // 公共接口（供 UI / 战斗 / 组合系统消费）
    // ============================================================

    /// <summary>当前装备武器对应的技能数据（可能为 null）</summary>
    public WeaponSkillData CurrentWeaponSkill => _currentSkill;

    /// <summary>是否持有可用的武器技能（装备中且未被消耗）</summary>
    public bool HasWeaponSkill => _currentSkill != null && !_skillConsumed;

    /// <summary>[P6] 武器技能是否已被组合消耗（UI 可据此显示恢复提示）</summary>
    public bool IsWeaponSkillConsumed => _skillConsumed;

    /// <summary>当前装备的武器类型（无武器时返回 null）</summary>
    public WeaponType? CurrentWeaponType =>
        _currentSkill != null ? _currentSkill.weaponType : null;

    // ============================================================
    // 组合消耗接口（P5 组合系统调用）
    // ============================================================

    /// <summary>
    /// [P5] 消耗当前武器技能（组合合成时调用）。
    /// 消耗后技能引用被清除，不自动恢复，直到下次重新装备。
    /// </summary>
    /// <returns>消耗成功返回 true；当前无可消耗的武器技能返回 false</returns>
    public bool ConsumeWeaponSkill()
    {
        if (!HasWeaponSkill) return false;
        _skillConsumed = true;
        _currentSkill = null;
        return true;
    }

    // ============================================================
    // 事件处理
    // ============================================================

    private void OnEnable()
    {
        EventBus.Subscribe<WeaponEquippedEvent>(OnWeaponEquipped);
        EventBus.Subscribe<WeaponUnequippedEvent>(OnWeaponUnequipped);
    }

    private void OnDisable()
    {
        EventBus.Unsubscribe<WeaponEquippedEvent>(OnWeaponEquipped);
        EventBus.Unsubscribe<WeaponUnequippedEvent>(OnWeaponUnequipped);
    }

    private void OnWeaponEquipped(WeaponEquippedEvent e)
    {
        _currentSkill = e.skillData;
        _skillConsumed = false;
    }

    private void OnWeaponUnequipped(WeaponUnequippedEvent e)
    {
        _currentSkill = null;
        _skillConsumed = false;
    }
}
