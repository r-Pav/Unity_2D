using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// Binds the five-layer passive equipment panel to PassiveEquipManager.
/// Flat arrays use layer * 3 + slot indexing as documented by the UI layout.
/// </summary>
public class PassiveUI : MonoBehaviour, IPanel
{
    PanelType IPanel.PanelType => PanelType.FullScreen;
    bool IPanel.PauseGame => true;
    bool IPanel.LockInput => true;
    bool IPanel.ShowCursor => true;

    private static readonly string[] LineNames =
    {
        "HP恢复", "伤害+攻速", "移速+闪避", "减伤+控制", "法力+CD"
    };

    [SerializeField] private PassiveEquipManager passiveEquipManager;
    [SerializeField] private Button[] slotButtons = new Button[15];
    [SerializeField] private Image[] slotIcons = new Image[15];
    [SerializeField] private TMP_Text[] slotLineNames = new TMP_Text[15];
    [SerializeField] private TMP_Text[] slotEffects = new TMP_Text[15];
    [SerializeField] private Image[] lockOverlays = new Image[15];
    [SerializeField] private TMP_Text[] unlockLabels = new TMP_Text[15];
    [SerializeField] private TMP_Text[] layerTitles;
    [SerializeField] private Image[] layerLockIcons;
    [SerializeField] private LineSelectDialog lineSelectDialog;
    [SerializeField] private Button[] lineDialogOptions;

    private int selectedLayer = -1;
    private int selectedSlot = -1;

    private void Awake()
    {
        BindSlotButtons();
        if (lineSelectDialog != null)
            lineSelectDialog.Hide();
    }

    private void OnEnable()
    {
        EventBus.Subscribe<PassiveSlotsChangedEvent>(OnPassiveSlotsChanged);
        EventBus.Subscribe<ChapterChangedEvent>(OnChapterChanged);
        Refresh();
    }

    private void OnDisable()
    {
        EventBus.Unsubscribe<PassiveSlotsChangedEvent>(OnPassiveSlotsChanged);
        EventBus.Unsubscribe<ChapterChangedEvent>(OnChapterChanged);
    }

    private void BindSlotButtons()
    {
        for (int layer = 0; layer < PassiveEquipManager.LayerCount; layer++)
        {
            for (int slot = 0; slot < PassiveEquipManager.SlotPerLayer; slot++)
            {
                Button button = slotButtons[layer * 3 + slot];
                if (button == null) continue;
                int capturedLayer = layer;
                int capturedSlot = slot;
                button.onClick.AddListener(() => OpenLineDialog(capturedLayer, capturedSlot));

                // 右键卸下被动（右键已装备槽位直接卸载）
                var rightClickTrigger = button.gameObject.AddComponent<EventTrigger>();
                var rightClickEntry = new EventTrigger.Entry();
                rightClickEntry.eventID = EventTriggerType.PointerClick;
                rightClickEntry.callback.AddListener((data) =>
                {
                    var ped = (PointerEventData)data;
                    if (ped.button == PointerEventData.InputButton.Right)
                    {
                        if (passiveEquipManager == null || passiveEquipManager.InCombat)
                            return;
                        int lineId = passiveEquipManager.GetEquippedLineId(capturedLayer, capturedSlot);
                        if (lineId >= 0)
                            passiveEquipManager.UnequipPassive(capturedLayer, lineId);
                    }
                });
                rightClickTrigger.triggers.Add(rightClickEntry);
            }
        }
    }

    private void OpenLineDialog(int layer, int slot)
    {
        if (passiveEquipManager == null || passiveEquipManager.InCombat ||
            !passiveEquipManager.IsLayerUnlocked(layer))
            return;

        selectedLayer = layer;
        selectedSlot = slot;
        UpdateDialogOptions(layer);
        lineSelectDialog?.Show(layer, OnLineSelected);
    }

    private void UpdateDialogOptions(int layer)
    {
        if (lineDialogOptions == null) return;
        for (int line = 0; line < lineDialogOptions.Length; line++)
        {
            if (lineDialogOptions[line] != null)
                lineDialogOptions[line].interactable = !passiveEquipManager.IsLineEquippedInLayer(layer, line);
        }
    }

    private void OnLineSelected(int lineId)
    {
        if (passiveEquipManager != null && selectedLayer >= 0 && selectedSlot >= 0)
            passiveEquipManager.EquipPassive(selectedLayer, lineId, selectedSlot);
        selectedLayer = -1;
        selectedSlot = -1;
    }

    private void OnPassiveSlotsChanged(PassiveSlotsChangedEvent eventData)
    {
        Refresh();
    }

    private void OnChapterChanged(ChapterChangedEvent eventData)
    {
        Refresh();
    }

    private void Refresh()
    {
        if (passiveEquipManager == null) return;

        for (int layer = 0; layer < PassiveEquipManager.LayerCount; layer++)
        {
            bool unlocked = passiveEquipManager.IsLayerUnlocked(layer);
            int unlockChapter = GetUnlockChapter(layer);
            SetLayerState(layer, unlocked, unlockChapter);

            for (int slot = 0; slot < PassiveEquipManager.SlotPerLayer; slot++)
                SetSlotState(layer, slot, unlocked, unlockChapter);
        }
    }

    private void SetLayerState(int layer, bool unlocked, int unlockChapter)
    {
        TMP_Text title = Get(layerTitles, layer);
        if (title != null)
        {
            title.text = $"T{ToRoman(layer + 1)} [第{unlockChapter}章解锁]";
            title.color = unlocked ? UIConstants.ActiveIconGold : UIConstants.LockedGray;
        }

        Image lockIcon = Get(layerLockIcons, layer);
        if (lockIcon != null)
            lockIcon.gameObject.SetActive(!unlocked);
    }

    private void SetSlotState(int layer, int slot, bool unlocked, int unlockChapter)
    {
        int lineId = passiveEquipManager.GetEquippedLineId(layer, slot);
        PassiveSkillData data = FindPassiveData(layer, lineId);
        bool interactable = unlocked && !passiveEquipManager.InCombat;

        Button button = slotButtons[layer * 3 + slot];
        if (button != null) button.interactable = interactable;

        Image icon = slotIcons[layer * 3 + slot];
        if (icon != null)
        {
            icon.sprite = data != null ? data.icon : null;
            icon.enabled = data != null;
            icon.color = interactable ? Color.white : UIConstants.LockedGray;
        }

        TMP_Text lineName = slotLineNames[layer * 3 + slot];
        if (lineName != null)
            lineName.text = lineId >= 0 && lineId < LineNames.Length ? LineNames[lineId] : string.Empty;

        TMP_Text effect = slotEffects[layer * 3 + slot];
        if (effect != null)
            effect.text = FormatFirstEffect(data);

        Image overlay = lockOverlays[layer * 3 + slot];
        if (overlay != null) overlay.gameObject.SetActive(!interactable);

        TMP_Text unlockLabel = unlockLabels[layer * 3 + slot];
        if (unlockLabel != null)
        {
            unlockLabel.gameObject.SetActive(!unlocked);
            unlockLabel.text = $"第{unlockChapter}章解锁";
        }
    }

    private PassiveSkillData FindPassiveData(int layer, int lineId)
    {
        if (lineId < 0 || passiveEquipManager.AllPassiveData == null) return null;
        foreach (PassiveSkillData data in passiveEquipManager.AllPassiveData)
        {
            if (data != null && data.layer == layer + 1 && data.lineId == lineId)
                return data;
        }
        return null;
    }

    private int GetUnlockChapter(int layer)
    {
        return passiveEquipManager != null ? passiveEquipManager.GetUnlockChapter(layer) : PassiveEquipManager.UnlockChapterOf(layer);
    }

    private static string FormatFirstEffect(PassiveSkillData data)
    {
        if (data == null || data.effects == null || data.effects.Length == 0) return string.Empty;
        PassiveSkillData.PassiveEffect effect = data.effects[0];
        return effect.type == ModifierType.Percent ? $"{effect.value:+0%;-0%}" : $"{effect.value:+0.##;-0.##}";
    }

    private static string ToRoman(int value)
    {
        string[] labels = { "I", "II", "III", "IV", "V" };
        return value >= 1 && value <= labels.Length ? labels[value - 1] : value.ToString();
    }

    private static T Get<T>(T[] values, int index) where T : class =>
        values != null && index >= 0 && index < values.Length ? values[index] : null;

}
