using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>Coordinates material selection, recipe preview, and craft confirmation.</summary>
public class CraftUI : MonoBehaviour
{
    [SerializeField] private CombinationCraftSystem craftSystem;
    private SkillPool skillPool;
    [SerializeField] private Button slotLeft;
    [SerializeField] private Button slotRight;
    [SerializeField] private Image slotLeftIcon;
    [SerializeField] private Image slotRightIcon;
    [SerializeField] private TMP_Text slotLeftName;
    [SerializeField] private TMP_Text slotRightName;
    [SerializeField] private TMP_Text slotLeftLevel;
    [SerializeField] private TMP_Text slotRightLevel;
    [SerializeField] private TMP_Text slotLeftPlaceholder;
    [SerializeField] private TMP_Text slotRightPlaceholder;
    [SerializeField] private TMP_Text levelIndicator;
    [SerializeField] private Image previewIcon;
    [SerializeField] private TMP_Text previewName;
    [SerializeField] private TMP_Text previewDesc;
    [SerializeField] private TMP_Text previewStats;
    [SerializeField] private TMP_Text previewPlaceholder;
    [SerializeField] private Button craftBtn;
    [SerializeField] private CraftConfirmDialog confirmDialog;
    [SerializeField] private TMP_Text confirm_Mat1Text;
    [SerializeField] private TMP_Text confirm_Mat2Text;
    [SerializeField] private TMP_Text confirm_ResultText;
    [SerializeField] private Button confirm_ConfirmBtn;
    [SerializeField] private Button confirm_CancelBtn;
    [SerializeField] private CraftMatListDialog matListDialog;
    [SerializeField] private Button matListItemPrefab;
    [SerializeField] private Transform matListContainer;

    private readonly CombinationCraftSystem.MaterialInfo[] selectedMaterials = new CombinationCraftSystem.MaterialInfo[2];
    private readonly bool[] hasMaterial = new bool[2];
    private CombinationSkillData previewResult;

    private void Awake()
    {
        if (craftSystem == null)
        {
            PlayerController player = PlayerController.Instance;
            if (player != null)
            {
                craftSystem = player.GetComponent<CombinationCraftSystem>();
                skillPool = player.GetComponent<SkillPool>();
            }
        }
        slotLeft?.onClick.AddListener(() => OpenMaterialList(0));
        slotRight?.onClick.AddListener(() => OpenMaterialList(1));

        // 悬停材料槽显示该材料的技能详情(2026-09-22)。
        // 槽位点击是「打开材料列表」,悬停走 EventTrigger,不动点击。
        BindSlotTooltip(slotLeft, 0);
        BindSlotTooltip(slotRight, 1);

        // 预览区悬停显示合成结果详情(配方 + 效果),2026-09-22。
        // previewIcon 在配方无效时 enabled=false,Graphic 不接收射线,所以无效时自然不会弹。
        BindPreviewTooltip();
        craftBtn?.onClick.AddListener(OpenConfirmation);
        if (confirmDialog != null) confirmDialog.Hide();
        if (matListDialog != null) matListDialog.Hide();
    }

    private void OnEnable()
    {
        Refresh();
    }

    private void OpenMaterialList(int targetSlot)
    {
        if (craftSystem == null)
        {
            Debug.LogError("[CraftUI] craftSystem is null — CombinationCraftSystem not found on Player.", this);
            return;
        }
        if (matListDialog == null)
        {
            Debug.LogError("[CraftUI] matListDialog is null — Inspector field not assigned.", this);
            return;
        }
        // 第二槽打开时，排除第一槽已选技能树
        string exclude = null;
        if (targetSlot == 1 && hasMaterial[0])
            exclude = selectedMaterials[0].rootSkillName;
        var filtered = craftSystem.GetAvailableMaterials(exclude);
        matListDialog.Show(filtered, selectedIndex => {
            if (selectedIndex >= 0 && selectedIndex < filtered.Count)
                SelectMaterial(targetSlot, filtered[selectedIndex]);
        });
    }

    /// <summary>
    /// 绑定预览区悬停提示(幂等)。
    /// 挂点是预览区容器(ResultPreview),不是里面的 Icon(2026-09-22 定位):
    /// Icon 是纯 Image,Refresh() 会改它的 enabled,Graphic 一旦 disabled 就不参与射线,
    /// EventSystem 随即发 Exit —— 表现是「鼠标没动但 tip 刚出现就被关掉」。
    /// 容器是铺满预览区、常驻 raycastTarget=1 的背景图,命中稳定,语义也对(悬停整个预览区)。
    /// </summary>
    private void BindPreviewTooltip()
    {
        if (previewIcon == null) return;

        Transform host = previewIcon.transform.parent != null
            ? previewIcon.transform.parent
            : previewIcon.transform;
        if (host.GetComponent<EventTrigger>() != null) return;

        var trigger = host.gameObject.AddComponent<EventTrigger>();
        var rect = (RectTransform)host;

        var enter = new EventTrigger.Entry { eventID = EventTriggerType.PointerEnter };
        enter.callback.AddListener(_ => ShowPreviewTooltip(rect));
        trigger.triggers.Add(enter);

        var exit = new EventTrigger.Entry { eventID = EventTriggerType.PointerExit };
        exit.callback.AddListener(_ => UITooltip.Hide());
        trigger.triggers.Add(exit);
    }

    /// <summary>悬停合成结果预览:结果技能详情 + 配方(materialSkillA/B + 等级 → combinationLevel)</summary>
    private void ShowPreviewTooltip(RectTransform anchor)
    {
        if (previewResult == null) return;

        string a = previewResult.materialSkillA != null
            ? $"{previewResult.materialSkillA.skillName} Lv{previewResult.materialLevelA}"
            : "—";
        string b = previewResult.materialSkillB != null
            ? $"{previewResult.materialSkillB.skillName} Lv{previewResult.materialLevelB}"
            : "—";

        string tip = $"CD {previewResult.cooldown:0.##}s　MP {previewResult.manaCost:0.##}";
        if (!string.IsNullOrEmpty(previewResult.effectType)) tip += $"　{previewResult.effectType}";
        tip += $"\n配方 {a} + {b} → Lv{previewResult.combinationLevel}";

        UITooltip.ShowText(previewResult.skillName, previewResult.description, tip, anchor);
    }

    /// <summary>绑定材料槽悬停提示(幂等:已挂就不重复添加)</summary>
    private void BindSlotTooltip(Button button, int slotIndex)
    {
        if (button == null || button.GetComponent<EventTrigger>() != null) return;

        var trigger = button.gameObject.AddComponent<EventTrigger>();

        var enter = new EventTrigger.Entry { eventID = EventTriggerType.PointerEnter };
        enter.callback.AddListener(_ => ShowMaterialTooltip(slotIndex));
        trigger.triggers.Add(enter);

        var exit = new EventTrigger.Entry { eventID = EventTriggerType.PointerExit };
        exit.callback.AddListener(_ => UITooltip.Hide());
        trigger.triggers.Add(exit);
    }

    /// <summary>
    /// 悬停材料槽:材料本身就是「某个技能的某个等级」,所以直接复用技能 tip
    /// (名称 / 当前等级分支描述 / 分支名+冷却+消耗+伤害 / 技能点)。空槽不弹。
    /// </summary>
    private void ShowMaterialTooltip(int slotIndex)
    {
        if (selectedMaterials == null || slotIndex < 0 || slotIndex >= selectedMaterials.Length) return;

        CombinationCraftSystem.MaterialInfo material = selectedMaterials[slotIndex];
        if (material.skillData == null) return;

        UITooltip.ShowSkill(material.skillData, material.level,
            (RectTransform)(slotIndex == 0 ? slotLeft.transform : slotRight.transform));
    }

    private void SelectMaterial(int targetSlot, CombinationCraftSystem.MaterialInfo material)
    {
        selectedMaterials[targetSlot] = material;
        hasMaterial[targetSlot] = true;
        Refresh();
    }

    private void Refresh()
    {
        SetMaterialSlot(0, slotLeftIcon, slotLeftName, slotLeftLevel, slotLeftPlaceholder);
        SetMaterialSlot(1, slotRightIcon, slotRightName, slotRightLevel, slotRightPlaceholder);

        bool valid = false;
        string failReason = null;
        if (hasMaterial[0] && hasMaterial[1] && craftSystem != null)
            valid = craftSystem.ValidateRecipe(selectedMaterials[0], selectedMaterials[1], out previewResult, out failReason);
        else
            previewResult = null;

        if (levelIndicator != null)
        {
            // 校验失败 → 显示原因（如"已有技能，不可重复合成"）
            if (hasMaterial[0] && hasMaterial[1] && !string.IsNullOrEmpty(failReason))
            {
                levelIndicator.text = failReason;
                levelIndicator.color = UIConstants.ConflictRed;
            }
            else
            {
                levelIndicator.text = hasMaterial[0] && hasMaterial[1]
                    ? $"材料等级: Lv{Mathf.Min(selectedMaterials[0].level, selectedMaterials[1].level)}"
                    : string.Empty;
                levelIndicator.color = valid || string.IsNullOrEmpty(failReason) ? Color.white : UIConstants.ConflictRed;
            }
        }

        SetPreview(valid);
        if (craftBtn != null) craftBtn.interactable = valid;
    }

    private void SetMaterialSlot(int index, Image icon, TMP_Text nameText, TMP_Text levelText, TMP_Text placeholder)
    {
        bool present = hasMaterial[index];
        var material = selectedMaterials[index];
        if (icon != null)
        {
            icon.enabled = present && material.skillData != null;
            if (present && material.skillData != null)
            {
                var active = material.skillData as ActiveSkillData;
                icon.sprite = active != null ? active.GetIconForLevel(material.level) : material.skillData.icon;
            }
            else
            {
                icon.sprite = null;
            }
            icon.color = present && material.isWeaponSkill ? UIConstants.WeaponIconBlue : UIConstants.ActiveIconGold;
        }
        if (nameText != null) nameText.text = present ? material.skillName : string.Empty;
        if (levelText != null) levelText.text = present ? material.isWeaponSkill ? "武器" : $"Lv{material.level}" : string.Empty;
        if (placeholder != null) placeholder.gameObject.SetActive(!present);
    }

    private void SetPreview(bool valid)
    {
        if (previewIcon != null)
        {
            previewIcon.enabled = valid;
            previewIcon.sprite = valid ? previewResult.icon : null;
            previewIcon.color = UIConstants.ComboIconPurple;
        }
        if (previewName != null) previewName.text = valid ? previewResult.skillName : string.Empty;
        if (previewDesc != null) previewDesc.text = valid ? previewResult.description : string.Empty;
        if (previewStats != null) previewStats.text = valid ? $"CD: {previewResult.cooldown:0.##}s | MP: {previewResult.manaCost:0.##}" : string.Empty;
        if (previewPlaceholder != null) previewPlaceholder.gameObject.SetActive(!valid);
    }

    private void OpenConfirmation()
    {
        if (previewResult == null || confirmDialog == null) return;
        string mat1 = FormatMaterial(selectedMaterials[0]);
        string mat2 = FormatMaterial(selectedMaterials[1]);
        string result = $"产出: {previewResult.skillName}";
        if (confirm_Mat1Text != null) confirm_Mat1Text.text = mat1;
        if (confirm_Mat2Text != null) confirm_Mat2Text.text = mat2;
        if (confirm_ResultText != null) confirm_ResultText.text = result;
        if (confirm_ConfirmBtn != null) confirm_ConfirmBtn.interactable = true;
        if (confirm_CancelBtn != null) confirm_CancelBtn.interactable = true;
        confirmDialog.Show(mat1, mat2, result, ConfirmCraft);
    }

    private void ConfirmCraft()
    {
        if (!craftSystem.Craft(selectedMaterials[0], selectedMaterials[1])) return;
        hasMaterial[0] = false;
        hasMaterial[1] = false;
        previewResult = null;
        Refresh();
    }

    private static string FormatMaterial(CombinationCraftSystem.MaterialInfo material) =>
        material.isWeaponSkill ? $"{material.skillName} [武器]" : $"{material.skillName} Lv{material.level}";
}
