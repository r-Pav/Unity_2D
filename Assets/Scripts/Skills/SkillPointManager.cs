using UnityEngine;

/// <summary>
/// [P1] 技能点管理器 — 挂 Player GameObject
/// 职责：技能点数的增删查、升级消耗校验、点数变化事件通知 UI
/// </summary>
public class SkillPointManager : MonoBehaviour
{
    // ============================================================
    // 配置参数
    // ============================================================

    [Header("技能点")]
    [Tooltip("初始技能点数")]
    [SerializeField] private int initialSkillPoints = 3;

    [Tooltip("最大技能点数")]
    [SerializeField] private int maxSkillPoints = 99;

    // ============================================================
    // 运行时状态
    // ============================================================

    private int currentSkillPoints;

    // ============================================================
    // 公开属性
    // ============================================================

    public int CurrentSkillPoints => currentSkillPoints;
    public int MaxSkillPoints => maxSkillPoints;

    // ============================================================
    // 生命周期
    // ============================================================

    private void Awake()
    {
        currentSkillPoints = initialSkillPoints;
    }

    // ============================================================
    // 公开接口 — 技能点管理
    // ============================================================

    /// <summary>消耗技能点（返回是否成功）</summary>
    public bool SpendPoints(int amount)
    {
        if (currentSkillPoints < amount) return false;
        currentSkillPoints -= amount;
        NotifyChanged();
        return true;
    }

    /// <summary>获得技能点（钳制到 maxSkillPoints）</summary>
    public void GainPoints(int amount)
    {
        int old = currentSkillPoints;
        currentSkillPoints = Mathf.Min(currentSkillPoints + amount, maxSkillPoints);

        if (currentSkillPoints != old)
            NotifyChanged();
    }

    /// <summary>是否有足够技能点</summary>
    public bool CanSpend(int amount) => currentSkillPoints >= amount;

    /// <summary>设置技能点数（调试/存档恢复用）</summary>
    public void SetPoints(int amount)
    {
        int old = currentSkillPoints;
        currentSkillPoints = Mathf.Clamp(amount, 0, maxSkillPoints);

        if (currentSkillPoints != old)
            NotifyChanged();
    }

    // ============================================================
    // 内部方法
    // ============================================================

    /// <summary>通知 UI 技能点变化</summary>
    private void NotifyChanged()
    {
        EventBus.Trigger(new PlayerSkillPointsChangedEvent(currentSkillPoints, maxSkillPoints));
    }
}
