using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 技能合并页控制器 — 挂在 SkillPages 上（合并页单元，IPanel FullScreen）。
/// SkillPages = CraftPanel + SkillConfigPanel 同时显示。
/// 页面互斥 / ESC 回退由 PanelManager 的 FullScreen history 机制处理（与 git 原本一致）：
/// 打开技能树等其它页面时本页被替换关闭并记录，ESC 逐层恢复。
/// SkillPanel 为纯容器（不参与互斥），保证子页面互斥时父级不被关闭。
///
/// BG 门卫（2026-09-07 修复：技能页 → 技能树替换时 BG 被延迟关闭的旧页 OnDisable 灭掉）：
/// 技能系全屏页（SkillPages / SkillTreeUI / PassiveUI）在 OnEnable 把自己 Register 进注册表、
/// OnDisable Unregister；注册表非空 → 底图亮，空 → 底图灭。
/// 替换切换：新页先亮，旧页动画后延迟注销，注册表仍有新页 → BG 保持亮；全部退出才灭。
/// </summary>
public class SkillPanelController : MonoBehaviour, IPanel
{
    PanelType IPanel.PanelType => PanelType.FullScreen;
    bool IPanel.PauseGame => true;
    bool IPanel.LockInput => true;
    bool IPanel.ShowCursor => true;

    [Tooltip("技能页背景（SkillPanel 下），随技能系页面注册情况亮灭")]
    [SerializeField] private GameObject bg;

    public static SkillPanelController Instance { get; private set; }

    /// <summary>当前打开中的技能系全屏页（非空 = 底图应亮）</summary>
    private static readonly List<GameObject> _openPages = new List<GameObject>();

    private void Awake()
    {
        Instance = this;
    }

    /// <summary>技能系页面打开时注册自己（保持底图亮）</summary>
    public static void RegisterPage(GameObject page)
    {
        if (page == null) return;
        if (!_openPages.Contains(page))
            _openPages.Add(page);
        ApplyBg();
    }

    /// <summary>技能系页面关闭时注销自己（注册表空才灭底图）</summary>
    public static void UnregisterPage(GameObject page)
    {
        if (page != null)
            _openPages.Remove(page);
        ApplyBg();
    }

    /// <summary>按注册表是否非空刷新底图（清理销毁残留后判定）</summary>
    private static void ApplyBg()
    {
        _openPages.RemoveAll(p => p == null);   // 已销毁页面剔除
        bool any = _openPages.Count > 0;
        if (Instance != null && Instance.bg != null)
            Instance.bg.SetActive(any);
    }

    private void OnEnable()
    {
        RegisterPage(gameObject);
    }

    private void OnDisable()
    {
        UnregisterPage(gameObject);
    }

    /// <summary>打开合并页（HotkeySkillPage 等入口调用）</summary>
    public void Open()
    {
        PanelManager.Instance?.OpenPanel(gameObject);
    }
}
