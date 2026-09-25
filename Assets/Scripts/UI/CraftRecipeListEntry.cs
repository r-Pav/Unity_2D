using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// 配方列表项（[装备制作 S6]）—— Description 页配方列表里的一行，
/// 由 <see cref="CraftRecipeListView"/> 在运行时按当前配方数据 Instantiate 出来（不做编辑器逐个预摆）。
///
/// 职责只有两件：
///   1. 显示这一条配方：产物图标 + 「产物名 x产出数量」+ 需求材料文本；
///   2. 点整行 → 把这条 <see cref="CraftRecipeSO"/> 回调给列表容器（再由容器交给界面主控 S7）。
///
/// 本组件不碰背包/仓库、不判材料是否齐（口径 3：列表是纯展示页），也不控显隐（Description 页的开关是 S7 的事）。
///
/// 显示元素由 saika 在 Assets/Prefab/UI/Item_description.prefab 上搭好后拖进下面四个槽：
/// 槽全空时本组件什么都不显示、不报错（元素还没搭的中间态也能编译进场景）。
/// selectBtn 留空 → 退化成用本物体上的 Button；两者都没有 = 这一行点了不响应。
/// </summary>
public class CraftRecipeListEntry : MonoBehaviour
{
    // ============================================================
    // 文案口径（公开常量 + 静态格式化方法，S7 的产物预览要显示同一套文案时直接复用）
    // ============================================================

    /// <summary>名称与数量之间的连接符（如「铁剑 x1」）</summary>
    public const string CountSuffix = " x";

    /// <summary>多条材料之间的连接符 —— 一条一行（材料多时也能一眼看全，不用横向挤）</summary>
    public const string MaterialSeparator = "\n";

    // ============================================================
    // 配置（Inspector，由 saika 拖）
    // ============================================================

    [Tooltip("产物图标 —— 填 recipe.resultItem.icon")]
    [SerializeField] private Image resultIcon = null;

    [Tooltip("产物名称文本 —— 填「产物名 x产出数量」，如「铁剑 x1」")]
    [SerializeField] private TMP_Text resultName = null;

    [Tooltip("需求材料文本 —— 每条「材料名 x数量」，多条之间换行；空材料槽自动跳过")]
    [SerializeField] private TMP_Text materialsText = null;

    [Tooltip("整行的点选按钮 —— 留空则用本物体上的 Button；再没有 = 这一行不可以点")]
    [SerializeField] private Button selectBtn = null;

    // ============================================================
    // 运行时状态
    // ============================================================

    /// <summary>点选回调（每次 Setup 覆盖；Setup 里先摘旧监听再挂新的 —— 列表项会复用）</summary>
    private System.Action<CraftRecipeSO> onPick;

    /// <summary>悬停用的 EventTrigger（同一个实例可能被 Setup 多次，只加一次组件、每次换内容）</summary>
    private EventTrigger tooltipTrigger;

    // ============================================================
    // 填值 / 点选
    // ============================================================

    /// <summary>
    /// 用一条配方填值并接管点选（列表项复用：每次都先 RemoveAllListeners 再挂）。
    /// recipe 为 null（或产物为空）时清空显示、摘掉点选后安全返回，不留上一次的残留。
    /// </summary>
    public void Setup(CraftRecipeSO recipe, System.Action<CraftRecipeSO> onPick)
    {
        this.onPick = onPick;

        // 先接管点选（recipe 为 null 时这一步顺带把旧监听摘干净）
        BindSelectButton(recipe);

        ItemSO result = recipe != null ? recipe.resultItem : null;

        if (resultName != null)
            resultName.text = recipe != null ? BuildResultLabel(recipe) : string.Empty;

        if (materialsText != null)
            materialsText.text = recipe != null ? BuildMaterialsLabel(recipe) : string.Empty;

        if (resultIcon != null)
            resultIcon.sprite = result != null ? result.icon : null;

        // 悬停提示跟着产物走（产物为空 = 没有可提示的物品）
        BindTooltip(result);
    }

    /// <summary>
    /// 挂点选监听。每次都先 RemoveAllListeners（列表项会复用，旧监听的闭包里是上一条配方）。
    /// selectBtn 留空时用自己所在物体的 Button；都没有就不响应点击。
    /// </summary>
    private void BindSelectButton(CraftRecipeSO recipe)
    {
        Button button = selectBtn != null ? selectBtn : GetComponent<Button>();
        if (button == null) return;

        button.onClick.RemoveAllListeners();
        if (recipe == null) return; // 没配方就不挂监听（点了也不该回调出 null）

        button.onClick.AddListener(() => Pick(recipe));
    }

    /// <summary>点选：回调先取走再调（同 CraftMatPickDialog.Pick 的写法，回调里改字段不影响本次）</summary>
    private void Pick(CraftRecipeSO recipe)
    {
        System.Action<CraftRecipeSO> callback = onPick;
        callback?.Invoke(recipe);
    }

    // ============================================================
    // 悬停提示
    // ============================================================

    /// <summary>
    /// 悬停显示产物的物品提示（口径同 CraftMatListDialog.BindItemTooltip，但走物品 tooltip）。
    /// 列表项会被复用（同一实例可能 Setup 多次），所以 EventTrigger 只加一次、每次只换内容，
    /// 不会叠出两个 trigger 把上一条配方的提示一起弹出来。
    /// </summary>
    private void BindTooltip(ItemSO result)
    {
        if (tooltipTrigger == null)
        {
            tooltipTrigger = GetComponent<EventTrigger>();
            if (tooltipTrigger == null)
                tooltipTrigger = gameObject.AddComponent<EventTrigger>();
        }

        if (tooltipTrigger == null) return;

        tooltipTrigger.triggers.Clear();
        if (result == null) return; // 产物为空：没有可提示的物品

        // 列表项是 UI 物体，锚点就是自己的 RectTransform（拿不到就不挂，不抛异常）
        var rect = transform as RectTransform;
        if (rect == null) return;

        var enter = new EventTrigger.Entry { eventID = EventTriggerType.PointerEnter };
        enter.callback.AddListener(_ => UITooltip.ShowItem(result, rect));
        tooltipTrigger.triggers.Add(enter);

        var exit = new EventTrigger.Entry { eventID = EventTriggerType.PointerExit };
        exit.callback.AddListener(_ => UITooltip.Hide());
        tooltipTrigger.triggers.Add(exit);
    }

    // ============================================================
    // 文本口径（静态，供列表项与 S7 复用）
    // ============================================================

    /// <summary>产物文本：「产物名 x产出数量」，如「铁剑 x1」。产物为空时只留数量，不抛异常。</summary>
    public static string BuildResultLabel(CraftRecipeSO recipe)
    {
        if (recipe == null) return string.Empty;

        string productName = recipe.resultItem != null ? recipe.resultItem.itemName : null;
        return (productName ?? string.Empty) + CountSuffix + recipe.resultCount;
    }

    /// <summary>
    /// 材料文本：用 recipe.GetValidMaterials() 口径（item 非空即算一条，空材料槽跳过），
    /// 每条「材料名 x数量」、多条之间换行。一条都没有时返回空串。
    /// </summary>
    public static string BuildMaterialsLabel(CraftRecipeSO recipe)
    {
        if (recipe == null) return string.Empty;

        List<CraftMaterial> materials = recipe.GetValidMaterials();
        if (materials == null || materials.Count == 0) return string.Empty;

        var lines = new List<string>(materials.Count);
        for (int i = 0; i < materials.Count; i++)
        {
            string materialName = materials[i].item != null ? materials[i].item.itemName : null;
            lines.Add((materialName ?? string.Empty) + CountSuffix + materials[i].count);
        }
        return string.Join(MaterialSeparator, lines);
    }
}
