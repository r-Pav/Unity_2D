using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 配方列表（[装备制作 S6]）—— Description 页的配方列表容器。
///
/// 职责：
///   1. 拿到配方数据（默认启动时 Resources.LoadAll&lt;CraftRecipeSO&gt;("Recipes") 全量读，
///      与 CombinationCraftSystem 读 Skills/Combo 同口径；也可由 S7 用 SetRecipes 显式喂）；
///   2. Rebuild：清旧项 → 只留 IsValid() 的配方 → 按资产名升序 → 逐条 Instantiate 列表项并填值；
///   3. 列表项被点 → 通过 OnRecipePicked 把那条配方交出去（S7 接，载入合成界面）。
///
/// 本组件**不做显隐控制**（Description 页的开关由 S7 主控），也不依赖任何面板管理器；
/// 只做「给一批配方 → 生成列表」这一件事，拆成 SetRecipes + Rebuild 两步是为了可测
/// （Resources.LoadAll 在离线自测里跑不了）。
///
/// 界面物体现状：Description 下已有 Scroll View(Viewport/Scrollbar Vertical)，
/// 但 Viewport 里的 Content 这层 saika 还没搭 —— 所以 itemContainer 允许留空：
/// 留空（或 entryPrefab 没接线）时只清空列表、不生成、不报错、不打红字。
/// </summary>
public class CraftRecipeListView : MonoBehaviour
{
    /// <summary>配方资产的 Resources 目录（Assets/Resources/Recipes/）</summary>
    public const string RecipesResourcePath = "Recipes";

    [Tooltip("列表项父级 —— 留空 = 不生成（Description 的 Content 还没搭时就是这种状态）")]
    [SerializeField] private Transform itemContainer = null;

    [Tooltip("列表项模板 prefab（Assets/Prefab/UI/Item_description.prefab，元素由 saika 搭）")]
    [SerializeField] private CraftRecipeListEntry entryPrefab = null;

    [Header("滚动窗口")]
    [Tooltip("列表所在的 ScrollRect：Rebuild 完刷布局并把滚动位置复位到顶部。留空 = 从 itemContainer 向下/向上自动找")]
    [SerializeField] private ScrollRect scrollRect = null;

    /// <summary>点选回调 —— 点列表里某一条时把那条配方交出去（S7 接）</summary>
    public System.Action<CraftRecipeSO> OnRecipePicked;

    // ============================================================
    // 运行时状态
    // ============================================================

    /// <summary>当前这一批配方（SetRecipes 存的引用清单；过滤与排序在 Rebuild 里做）</summary>
    private readonly List<CraftRecipeSO> allRecipes = new List<CraftRecipeSO>();

    /// <summary>已生成的列表项（每次 Rebuild 先全销毁再重建）</summary>
    private readonly List<CraftRecipeListEntry> spawnedEntries = new List<CraftRecipeListEntry>();

    /// <summary>外部是否显式喂过配方（决定 Awake 要不要自己读 Resources）</summary>
    private bool recipesSupplied;

    // ============================================================
    // 生命周期
    // ============================================================

    /// <summary>
    /// 启动时按全量读资产填充列表（与 CombinationCraftSystem 读 Skills/Combo 同口径）。
    /// 兜一层：外部若已显式 SetRecipes（S7 在自己的 Awake 里喂数据、两个 Awake 的相对顺序不确定），
    /// 就尊重外部数据、不再用 Resources.LoadAll 覆盖它。
    /// 目录为空 / 没有配方资产时 = 空列表，不报错、不打红字。
    /// </summary>
    private void Awake()
    {
        if (recipesSupplied)
        {
            Rebuild();
            return;
        }

        SetRecipes(Resources.LoadAll<CraftRecipeSO>(RecipesResourcePath));
    }

    // ============================================================
    // 数据入口
    // ============================================================

    /// <summary>
    /// 换一批配方并立即重建列表（S7 显式喂数据时用；null = 空列表）。
    /// 只存引用，过滤（IsValid）与排序都留到 Rebuild —— 改一条配方的 IsValid 只需再 Rebuild 一次。
    /// </summary>
    public void SetRecipes(IReadOnlyList<CraftRecipeSO> recipes)
    {
        recipesSupplied = true;

        allRecipes.Clear();
        if (recipes != null)
        {
            for (int i = 0; i < recipes.Count; i++)
                allRecipes.Add(recipes[i]);
        }

        Rebuild();
    }

    // ============================================================
    // 列表重建
    // ============================================================

    /// <summary>
    /// 重建列表：清旧项 → 过滤掉 IsValid() 为 false 的配方 → 按资产名（recipe.name）升序 → 逐条生成并填值。
    /// itemContainer / entryPrefab 没接线时只清空（不生成、不报错、不打红字）。
    /// </summary>
    public void Rebuild()
    {
        ClearEntries();

        if (itemContainer == null || entryPrefab == null) return;

        Transform parent = ResolveItemParent();
        if (parent == null) return;

        List<CraftRecipeSO> recipes = CollectValidRecipes();

        for (int i = 0; i < recipes.Count; i++)
        {
            CraftRecipeListEntry entry = Instantiate(entryPrefab, parent);
            if (entry == null) continue;

            entry.gameObject.SetActive(true); // 模板 prefab 可能是 inactive 的「样板行」
            entry.Setup(recipes[i], HandleRecipePicked);
            spawnedEntries.Add(entry);
        }

        ResetScrollAfterRebuild();
    }

    /// <summary>清掉已生成的列表项（Destroy 由 Unity 在帧末执行，故同时清 spawnedEntries 记录）</summary>
    private void ClearEntries()
    {
        for (int i = 0; i < spawnedEntries.Count; i++)
        {
            CraftRecipeListEntry entry = spawnedEntries[i];
            if (entry != null) Destroy(entry.gameObject);
        }
        spawnedEntries.Clear();
    }

    /// <summary>
    /// 取有效配方并按资产名升序。
    /// 口径 3：列表是纯展示页 —— 只按 IsValid() 过滤无效配方（产物为空 / 一条材料都没有），
    /// 不看材料是否齐、也不做可点性过滤。
    /// </summary>
    private List<CraftRecipeSO> CollectValidRecipes()
    {
        var valid = new List<CraftRecipeSO>(allRecipes.Count);

        for (int i = 0; i < allRecipes.Count; i++)
        {
            CraftRecipeSO recipe = allRecipes[i];
            if (recipe == null) continue;
            if (!recipe.IsValid()) continue;
            valid.Add(recipe);
        }

        // Ordinal：不随系统语言/区域变化，排序结果稳定可复现
        valid.Sort(CompareByName);
        return valid;
    }

    /// <summary>按资产名升序（Ordinal 比较）</summary>
    private static int CompareByName(CraftRecipeSO a, CraftRecipeSO b)
    {
        return string.CompareOrdinal(a.name, b.name);
    }

    /// <summary>列表项点选 → 转发给 OnRecipePicked（S7 接）</summary>
    private void HandleRecipePicked(CraftRecipeSO recipe)
    {
        OnRecipePicked?.Invoke(recipe);
    }

    // ============================================================
    // 滚动窗口（照 CraftMatPickDialog / CraftMatListDialog 的写法）
    // ============================================================

    /// <summary>
    /// 取 ScrollRect：优先 Inspector 拖的；留空则自动找。
    /// 实际结构 = Description/Scroll View/Viewport/Content —— 列表项挂在 Content 下，
    /// 所以「itemContainer 在外层、Scroll View 是它的子物体」时先向下找；兼容「ScrollView 在外层」的结构再向上找。
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
    /// 没配才退化成 itemContainer —— 直接挂外层 ItemContainer 会长在 Scroll View 旁边、不进滚动内容里。
    /// </summary>
    private Transform ResolveItemParent()
    {
        ResolveScrollRect();
        if (scrollRect != null && scrollRect.content != null)
            return scrollRect.content;
        return itemContainer;
    }

    /// <summary>
    /// Rebuild 收尾：当场刷一次布局 + 滚动复位；面板已激活时再钉两次（照 CraftMatPickDialog.Show 的顺序）。
    /// 为什么要等：面板刚激活那一帧 Content 的高度/滚动范围还没算，此时写 verticalNormalizedPosition
    /// 会被随后的布局重算覆盖，表现就是「下次打开停在上一轮的滚动位置」。
    /// 本组件不控显隐 —— 若 S7 是在 Rebuild 之后才打开页面，打开后再调一次 Rebuild 即可。
    /// </summary>
    private void ResetScrollAfterRebuild()
    {
        ResetScrollToTop();

        if (!isActiveAndEnabled) return; // 面板没激活，协程起不来（起协程也等不到布局）

        StopAllCoroutines(); // 连续 Rebuild 时别叠协程（复位本身幂等）
        StartCoroutine(ResetScrollAfterLayout());
    }

    /// <summary>
    /// 强制刷一次布局（Content 高度/滚动范围才是当前值）再把滚动位置复位到顶部。
    /// 顺序不能颠倒 —— 布局没刷新时写 verticalNormalizedPosition 会被随后的布局重算覆盖。
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

    /// <summary>面板激活后复位滚动位置：等一帧（布局提交后）+ 再等一帧（高度确定后）各钉一次。</summary>
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
