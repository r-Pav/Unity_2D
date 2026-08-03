using System.Collections.Generic;
using UnityEngine;

/// <summary>Defines how a registered panel interacts with other panels.</summary>
public enum PanelType
{
    FullScreen,
    Dialog
}

/// <summary>
/// Implement on any UI panel that should be stack-managed.  PanelManager
/// auto-discovers all IPanel components in its subtree at Start (including
/// inactive GameObjects), so no manual registration call is needed.
/// </summary>
public interface IPanel
{
    PanelType PanelType { get; }
    bool PauseGame   { get; }
    bool LockInput   { get; }
    bool ShowCursor  { get; }
}

/// <summary>
/// Manages panel stacking (ESC close-order), pause state, player input, and cursor state.
///
/// Panels implement <see cref="IPanel"/> and are auto-discovered from the Canvas subtree
/// at Start — no Awake registration or Inspector array dragging needed.
///
/// Side-effects (pause / lock input / show cursor) are aggregated across the active
/// stack — a single panel that requests pause will pause the game regardless of what
/// other panels declare.
/// </summary>
public sealed class PanelManager : MonoBehaviour
{
    // ============================================================
    // Singleton
    // ============================================================

    private static PanelManager _instance;

    public static PanelManager Instance
    {
        get
        {
            if (_instance == null)
                _instance = FindObjectOfType<PanelManager>();
            return _instance;
        }
    }

    // ============================================================
    // Internal types
    // ============================================================

    private sealed class RegisteredPanel
    {
        public GameObject panel;
        public PanelType type;
        public bool pauseGame;
        public bool lockInput;
        public bool showCursor;
    }

    // ============================================================
    // Internal state
    // ============================================================

    private readonly List<RegisteredPanel> _registry = new List<RegisteredPanel>();
    private readonly Stack<GameObject> _panelStack = new Stack<GameObject>();
    private readonly Stack<GameObject> _fullScreenHistory = new Stack<GameObject>();
    private PlayerController _player;

    public bool IsAnyPanelOpen => _panelStack.Count > 0;

    // ============================================================
    // Lifecycle
    // ============================================================

    private void Awake()
    {
        if (_instance != null)
        {
            Destroy(gameObject);
            return;
        }
        _instance = this;
    }

    private void Start()
    {
        _player = PlayerController.Instance;
        AutoRegisterPanels();
        PushActivePanelsToStack();
        _ApplyInteractionState(); // 确保初始光标状态正确
    }

    /// <summary>场景加载后，将已 active 的已注册面板自动入栈，确保 ESC/暂停一致</summary>
    private void PushActivePanelsToStack()
    {
        foreach (RegisteredPanel entry in _registry)
        {
            if (entry.panel != null && entry.panel.activeInHierarchy)
                _panelStack.Push(entry.panel);
        }
        _ApplyInteractionState();
    }

    private void Update()
    {
        if (Input.GetKeyDown(KeyCode.Escape) && IsAnyPanelOpen)
            CloseTopPanel();
    }

    private void OnEnable()
    {
        EventBus.Subscribe<PlayerDeathEvent>(OnPlayerDeath);
    }

    private void OnDisable()
    {
        EventBus.Unsubscribe<PlayerDeathEvent>(OnPlayerDeath);
        CloseAllPanels();
    }

    private void OnPlayerDeath(PlayerDeathEvent e)
    {
        foreach (RegisteredPanel entry in _registry)
        {
            if (entry.panel != null && entry.panel.GetComponent<DeathPanel>() != null)
            {
                OpenPanel(entry.panel);
                return;
            }
        }
    }

    /// <summary>Scan Canvas subtree for all IPanel components (including inactive).</summary>
    private void AutoRegisterPanels()
    {
        IPanel[] panels = GetComponentsInChildren<IPanel>(true);
        foreach (IPanel p in panels)
        {
            var mb = (MonoBehaviour)p;
            _Register(mb.gameObject, p.PanelType, p.PauseGame, p.LockInput, p.ShowCursor);
        }
    }

    // ============================================================
    // Public registration (also callable manually for runtime-created panels)
    // ============================================================

    public static void Register(GameObject panel, PanelType type,
        bool pauseGame, bool lockInput, bool showCursor)
    {
        if (Instance == null) return;
        Instance._Register(panel, type, pauseGame, lockInput, showCursor);
    }

    public static void Unregister(GameObject panel)
    {
        if (Instance == null) return;
        Instance._Unregister(panel);
    }

    // ============================================================
    // Public panel operations
    // ============================================================

    public void OpenPanel(GameObject panel)
    {
        RegisteredPanel entry = _FindRegistered(panel);
        if (entry == null)
        {
            Debug.LogError($"[PanelManager] Panel is not registered: {panel?.name}.", this);
            return;
        }

        _RemoveFromStack(panel, false);
        if (entry.type == PanelType.FullScreen)
            _ReplaceVisibleFullScreen(panel);

        _panelStack.Push(panel);
        panel.SetActive(true);
        _ApplyInteractionState();
    }

    public void CloseTopPanel()
    {
        GameObject panel = _PopTopValidPanel();
        if (panel == null) return;

        RegisteredPanel closedEntry = _FindRegistered(panel);
        panel.SetActive(false);
        if (closedEntry != null && closedEntry.type == PanelType.FullScreen)
            _RestorePreviousFullScreenPanel();

        _ApplyInteractionState();
    }

    public void ClosePanel(GameObject panel)
    {
        if (_FindRegistered(panel) == null)
        {
            Debug.LogError($"[PanelManager] Panel is not registered: {panel?.name}", this);
            return;
        }

        _RemoveFromStack(panel, true);
        if (panel != null && panel.activeSelf)
            panel.SetActive(false);
        _ApplyInteractionState();
    }

    public void CloseAllPanels()
    {
        while (_panelStack.Count > 0)
        {
            GameObject panel = _panelStack.Pop();
            if (panel != null) panel.SetActive(false);
        }

        while (_fullScreenHistory.Count > 0)
        {
            GameObject panel = _fullScreenHistory.Pop();
            if (panel != null) panel.SetActive(false);
        }

        _ApplyInteractionState();
    }

    public void TogglePanel(GameObject panel)
    {
        if (IsPanelOpen(panel))
            ClosePanel(panel);
        else
            OpenPanel(panel);
    }

    public bool IsPanelOpen(GameObject panel)
    {
        return panel != null && _panelStack.Contains(panel);
    }

    // ============================================================
    // Internal — registry
    // ============================================================

    private void _Register(GameObject panel, PanelType type,
        bool pauseGame, bool lockInput, bool showCursor)
    {
        if (_FindRegistered(panel) != null)
        {
            Debug.LogWarning($"[PanelManager] Panel already registered: {panel?.name}", this);
            return;
        }

        _registry.Add(new RegisteredPanel
        {
            panel = panel,
            type = type,
            pauseGame = pauseGame,
            lockInput = lockInput,
            showCursor = showCursor
        });
    }

    private void _Unregister(GameObject panel)
    {
        for (int i = _registry.Count - 1; i >= 0; i--)
        {
            if (_registry[i].panel == panel)
                _registry.RemoveAt(i);
        }

        _RemoveFromStack(panel, false);
    }

    private RegisteredPanel _FindRegistered(GameObject panel)
    {
        if (panel == null) return null;
        for (int i = 0; i < _registry.Count; i++)
        {
            if (_registry[i].panel == panel)
                return _registry[i];
        }
        return null;
    }

    // ============================================================
    // Internal — stack
    // ============================================================

    private GameObject _PopTopValidPanel()
    {
        while (_panelStack.Count > 0)
        {
            GameObject candidate = _panelStack.Pop();
            if (candidate != null && _FindRegistered(candidate) != null && candidate.activeInHierarchy)
                return candidate;
        }
        return null;
    }

    private void _RemoveFromStack(GameObject panel, bool deactivate)
    {
        if (!_panelStack.Contains(panel)) return;

        Stack<GameObject> temp = new Stack<GameObject>();
        while (_panelStack.Count > 0)
        {
            GameObject current = _panelStack.Pop();
            if (current == panel)
            {
                if (deactivate && current != null) current.SetActive(false);
                break;
            }
            temp.Push(current);
        }

        while (temp.Count > 0)
            _panelStack.Push(temp.Pop());
    }

    private void _ReplaceVisibleFullScreen(GameObject panelToOpen)
    {
        GameObject[] stacked = _panelStack.ToArray();
        for (int i = 0; i < stacked.Length; i++)
        {
            RegisteredPanel entry = _FindRegistered(stacked[i]);
            if (stacked[i] == panelToOpen || entry == null || entry.type != PanelType.FullScreen)
                continue;

            stacked[i].SetActive(false);
            _RemoveFromStack(stacked[i], false);
            _fullScreenHistory.Push(stacked[i]);
        }
    }

    private void _RestorePreviousFullScreenPanel()
    {
        while (_fullScreenHistory.Count > 0)
        {
            GameObject panel = _fullScreenHistory.Pop();
            RegisteredPanel entry = _FindRegistered(panel);
            if (panel == null || entry == null || entry.type != PanelType.FullScreen)
                continue;

            panel.SetActive(true);
            _panelStack.Push(panel);
            return;
        }
    }

    private bool IsPanelInWorkflow(GameObject panel)
    {
        return IsPanelOpen(panel) || (panel != null && _fullScreenHistory.Contains(panel));
    }

    // ============================================================
    // Internal — side-effects (aggregated)
    // ============================================================

    private void _ApplyInteractionState()
    {
        bool shouldPause = false;
        bool shouldLockInput = false;
        bool shouldShowCursor = false;

        foreach (GameObject go in _panelStack)
        {
            RegisteredPanel entry = _FindRegistered(go);
            if (entry == null) continue;

            if (entry.pauseGame) shouldPause = true;
            if (entry.lockInput) shouldLockInput = true;
            if (entry.showCursor) shouldShowCursor = true;
        }

        Time.timeScale = shouldPause ? 0f : 1f;

        if (_player == null) _player = PlayerController.Instance;
        if (_player != null) _player.InputEnabled = !shouldLockInput;

        if (IsAnyPanelOpen)
        {
            Cursor.visible = shouldShowCursor;
            Cursor.lockState = shouldShowCursor ? CursorLockMode.None : CursorLockMode.Locked;
        }
        else
        {
            // 无面板打开 → 战斗状态，隐藏鼠标（用 None 避免 Unity 劫持 ESC）
            Cursor.visible = false;
            Cursor.lockState = CursorLockMode.None;
        }
    }
}
