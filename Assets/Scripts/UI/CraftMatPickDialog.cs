using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// 材料列表对话框返回的一条候选项（[装备制作 S5]）
///
/// 与技能合成的材料列表不同：本系统的材料本身就是背包/仓库物品，
/// 点选后要把「哪一格、来自哪个容器」原样交回调用方（界面主控 S7），
/// 由调用方决定填哪个槽、填多少 —— 本脚本不做任何槽位判断。
///
/// 不合并：同一种材料在背包与仓库各有一叠时会生成两条
/// （来源不同，归还时要回到各自容器）。
/// </summary>
public struct CraftMatChoice
{
    /// <summary>材料模板（ItemSO，category == ItemCategory.Material）</summary>
    public ItemSO template;

    /// <summary>来源容器：false = 背包（PlayerItems），true = 仓库（WarehouseItems）</summary>
    public bool fromWarehouse;

    /// <summary>来源容器里的格子下标（背包/仓库的格子号，可直接喂给现有 GetPlayerItem / GetWarehouseItem）</summary>
    public int sourceIndex;

    /// <summary>点选那一刻该格的可填数量（= ItemInstance.stackSize）</summary>
    public int available;
}

/// <summary>
/// 材料列表对话框（[装备制作 S5]）—— 界面 Btn_list/tianchong_Btn 的填充入口。
///
/// 职责只有两件：
///   1. 自己收集候选：遍历背包 + 仓库，凡 category == Material 且 stackSize &gt; 0 的物品各生成一条
///      （背包按索引升序在前、仓库在后；同种材料跨容器不合并）；
///   2. 点选某条 → 关掉对话框并把这个 <see cref="CraftMatChoice"/> 回调交出去。
///
/// 不碰槽位、不碰暂存区、不做「填哪个槽」的判断 —— 那是界面主控（S7）的事。
/// 结构照抄技能合成的 Assets/Scripts/UI/CraftMatListDialog.cs（同构，但数据源与回调不同）：
///   对话框 / ItemContainer / Scroll View / Viewport / Content，itemContainer 留空时自动找。
/// 组件本体不由本步骤挂到场景 —— 由 saika 在编辑器搭对话框物体并拖 itemContainer / itemPrefab / closeBtn。
/// </summary>
public class CraftMatPickDialog : MonoBehaviour, IPanel
{
    // ============================================================
    // IPanel（显式实现，与 CraftMatListDialog 同款）
    // ============================================================

    PanelType IPanel.PanelType => PanelType.Dialog;
    bool IPanel.PauseGame => false;
    bool IPanel.LockInput => false;
    bool IPanel.ShowCursor => false;

    // ============================================================
    // 配置（Inspector）
    // ============================================================

    [SerializeField] private Transform itemContainer = null;
    [SerializeField] private Button itemPrefab = null;
    [SerializeField] private Button closeBtn = null;
    [SerializeField] private PanelManager panelManager = null;

    [Header("滚动窗口")]
    [Tooltip("列表所在的 ScrollRect（加了滚动窗口后用）：Rebuild 完刷布局并把滚动位置复位到顶部。留空 = 从 itemContainer 向下/向上自动找")]
    [SerializeField] private ScrollRect scrollRect = null;

    // ============================================================
    // 运行时状态
    // ============================================================

    /// <summary>已生成的列表项（每次 Rebuild 先全销毁再重建）</summary>
    private readonly List<Button> spawnedItems = new List<Button>();

    /// <summary>本次列表的候选（下标与 spawnedItems 一一对应，点选时按下标取）</summary>
    private readonly List<CraftMatChoice> candidates = new List<CraftMatChoice>();

    /// <summary>点选回调（每次 Show 覆盖；Hide 清空）</summary>
    private System.Action<CraftMatChoice> onPicked;

    /// <summary>来源文字：背包</summary>
    public const string SourceInventoryLabel = "背包";

    /// <summary>来源文字：仓库</summary>
    public const string SourceWarehouseLabel = "仓库";

    // ============================================================
    // 生命周期
    // ============================================================

    private void Awake()
    {
        if (panelManager == null) panelManager = PanelManager.Instance;
        closeBtn?.onClick.AddListener(Hide);
        ResolveScrollRect();
    }

    // ============================================================
    // 打开 / 关闭
    // ============================================================

    /// <summary>
    /// 打开对话框并现场收集候选列表。
    /// 候选自己收集（不从调用方传）：背包 → 仓库，过滤 category == Material 且 stackSize &gt; 0。
    /// </summary>
    public void Show(System.Action<CraftMatChoice> onPicked)
    {
        if (panelManager == null) panelManager = PanelManager.Instance;
        this.onPicked = onPicked;
        Rebuild();
        panelManager?.OpenPanel(gameObject);

        // 滚动复位必须等面板激活 + 本帧布局算完（见 ResetScrollAfterLayout 注释）
        StopAllCoroutines();
        if (isActiveAndEnabled)
            StartCoroutine(ResetScrollAfterLayout());
    }

    /// <summary>关闭对话框（关闭按钮与点选后都走这里）。回调在 Pick 里已先取走再调，清空不影响本次点选。</summary>
    public void Hide()
    {
        onPicked = null;
        panelManager?.ClosePanel(gameObject);
    }

    // ============================================================
    // 数据收集
    // ============================================================

    /// <summary>
    /// 现场收集候选：先背包（按索引升序）后仓库（按索引升序）。
    /// 同种材料在两个容器各有一叠 → 两条（不合并）。
    /// InventoryManager 缺失时 = 空列表（不报错）。
    /// </summary>
    private void CollectCandidates()
    {
        candidates.Clear();

        InventoryManager inventory = InventoryManager.Instance;
        if (inventory == null) return;

        CollectFrom(inventory.PlayerItems, false);
        CollectFrom(inventory.WarehouseItems, true);
    }

    /// <summary>从单个容器收集：跳过空格、template 为空、非材料、数量非正的条目</summary>
    private void CollectFrom(IReadOnlyList<ItemInstance> items, bool fromWarehouse)
    {
        if (items == null) return;

        for (int i = 0; i < items.Count; i++)
        {
            ItemInstance item = items[i];
            if (item == null || item.template == null) continue;
            if (item.template.category != ItemCategory.Material) continue;
            if (item.stackSize <= 0) continue;

            candidates.Add(new CraftMatChoice
            {
                template = item.template,
                fromWarehouse = fromWarehouse,
                sourceIndex = i,
                available = item.stackSize
            });
        }
    }

    // ============================================================
    // 列表重建
    // ============================================================

    private void Rebuild()
    {
        ClearItems();
        CollectCandidates();

        if (itemContainer == null || itemPrefab == null) return;

        Transform parent = ResolveItemParent();

        for (int i = 0; i < candidates.Count; i++)
        {
            int capturedIndex = i;
            CraftMatChoice choice = candidates[i];
            Button item = Instantiate(itemPrefab, parent);
            item.gameObject.SetActive(true);
            item.onClick.RemoveAllListeners();
            item.onClick.AddListener(() => Pick(capturedIndex));
            PopulateItem(item, choice);
            BindItemTooltip(item, choice.template);
            spawnedItems.Add(item);
        }

        // 滚动复位不在这里：此刻面板还是 inactive（Show 里 Rebuild 先于 OpenPanel），
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
    /// 列表项填值：第一个 TMP = 物品名，第二个 = 来源（背包/仓库），第三个 = 拥有量；
    /// 第二个 Image 的 sprite = icon（填法与 CraftMatListDialog 一致 —— 第一个 Image 是按钮底板）。
    /// </summary>
    private static void PopulateItem(Button item, CraftMatChoice choice)
    {
        if (item == null) return;

        TMP_Text[] labels = item.GetComponentsInChildren<TMP_Text>(true);
        if (labels.Length > 0) labels[0].text = choice.template != null ? choice.template.itemName : string.Empty;
        if (labels.Length > 1) labels[1].text = choice.fromWarehouse ? SourceWarehouseLabel : SourceInventoryLabel;
        if (labels.Length > 2) labels[2].text = choice.available.ToString();

        Image[] images = item.GetComponentsInChildren<Image>(true);
        if (images.Length > 1 && choice.template != null)
            images[1].sprite = choice.template.icon;
    }

    /// <summary>
    /// 列表项悬停显示物品详情（口径同 CraftMatListDialog 的 BindItemTooltip，但走物品 tooltip）。
    /// 项是运行时 Instantiate 出来的，每次 Rebuild 都是新实例，所以直接挂、不用幂等。
    /// </summary>
    private static void BindItemTooltip(Button item, ItemSO template)
    {
        if (item == null || template == null) return;

        var trigger = item.gameObject.AddComponent<EventTrigger>();
        var rect = (RectTransform)item.transform;

        var enter = new EventTrigger.Entry { eventID = EventTriggerType.PointerEnter };
        enter.callback.AddListener(_ => UITooltip.ShowItem(template, rect));
        trigger.triggers.Add(enter);

        var exit = new EventTrigger.Entry { eventID = EventTriggerType.PointerExit };
        exit.callback.AddListener(_ => UITooltip.Hide());
        trigger.triggers.Add(exit);
    }

    // ============================================================
    // 点选
    // ============================================================

    /// <summary>
    /// 点选第 index 条：先取走回调再 Hide（Hide 会清空 onPicked），最后把选择结果交出去。
    /// 下标非法直接返回（正常不会发生）。
    /// </summary>
    private void Pick(int index)
    {
        if (index < 0 || index >= candidates.Count) return;

        CraftMatChoice choice = candidates[index];
        System.Action<CraftMatChoice> callback = onPicked;
        Hide();
        callback?.Invoke(choice);
    }

    // ============================================================
    // 滚动窗口（照 CraftMatListDialog 的写法）
    // ============================================================

    /// <summary>
    /// 取 ScrollRect：优先 Inspector 拖的；留空则自动找。
    /// 实际结构 = 对话框/ItemContainer/Scroll View/Viewport/Content ——
    /// Scroll View 是 itemContainer 的**子物体**，所以先向下找；兼容「ScrollView 在外层」的结构再向上找。
    /// </summary>
    private void ResolveScrollRect()
    {
        if (scrollRect != null || itemContainer == null) return;
        scrollRect = itemContainer.GetComponentInChildren<ScrollRect>(true);
        if (scrollRect == null)
            scrollRect = itemContainer.GetComponentInParent<ScrollRect>(true);
    }

    /// <summary>
    /// 列表项的生成父级：配了滚动窗口就挂到 ScrollRect.content（Scroll View/Viewport/Content 那一层），
    /// 没配才退化成 itemContainer —— itemContainer 是外层的 ItemContainer 节点，
    /// 直接挂它会长在 Scroll View 旁边、不进滚动内容里。
    /// </summary>
    private Transform ResolveItemParent()
    {
        ResolveScrollRect();
        if (scrollRect != null && scrollRect.content != null)
            return scrollRect.content;
        return itemContainer;
    }

    /// <summary>
    /// 加了滚动窗口后：生成完先强制刷一次布局（Content 高度/滚动范围才是当前值），
    /// 再把滚动位置复位到顶部。顺序不能颠倒 —— 布局没刷新时写 verticalNormalizedPosition
    /// 会被随后的布局重算覆盖，表现为「下次打开停在上一轮的滚动位置」。
    /// </summary>
    private void ResetScrollToTop()
    {
        ResolveScrollRect();

        // 刷新布局的对象 = ScrollRect.content（内容层，挂了 GridLayoutGroup + ContentSizeFitter）；
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
    /// 面板打开后复位滚动位置。为什么要等：
    ///   ① Show 里 Rebuild 先于 OpenPanel，那时面板还是 inactive；
    ///   ② 面板刚激活那一帧，Content 的高度/滚动范围还没算；
    /// 这两步没走完就写 verticalNormalizedPosition，会被随后的布局重算覆盖，
    /// 表现就是「下次打开还停在上一轮回滚到的位置」。两次钉：布局提交后 + 高度确定后。
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
