using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// 玩家 HUD — 血条、蓝条显示
/// 订阅 PlayerHealthChangedEvent / PlayerManaChangedEvent 事件驱动更新
/// 挂到 Canvas 下的 HUD GameObject 上，Slider + TMP_Text + Image 通过 Inspector 绑定
/// HP 槽宽度: min 200px → max 400px（maxHealth ≥ 400 时达上限）
/// MP 槽宽度: min 150px → max 400px（maxMana ≥ 400 时达上限）
/// </summary>
public class PlayerHUD : MonoBehaviour
{
    [SerializeField] private Slider hpBar;
    [SerializeField] private Slider mpBar;
    [SerializeField] private TMP_Text hpText;
    [SerializeField] private TMP_Text mpText;

    [Header("HP 槽宽度")]
    [Tooltip("HP Slider 的 RectTransform（留空则自动取 Slider 自身）")]
    [SerializeField] private RectTransform hpBarRect;
    [Tooltip("HP 槽最小宽度 (px)")]
    [SerializeField] private float hpMinWidth = 200f;
    [Tooltip("HP 槽达到最大宽度时对应的 maxHealth 值")]
    [SerializeField] private float hpThreshold = 400f;

    [Header("MP 槽宽度")]
    [Tooltip("MP Slider 的 RectTransform（留空则自动取 Slider 自身）")]
    [SerializeField] private RectTransform mpBarRect;
    [Tooltip("MP 槽最小宽度 (px)")]
    [SerializeField] private float mpMinWidth = 150f;
    [Tooltip("MP 槽达到最大宽度时对应的 maxMana 值")]
    [SerializeField] private float mpThreshold = 400f;

    [Header("HP 延迟条（掉血动画 · 第一步）")]
    [Tooltip("延迟条填充 Image。只需放在与 HP Slider 同一个父节点下，位置/尺寸/填充模式由代码接管（留空 = 不做掉血动画）")]
    [SerializeField] private Image hpDelayFill;

    [Tooltip("延迟条 RectTransform（留空则自动取上面 Image 自身的）")]
    [SerializeField] private RectTransform hpDelayRect;

    [Tooltip("（可留空，已弃用）延迟条的兜底父节点。正常情况代码会自动取血条 Fill 的父节点（Fill Area），只有取不到时才用这个")]
    [SerializeField] private RectTransform hpDelayAnchorSource;

    [Tooltip("掉血后延迟多久开始追赶（秒）")]
    [SerializeField] private float hpDelayStart = 0.35f;

    [Tooltip("追赶速度（每秒恢复多少比例，1 = 满条用 1 秒追完）")]
    [SerializeField] private float hpDelaySpeed = 0.6f;

    [Header("通用")]
    [Tooltip("两条槽的最大宽度 (px)")]
    [SerializeField] private float barMaxWidth = 400f;

    // 缓存的基准值（无装备时的最大 HP/MP）
    private float _baseMaxHp;
    private float _baseMaxMp;
    private bool _baseValuesCached;

    // HP 延迟条运行时状态
    private float _hpDelayValue;     // 延迟层当前填充量(0~1)
    private float _hpDelayTimer;     // 追赶启动倒计时
    private bool _hpDelayRunning;    // 是否正在追赶
    private bool _warnedDelaySource; // 参考源误拖只警告一次

    void Awake()
    {
        // RectTransform 回退：未拖入时自动取 Slider 自身的 RectTransform
        if (hpBarRect == null && hpBar != null) hpBarRect = hpBar.GetComponent<RectTransform>();
        if (mpBarRect == null && mpBar != null) mpBarRect = mpBar.GetComponent<RectTransform>();
        if (hpDelayRect == null && hpDelayFill != null) hpDelayRect = hpDelayFill.GetComponent<RectTransform>();
    }

    void Start()
    {
        CacheBaseValues();
        SyncBarsFromCurrentStats();
    }

    /// <summary>
    /// 启动时主动套一次槽宽与数值（2026-09-26 修）：
    /// 原先宽度只在 PlayerHealthChangedEvent 里更新，而玩家满血进 Play 时从不发这个事件，
    /// 于是「手动摆的宽度不会被代码纠正、穿装备后宽度也不变」。
    /// </summary>
    void SyncBarsFromCurrentStats()
    {
        var health = PlayerHealth.Instance;
        if (health != null)
        {
            UpdateBarWidth(hpBarRect, health.MaxHealth, _baseMaxHp, hpMinWidth, hpThreshold);

            if (hpBar != null)
                hpBar.value = health.MaxHealth > 0f ? health.CurrentHealth / health.MaxHealth : 0f;
            if (hpText != null)
                hpText.text = $"HP: {health.CurrentHealth:F0}/{health.MaxHealth:F0}";

            if (hpDelayFill != null)
            {
                EnsureDelayBarSetup();
                ApplyDelayBarFill(_hpDelayValue);
                _hpDelayValue = hpBar != null ? hpBar.value : 0f;
                _hpDelayRunning = false;
                SetDelayBarVisible(false);   // 满血进 Play 时不该有条图露着
            }
            else
            {
                Debug.LogWarning("[PlayerHUD][延迟条] 未绑定！请把 DelayBar 那个 Image 拖到 Hp Delay Fill 字段，否则不做掉血动画");
            }
        }

        var skillMgr = SkillManager.Instance;
        if (skillMgr != null)
        {
            UpdateBarWidth(mpBarRect, skillMgr.MaxMana, _baseMaxMp, mpMinWidth, mpThreshold);

            if (mpBar != null)
                mpBar.value = skillMgr.MaxMana > 0f ? skillMgr.CurrentMana / skillMgr.MaxMana : 0f;
            if (mpText != null)
                mpText.text = $"MP: {skillMgr.CurrentMana:F0}/{skillMgr.MaxMana:F0}";
        }
    }

    void OnEnable()
    {
        EventBus.Subscribe<PlayerHealthChangedEvent>(OnHPChanged);
        EventBus.Subscribe<PlayerManaChangedEvent>(OnMPChanged);
    }

    void OnDisable()
    {
        EventBus.Unsubscribe<PlayerHealthChangedEvent>(OnHPChanged);
        EventBus.Unsubscribe<PlayerManaChangedEvent>(OnMPChanged);
    }

    /// <summary>从 PlayerHealth / SkillManager 获取无装备时的基准值</summary>
    void CacheBaseValues()
    {
        var health = PlayerHealth.Instance;
        if (health != null)
            _baseMaxHp = health.BaseMaxHealth;
        else
            Debug.LogWarning("[PlayerHUD] 场景中未找到 PlayerHealth 组件，HP 槽宽度不会变化");

        var skillMgr = SkillManager.Instance;
        if (skillMgr != null)
            _baseMaxMp = skillMgr.BaseMaxMana;
        else
            Debug.LogWarning("[PlayerHUD] 场景中未找到 SkillManager 组件，MP 槽宽度不会变化");

        _baseValuesCached = true;
    }

    void OnHPChanged(PlayerHealthChangedEvent e)
    {
        if (!_baseValuesCached) CacheBaseValues();

        if (hpBar == null)
        {
            Debug.LogError("[PlayerHUD] hpBar 未绑定！请在 Inspector 中把 HP_Bar Slider 拖入 hpBar 槽位");
            return;
        }
        float oldValue = hpBar.value;
        hpBar.value = e.ratio;
        hpText.text = $"HP: {e.currentHealth:F0}/{e.maxHealth:F0}";
        UpdateBarWidth(hpBarRect, e.maxHealth, _baseMaxHp, hpMinWidth, hpThreshold);
        OnHpValueChanged(oldValue, e.ratio);
    }

    void OnMPChanged(PlayerManaChangedEvent e)
    {
        if (!_baseValuesCached) CacheBaseValues();

        if (mpBar == null)
        {
            Debug.LogError("[PlayerHUD] mpBar 未绑定！请在 Inspector 中把 MP_Bar Slider 拖入 mpBar 槽位");
            return;
        }
        mpBar.value = e.ratio;
        mpText.text = $"MP: {e.currentMana:F0}/{e.maxMana:F0}";
        UpdateBarWidth(mpBarRect, e.maxMana, _baseMaxMp, mpMinWidth, mpThreshold);
    }

    // ============================================================
    // HP 掉血动画 — 第一步：延迟条（2026-09-26）
    // 即时条立刻掉到新值，延迟条先停在旧值上，等 hpDelayStart 秒后匀速追下来，
    // 于是那段「刚失去的血量」会以浅色延迟条的形式停留一小会儿。
    // 回血时延迟层立即跟随（否则看起来比即时条还慢）。
    // ============================================================

    private void OnHpValueChanged(float oldValue, float newValue)
    {
        if (hpDelayFill == null) return;

        if (newValue < oldValue)
        {
            // 掉血：延迟条停在旧值（连续掉血取较高者，落后感累积不丢失），延迟启动后匀速追下来
            ShowHpDelay(Mathf.Max(_hpDelayValue, oldValue));
            _hpDelayTimer = Mathf.Max(0f, hpDelayStart);
            _hpDelayRunning = true;
        }
        else
        {
            // 回血 / 无变化：直接隐藏，不做"追赶"（否则比即时条还慢，看着别扭）
            _hpDelayValue = newValue;
            _hpDelayRunning = false;
            SetDelayBarVisible(false);
        }
    }

    /// <summary>延迟条追赶（unscaledDeltaTime：暂停、卡帧时也照常追完，不会留半截）</summary>
    private void UpdateHpDelayFill()
    {
        if (hpDelayFill == null || !_hpDelayRunning) return;

        if (_hpDelayTimer > 0f)
        {
            _hpDelayTimer -= Time.unscaledDeltaTime;
            return;
        }

        float target = hpBar != null ? hpBar.value : 0f;
        float speed = Mathf.Max(0.01f, hpDelaySpeed);
        _hpDelayValue = Mathf.MoveTowards(_hpDelayValue, target, speed * Time.unscaledDeltaTime);
        ApplyDelayBarFill(_hpDelayValue);

        if (Mathf.Approximately(_hpDelayValue, target))
        {
            _hpDelayValue = target;
            _hpDelayRunning = false;
            SetDelayBarVisible(false);   // 追上了 → 整条收起，不留一条满图在那露着
        }
    }

    /// <summary>显示延迟条并把填充量设成指定值</summary>
    private void ShowHpDelay(float value)
    {
        if (hpDelayFill == null) return;
        _hpDelayValue = value;
        ApplyDelayBarFill(value);
        SetDelayBarVisible(true);
    }

    /// <summary>延迟条显隐（只在追赶期间显示，追完/回血/满血一律收起）</summary>
    private void SetDelayBarVisible(bool on)
    {
        if (hpDelayFill == null) return;
        GameObject go = hpDelayFill.gameObject;
        if (go.activeSelf != on) go.SetActive(on);
    }

    /// <summary>
    /// 延迟条接线（一次性）：挂到与即时条 Fill 同一个父节点下、排在 Fill 之前，
    /// 并让 Image 的类型与图片跟即时条保持一致。
    /// 之后宽度全靠 anchor 表达比例，父节点（血条轨道）变宽会自动跟随，不需要再同步尺寸。
    /// </summary>
    private void EnsureDelayBarSetup()
    {
        if (hpDelayFill == null || hpDelayRect == null) return;

        RectTransform refFill = hpBar != null ? hpBar.fillRect : null;
        if (refFill == null) return;

        // 参考父节点必须是「Fill Area」= 即时条 Fill 的父节点（它的宽度才是满血条轨道）。
        // 注意 Unity 语义：Slider.fillRect 指的是那个随血量伸缩的「Fill」本身，不是轨道，
        // 所以这里取的是它的 parent。手动参考源只作兜底，且如果拖的正是 Fill 会被忽略
        // （2026-09-26 saika 抓到写反：之前优先用手动参考源，挂到了 Fill 下面，于是又跟着一起缩）。
        RectTransform targetParent = refFill.parent as RectTransform;

        if (targetParent == null || targetParent == hpBarRect)
            targetParent = hpDelayAnchorSource;

        if (targetParent == refFill)          // 误拖成 Fill → 用它自己的父（Fill Area）
            targetParent = refFill.parent as RectTransform;

        if (targetParent == null) return;

        // ① 父级对齐：anchor 比例是相对父节点的，父必须与即时条一致，否则宽度基准不同
        if (hpDelayRect.parent != targetParent)
        {
            hpDelayRect.SetParent(targetParent, false);
            if (!_warnedDelaySource)
            {
                _warnedDelaySource = true;
                Debug.Log($"[PlayerHUD][延迟条] 已把 {hpDelayRect.name} 自动挂到「{targetParent.name}」下（与血条 Fill 同父）；" +
                          "建议在编辑器里手动挪过去，否则每次进 Play 都要重挂一次");
            }
        }

        // ② 层级：排在 Fill 之前（UI 靠前的在下层，即时条才能盖住延迟条，只露右边多出的那段）
        if (hpDelayRect.GetSiblingIndex() >= refFill.GetSiblingIndex())
            hpDelayRect.SetSiblingIndex(Mathf.Max(0, refFill.GetSiblingIndex() - 1));

        // ③ 图片与类型照抄即时条：同一张切片图 + 同一种拉伸方式，视觉才一致
        //    （早前强制设成 Filled 是错的：那是按比例裁剪，右侧圆角/边框会被切平）
        Image refImage = refFill.GetComponent<Image>();
        if (refImage != null)
        {
            if (hpDelayFill.type != refImage.type)
                hpDelayFill.type = refImage.type;
            if (hpDelayFill.sprite == null)
                hpDelayFill.sprite = refImage.sprite;
        }
    }

    /// <summary>
    /// 按比例设置延迟条宽度（anchor 驱动，与 Slider 的 Fill 同构）。
    /// 纵向 anchor / offset / pivot 照抄即时条，保证两条完全重合，只有右边多出来的那段会露出来。
    /// </summary>
    private void ApplyDelayBarFill(float value)
    {
        if (hpDelayRect == null) return;

        RectTransform refFill = hpBar != null ? hpBar.fillRect : null;

        float yMin = 0f, yMax = 1f;
        Vector2 offMin = Vector2.zero, offMax = Vector2.zero;
        Vector2 pivot = new Vector2(0f, 0.5f);

        if (refFill != null && refFill != hpDelayRect)
        {
            yMin = refFill.anchorMin.y;
            yMax = refFill.anchorMax.y;
            offMin = refFill.offsetMin;
            offMax = refFill.offsetMax;
            pivot = refFill.pivot;
        }

        hpDelayRect.anchorMin = new Vector2(0f, yMin);
        hpDelayRect.anchorMax = new Vector2(Mathf.Clamp01(value), yMax);
        hpDelayRect.offsetMin = offMin;
        hpDelayRect.offsetMax = offMax;
        hpDelayRect.pivot = pivot;
        hpDelayRect.localScale = Vector3.one;
        hpDelayRect.localRotation = Quaternion.identity;
    }

    void Update()
    {
        UpdateHpDelayFill();
    }

    /// <summary>
    /// 根据当前最大属性值动态调整 Bar 的 RectTransform 宽度
    /// 在 [minWidth, barMaxWidth] 之间线性插值，基准值为 minWidth，threshold 处达到 maxWidth
    /// 超出 threshold 后保持 maxWidth 不变
    /// </summary>
    void UpdateBarWidth(RectTransform barRect, float currentMax, float baseMax, float minWidth, float threshold)
    {
        if (barRect == null) return;
        if (baseMax <= 0f) return;

        // threshold 必须大于 baseMax 才构成线性区间。配置反了（例如 initialHealth 从 5 调到 1000 后
        // 忘记同步改 threshold）时退化为「不超过基础血量 = 最短宽度，超过 = 直接取最大宽度」，
        // 而不是像旧代码那样整体 return 导致宽度永远不更新（2026-09-26 修）。
        float width;
        if (threshold > baseMax)
        {
            float t = Mathf.Clamp01(Mathf.InverseLerp(baseMax, threshold, currentMax));
            width = Mathf.Lerp(minWidth, barMaxWidth, t);
        }
        else
        {
            width = currentMax > baseMax ? barMaxWidth : minWidth;
        }

        float oldWidth = barRect.sizeDelta.x;
        Vector2 size = barRect.sizeDelta;
        size.x = width;
        barRect.sizeDelta = size;

        float delta = width - oldWidth;
        Vector2 pos = barRect.anchoredPosition;
        pos.x += delta * barRect.pivot.x;
        barRect.anchoredPosition = pos;
    }
}
