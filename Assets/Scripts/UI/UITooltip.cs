using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 悬停提示窗 — 挂场景里那个 ToolTip 面板根物体上(与 UIPanelMotion 同物体)。
///
/// 内容三块,与面板下的子物体一一对应:
///   Title   → 标题
///   Content → 详细内容(Scroll View/Viewport/Content)
///   Tip     → 底部「其他」行(装备=售价;技能=冷却+技能点;合成/商店预留)
///
/// 定位规则(2026-09-22 saika 定):
///   水平:优先贴在元素右侧,右侧放不下翻到左侧 —— 只换边,不做镜像翻转;
///   竖直:与元素中心对齐,超出屏幕只做最小平移夹进屏幕;
///   总原则是紧贴元素,不为了「自适应」把窗子甩到离元素很远的地方。
///
/// 开关:走同物体上的 UIPanelMotion(场景里配的是 SlideRight 滑入滑出)。
/// 用法:UITooltip.ShowItem(so, 格子Rect) / UITooltip.Show(title, content, tip, rect) / UITooltip.Hide()
///
/// 注意:本组件依赖自己在 Awake 里 SetActive(false)(面板保持 inactive 是 UIPanelMotion 的约定),
/// 所以**场景里这个物体要保持勾选 active**,由 Awake 负责关掉;若在 Inspector 里预先取消勾选,
/// Awake 不会执行、静态入口会失效。
/// </summary>
public class UITooltip : MonoBehaviour
{
    /// <summary>当前实例(Awake 接管 + OnDestroy 自清,与项目其他单例一致,无 Find 兜底)</summary>
    public static UITooltip Instance { get; private set; }

    // ============================================================
    // 引用
    // ============================================================

    [Header("内容引用(拖场景里的子物体)")]
    [Tooltip("Title —— 标题")]
    [SerializeField] private TMP_Text titleText;

    [Tooltip("Scroll View/Viewport/Content —— 详细内容")]
    [SerializeField] private TMP_Text contentText;

    [Tooltip("Tip —— 底部「其他」行(售价 / 冷却 等)")]
    [SerializeField] private TMP_Text tipText;

    [Tooltip("Line —— 标题与内容之间的分隔线(内容为空时收起)")]
    [SerializeField] private GameObject lineObj;

    [Tooltip("Scroll View —— 详细内容整块(内容为空时收起)")]
    [SerializeField] private GameObject contentArea;

    [Header("行为")]
    [Tooltip("开/关动效(留空自动取同物体上的 UIPanelMotion)")]
    [SerializeField] private UIPanelMotion motion;

    [Tooltip("Btn_Back —— 关闭按钮(留空自动取子物体里的第一个 Button)")]
    [SerializeField] private Button closeButton;

    [Tooltip("窗子与元素之间的间距(像素)—— 越小越贴")]
    [SerializeField] private float gap = 8f;

    [Tooltip("窗子与屏幕边缘的最小距离(像素)")]
    [SerializeField] private float edgeMargin = 8f;

    [Tooltip("鼠标移开后 tip 延迟隐藏的时间(秒);到点时鼠标还在 tip 上就继续等(2026-09-26 加)")]
    [SerializeField] private float hideDelay = 0.3f;

    [Header("文案")]
    [Tooltip("装备/物品「其他」行的售价格式")]
    [SerializeField] private string sellPriceFormat = "售价：{0}";

    // ============================================================
    // 运行时
    // ============================================================

    private RectTransform _rect;
    private bool _visible;

    /// <summary>tip 被固定(选中)在哪个元素上。非空 = 鼠标移开也不隐藏,直到取消选中 / 强制关闭。</summary>
    private RectTransform _pinned;

    /// <summary>当前窗口内容对应的锚点元素。用于判断「同一格子的重复 Show」——那种情况只换文字,
    /// 不再重设激活状态/重算位置,否则单击那一下会让窗口整体闪一遍(2026-09-22 saika 报「出现两次」)。</summary>
    private RectTransform _shownAnchor;

    private void Awake()
    {
        Instance = this;
        _rect = GetComponent<RectTransform>();

        // 滑入动画的路径会横穿屏幕,经过鼠标时会把悬停打断(格子收到 OnPointerExit → tip 隐藏 →
        // 鼠标又落回格子 → OnPointerEnter → 再播一次),表现就是 tip 反复闪。
        // 对策见 SetRaycast:显示期间整块不吃鼠标,滑入播完再恢复。
        if (motion == null) motion = GetComponent<UIPanelMotion>();
        if (closeButton == null) closeButton = GetComponentInChildren<Button>(true);
        if (closeButton != null) closeButton.onClick.AddListener(CloseInternal);


        // 面板根底图永久不吃射线(2026-09-22 实测定位)。
        // 底图只是背景,但 SetRaycast 对它是「跳过不动」,不会把 raycastTarget 关掉。
        // 结果:tip 一出现就顶在鼠标下方,EventSystem 下一帧重算射线时鼠标下最上层变成底图,
        // 悬停元素立刻收到 OnPointerExit → 刚播的 PlayOpen 被 PlayClose 杀掉。
        // 表现:鼠标放上去看不到(只有一帧),移开时反而看到极短的"出现+退出"动画。
        Graphic selfGraphic = GetComponent<Graphic>();
        if (selfGraphic != null) selfGraphic.raycastTarget = false;

        // UIPanelMotion 的约定:面板保持 inactive,由调用方负责 SetActive
        gameObject.SetActive(false);
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    // ============================================================
    // 对外 API
    // ============================================================

    /// <summary>
    /// 技能悬停内容:标题=技能名;正文=描述(主动技能取当前等级分支的描述);
    /// 其他=分支名 + 冷却 + 消耗 + 伤害 + 技能点。
    /// 主动技能(ActiveSkillData)按 level 取 ActiveBranchData,CD/蓝耗/伤害用分支里的实际值;
    /// 被动与普通技能回落到 SkillData 自身的 cooldown / manaCost。
    /// </summary>
    public static void ShowSkill(SkillData data, int level, RectTransform anchor)
    {
        if (Instance == null || data == null || anchor == null) return;

        string content = data.description;
        string tip = string.Empty;

        var active = data as ActiveSkillData;
        ActiveSkillData.ActiveBranchData branch =
            active != null ? active.GetBranchData(Mathf.Max(1, level)) : null;

        if (branch != null)
        {
            if (!string.IsNullOrEmpty(branch.description)) content = branch.description;

            if (!string.IsNullOrEmpty(branch.branchName)) tip = branch.branchName;
            if (branch.cooldown > 0f) tip += (tip.Length > 0 ? "　" : string.Empty) + $"冷却 {branch.cooldown:0.##}s";
            if (branch.manaCost > 0f) tip += (tip.Length > 0 ? "　" : string.Empty) + $"消耗 {branch.manaCost:0.##}";
            if (branch.damage > 0f) tip += (tip.Length > 0 ? "　" : string.Empty) + $"伤害 {branch.damage:0.##}";
        }
        else
        {
            if (data.cooldown > 0f) tip = $"冷却 {data.cooldown:0.##}s";
            if (data.manaCost > 0f) tip += (tip.Length > 0 ? "　" : string.Empty) + $"消耗 {data.manaCost:0.##}";
        }

        if (level > 0 && data.maxLevel > 0)
            tip = (tip.Length > 0 ? tip + "\n" : string.Empty) + $"技能点 {level}/{data.maxLevel}";

        Instance.Show(data.skillName, content, tip, anchor);
    }

    /// <summary>通用静态入口:三块文字 + 锚点元素。UI 各处直接调,不用各自持有引用</summary>
    public static void ShowText(string title, string content, string tip, RectTransform anchor)
    {
        if (Instance != null) Instance.Show(title, content, tip, anchor);
    }

    /// <summary>是否有元素被固定(选中)中 —— 悬停方据此让位,不去抢显示</summary>
    public static bool HasPinned => Instance != null && Instance._pinned != null;

    /// <summary>tip 是否固定在指定元素上(= 该元素处于选中态)</summary>
    public static bool IsPinnedTo(RectTransform anchor)
        => Instance != null && anchor != null && Instance._pinned == anchor;

    /// <summary>
    /// 固定/取消固定。固定到新元素时会把旧元素的选中高亮收掉(选中互斥,同时只有一个)。
    /// </summary>
    public static void SetPinned(RectTransform anchor, bool pinned)
    {
        if (Instance == null) return;

        if (!pinned)
        {
            // anchor 传 null = 无条件清(悬停换格子时用)
            if (anchor == null || Instance._pinned == anchor)
            {
                if (Instance._pinned != null)
                    Instance._pinned.GetComponent<ItemCell>()?.SetSelected(false);
                Instance._pinned = null;
            }
            return;
        }

        if (Instance._pinned != null && Instance._pinned != anchor)
            Instance._pinned.GetComponent<ItemCell>()?.SetSelected(false);
        Instance._pinned = anchor;
    }

    /// <summary>
    /// 鼠标移开时调(2026-09-26 改):
    ///   未固定(普通悬停) → 立即隐藏,保持原行为;
    ///   已固定(选中)     → 延迟隐藏;到点时鼠标还在 tip 上就再等一个延迟,直到移开才收。
    /// 选中态与格子高亮不受影响(取消选中仍走 Close/SetPinned)。
    /// </summary>
    public static void Hide()
    {
        if (Instance == null) return;
        Instance.RequestHide();
    }

    private void RequestHide()
    {
        if (!_visible) return;

        CancelPendingHide();

        if (_pinned == null)
        {
            HideSelf();
            return;
        }

        Invoke(nameof(DelayedHide), Mathf.Max(0f, hideDelay));
    }

    private void CancelPendingHide()
    {
        CancelInvoke(nameof(DelayedHide));
    }

    /// <summary>延迟到点:鼠标停在 tip 上就继续等,否则收起 tip(选中态与高亮保留)</summary>
    private void DelayedHide()
    {
        if (!_visible) return;

        if (IsMouseOverSelf())
        {
            Invoke(nameof(DelayedHide), Mathf.Max(0f, hideDelay));
            return;
        }

        HideSelf();
    }

    /// <summary>鼠标是否停在 tip 自己的矩形内(用矩形判定,与 raycast 开关无关)</summary>
    private bool IsMouseOverSelf()
    {
        if (_rect == null || !gameObject.activeInHierarchy) return false;

        Canvas canvas = GetComponentInParent<Canvas>();
        Camera cam = canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay
            ? canvas.worldCamera
            : null;

        return RectTransformUtility.RectangleContainsScreenPoint(_rect, Input.mousePosition, cam);
    }

    /// <summary>强制关闭(点 Btn_Back / 装备后):取消选中 + 隐藏</summary>
    public static void Close()
    {
        if (Instance != null) Instance.CloseInternal();
    }

    /// <summary>取消选中并隐藏</summary>
    public void CloseInternal()
    {
        CancelPendingHide();

        if (_pinned != null)
        {
            _pinned.GetComponent<ItemCell>()?.SetSelected(false);
            _pinned = null;
        }
        HideSelf();
    }

    /// <summary>
    /// 装备/物品的悬停内容:标题 = 物品名;正文 = 描述;其他 = 售价。
    /// 合成那两行(「通过 xx 合成」/「可以合成 xx」)按 saika 要求只预留位置,数据未接,
    /// 等配方口径定了在这里拼进 tip 参数即可,不用改 UI。
    /// </summary>
    public static void ShowItem(ItemSO so, RectTransform anchor)
    {
        if (Instance == null || so == null || anchor == null) return;

        string tip = so.sellPrice > 0 ? string.Format(Instance.sellPriceFormat, so.sellPrice) : string.Empty;
        // [预留] 合成/商店行拼在这里
        Instance.Show(so.itemName, so.description, tip, anchor);
    }

    /// <summary>通用入口:三块文字 + 锚点元素。空块自动收起(标题空收标题,正文空收内容区,其他空收 Tip)</summary>
    public void Show(string title, string content, string tip, RectTransform anchor)
    {
        if (anchor == null) return;

        bool hasTitle = !string.IsNullOrEmpty(title);
        bool hasContent = !string.IsNullOrEmpty(content);
        bool hasTip = !string.IsNullOrEmpty(tip);

        if (titleText != null)
        {
            titleText.text = title ?? string.Empty;
            titleText.gameObject.SetActive(hasTitle);
        }
        if (contentText != null)
            contentText.text = content ?? string.Empty;
        if (tipText != null)
        {
            tipText.text = tip ?? string.Empty;
            tipText.gameObject.SetActive(hasTip);
        }
        if (contentArea != null) contentArea.SetActive(hasContent);
        if (lineObj != null) lineObj.SetActive(hasTitle && (hasContent || hasTip));

        // 同一个锚点重复 Show(悬停那次之后单击又来一次)= 只换文字,不动窗口本身
        bool sameAnchorShown = _visible && _shownAnchor == anchor && gameObject.activeSelf;

        if (!sameAnchorShown)
        {
            gameObject.SetActive(true);
            Place(anchor);      // 换锚点/首次显示才重算位置/播出现动画

            // 出现动画保留(2026-09-22 saika 明确要求改回来)。
            // 动画期间整块穿透鼠标:滑入路径会经过悬停元素,不穿透就会把悬停打断
            // (Hide → OnPointerEnter → 再播一次),表现成窗口闪烁。播完恢复,Scroll View 照常能滚。
            if (motion != null)
            {
                SetRaycast(false);
                motion.PlayOpen(() => SetRaycast(true));
            }
            else
            {
                SetRaycast(true);
            }
        }
        _shownAnchor = anchor;
        CancelPendingHide();   // 重新显示时取消待隐藏(2026-09-26 加)

        // 面板关闭时跟着收掉(幂等,同一锚点只挂一个)
        if (anchor.GetComponent<AnchorWatcher>() == null)
            anchor.gameObject.AddComponent<AnchorWatcher>();

        _visible = true;
    }

    /// <summary>
    /// 挂在锚点上的看门狗:锚点自己或它的父级被禁用(ESC 关面板 / 切面板)时收掉 tip。
    /// 用 OnDisable 事件而不是每帧查 activeInHierarchy(2026-09-22 saika:不要 Update 轮询)。
    /// 面板 SetActive(false) 会让整棵子树收到 OnDisable,所以挂在锚点上即可覆盖所有面板。
    /// </summary>
    private class AnchorWatcher : MonoBehaviour
    {
        private void OnDisable()
        {
            UITooltip tip = Instance;
            if (tip != null && tip._visible) tip.CloseInternal();
        }
    }

    /// <summary>隐藏自身(幂等)</summary>
    public void HideSelf()
    {
        if (!_visible) return;
        _visible = false;

        if (motion != null && gameObject.activeSelf)
            motion.PlayClose(() => gameObject.SetActive(false));
        else
            gameObject.SetActive(false);
    }

    // ============================================================
    // 鼠标穿透
    // ============================================================

    /// <summary>
    /// 统一开关整块 UI 的射线接收(含底图与所有文字子物体)。
    /// 关掉的原因:滑入/滑出动画的路径会横穿屏幕,面板经过鼠标下方时会让悬停元素收到
    /// OnPointerExit → 调用方 Hide → 鼠标又落回元素 → OnPointerEnter → 再播一次动画,
    /// 表现就是「tip 一直重复出现、在闪」。动画期间整块穿透即可根治。
    /// Btn_Back 例外(始终保持可点);播完后全部恢复,正文的 Scroll View 照常可滚。
    /// </summary>
    private void SetRaycast(bool on)
    {
        foreach (var g in GetComponentsInChildren<Graphic>(true))
        {
            if (g.transform == transform)
                continue;   // 面板底图:永久穿透(双保险 —— 窄屏下 Clamp 可能把窗子拉到与格子重叠)
            if (closeButton != null && (g.transform == closeButton.transform || g.transform.IsChildOf(closeButton.transform)))
                continue;   // 关闭按钮始终可点
            g.raycastTarget = on;
        }
    }

    // ============================================================
    // 定位
    // ============================================================

    /// <summary>
    /// 把窗子摆到 anchor 旁边。水平右优先、放不下翻左(仍紧贴);
    /// 竖直与元素中心对齐,只做最小平移夹进屏幕 —— 不翻到另一端,免得离元素太远。
    /// </summary>
    private void Place(RectTransform anchor)
    {
        if (_rect == null) return;
        RectTransform parent = _rect.parent as RectTransform;
        if (parent == null) return;

        Canvas canvas = GetComponentInParent<Canvas>();
        Camera cam = canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay
            ? canvas.worldCamera
            : null;

        // 元素在屏幕上的矩形
        Vector3[] corners = new Vector3[4];
        anchor.GetWorldCorners(corners);
        Vector2 bl = RectTransformUtility.WorldToScreenPoint(cam, corners[0]);
        Vector2 tr = RectTransformUtility.WorldToScreenPoint(cam, corners[2]);
        float aLeft = Mathf.Min(bl.x, tr.x);
        float aRight = Mathf.Max(bl.x, tr.x);
        float aBottom = Mathf.Min(bl.y, tr.y);
        float aTop = Mathf.Max(bl.y, tr.y);

        // 尺寸用 sizeDelta:面板是固定尺寸摆好的,不依赖布局系统(<sizeDelta 与 rect.size 等价,但不吃布局未刷新)
        Vector2 size = _rect.sizeDelta;
        float w = size.x;
        float h = size.y;
        float sw = Screen.width;
        float sh = Screen.height;

        // 水平:优先右侧,放不下换左侧
        float left = aRight + gap;
        if (left + w > sw - edgeMargin)
            left = aLeft - gap - w;
        left = Mathf.Clamp(left, edgeMargin, Mathf.Max(edgeMargin, sw - edgeMargin - w));

        // 竖直:与元素中心对齐;超出屏幕只做最小平移
        float bottom = (aBottom + aTop) * 0.5f - h * 0.5f;
        bottom = Mathf.Clamp(bottom, edgeMargin, Mathf.Max(edgeMargin, sh - edgeMargin - h));

        // 屏幕坐标(以左下为原点) → 父级局部坐标 → anchoredPosition
        Vector2 screenCenter = new Vector2(left + w * 0.5f, bottom + h * 0.5f);
        Vector2 localPoint;
        if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(parent, screenCenter, cam, out localPoint))
            return;

        // anchoredPosition 的基准 = 父矩形内按自身 anchor 比例算出的参考点
        Vector2 anchorRef = parent.rect.min + Vector2.Scale(_rect.anchorMin, parent.rect.size);
        Vector2 pos = localPoint - anchorRef;

        if (motion != null) motion.SetHomePosition(pos);
        _rect.anchoredPosition = pos;

    }
}
