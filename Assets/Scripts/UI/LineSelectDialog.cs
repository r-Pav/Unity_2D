using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>Routes the five passive-line options back to PassiveUI.
/// 跟随触发按钮弹出（下拉式）：打开时定位到按钮下方，点击对话框外区域自动关闭。</summary>
public class LineSelectDialog : MonoBehaviour, IPanel
{
    PanelType IPanel.PanelType => PanelType.Dialog;
    bool IPanel.PauseGame => false;
    bool IPanel.LockInput => false;
    bool IPanel.ShowCursor => false;

    [SerializeField] private Button[] optionButtons;
    [SerializeField] private TMP_Text title;
    [SerializeField] private PanelManager panelManager;

    [Header("下拉定位")]
    [Tooltip("对话框相对 slot 左上角的偏移（默认 0=左上角对齐；y 负值=稍微下移留间隙）")]
    [SerializeField] private Vector2 offsetBelow = new Vector2(0f, -8f);
    [Tooltip("对话框固定尺寸（按钮下方展开的大小）")]
    [SerializeField] private Vector2 fixedSize = new Vector2(300f, 400f);

    private System.Action<int> onLineSelected;
    private RectTransform selfRect;
    private RectTransform lastAnchorButton;
    private GameObject blocker;

    private void Awake()
    {
        selfRect = (RectTransform)transform;
        if (panelManager == null) panelManager = PanelManager.Instance;
        if (optionButtons != null)
        {
            for (int i = 0; i < optionButtons.Length; i++)
            {
                if (optionButtons[i] == null) continue;
                // 最后一个按钮是"空"选项 → 传 EmptyChoice(-2)
                bool isLast = (i == optionButtons.Length - 1);
                int capturedLine = isLast ? PassiveEquipManager.EmptyChoice : i;
                optionButtons[i].onClick.AddListener(() => Select(capturedLine));
            }
        }
    }

    private void OnValidate()
    {
        // Inspector 调整 offsetBelow/fixedSize 时立即重新定位（Play 模式也实时生效）
        if (lastAnchorButton != null && selfRect != null && gameObject.activeInHierarchy)
            PositionBelow(lastAnchorButton);
    }

    private void OnDisable()
    {
        // blocker 是 Canvas 下的独立节点，不随本面板 SetActive 联动，必须手动关，
        // 否则残留的全屏透明层会拦截后续点击（点击外部关闭时而有效时而无）。
        if (blocker != null)
            blocker.SetActive(false);
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
        lastAnchorButton = anchorButton;
        CreateBlocker();
        panelManager?.OpenPanel(gameObject);
        PositionBelow(anchorButton);
    }

    /// <summary>把对话框定位到 slot 附近：以 slot 左上角为锚点，宽度=slot 宽，高度用 fixedSize.y。
    /// 越界自动翻转：超底往上、超右往左。</summary>
    private void PositionBelow(RectTransform anchorButton)
    {
        if (anchorButton == null || selfRect == null) return;

        // 锚点对齐 0,0，由代码控制位置；尺寸：宽=slot宽，高=手动
        selfRect.anchorMin = Vector2.zero;
        selfRect.anchorMax = Vector2.zero;

        Canvas canvas = GetComponentInParent<Canvas>();
        if (canvas == null) return;

        RectTransform parentRect = (RectTransform)selfRect.parent;
        float slotW = anchorButton.rect.width * anchorButton.lossyScale.x;
        float slotH = anchorButton.rect.height * anchorButton.lossyScale.y;
        float w = slotW;                 // 宽度跟随 slot
        float h = fixedSize.y;           // 高度手动
        selfRect.sizeDelta = new Vector2(w, h);

        // 取 slot 四角世界坐标: [0]=左下 [1]=左上 [2]=右上 [3]=右下
        Vector3[] corners = new Vector3[4];
        anchorButton.GetWorldCorners(corners);
        Vector2 topLeft;
        if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(
                parentRect,
                RectTransformUtility.WorldToScreenPoint(canvas.worldCamera, corners[1]),
                canvas.worldCamera, out topLeft))
            return;

        // ScreenPointToLocalPointInRectangle 返回相对父级 pivot(中心)的坐标，
        // 而 anchoredPosition(anchor=0,0) 需要相对父级左下角的坐标 → 补 pivot 偏移
        Vector2 pivotOffset = new Vector2(
            parentRect.pivot.x * parentRect.rect.width,
            parentRect.pivot.y * parentRect.rect.height);
        topLeft += pivotOffset;

        // 父级（Canvas）可视区域尺寸
        Vector2 viewSize = parentRect.rect.size;

        // 默认：左上角对齐 slot 左上角，向下展开
        selfRect.pivot = new Vector2(0f, 1f); // 左上角为锚，向下展开
        float x = topLeft.x + offsetBelow.x;
        float y = topLeft.y + offsetBelow.y;

        // 水平越界：超出右侧 → 右边缘对齐 slot 右边缘往左展开；再超左则钳制
        if (x + w > viewSize.x)
        {
            x = topLeft.x + slotW - w - offsetBelow.x;
            if (x < 0f) x = 0f;
        }

        // 垂直越界：超出底部 → 翻到 slot 上方（pivot 改左下，向上展开）
        if (y - h < 0f)
        {
            selfRect.pivot = new Vector2(selfRect.pivot.x, 0f); // 左下角为锚，向上展开
            y = topLeft.y - slotH - offsetBelow.y;              // 底部贴 slot 顶部之上
            if (y + h > viewSize.y)
                y = viewSize.y - h; // 仍超顶则钳制
        }

        selfRect.anchoredPosition = new Vector2(x, y);
    }

    /// <summary>创建全屏透明遮挡层：点击对话框外区域关闭。插入到对话框之下，不影响对话框内按钮点击。</summary>
    private void CreateBlocker()
    {
        if (blocker != null)
        {
            blocker.SetActive(true);
            return;
        }

        Canvas canvas = GetComponentInParent<Canvas>();
        if (canvas == null) return;

        blocker = new GameObject("LineSelectBlocker");
        blocker.transform.SetParent(canvas.transform, false);

        RectTransform rt = blocker.AddComponent<RectTransform>();
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.sizeDelta = Vector2.zero;

        Image img = blocker.AddComponent<Image>();
        img.color = new Color(0f, 0f, 0f, 0f); // 全透明，仅拦截点击
        img.raycastTarget = true;

        Button btn = blocker.AddComponent<Button>();
        btn.transition = Selectable.Transition.None;
        btn.onClick.AddListener(Hide);

        // 插到对话框之下：对话框先渲染，blocker 在下层只接未被对话框覆盖的点击
        blocker.transform.SetSiblingIndex(transform.GetSiblingIndex());
    }

    public void Hide()
    {
        onLineSelected = null;
        if (blocker != null)
            blocker.SetActive(false);
        panelManager?.ClosePanel(gameObject);
    }

    private void Select(int lineId)
    {
        System.Action<int> callback = onLineSelected;
        Hide();
        callback?.Invoke(lineId);
    }
}
