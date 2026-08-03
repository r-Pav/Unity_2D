using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// Boss 血条 UI — 挂到 Canvas 下的 Boss 血条 GameObject 上。
/// 订阅 BossActivatedEvent / BossHpChangedEvent / BossPhaseChangedEvent / BossDefeatedEvent，
/// 通过 currentBoss 引用过滤，只响应所属 Boss 的事件。
/// 不同 Boss 各自挂一份，Inspector 独立配置阶段颜色。
/// </summary>
public class BossHealthBar : MonoBehaviour
{
    [Header("UI 绑定")]
    [SerializeField] private Slider hpSlider;
    [SerializeField] private TMP_Text hpText;
    [SerializeField] private TMP_Text nameText;
    [SerializeField] private GameObject barRoot;

    [Header("阶段颜色")]
    [SerializeField] private Color p1Color = new Color(0.9f, 0.2f, 0.2f);
    [SerializeField] private Color p2Color = new Color(1f, 0.55f, 0f);
    [SerializeField] private Color p3Color = new Color(0.7f, 0.15f, 0.7f);

    private BossControllerBase _currentBoss;
    private Image _fillImage;

    void Awake()
    {
        if (barRoot != null)
            barRoot.SetActive(false);

        if (hpSlider != null && hpSlider.fillRect != null)
            _fillImage = hpSlider.fillRect.GetComponent<Image>();
    }

    void OnEnable()
    {
        EventBus.Subscribe<BossActivatedEvent>(OnBossActivated);
        EventBus.Subscribe<BossDefeatedEvent>(OnBossDefeated);
        EventBus.Subscribe<BossHpChangedEvent>(OnBossHpChanged);
        EventBus.Subscribe<BossPhaseChangedEvent>(OnBossPhaseChanged);
    }

    void OnDisable()
    {
        EventBus.Unsubscribe<BossActivatedEvent>(OnBossActivated);
        EventBus.Unsubscribe<BossDefeatedEvent>(OnBossDefeated);
        EventBus.Unsubscribe<BossHpChangedEvent>(OnBossHpChanged);
        EventBus.Unsubscribe<BossPhaseChangedEvent>(OnBossPhaseChanged);
    }

    void OnBossActivated(BossActivatedEvent e)
    {
        _currentBoss = e.boss;

        if (barRoot != null)
            barRoot.SetActive(true);

        if (nameText != null && e.boss != null)
            nameText.text = e.boss.BossName;

        UpdateBar(e.currentHp, e.maxHp);
        SetPhaseColor(_currentBoss != null ? _currentBoss.CurrentPhase : 0);
    }

    void OnBossDefeated(BossDefeatedEvent e)
    {
        if (e.boss != _currentBoss) return;

        if (barRoot != null)
            barRoot.SetActive(false);

        _currentBoss = null;
    }

    void OnBossHpChanged(BossHpChangedEvent e)
    {
        if (e.boss != _currentBoss) return;
        UpdateBar(e.currentHp, e.maxHp);
    }

    void OnBossPhaseChanged(BossPhaseChangedEvent e)
    {
        if (e.boss != _currentBoss) return;
        SetPhaseColor(e.newPhase);
    }

    void UpdateBar(float cur, float max)
    {
        if (hpSlider != null)
            hpSlider.value = max > 0f ? cur / max : 0f;

        if (hpText != null)
            hpText.text = $"{cur:F0} / {max:F0}";
    }

    void SetPhaseColor(int phase)
    {
        if (_fillImage == null) return;

        _fillImage.color = phase switch
        {
            0 => p1Color,
            1 => p2Color,
            _ => p3Color
        };
    }
}
