using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>Builds a selectable list from the current craft material pool.</summary>
public class CraftMatListDialog : MonoBehaviour, IPanel
{
    PanelType IPanel.PanelType => PanelType.Dialog;
    bool IPanel.PauseGame => false;
    bool IPanel.LockInput => false;
    bool IPanel.ShowCursor => false;

    [SerializeField] private Transform itemContainer;
    [SerializeField] private Button itemPrefab;
    [SerializeField] private Button closeBtn;
    [SerializeField] private PanelManager panelManager;

    [Header("滚动窗口")]
    [Tooltip("列表所在的 ScrollRect(加了滚动窗口后用):Rebuild 完刷布局并把滚动位置复位到顶部。留空 = 从 itemContainer 向上自动找")]
    [SerializeField] private ScrollRect scrollRect;

    private readonly List<Button> spawnedItems = new List<Button>();
    private System.Action<int> onMaterialSelected;

    private void Awake()
    {
        if (panelManager == null) panelManager = PanelManager.Instance;
        closeBtn?.onClick.AddListener(Hide);
        ResolveScrollRect();
    }

    /// <summary>
    /// 取 ScrollRect:优先 Inspector 拖的;留空则自动找。
    /// 实际结构 = CraftMatListDialog/ItemContainer/Scroll View/Viewport/Content ——
    /// Scroll View 是 itemContainer 的**子物体**,所以先向下找;兼容"ScrollView 在外层"的结构再向上找。
    /// </summary>
    private void ResolveScrollRect()
    {
        if (scrollRect != null || itemContainer == null) return;
        scrollRect = itemContainer.GetComponentInChildren<ScrollRect>(true);
        if (scrollRect == null)
            scrollRect = itemContainer.GetComponentInParent<ScrollRect>(true);
    }

    /// <summary>
    /// 列表项的生成父级:配了滚动窗口就挂到 ScrollRect.content(实际是 Scroll View/Viewport/Content 那一层),
    /// 没配才退化成 itemContainer —— itemContainer 是外层的 ItemContainer 节点,
    /// 直接挂它会长在 Scroll View 旁边、不进滚动内容里。
    /// </summary>
    private Transform ResolveItemParent()
    {
        ResolveScrollRect();
        if (scrollRect != null && scrollRect.content != null)
            return scrollRect.content;
        return itemContainer;
    }

    public void Show(IReadOnlyList<CombinationCraftSystem.MaterialInfo> materials, System.Action<int> callback)
    {
        if (panelManager == null) panelManager = PanelManager.Instance;
        onMaterialSelected = callback;
        Rebuild(materials);
        panelManager?.OpenPanel(gameObject);

        // 滚动复位必须等面板激活 + 本帧布局算完(见 ResetScrollAfterLayout 注释)
        StopAllCoroutines();
        if (isActiveAndEnabled)
            StartCoroutine(ResetScrollAfterLayout());
    }

    public void Hide()
    {
        onMaterialSelected = null;
        panelManager?.ClosePanel(gameObject);
    }

    private void Rebuild(IReadOnlyList<CombinationCraftSystem.MaterialInfo> materials)
    {
        ClearItems();
        if (itemContainer == null || itemPrefab == null || materials == null) return;

        Transform parent = ResolveItemParent();

        for (int i = 0; i < materials.Count; i++)
        {
            int capturedIndex = i;
            CombinationCraftSystem.MaterialInfo material = materials[i];
            Button item = Instantiate(itemPrefab, parent);
            item.gameObject.SetActive(true);
            item.onClick.RemoveAllListeners();
            item.onClick.AddListener(() => Select(capturedIndex));
            PopulateItem(item, material);
            BindItemTooltip(item, material);
            spawnedItems.Add(item);
        }

        // 滚动复位不在这里:此刻面板还是 inactive(Show 里 Rebuild 先于 OpenPanel),
        // 写 verticalNormalizedPosition 会被随后的布局重算覆盖 —— 见 Show 里的协程
    }

    private void ClearItems()
    {
        foreach (Button item in spawnedItems)
        {
            if (item != null) Destroy(item.gameObject);
        }
        spawnedItems.Clear();
    }

    /// <summary>
    /// 列表项悬停显示材料详情(2026-09-22)。项是运行时 Instantiate 出来的,
    /// 每次 Rebuild 都是新实例,所以直接挂、不用幂等;点击(选中该材料)不受影响。
    /// </summary>
    private static void BindItemTooltip(Button item, CombinationCraftSystem.MaterialInfo material)
    {
        if (item == null) return;

        var trigger = item.gameObject.AddComponent<EventTrigger>();
        var rect = (RectTransform)item.transform;

        var enter = new EventTrigger.Entry { eventID = EventTriggerType.PointerEnter };
        enter.callback.AddListener(_ =>
        {
            if (material.skillData != null)
                UITooltip.ShowSkill(material.skillData, material.level, rect);
        });
        trigger.triggers.Add(enter);

        var exit = new EventTrigger.Entry { eventID = EventTriggerType.PointerExit };
        exit.callback.AddListener(_ => UITooltip.Hide());
        trigger.triggers.Add(exit);
    }

    private static void PopulateItem(Button item, CombinationCraftSystem.MaterialInfo material)
    {
        TMP_Text[] labels = item.GetComponentsInChildren<TMP_Text>(true);
        if (labels.Length > 0) labels[0].text = material.skillName;
        if (labels.Length > 1) labels[1].text = material.isWeaponSkill ? "武器" : $"Lv{material.level}";
        if (labels.Length > 2) labels[2].text = material.isWeaponSkill ? "武器" : "主动";

        Image[] images = item.GetComponentsInChildren<Image>(true);
        if (images.Length > 1 && material.skillData != null)
            images[1].sprite = material.skillData.icon;
    }

    private void Select(int index)
    {
        System.Action<int> callback = onMaterialSelected;
        Hide();
        callback?.Invoke(index);
    }

    /// <summary>
    /// 加了滚动窗口后:生成完先强制刷一次布局(Content 高度/滚动范围才是当前值),
    /// 再把滚动位置复位到顶部。顺序不能颠倒 —— 布局没刷新时写 verticalNormalizedPosition
    /// 会被随后的布局重算覆盖,表现为"下次打开停在上一轮的滚动位置"。
    /// </summary>
    private void ResetScrollToTop()
    {
        ResolveScrollRect();

        // 刷新布局的对象 = ScrollRect.content(内容层,挂了 GridLayoutGroup + ContentSizeFitter);
        // 没有滚动窗口时才刷 itemContainer
        Transform content = scrollRect != null && scrollRect.content != null
            ? (Transform)scrollRect.content
            : itemContainer;
        if (content is RectTransform contentRect)
            LayoutRebuilder.ForceRebuildLayoutImmediate(contentRect);

        if (scrollRect != null)
            scrollRect.verticalNormalizedPosition = 1f;
    }

    /// <summary>
    /// 面板打开后复位滚动位置。为什么要等:
    ///   ① Show 里 Rebuild 先于 OpenPanel,那时面板还是 inactive;
    ///   ② 面板刚激活那一帧,Content 的高度/滚动范围还没算;
    /// 这两步没走完就写 verticalNormalizedPosition,会被随后的布局重算覆盖,
    /// 表现就是"下次打开还停在上一轮回滚到的位置"。两次钉:布局提交后 + 高度确定后。
    /// </summary>
    private System.Collections.IEnumerator ResetScrollAfterLayout()
    {
        yield return null;
        ResetScrollToTop();
        yield return null;
        ResolveScrollRect();
        if (scrollRect != null)
            scrollRect.verticalNormalizedPosition = 1f;
    }
}
