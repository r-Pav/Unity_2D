using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// [P7] HUD 技能栏 — 挂在 Canvas/HUD/SkillBarPanel 上。
/// 职责：读取 SkillPool + SkillManager 的当前装备数据，
///       更新 4 个槽位(Q/E/R/F)的图标/冷却/按键提示。
/// 通过 Inspector 暴露 4 组 UI 元素让用户手动拖拽绑定。
/// </summary>
public class SkillBarHUD : MonoBehaviour
{
    // ============================================================
    // Inspector 绑定 — 4 组槽位 UI 元素
    // ============================================================

    [Header("槽位 0 (Q)")]
    [SerializeField] private Image slot0Icon;
    [SerializeField] private TMP_Text slot0KeyText;
    [SerializeField] private Image slot0CooldownOverlay;
    [SerializeField] private TMP_Text slot0CooldownText;
    [SerializeField] private Button slot0Button;

    [Header("槽位 1 (E)")]
    [SerializeField] private Image slot1Icon;
    [SerializeField] private TMP_Text slot1KeyText;
    [SerializeField] private Image slot1CooldownOverlay;
    [SerializeField] private TMP_Text slot1CooldownText;
    [SerializeField] private Button slot1Button;

    [Header("槽位 2 (R)")]
    [SerializeField] private Image slot2Icon;
    [SerializeField] private TMP_Text slot2KeyText;
    [SerializeField] private Image slot2CooldownOverlay;
    [SerializeField] private TMP_Text slot2CooldownText;
    [SerializeField] private Button slot2Button;

    [Header("槽位 3 (F)")]
    [SerializeField] private Image slot3Icon;
    [SerializeField] private TMP_Text slot3KeyText;
    [SerializeField] private Image slot3CooldownOverlay;
    [SerializeField] private TMP_Text slot3CooldownText;
    [SerializeField] private Button slot3Button;

    [Header("默认按键文字（未绑定技能时显示）")]
    [SerializeField] private string[] defaultKeyLabels = { "Q", "E", "R", "F" };

    // ============================================================
    // 运行时引用
    // ============================================================

    private SkillManager skillManager;
    private SkillPool skillPool;

    // ============================================================
    // 生命周期
    // ============================================================

    private void Awake()
    {
        var player = PlayerController.Instance;
        if (player != null)
        {
            skillManager = player.GetComponent<SkillManager>();
            skillPool = player.GetComponent<SkillPool>();
        }
    }

    private void OnEnable()
    {
        // 订阅技能变化事件
        if (skillPool != null)
        {
            skillPool.OnPoolChanged += RefreshAll;
            skillPool.OnHudSlotChanged += RefreshSlot;
        }
        EventBus.Subscribe<SkillCooldownEndEvent>(OnCooldownEnd);
        EventBus.Subscribe<SkillLevelChangedEvent>(OnSkillLevelChanged);
        RefreshAll();
    }

    private void OnDisable()
    {
        if (skillPool != null)
        {
            skillPool.OnPoolChanged -= RefreshAll;
            skillPool.OnHudSlotChanged -= RefreshSlot;
        }
        EventBus.Unsubscribe<SkillCooldownEndEvent>(OnCooldownEnd);
        EventBus.Unsubscribe<SkillLevelChangedEvent>(OnSkillLevelChanged);
    }

    private void Update()
    {
        // 每帧更新冷却覆盖（冷却持续变化，不适合纯事件驱动）
        UpdateCooldownDisplay(0);
        UpdateCooldownDisplay(1);
        UpdateCooldownDisplay(2);
        UpdateCooldownDisplay(3);
    }

    // ============================================================
    // 刷新逻辑
    // ============================================================

    private void RefreshAll()
    {
        for (int i = 0; i < 4; i++) RefreshSlot(i);
    }

    /// <summary>刷新单个槽位：图标 + 按键文字</summary>
    private void RefreshSlot(int index)
    {
        var elements = GetSlotElements(index);
        if (elements.icon == null) return; // 未在 Inspector 中绑定

        var ownedSkill = skillPool?.GetHudSkill(index);
        bool hasSkill = ownedSkill != null && ownedSkill.skillData != null;

        // 图标
        elements.icon.enabled = hasSkill;
        if (hasSkill)
        {
            var active = ownedSkill.skillData as ActiveSkillData;
            elements.icon.sprite = active != null ? active.GetIconForLevel(ownedSkill.level) : ownedSkill.skillData.icon;
        }
        else
        {
            elements.icon.sprite = null;
        }

        // 按键文字
        if (elements.keyText != null)
        {
            elements.keyText.text = hasSkill
                ? GetKeyLabel(index)
                : (defaultKeyLabels != null && index < defaultKeyLabels.Length
                    ? defaultKeyLabels[index] : "");
        }

        // 按钮（可点击激活技能）
        if (elements.button != null)
        {
            elements.button.interactable = hasSkill;
            elements.button.onClick.RemoveAllListeners();
            if (hasSkill)
            {
                int capturedIndex = index;
                elements.button.onClick.AddListener(() => skillManager?.TryActivate(capturedIndex));
            }
        }
    }

    private void UpdateCooldownDisplay(int index)
    {
        if (skillManager == null) return;
        var elements = GetSlotElements(index);
        if (elements.cooldownOverlay == null && elements.cooldownText == null) return;

        float ratio = skillManager.GetCooldownRatio(index);
        float remaining = skillManager.GetCooldownTimer(index);

        if (elements.cooldownOverlay != null)
        {
            elements.cooldownOverlay.fillAmount = ratio;
            elements.cooldownOverlay.enabled = ratio > 0.01f;
        }

        if (elements.cooldownText != null)
        {
            elements.cooldownText.text = remaining > 0.1f ? remaining.ToString("F1") : "";
            elements.cooldownText.enabled = remaining > 0.1f;
        }
    }

    // ============================================================
    // 辅助方法
    // ============================================================

    private static string GetKeyLabel(int index) => index switch
    {
        0 => "Q", 1 => "E", 2 => "R", 3 => "F", _ => ""
    };

    // 事件回调
    private void OnCooldownEnd(SkillCooldownEndEvent e) { }
    private void OnSkillLevelChanged(SkillLevelChangedEvent e) => RefreshSlot(e.slotIndex);

    // ============================================================
    // 槽位元素辅助结构（减少重复代码）
    // ============================================================

    private SlotElements GetSlotElements(int index) => index switch
    {
        0 => new SlotElements(slot0Icon, slot0KeyText, slot0CooldownOverlay, slot0CooldownText, slot0Button),
        1 => new SlotElements(slot1Icon, slot1KeyText, slot1CooldownOverlay, slot1CooldownText, slot1Button),
        2 => new SlotElements(slot2Icon, slot2KeyText, slot2CooldownOverlay, slot2CooldownText, slot2Button),
        3 => new SlotElements(slot3Icon, slot3KeyText, slot3CooldownOverlay, slot3CooldownText, slot3Button),
        _ => new SlotElements(null, null, null, null, null)
    };

    private struct SlotElements
    {
        public Image icon;
        public TMP_Text keyText;
        public Image cooldownOverlay;
        public TMP_Text cooldownText;
        public Button button;

        public SlotElements(Image i, TMP_Text k, Image c, TMP_Text ct, Button b)
        {
            icon = i; keyText = k; cooldownOverlay = c; cooldownText = ct; button = b;
        }
    }
}
