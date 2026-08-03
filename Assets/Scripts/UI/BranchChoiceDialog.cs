using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>Lets the player preview and confirm one irreversible skill branch.</summary>
public class BranchChoiceDialog : MonoBehaviour, IPanel
{
    PanelType IPanel.PanelType => PanelType.Dialog;
    bool IPanel.PauseGame => false;
    bool IPanel.LockInput => false;
    bool IPanel.ShowCursor => false;

    [SerializeField] private Button leftCard;
    [SerializeField] private Button rightCard;
    [SerializeField] private Button confirmBtn;
    [SerializeField] private Button closeBtn;
    [SerializeField] private TMP_Text[] lv2Info;
    [SerializeField] private TMP_Text[] lv3Info;
    [SerializeField] private PanelManager panelManager;

    private System.Action<string> onBranchChosen;
    private string selectedBranch;

    private void Awake()
    {
        if (panelManager == null) panelManager = PanelManager.Instance;
        leftCard?.onClick.AddListener(() => Select("Left"));
        rightCard?.onClick.AddListener(() => Select("Right"));
        confirmBtn?.onClick.AddListener(Confirm);
        closeBtn?.onClick.AddListener(Hide);
    }

    public void Show(int slotIndex, ActiveSkillData data, System.Action<string> callback)
    {
        if (panelManager == null) panelManager = PanelManager.Instance;
        onBranchChosen = callback;
        selectedBranch = null;
        if (confirmBtn != null) confirmBtn.interactable = false;
        Populate(data);
        panelManager?.OpenPanel(gameObject);
    }

    public void Hide()
    {
        onBranchChosen = null;
        selectedBranch = null;
        panelManager?.ClosePanel(gameObject);
    }

    private void Select(string branch)
    {
        selectedBranch = branch;
        if (confirmBtn != null) confirmBtn.interactable = true;
    }

    private void Confirm()
    {
        if (string.IsNullOrEmpty(selectedBranch)) return;
        System.Action<string> callback = onBranchChosen;
        string branch = selectedBranch;
        Hide();
        callback?.Invoke(branch);
    }

    private void Populate(ActiveSkillData data)
    {
        if (data == null) return;
        Set(lv2Info, 0, data.lv2Left);
        Set(lv2Info, 1, data.lv2Right);
        Set(lv3Info, 0, data.lv3Left);
        Set(lv3Info, 1, data.lv3Right);
    }

    private static void Set(TMP_Text[] texts, int index, ActiveSkillData.ActiveBranchData data)
    {
        if (texts == null || index >= texts.Length || texts[index] == null) return;
        texts[index].text = data == null ? string.Empty : $"{data.branchName}\n{data.description}";
    }
}
