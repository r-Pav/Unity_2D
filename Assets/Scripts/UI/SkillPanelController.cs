using UnityEngine;

/// <summary>
/// 技能合并页控制器 — 挂在 SkillPages 上（合并页单元，IPanel FullScreen）。
/// SkillPages = CraftPanel + SkillConfigPanel 同时显示。
/// 页面互斥 / ESC 回退由 PanelManager 的 FullScreen history 机制处理（与 git 原本一致）：
/// 打开技能树等其它页面时本页被替换关闭并记录，ESC 逐层恢复。
/// SkillPanel 为纯容器（不参与互斥），保证子页面互斥时父级不被关闭。
/// </summary>
public class SkillPanelController : MonoBehaviour, IPanel
{
    PanelType IPanel.PanelType => PanelType.FullScreen;
    bool IPanel.PauseGame => true;
    bool IPanel.LockInput => true;
    bool IPanel.ShowCursor => true;

    [Tooltip("技能页背景（SkillPanel 下），随合并页开关亮灭")]
    [SerializeField] private GameObject bg;

    public static SkillPanelController Instance { get; private set; }

    private void Awake()
    {
        Instance = this;
    }

    private void OnEnable()
    {
        if (bg != null) bg.SetActive(true);
    }

    private void OnDisable()
    {
        if (bg != null) bg.SetActive(false);
    }

    /// <summary>打开合并页（HotkeySkillPage 等入口调用）</summary>
    public void Open()
    {
        PanelManager.Instance?.OpenPanel(gameObject);
    }
}
