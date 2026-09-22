using TMPro;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;

/// <summary>
/// [P7] 技能列表条目组件 — 挂在 SkillConfigUI 左栏的技能条目 Prefab 上。
/// 负责绑定自身 UI 元素、显示 OwnedSkillEntry 数据，并作为拖拽源。
/// </summary>
public class SkillListEntry : MonoBehaviour, IBeginDragHandler, IDragHandler, IEndDragHandler, IPointerDownHandler, IPointerUpHandler
{
    // ============================================================
    // Inspector 绑定 — 显示元素
    // ============================================================

    [SerializeField] private Image icon;
    [SerializeField] private TMP_Text nameText;
    [SerializeField] private TMP_Text levelText;

    // ============================================================
    // Inspector 绑定 — 拖拽
    // ============================================================

    [Header("拖拽")]
    [SerializeField] private CanvasGroup canvasGroup;
    [SerializeField] private float dragAlpha = 0.6f;
    [Tooltip("长按时填充的圆形指示器（Image，Fill Method=Radial360）")]
    [SerializeField] private Image holdIndicator;

    // ============================================================
    // 私有状态
    // ============================================================

    private OwnedSkillEntry _entry;
    private GameObject _dragGhost;
    private RectTransform _dragGhostRect;
    private ScrollRect _parentScrollRect;

    private bool _isDragging;
    private bool _isHolding;
    private float _holdTimer;
    private const float HoldThreshold = 0.5f;

    // ============================================================
    // 生命周期
    // ============================================================

    private void OnDestroy()
    {
        // 自己被 Destroy 时清理幽灵（防止悬空残留）
        CleanupDragGhost();
    }

    private void OnDisable()
    {
        // 被禁用时也清理（列表刷新等场景）
        CleanupDragGhost();
        _isHolding = false;
        _isDragging = false;
        if (holdIndicator != null)
            holdIndicator.enabled = false;
    }

    private void CleanupDragGhost()
    {
        if (_dragGhost != null)
        {
            Destroy(_dragGhost);
            _dragGhost = null;
            _dragGhostRect = null;
        }
    }

    // ============================================================
    // 公开属性
    // ============================================================

    /// <summary>当前显示的技能条目（供 SkillHudSlot 读取）</summary>
    public OwnedSkillEntry CurrentEntry => _entry;

    // ============================================================
    // 初始化
    // ============================================================

    private void Awake()
    {
        _parentScrollRect = GetComponentInParent<ScrollRect>();
    }

    /// <summary>
    /// 绑定悬停提示(2026-09-22)。条目带拖拽,所以走 EventTrigger,不动拖拽接口;
    /// Setup 会被反复调用,这里做幂等(已挂就不再添加 entry)。
    /// </summary>
    private void BindHoverTooltip()
    {
        EventTrigger trigger = GetComponent<EventTrigger>();
        if (trigger == null)
        {
            trigger = gameObject.AddComponent<EventTrigger>();

            var enter = new EventTrigger.Entry { eventID = EventTriggerType.PointerEnter };
            enter.callback.AddListener(_ => ShowEntryTooltip());
            trigger.triggers.Add(enter);

            var exit = new EventTrigger.Entry { eventID = EventTriggerType.PointerExit };
            exit.callback.AddListener(_ => UITooltip.Hide());
            trigger.triggers.Add(exit);
        }
    }

    /// <summary>悬停技能列表条目:把该条目的技能交给 tooltip</summary>
    private void ShowEntryTooltip()
    {
        if (_entry == null || _entry.skillData == null) return;
        UITooltip.ShowSkill(_entry.skillData, _entry.level, (RectTransform)transform);
    }

    public void Setup(OwnedSkillEntry entry)
    {
        BindHoverTooltip();
        _entry = entry;
        if (entry == null) return;

        if (icon != null && entry.skillData != null)
        {
            var active = entry.skillData as ActiveSkillData;
            icon.sprite = active != null ? active.GetIconForLevel(entry.level) : entry.skillData.icon;
            icon.enabled = true;
        }
        if (nameText != null) nameText.text = entry.skillData?.skillName ?? entry.id;
        if (levelText != null) levelText.text = $"Lv{entry.level}";
    }

    // ============================================================
    // 指针按下/抬起 — IPointerDownHandler / IPointerUpHandler
    // ============================================================

    public void OnPointerDown(PointerEventData eventData)
    {
        if (_entry == null || _entry.skillData == null) return;

        // [2026-09-22 saika] 不再需要长按状态,按下只重置拖拽标记
        _isDragging = false;
        _isHolding = false;
        if (holdIndicator != null) holdIndicator.enabled = false;
    }

    public void OnPointerUp(PointerEventData eventData)
    {
        _isHolding = false;
        if (!_isDragging && holdIndicator != null)
            holdIndicator.enabled = false;
    }

    // ============================================================
    // 拖拽接口
    // ============================================================

    public void OnBeginDrag(PointerEventData eventData)
    {
        if (_isDragging) return;

        // [2026-09-22 saika] 去掉长按:按下就拖,和背包装备一致(那边 OnBeginDrag 直接 BeginDrag)。
        if (_entry == null || _entry.skillData == null) return;

        _isHolding = false;
        if (holdIndicator != null) holdIndicator.enabled = false;

        StartRealDrag();
        if (_dragGhostRect != null) _dragGhostRect.position = eventData.position;
    }

    public void OnDrag(PointerEventData eventData)
    {
        if (_isDragging)
        {
            if (_dragGhostRect != null)
                _dragGhostRect.position = eventData.position;
            return;
        }

        // 未激活拖拽 → 交给 ScrollRect 滚动
        if (_parentScrollRect != null)
            _parentScrollRect.OnDrag(eventData);
    }

    public void OnEndDrag(PointerEventData eventData)
    {
        _isHolding = false;

        if (_isDragging)
        {
            if (canvasGroup != null)
            {
                canvasGroup.alpha = 1f;
                canvasGroup.blocksRaycasts = true;
            }
            if (_dragGhost != null)
            {
                Destroy(_dragGhost);
                _dragGhost = null;
                _dragGhostRect = null;
            }
        }
        else
        {
            if (holdIndicator != null)
                holdIndicator.enabled = false;
            if (_parentScrollRect != null)
                _parentScrollRect.OnEndDrag(eventData);
        }
    }

    /// <summary>确认水平拖拽意图后，正式开始技能拖拽</summary>
    private void StartRealDrag()
    {
        _isDragging = true;

        if (canvasGroup != null)
        {
            canvasGroup.alpha = dragAlpha;
            canvasGroup.blocksRaycasts = false;
        }

        _dragGhost = CreateDragGhost();
        if (_dragGhost != null)
        {
            _dragGhost.transform.SetParent(GetCanvasRoot(), false);
            _dragGhost.transform.SetAsLastSibling();
        }
    }

    // ============================================================
    // 拖拽辅助方法
    // ============================================================

    /// <summary>创建一个跟随鼠标的半透明技能图标</summary>
    private GameObject CreateDragGhost()
    {
        var ghost = new GameObject("DragGhost", typeof(RectTransform), typeof(CanvasGroup), typeof(Image));
        _dragGhostRect = ghost.GetComponent<RectTransform>();
        _dragGhostRect.sizeDelta = new Vector2(64, 64);

        var img = ghost.GetComponent<Image>();
        var active = _entry.skillData as ActiveSkillData;
        img.sprite = active != null ? active.GetIconForLevel(_entry.level) : _entry.skillData.icon;
        img.raycastTarget = false;

        var cg = ghost.GetComponent<CanvasGroup>();
        cg.alpha = 0.7f;
        cg.blocksRaycasts = false;

        return ghost;
    }

    /// <summary>向上查找 Canvas 根节点</summary>
    private Transform GetCanvasRoot()
    {
        var canvas = GetComponentInParent<Canvas>();
        return canvas != null ? canvas.transform : transform.root;
    }
}
