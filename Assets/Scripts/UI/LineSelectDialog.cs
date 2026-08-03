using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>Routes the five passive-line options back to PassiveUI.</summary>
public class LineSelectDialog : MonoBehaviour, IPanel
{
    PanelType IPanel.PanelType => PanelType.Dialog;
    bool IPanel.PauseGame => false;
    bool IPanel.LockInput => false;
    bool IPanel.ShowCursor => false;

    [SerializeField] private Button[] optionButtons;
    [SerializeField] private Button closeBtn;
    [SerializeField] private TMP_Text title;
    [SerializeField] private PanelManager panelManager;

    private System.Action<int> onLineSelected;

    private void Awake()
    {
        if (panelManager == null) panelManager = PanelManager.Instance;
        if (optionButtons != null)
        {
            for (int i = 0; i < optionButtons.Length; i++)
            {
                if (optionButtons[i] == null) continue;
                int capturedLine = i;
                optionButtons[i].onClick.AddListener(() => Select(capturedLine));
            }
        }
        closeBtn?.onClick.AddListener(Hide);
    }

    public void Show(int layer, System.Action<int> callback)
    {
        if (panelManager == null) panelManager = PanelManager.Instance;
        onLineSelected = callback;
        if (title != null) title.text = $"选择 T{layer + 1} 要装备的线";
        panelManager?.OpenPanel(gameObject);
    }

    public void Hide()
    {
        onLineSelected = null;
        panelManager?.ClosePanel(gameObject);
    }

    private void Select(int lineId)
    {
        System.Action<int> callback = onLineSelected;
        Hide();
        callback?.Invoke(lineId);
    }
}
