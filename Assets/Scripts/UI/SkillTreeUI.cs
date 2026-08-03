using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>Binds Q/E skill-tree nodes and refreshes them from skill events.</summary>
public class SkillTreeUI : MonoBehaviour, IPanel
{
    PanelType IPanel.PanelType => PanelType.FullScreen;
    bool IPanel.PauseGame => true;
    bool IPanel.LockInput => true;
    bool IPanel.ShowCursor => true;

    [SerializeField] private SkillManager skillManager;
    [SerializeField] private BranchUpgradeSystem branchSystem;
    [SerializeField] private SkillPointManager skillPointManager;
    [SerializeField] private TMP_Text skillPointLabel;
    [SerializeField] private Button[] nodeButtons = new Button[10];
    [SerializeField] private Image[] nodeIcons = new Image[10];
    [SerializeField] private TMP_Text[] nodeNames = new TMP_Text[10];
    [SerializeField] private TMP_Text[] nodeLevels = new TMP_Text[10];
    [SerializeField] private TMP_Text[] nodeCostBadges = new TMP_Text[10];
    [SerializeField] private Image[] nodeBranchMasks = new Image[10];
    [SerializeField] private Image[] nodeGlows = new Image[10];
    [SerializeField] private Image[] connectorLines;
    [SerializeField] private BranchChoiceDialog branchChoiceDialog;
    [SerializeField] private GameObject dialog_LeftCard;
    [SerializeField] private GameObject dialog_RightCard;
    [SerializeField] private TMP_Text[] dialog_Lv2Info;
    [SerializeField] private TMP_Text[] dialog_Lv3Info;
    [SerializeField] private Button dialog_ConfirmBtn;
    [SerializeField] private Button dialog_CloseBtn;

    [Header("页面跳转")]
    [SerializeField] private Button toCraftBtn;
    [SerializeField] private Button toPassiveBtn;
    [SerializeField] private PanelManager panelManager;
    [SerializeField] private GameObject craftPanel;
    [SerializeField] private GameObject passivePanel;

    private void Awake()
    {
        PlayerController player = PlayerController.Instance;
        if (skillManager == null && player != null)
            skillManager = player.GetComponent<SkillManager>();
        if (skillPointManager == null && player != null)
            skillPointManager = player.GetComponent<SkillPointManager>();
        if (skillManager != null) branchSystem = skillManager.BranchSystem;
        BindNodeButtons();
        if (branchChoiceDialog != null) branchChoiceDialog.Hide();

        if (panelManager == null) panelManager = PanelManager.Instance;
        toCraftBtn?.onClick.AddListener(() => panelManager?.OpenPanel(craftPanel));
        toPassiveBtn?.onClick.AddListener(() => panelManager?.OpenPanel(passivePanel));
    }

    private void OnEnable()
    {
        EventBus.Subscribe<SkillLevelChangedEvent>(OnSkillLevelChanged);
        EventBus.Subscribe<BranchChosenEvent>(OnBranchChosen);
        EventBus.Subscribe<PlayerSkillPointsChangedEvent>(OnSkillPointsChanged);
        Refresh();
    }

    private void OnDisable()
    {
        EventBus.Unsubscribe<SkillLevelChangedEvent>(OnSkillLevelChanged);
        EventBus.Unsubscribe<BranchChosenEvent>(OnBranchChosen);
        EventBus.Unsubscribe<PlayerSkillPointsChangedEvent>(OnSkillPointsChanged);
    }

    private void BindNodeButtons()
    {
        // [P7] 遍历所有 4 个 HUD 槽位（仅在有对应按钮时绑定）
        for (int skill = 0; skill < 4; skill++)
        {
            for (int node = 0; node < 5; node++)
            {
                int idx = skill * 5 + node;
                if (idx >= nodeButtons.Length) break;
                Button button = nodeButtons[idx];
                if (button == null) continue;
                int capturedSkill = skill;
                int capturedNode = node;
                button.onClick.AddListener(() => OnNodeClicked(capturedSkill, capturedNode));
            }
        }
    }

    private void OnNodeClicked(int slotIndex, int node)
    {
        if (skillManager == null) return;
        branchSystem = skillManager.BranchSystem;
        if (branchSystem == null) return;

        bool changed = node switch
        {
            0 => branchSystem.UnlockLevel1(slotIndex),
            1 => branchSystem.ChooseLevel2(slotIndex, "Left"),
            2 => branchSystem.ChooseLevel2(slotIndex, "Right"),
            3 => branchSystem.UpgradeLevel3(slotIndex, "Left"),
            4 => branchSystem.UpgradeLevel3(slotIndex, "Right"),
            _ => false
        };

        if (changed) Refresh();
    }

    private void ShowBranchDialog(int slotIndex)
    {
        ActiveSkillData data = skillManager.GetSlotData(slotIndex) as ActiveSkillData;
        if (data == null || branchChoiceDialog == null) return;

        SetPreviewText(data);
        branchChoiceDialog.Show(slotIndex, data, branch => branchSystem.OnBranchChosen(slotIndex, branch));
    }

    private void SetPreviewText(ActiveSkillData data)
    {
        Set(Get(dialog_Lv2Info, 0), FormatBranch(data.lv2Left));
        Set(Get(dialog_Lv2Info, 1), FormatBranch(data.lv2Right));
        Set(Get(dialog_Lv3Info, 0), FormatBranch(data.lv3Left));
        Set(Get(dialog_Lv3Info, 1), FormatBranch(data.lv3Right));
        if (dialog_LeftCard != null) dialog_LeftCard.SetActive(true);
        if (dialog_RightCard != null) dialog_RightCard.SetActive(true);
        if (dialog_ConfirmBtn != null) dialog_ConfirmBtn.interactable = false;
        if (dialog_CloseBtn != null) dialog_CloseBtn.interactable = true;
    }

    private void Refresh()
    {
        if (skillManager == null) return;
        branchSystem = skillManager.BranchSystem;
        if (skillPointLabel != null)
            skillPointLabel.text = $"技能点: {(skillPointManager != null ? skillPointManager.CurrentSkillPoints : skillManager.AvailableSkillPoints)}";

        // [P7] 遍历所有 4 个 HUD 槽位（只刷新有 ActiveSkillData 的槽位）
        for (int slot = 0; slot < 4; slot++)
            RefreshSkill(slot);
    }

    private void RefreshSkill(int slot)
    {
        // [P7] 边界检查：如果没有对应槽位的 UI 数组条目，跳过
        int baseIdx = slot * 5;
        if (baseIdx >= nodeButtons.Length) return;

        ActiveSkillData data = skillManager.GetSlotData(slot) as ActiveSkillData;
        int level = skillManager.GetSkillLevel(slot);
        for (int node = 0; node < 5; node++)
        {
            int idx = baseIdx + node;
            if (idx >= nodeButtons.Length) break;
            int nodeLevel = node == 0 ? 1 : node <= 2 ? 2 : 3;
            string branch = node == 1 || node == 3 ? "Left" : node == 2 || node == 4 ? "Right" : null;
            ActiveSkillData.ActiveBranchData branchData = GetBranchData(data, node);
            bool locked = IsNodeLocked(level, node, branch, data, slot);
            bool learned = IsLearned(data, level, nodeLevel, branch);
            bool canUpgrade = IsNodeUpgradeable(level, node, branch, data, slot);

            Button button = Get(nodeButtons, idx);
            if (button != null) button.interactable = canUpgrade;
            Image icon = Get(nodeIcons, idx);
            if (icon != null)
            {
                icon.sprite = data != null ? data.icon : null;
                icon.color = learned ? UIConstants.ActiveIconGold : canUpgrade ? Color.white : UIConstants.LockedGray;
            }
            Set(nodeNames[idx], branchData != null ? branchData.branchName : data != null && node == 0 ? data.skillName : string.Empty);
            Set(nodeLevels[idx], $"Lv{nodeLevel}");
            TMP_Text cost = Get(nodeCostBadges, idx);
            if (cost != null)
            {
                cost.gameObject.SetActive(canUpgrade);
                cost.text = branchSystem != null ? $"{branchSystem.GetUpgradeCost(slot)} SP" : string.Empty;
            }
            SetActive(nodeBranchMasks[idx], locked);
            SetActive(nodeGlows[idx], canUpgrade);
        }

        Image connector = Get(connectorLines, slot);
        if (connector != null) connector.color = level > 1 ? UIConstants.ActiveIconGold : UIConstants.LockedGray;
    }

    private bool IsNodeUpgradeable(int level, int node, string branch, ActiveSkillData data, int slot)
    {
        if (data == null || branchSystem == null || !branchSystem.CanUpgrade(slot)) return false;
        return node switch
        {
            0 => level == 0,
            1 or 2 => level == 1 && string.IsNullOrEmpty(data.chosenBranch),
            3 or 4 => level == 2 && data.chosenBranch == branch,
            _ => false
        };
    }

    private bool IsNodeLocked(int level, int node, string branch, ActiveSkillData data, int slot)
    {
        if (data == null) return true;
        if (node == 0) return false;
        if (node == 1 || node == 2) return level == 0 || branchSystem.IsBranchLocked(slot, branch);
        return level < 2 || data.chosenBranch != branch;
    }

    private static bool IsLearned(ActiveSkillData data, int currentLevel, int nodeLevel, string branch)
    {
        if (data == null || currentLevel == 0 || nodeLevel > currentLevel) return false;
        return branch == null || data.chosenBranch == branch;
    }

    private static ActiveSkillData.ActiveBranchData GetBranchData(ActiveSkillData data, int node)
    {
        if (data == null) return null;
        return node switch
        {
            0 => data.lv1Data,
            1 => data.lv2Left,
            2 => data.lv2Right,
            3 => data.lv3Left,
            4 => data.lv3Right,
            _ => null
        };
    }

    private static string FormatBranch(ActiveSkillData.ActiveBranchData data) =>
        data == null ? string.Empty : $"{data.branchName}\n{data.description}\nCD: {data.cooldown:0.##}s | MP: {data.manaCost:0.##}";

    private void OnSkillLevelChanged(SkillLevelChangedEvent eventData) => Refresh();
    private void OnBranchChosen(BranchChosenEvent eventData) { branchChoiceDialog?.Hide(); Refresh(); }
    private void OnSkillPointsChanged(PlayerSkillPointsChangedEvent eventData) => Refresh();

    private static void Set(TMP_Text text, string value) { if (text != null) text.text = value; }
    private static void SetActive(Component component, bool active) { if (component != null) component.gameObject.SetActive(active); }
    private static T Get<T>(T[] values, int index) where T : class => values != null && index >= 0 && index < values.Length ? values[index] : null;
}
