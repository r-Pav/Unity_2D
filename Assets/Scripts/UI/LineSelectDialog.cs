using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>Routes the five passive-line options back to PassiveUI.
/// 跟随触发按钮弹出（下拉式）：打开时定位到按钮下方。</summary>
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

    [Header("下拉定位")]
    [Tooltip("对话框相对触发按钮的偏移（按钮下方，如 x=0 对齐按钮左侧, y=-8 紧贴下方）")]
    [SerializeField] private Vector2 offsetBelow = new Vector2(0f, -8f);
    [Tooltip("对话框固定尺寸（按钮下方展开的大小）")]
    [SerializeField] private Vector2 fixedSize = new Vector2(300f, 400f);

    private System.Action<int> onLineSelected;
    private RectTransform selfRect;

    private void Awake()
    {
        selfRect = (RectTransform)transform;
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

    /// <summary>在指定按钮下方弹出选择列表</summary>
    /// <param name="anchorButton">触发按钮（用于定位）</param>
    /// <param name="layer">层级（仅用于标题显示）</param>
    /// <param name="callback">线选择回调</param>
    public void Show(RectTransform anchorButton, int layer, System.Action<int> callback)
    {
        if (panelManager == null) panelManager = PanelManager.Instance;
        onLineSelected = callback;
        if (title != null) title.text = $"选择 T{layer + 1} 要装备的线";
        panelManager?.OpenPanel(gameObject);
        PositionBelow(anchorButton);
    }

    /// <summary>把对话框定位到按钮下方（世界坐标换算，兼容 Overlay/World 相机 Canvas）</summary>
    private void PositionBelow(RectTransform anchorButton)
    {
        if (anchorButton == null || selfRect == null) return;

        // 锚点/枢轴对齐 0,0（左下），尺寸固定，由代码控制布局
        selfRect.anchorMin = Vector2.zero;
        selfRect.anchorMax = Vector2.zero;
        selfRect.pivot = new Vector2(0f, 1f); // 左下角为锚，向下展开
        selfRect.sizeDelta = fixedSize;

        Canvas canvas = GetComponentInParent<Canvas>();
        if (canvas == null) return;

        // 按钮世界坐标 → 对话框父级本地坐标
        Vector2 buttonWorld = anchorButton.position;
        Vector2 localPos;
        if (RectTransformUtility.ScreenPointToLocalPointInRectangle(
                (RectTransform)selfRect.parent, buttonWorld, canvas.worldCamera, out localPos))
        {
            // localPos 是按钮左下角在对话框父级中的位置
            selfRect.anchoredPosition = localPos + offsetBelow;
        }
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
