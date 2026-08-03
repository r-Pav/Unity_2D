using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>Displays irreversible craft details and routes confirmation.</summary>
public class CraftConfirmDialog : MonoBehaviour, IPanel
{
    PanelType IPanel.PanelType => PanelType.Dialog;
    bool IPanel.PauseGame => false;
    bool IPanel.LockInput => false;
    bool IPanel.ShowCursor => false;

    [SerializeField] private TMP_Text mat1Text;
    [SerializeField] private TMP_Text mat2Text;
    [SerializeField] private TMP_Text resultText;
    [SerializeField] private Button confirmBtn;
    [SerializeField] private Button cancelBtn;
    [SerializeField] private PanelManager panelManager;

    private System.Action onConfirm;

    private void Awake()
    {
        if (panelManager == null) panelManager = PanelManager.Instance;
        confirmBtn?.onClick.AddListener(Confirm);
        cancelBtn?.onClick.AddListener(Hide);
    }

    public void Show(string material1, string material2, string result, System.Action callback)
    {
        if (panelManager == null) panelManager = PanelManager.Instance;
        if (mat1Text != null) mat1Text.text = material1;
        if (mat2Text != null) mat2Text.text = material2;
        if (resultText != null) resultText.text = result;
        onConfirm = callback;
        panelManager?.OpenPanel(gameObject);
    }

    public void Hide()
    {
        onConfirm = null;
        panelManager?.ClosePanel(gameObject);
    }

    private void Confirm()
    {
        System.Action callback = onConfirm;
        Hide();
        callback?.Invoke();
    }
}
