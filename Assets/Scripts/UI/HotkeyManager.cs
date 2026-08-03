using UnityEngine;

/// <summary>
/// Inspector-configured hotkey dispatcher.
/// Each hotkey is a pair of serialized fields — panel GameObject + KeyCode.
/// Expand this component in the Inspector to configure.
/// </summary>
public sealed class HotkeyManager : MonoBehaviour
{
    // ============================================================
    // Hotkey 1
    // ============================================================

    [Header("Hotkey 1")]
    [SerializeField] private GameObject panel1;
    [SerializeField] private KeyCode key1 = KeyCode.None;

    // ============================================================
    // Hotkey 2
    // ============================================================

    [Header("Hotkey 2")]
    [SerializeField] private GameObject panel2;
    [SerializeField] private KeyCode key2 = KeyCode.None;

    // ============================================================
    // Hotkey 3
    // ============================================================

    [Header("Hotkey 3")]
    [SerializeField] private GameObject panel3;
    [SerializeField] private KeyCode key3 = KeyCode.None;

    // ============================================================
    // Hotkey 4
    // ============================================================

    [Header("Hotkey 4")]
    [SerializeField] private GameObject panel4;
    [SerializeField] private KeyCode key4 = KeyCode.None;

    // ============================================================
    // Internal
    // ============================================================

    private PanelManager _panelManager;

    private void Awake()
    {
        _panelManager = GetComponent<PanelManager>();
        if (_panelManager == null)
            _panelManager = PanelManager.Instance;
    }

    private void Update()
    {
        if (_panelManager == null) return;

        TryToggle(panel1, key1);
        TryToggle(panel2, key2);
        TryToggle(panel3, key3);
        TryToggle(panel4, key4);
    }

    private void TryToggle(GameObject panel, KeyCode key)
    {
        if (panel != null && key != KeyCode.None && Input.GetKeyDown(key))
            _panelManager.TogglePanel(panel);
    }
}
