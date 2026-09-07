using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// 能量球进度 HUD(技能点获取进度条)— 挂 HUD Canvas 下的容器物体。
/// 数据语义:能量进度 0~99,满 100 自动兑换 1 技能点归 0;技能点满 99 时进度停 99。
/// 数据源:SkillPointManager(玩家身上);事件 PlayerEnergyProgressChangedEvent(进度)/ PlayerSkillPointsChangedEvent(技能点)。
/// UI 布局 saika 搭:本组件只做数据 → fillAmount/文本 的同步,不含任何布局。
/// </summary>
public class EnergyProgressHUD : MonoBehaviour
{
    [Tooltip("进度条 Slider(二选一,优先于 Fill Image;建议设 Min 0 / Max 1,删掉 Handle 只留 Fill Area)")]
    [SerializeField] private Slider progressSlider;

    [Tooltip("进度条填充 Image(Image Type = Filled;fillAmount = 进度 / 100;用 Slider 时可不拖)")]
    [SerializeField] private Image fillImage;

    [Tooltip("可选:状态文本,如 技能点 12/99 · 能量 34/100")]
    [SerializeField] private TextMeshProUGUI label;

    private SkillPointManager _spm;

    private void OnEnable()
    {
        EventBus.Subscribe<PlayerEnergyProgressChangedEvent>(OnEnergyChanged);
        EventBus.Subscribe<PlayerSkillPointsChangedEvent>(OnSkillPointsChanged);
        Refresh();
    }

    private void OnDisable()
    {
        EventBus.Unsubscribe<PlayerEnergyProgressChangedEvent>(OnEnergyChanged);
        EventBus.Unsubscribe<PlayerSkillPointsChangedEvent>(OnSkillPointsChanged);
    }

    private SkillPointManager Spm()
    {
        if (_spm == null && PlayerController.Instance != null)
            _spm = PlayerController.Instance.GetComponent<SkillPointManager>();
        return _spm;
    }

    private void OnEnergyChanged(PlayerEnergyProgressChangedEvent e) => Refresh();
    private void OnSkillPointsChanged(PlayerSkillPointsChangedEvent e) => Refresh();

    private void Refresh()
    {
        SkillPointManager spm = Spm();
        if (spm == null) return;

        int progress = spm.EnergyProgress;
        int points = spm.CurrentSkillPoints;
        int max = spm.MaxSkillPoints;

        if (progressSlider != null)
        {
            progressSlider.minValue = 0f;
            progressSlider.maxValue = 100f;
            progressSlider.value = progress;
        }
        else if (fillImage != null)
        {
            fillImage.fillAmount = progress / 100f;
        }

        if (label != null)
            label.text = $"技能点 {points}/{max} · 能量 {progress}/100";
    }
}
