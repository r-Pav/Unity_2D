using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 元素模块（MonoBehaviour，挂 Player）— 元素解锁与切换的唯一入口（决策 D17）。
/// 对外只暴露：CurrentElement / UnlockedElements / UnlockElement / SetElement / IsUnlocked，
/// 内部实现细节不暴露。
///
/// 切换语义（决策 N5）：SetElement 战斗中即时生效；伤害实例按触发时刻读取 CurrentElement，
/// 不做释放时快照。未解锁的元素不可选（SetElement 直接忽略）。
///
/// 解锁入口（手册 B3）：章节推进 → 关卡流程接入后由外部（UI / SaveSystem 读档）调用 UnlockElement；
/// 本阶段不接 UI，测试可用代码 / 临时入口调用。
/// </summary>
public class ElementModule : MonoBehaviour
{
    // ============================================================
    // 运行时状态
    // ============================================================

    /// <summary>当前选中元素（默认 None = 无元素）。序列化支持 Inspector 预填（测试期临时选 Fire 等直接生效；运行时由 SetElement 切换）</summary>
    [Tooltip("当前选中元素（测试可临时在 Inspector 选择；运行时由 SetElement 切换；未在解锁列表的元素直接预填也生效）")]
    [SerializeField] private ElementType _currentElement = ElementType.None;

    public ElementType CurrentElement => _currentElement;

    /// <summary>已解锁元素列表（Inspector 可预填；运行时由 UnlockElement 追加）</summary>
    [Tooltip("已解锁元素列表（None 恒视为已解锁；测试可临时在 Inspector 勾选）")]
    [SerializeField] private List<ElementType> unlockedElements = new List<ElementType>();

    /// <summary>已解锁元素（只读视图，供 UI / 存档读取）</summary>
    public IReadOnlyList<ElementType> UnlockedElements => unlockedElements;

    // ============================================================
    // 公开接口
    // ============================================================

    /// <summary>解锁元素（幂等：重复解锁忽略）。关卡推进 / 存档恢复调用。</summary>
    public void UnlockElement(ElementType element)
    {
        if (element == ElementType.None) return;               // None 恒可用，无需入列表
        if (unlockedElements.Contains(element)) return;
        unlockedElements.Add(element);
    }

    /// <summary>
    /// 切换当前元素。未解锁不可选（忽略调用）；切换到相同元素忽略；
    /// 切换成功触发 ElementChangedEvent（UI 指示器等订阅）。
    /// </summary>
    public void SetElement(ElementType element)
    {
        if (element != ElementType.None && !IsUnlocked(element)) return;
        if (CurrentElement == element) return;

        ElementType old = CurrentElement;
        _currentElement = element;
        EventBus.Trigger(new ElementChangedEvent(old, element));
    }

    /// <summary>指定元素是否已解锁（None 恒为 true）</summary>
    public bool IsUnlocked(ElementType element)
    {
        return element == ElementType.None || unlockedElements.Contains(element);
    }
}
