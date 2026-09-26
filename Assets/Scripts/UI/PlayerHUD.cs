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

    [Header("通用")]
    [Tooltip("两条槽的最大宽度 (px)")]
    [SerializeField] private float barMaxWidth = 400f;

    // 缓存的基准值（无装备时的最大 HP/MP）
    private float _baseMaxHp;
    private float _baseMaxMp;
    private bool _baseValuesCached;

    void Awake()
    {
        // RectTransform 回退：未拖入时自动取 Slider 自身的 RectTransform
        if (hpBarRect == null && hpBar != null) hpBarRect = hpBar.GetComponent<RectTransform>();
        if (mpBarRect == null && mpBar != null) mpBarRect = mpBar.GetComponent<RectTransform>();
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
        hpBar.value = e.ratio;
        hpText.text = $"HP: {e.currentHealth:F0}/{e.maxHealth:F0}";
        UpdateBarWidth(hpBarRect, e.maxHealth, _baseMaxHp, hpMinWidth, hpThreshold);
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
