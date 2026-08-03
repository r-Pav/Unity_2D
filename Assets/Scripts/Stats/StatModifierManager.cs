using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// [P1] 属性修饰器管理器 — 挂 Player GameObject
/// 职责：修饰器增删改查、最终值计算（基础值×(1+Σ百分比)+Σ数值）、属性刷新事件触发
/// 叠加规则：同 source 覆盖、条件修饰器、最小值钳制
/// </summary>
public class StatModifierManager : MonoBehaviour
{
    // ============================================================
    // Singleton 注册表（Player 子组件；调用方统一走 Instance）
    // ============================================================

    private static StatModifierManager _instance;

    public static StatModifierManager Instance
    {
        get
        {
            if (_instance == null)
                _instance = FindObjectOfType<StatModifierManager>();
            return _instance;
        }
    }

    // ============================================================
    // 配置 — 最小值钳制
    // ============================================================

    [Header("最小值钳制")]
    [Tooltip("移速最小值（避免卡死）")]
    [SerializeField] private float moveSpeedMin = 0.5f;
    [Tooltip("通用属性最小值")]
    [SerializeField] private float defaultMin = 0f;

    // ============================================================
    // 运行时状态
    // ============================================================

    /// <summary>活跃修饰器列表</summary>
    private readonly List<Modifier> activeModifiers = new List<Modifier>();

    /// <summary>每属性的 debug 帧计数器（限 3 帧输出）</summary>
    private readonly Dictionary<string, int> debugFrameCounters = new Dictionary<string, int>();

    // ============================================================
    // 公开接口 — 修饰器管理
    // ============================================================

    /// <summary>添加修饰器（同 source 自动覆盖旧值），触发属性刷新</summary>
    public void AddModifier(Modifier mod)
    {
        // 同 source 覆盖：先移除同 source 的旧修饰器
        for (int i = activeModifiers.Count - 1; i >= 0; i--)
        {
            if (activeModifiers[i].source == mod.source)
                activeModifiers.RemoveAt(i);
        }

        activeModifiers.Add(mod);

        DebugModifierChange("Add", mod);
        NotifyModifiersChanged(new[] { mod.targetStat });
    }

    /// <summary>移除指定来源的所有修饰器，触发属性刷新</summary>
    public void RemoveModifier(string source)
    {
        var affectedStats = new HashSet<string>();
        for (int i = activeModifiers.Count - 1; i >= 0; i--)
        {
            if (activeModifiers[i].source == source)
            {
                affectedStats.Add(activeModifiers[i].targetStat);
                activeModifiers.RemoveAt(i);
            }
        }

        if (affectedStats.Count > 0)
        {
            DebugModifierChange("Remove", null, source);
            NotifyModifiersChanged(ToArray(affectedStats));
        }
    }

    /// <summary>清空所有修饰器，触发属性刷新</summary>
    public void RemoveAllModifiers()
    {
        var affectedStats = new HashSet<string>();
        foreach (var m in activeModifiers)
            affectedStats.Add(m.targetStat);

        activeModifiers.Clear();

        if (affectedStats.Count > 0)
        {
            NotifyModifiersChanged(ToArray(affectedStats));
        }
    }

    /// <summary>
    /// 批量添加修饰器（同 source 自动覆盖），仅触发一次属性刷新。
    /// 用于重算管道等一次性注入多个修饰器的场景，避免逐个 AddModifier 的事件风暴。
    /// </summary>
    public void AddModifiers(IEnumerable<Modifier> mods)
    {
        var affectedStats = new HashSet<string>();
        foreach (var mod in mods)
        {
            // 同 source 覆盖：先移除同 source 的旧修饰器
            for (int i = activeModifiers.Count - 1; i >= 0; i--)
            {
                if (activeModifiers[i].source == mod.source)
                    activeModifiers.RemoveAt(i);
            }

            activeModifiers.Add(mod);
            affectedStats.Add(mod.targetStat);
        }

        if (affectedStats.Count > 0)
            NotifyModifiersChanged(ToArray(affectedStats));
    }

    /// <summary>
    /// 批量移除多个来源的所有修饰器，仅触发一次属性刷新。
    /// 用于重算管道等一次性清理多个 source 的场景，避免逐个 RemoveModifier 的事件风暴。
    /// </summary>
    public void RemoveModifiers(IEnumerable<string> sources)
    {
        var affectedStats = new HashSet<string>();
        foreach (var source in sources)
        {
            for (int i = activeModifiers.Count - 1; i >= 0; i--)
            {
                if (activeModifiers[i].source == source)
                {
                    affectedStats.Add(activeModifiers[i].targetStat);
                    activeModifiers.RemoveAt(i);
                }
            }
        }

        if (affectedStats.Count > 0)
            NotifyModifiersChanged(ToArray(affectedStats));
    }

    // ============================================================
    // 公开接口 — 最终值查询
    // ============================================================

    /// <summary>
    /// 计算最终属性值（不触发事件，仅查询）
    /// 公式：最终值 = baseValue × (1 + ΣPercent) + ΣFlat，然后钳制最小值
    /// </summary>
    /// <param name="baseValue">基础值</param>
    /// <param name="statId">属性标识符</param>
    public float GetFinalValue(float baseValue, string statId)
    {
        float percentSum = 0f;
        float flatSum = 0f;

        foreach (var mod in activeModifiers)
        {
            if (mod.targetStat != statId || !mod.IsActive())
                continue;

            if (mod.type == ModifierType.Percent)
                percentSum += mod.value;
            else // Flat
                flatSum += mod.value;
        }

        float result = baseValue * (1f + percentSum) + flatSum;

        // 最终值钳制：字典配置优先，无配置则仅下限兜底
        if (ClampConfig.TryGetValue(statId, out var range))
            result = Mathf.Clamp(result, range.min, range.max);
        else
        {
            float min = defaultMin;
            if (result < min) result = min;
        }

        return result;
    }

    /// <summary>获取指定属性的所有活跃修饰器（调试/UI）</summary>
    public List<Modifier> GetActiveModifiers(string statId)
    {
        var result = new List<Modifier>();
        foreach (var m in activeModifiers)
            if (m.targetStat == statId) result.Add(m);
        return result;
    }

    /// <summary>检查指定属性是否有活跃修饰器</summary>
    public bool HasModifier(string statId)
    {
        foreach (var m in activeModifiers)
            if (m.targetStat == statId) return true;
        return false;
    }

    // ============================================================
    // 钳制配置 — 字典策略替代 if-else
    // ============================================================

    /// <summary>每个属性的最终值钳制范围（min, max）</summary>
    private static readonly System.Collections.Generic.Dictionary<string, (float min, float max)> ClampConfig =
        new System.Collections.Generic.Dictionary<string, (float min, float max)>()
    {
        { StatId.DamageMultiplier,    (0f,   3.0f) },
        { StatId.DamageReduction,     (0f,   0.8f) },
        { StatId.DodgeChance,         (0f,   0.6f) },
        { StatId.MoveSpeed,           (1.0f, 12.0f) },
    };

    // ============================================================
    // 内部方法
    // ============================================================

    /// <summary>触发修饰器变化事件（EventBus 广播）</summary>
    private void NotifyModifiersChanged(string[] affectedStatIds)
    {
        EventBus.Trigger(new StatModifiersChangedEvent(affectedStatIds));

        // [P1] 同步触发属性重算事件 — 供 HUD 数值显示等订阅
        foreach (var statId in affectedStatIds)
            EventBus.Trigger(new PlayerStatRecalculatedEvent(statId, 0f, 0f));
    }

    /// <summary>[公开] 强制刷新指定属性（供外部 HP 变化等触发低血条件重算）</summary>
    public void ForceRefreshStat(string statId)
    {
        NotifyModifiersChanged(new[] { statId });
    }

    /// <summary>触发式 debug 日志，每 stat 最多 3 帧</summary>
    private void DebugModifierChange(string action, Modifier mod = null, string source = null)
    {
        string key = mod != null ? mod.targetStat : (source ?? "all");
        if (!debugFrameCounters.TryGetValue(key, out int count))
            count = 0;

        if (count >= 3) return;
        debugFrameCounters[key] = count + 1;

        string detail = mod != null
            ? $"{mod.targetStat} [{mod.type}] {mod.value:+0.##;-0.##} src={mod.source}"
            : $"source={source}";
        // Debug.Log($"[StatMod] {action}: {detail}");
    }

    // ============================================================
    // 工具方法
    // ============================================================

    private static string[] ToArray(HashSet<string> set)
    {
        var arr = new string[set.Count];
        set.CopyTo(arr);
        return arr;
    }
}
