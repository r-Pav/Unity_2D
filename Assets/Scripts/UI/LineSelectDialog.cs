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

    private System.Action<int> onLineSelected;
    private RectTransform selfRect;
    private RectTransform lastAnchorButton;
    private GameObject blocker;
    private UIPanelMotion motion;   // 同物体上的开关动效：动态面板每次 Show 前要把本次摆位交给它

    private void Awake()
    {
        selfRect = (RectTransform)transform;
        if (motion == null) motion = GetComponent<UIPanelMotion>();
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
        // Inspector 调整 offsetBelow 时立即重新定位（Play 模式且面板显示中才实时生效）
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

        // 顺序关键：定位必须在 OpenPanel 之前。
        // 本物体挂了 UIPanelMotion(打开=从屏幕外滑入)，PanelManager.OpenPanel → PlayOpen 内部会把
        // 当时的 anchoredPosition 当目标摆位(只缓存一次)并 tween 过去；若先 OpenPanel 再定位，
        // 这次算好的位置会被 tween 覆盖、滑回上一次缓存的旧摆位(anchoredPosition 的含义还随 anchor 一起变)
        // → 对话框落在屏幕外。动态面板约定：PlayOpen 前先 SetHomePosition(PositionBelow 末尾已调)。
        PositionBelow(anchorButton);

        panelManager?.OpenPanel(gameObject);
    }

    /// <summary>
    /// 把对话框摆到触发槽位旁边（只写位置，尺寸/锚点/Pivot 全取场景里摆好的值，与 UITooltip 同口径）：
    ///   默认：框左上角对齐槽位左上角 + offsetBelow（x 右移，y 负值=下移），向下展开；
    ///   下方放不下 → 翻到槽位上方（框底边贴槽位上边，偏移量同口径反向）；顶到边仍放不下 → 夹住；
    ///   右侧放不下 → 框右边缘对齐槽位右边缘往左展开；左侧放不下 → 夹住。
    /// 尺寸读 sizeDelta（固定尺寸摆好的，不吃布局系统），所以框多大由 Inspector 决定。
    /// 前提：本物体 anchor 必须是固定锚点（anchorMin == anchorMax，如 Center），stretch 下 anchoredPosition 的含义不同。
    /// </summary>
    private void PositionBelow(RectTransform anchorButton)
    {
        // 面板常驻 inactive，Awake 要等第一次 SetActive 才执行 → 这里懒取，不能依赖 Awake 已赋值
        if (selfRect == null) selfRect = (RectTransform)transform;
        if (motion == null) motion = GetComponent<UIPanelMotion>();
        if (anchorButton == null || selfRect == null) return;

        RectTransform parentRect = selfRect.parent as RectTransform;
        if (parentRect == null) return;

        Canvas canvas = GetComponentInParent<Canvas>();
        if (canvas == null) return;
        Camera cam = canvas.renderMode != RenderMode.ScreenSpaceOverlay ? canvas.worldCamera : null;

        // 尺寸：读场景摆好的固定尺寸，代码不设
        Vector2 size = selfRect.sizeDelta;
        float w = size.x;
        float h = size.y;

        // 槽位四角 → 屏幕坐标：[0]=左下 [1]=左上 [2]=右上 [3]=右下
        Vector3[] corners = new Vector3[4];
        anchorButton.GetWorldCorners(corners);
        Vector2 slotBL = RectTransformUtility.WorldToScreenPoint(cam, corners[0]);
        Vector2 slotTL = RectTransformUtility.WorldToScreenPoint(cam, corners[1]);
        Vector2 slotBR = RectTransformUtility.WorldToScreenPoint(cam, corners[3]);
        float slotLeft = Mathf.Min(slotBL.x, slotTL.x);
        float slotTop = Mathf.Max(slotBL.y, slotTL.y);
        float slotRight = Mathf.Max(slotBR.x, RectTransformUtility.WorldToScreenPoint(cam, corners[2]).x);

        float sw = Screen.width;
        float sh = Screen.height;

        // 默认：左上角对齐槽位左上角 + 偏移，向下展开
        float left = slotLeft + offsetBelow.x;
        float top = slotTop + offsetBelow.y;

        // 下方放不下 → 翻到槽位上方
        if (top - h < 0f)
        {
            float bottom = slotBL.y - offsetBelow.y;   // 框底边贴槽位下边，偏移量反向
            top = bottom + h;
            if (top > sh) top = sh;                    // 仍超顶 → 夹住
        }

        // 右侧放不下 → 右边缘对齐槽位右边缘往左展开
        if (left + w > sw)
        {
            left = slotRight - w;
            if (left < 0f) left = 0f;
        }
        if (left < 0f) left = 0f;

        // 框的 Pivot 点（屏幕坐标）→ 父级局部坐标
        Vector2 pivotScreen = new Vector2(
            left + selfRect.pivot.x * w,
            top - (1f - selfRect.pivot.y) * h);

        Vector2 localPoint;
        if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(parentRect, pivotScreen, cam, out localPoint))
            return;

        // anchoredPosition 的基准 = 父矩形内按自身 anchor 比例算出的参考点（与 UITooltip.Place 同算法）
        Vector2 anchorRef = parentRect.rect.min + Vector2.Scale(selfRect.anchorMin, parentRect.rect.size);
        Vector2 pos = localPoint - anchorRef;

        selfRect.anchoredPosition = pos;

        // 登记本次摆位为「家」：UIPanelMotion 的 PlayOpen 从这里滑入、PlayClose 向这里滑出。
        // 动态面板必须每次 Show 重设，否则会滑回首次缓存的旧摆位。
        motion?.SetHomePosition(pos);
    }

    /// <summary>
    /// 创建全屏透明遮挡层：面板显示期间点面板外任意位置都关闭它。
    /// 父级取对话框自己的父级(不是 Canvas)：blocker 插在对话框前一个 sibling =
    /// 渲染在对话框之下、其余 UI 之上，点框外任何位置都先落到 blocker，
    /// 既不会被下层 UI 先吃掉点击，也不会误触其它按钮。
    /// (挂到 Canvas 下时 SetSiblingIndex 的索引语义对不上，会出现「时好时坏」。)
    /// </summary>
    private void CreateBlocker()
    {
        if (blocker != null)
        {
            blocker.SetActive(true);
            return;
        }

        if (selfRect == null) selfRect = (RectTransform)transform;
        RectTransform parentRect = selfRect != null ? selfRect.parent as RectTransform : null;
        if (parentRect == null) return;

        blocker = new GameObject("LineSelectBlocker");
        blocker.transform.SetParent(parentRect, false);

        RectTransform rt = blocker.AddComponent<RectTransform>();
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.sizeDelta = Vector2.zero;
        rt.anchoredPosition = Vector2.zero;
        rt.pivot = new Vector2(0.5f, 0.5f);

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
