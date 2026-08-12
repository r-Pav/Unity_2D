using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 死亡面板 — 玩家死亡时弹出。
/// [2026-08-10] PauseGame 改 false：死亡后游戏世界继续运行（敌人照常行动），仅玩家输入被锁（PlayerDeadState.LocksInput）。
/// [2026-08-10] 复活按钮：绑定后点击 → PlayerHealth.Revive()（原地复活，不清位置；背包装备保留，身上 4 槽已掉落）。
/// 挂 Canvas 下的 DeathPanel GameObject。页面内容你自己在 Unity 里搭。
/// 注意：PanelManager 负责订阅 PlayerDeathEvent 并打开此面板，本类只声明 IPanel 接口 + 复活按钮绑定。
/// </summary>
public class DeathPanel : MonoBehaviour, IPanel
{
    public PanelType PanelType => PanelType.FullScreen;
    public bool PauseGame => false;   // 死亡不暂停游戏，世界继续
    public bool LockInput => true;
    public bool ShowCursor => true;

    [Header("复活")]
    [Tooltip("复活按钮（你在页面里加的）— 点击原地复活；拖到此处")]
    [SerializeField] private Button reviveButton;

    [Header("读档")]
    [Tooltip("读档按钮（页面里新增 Btn_Load）— 点击打开读档面板")]
    [SerializeField] private Button loadButton;
    [Tooltip("读档面板（Canvas > Panels > LoadPanel）")]
    [SerializeField] private GameObject loadPanel;

    private void OnEnable()
    {
        if (reviveButton != null)
            reviveButton.onClick.AddListener(OnReviveClicked);
        if (loadButton != null)
            loadButton.onClick.AddListener(OnLoadClicked);
    }

    private void OnDisable()
    {
        if (reviveButton != null)
            reviveButton.onClick.RemoveListener(OnReviveClicked);
        if (loadButton != null)
            loadButton.onClick.RemoveListener(OnLoadClicked);
    }

    private void OnReviveClicked()
    {
        // 原地复活：Revive() 不动位置；背包装备保留，身上 4 槽装备已在死亡动画末帧掉落
        PlayerHealth health = PlayerController.Instance != null
            ? PlayerController.Instance.GetComponent<PlayerHealth>()
            : null;
        health?.Revive();

        // 关闭死亡面板
        PanelManager.Instance?.CloseTopPanel();
    }

    private void OnLoadClicked()
    {
        // 打开读档面板（LoadPanel，SaveLoadPanel mode=Load）
        if (loadPanel != null)
            PanelManager.Instance?.OpenPanel(loadPanel);
    }
}
