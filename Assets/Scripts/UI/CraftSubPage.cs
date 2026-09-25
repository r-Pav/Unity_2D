using UnityEngine;

/// <summary>
/// 合成界面里的子页（2026-09-25）—— 挂在主面板的子页物体上（如 Canvas/Create/Description）。
///
/// 作用：让子页也进 PanelManager 的栈，于是 ESC 是「先收子页、再关主面板」的分层关闭，
/// 而不是一下把整个合成面板关掉。开关走 CraftMakePanel 的 Toggle / Close 方法（内部调 PanelManager），
/// 重复调用有面板管理器那一层的幂等守卫，不会重播动效。
///
/// 口径（它只是主面板里的一页，不独立承担交互态）：
///   · Dialog 类型：与主面板（FullScreen）共存，不会把主面板顶掉
///   · PauseGame / LockInput / ShowCursor 全 false：暂停与锁输入由主面板那层负责，子页不重复声明
///
/// 没有任何序列化字段 —— 挂上即可，不用拖引用。
/// </summary>
public class CraftSubPage : MonoBehaviour, IPanel
{
    public PanelType PanelType => PanelType.Dialog;

    public bool PauseGame => false;

    public bool LockInput => false;

    public bool ShowCursor => false;
}
