using UnityEngine;

/// <summary>
/// 死亡面板 — 玩家死亡时弹出，暂停游戏。
/// 挂 Canvas 下的 DeathPanel GameObject。页面内容你自己在 Unity 里搭。
/// 注意：PanelManager 负责订阅 PlayerDeathEvent 并打开此面板，本类只声明 IPanel 接口。
/// </summary>
public class DeathPanel : MonoBehaviour, IPanel
{
    public PanelType PanelType => PanelType.FullScreen;
    public bool PauseGame => true;
    public bool LockInput => true;
    public bool ShowCursor => true;
}
