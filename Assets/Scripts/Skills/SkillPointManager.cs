using UnityEngine;

/// <summary>
/// [P1] 技能点管理器 — 挂 Player GameObject
/// 职责：技能点数的增删查、升级消耗校验、点数变化事件通知 UI
/// </summary>
public class SkillPointManager : MonoBehaviour
{
    // ============================================================
    // 常量
    // ============================================================

    /// <summary>能量球进度兑换阈值 — 每满 100 点自动兑换 +1 技能点（100 即兑换不驻留，进度存储上限恒为 99）</summary>
    private const int EnergyPerSkillPoint = 100;

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

    /// <summary>能量球进度（0~99 存储；满 100 自动兑换 +1 技能点；技能点满 99 后钳 99 不再涨，溢出丢弃）</summary>
    private int energyProgress;

    // ============================================================
    // 公开属性
    // ============================================================

    public int CurrentSkillPoints => currentSkillPoints;
    public int MaxSkillPoints => maxSkillPoints;

    /// <summary>当前能量球进度（只读 — HUD 进度条订阅数据）</summary>
    public int EnergyProgress => energyProgress;

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
    // 公开接口 — 能量球进度
    // ============================================================

    /// <summary>
    /// 增加能量球进度 — 每满 100 自动兑换 +1 技能点（余数保留继续累计）。
    /// 技能点已满（maxSkillPoints）后停止兑换，进度钳 99（溢出丢弃）。
    /// </summary>
    public void AddEnergy(int amount)
    {
        energyProgress += amount;

        // 每满 100 兑换 +1 技能点（兑换中技能点达到上限则停止，剩余进度按"已满"钳制丢弃）
        while (energyProgress >= EnergyPerSkillPoint && currentSkillPoints < maxSkillPoints)
        {
            energyProgress -= EnergyPerSkillPoint;
            GainPoints(1);
        }

        // 技能点已满：进度不再累计（100 即兑换不驻留 → 钳到 99 = 上限-1，溢出丢弃）
        if (currentSkillPoints >= maxSkillPoints)
            energyProgress = Mathf.Min(energyProgress, EnergyPerSkillPoint - 1);

        NotifyEnergyProgressChanged();
    }

    /// <summary>设置能量球进度（读档恢复用，0~99 钳制）</summary>
    public void SetEnergyProgress(int amount)
    {
        energyProgress = Mathf.Clamp(amount, 0, EnergyPerSkillPoint - 1);
        NotifyEnergyProgressChanged();
    }

    // ============================================================
    // 内部方法
    // ============================================================

    /// <summary>通知 UI 技能点变化</summary>
    private void NotifyChanged()
    {
        EventBus.Trigger(new PlayerSkillPointsChangedEvent(currentSkillPoints, maxSkillPoints));
    }

    /// <summary>通知 UI 能量球进度变化</summary>
    private void NotifyEnergyProgressChanged()
    {
        EventBus.Trigger(new PlayerEnergyProgressChangedEvent(energyProgress));
    }
}
