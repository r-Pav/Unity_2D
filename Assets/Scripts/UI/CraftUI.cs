using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>Coordinates material selection, recipe preview, and craft confirmation.</summary>
public class CraftUI : MonoBehaviour, IPanel
{
    PanelType IPanel.PanelType => PanelType.FullScreen;
    bool IPanel.PauseGame => true;
    bool IPanel.LockInput => true;
    bool IPanel.ShowCursor => true;

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
            levelIndicator.text = hasMaterial[0] && hasMaterial[1]
                ? $"材料等级: Lv{Mathf.Min(selectedMaterials[0].level, selectedMaterials[1].level)}"
                : string.Empty;
            levelIndicator.color = valid || string.IsNullOrEmpty(failReason) ? Color.white : UIConstants.ConflictRed;
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
