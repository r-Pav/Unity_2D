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

    private readonly List<Button> spawnedItems = new List<Button>();
    private System.Action<int> onMaterialSelected;

    private void Awake()
    {
        if (panelManager == null) panelManager = PanelManager.Instance;
        closeBtn?.onClick.AddListener(Hide);
    }

    public void Show(IReadOnlyList<CombinationCraftSystem.MaterialInfo> materials, System.Action<int> callback)
    {
        if (panelManager == null) panelManager = PanelManager.Instance;
        onMaterialSelected = callback;
        Rebuild(materials);
        panelManager?.OpenPanel(gameObject);
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

        for (int i = 0; i < materials.Count; i++)
        {
            int capturedIndex = i;
            CombinationCraftSystem.MaterialInfo material = materials[i];
            Button item = Instantiate(itemPrefab, itemContainer);
            item.gameObject.SetActive(true);
            item.onClick.RemoveAllListeners();
            item.onClick.AddListener(() => Select(capturedIndex));
            PopulateItem(item, material);
            BindItemTooltip(item, material);
            spawnedItems.Add(item);
        }
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
}
