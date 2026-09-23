using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 「返回」按钮集中入口(2026-09-23 saika 定)— 挂常驻物体(推荐 Canvas),Inspector 里把各面板的
/// 返回/关闭按钮拖进 backButtons 数组即可,不用每个面板各写一份 onClick。
///
/// 语义 = ESC 的返回:逐层退栈顶那一层(PanelManager.CloseTopPanel)。
/// 若栈顶盖掉了某个 FullScreen 面板,CloseTopPanel 会把它恢复回来(技能树 → 回到被动页那种)。
/// ESC 的另一半(栈空时打开 ESC 菜单)不在这里:按钮长在面板上,栈不会空。
///
/// 注意:这些按钮若挂了 UIButtonFeedback,Click 音建议设 None —— 面板关闭时 PanelManager
/// 已经统一播过一次关闭音,不设会双响(与各面板 Btn_Back 同款规则)。
/// </summary>
public class UIEscBackButtons : MonoBehaviour
{
    [Tooltip("点一下 = ESC 返回一层。把所有界面的返回/关闭按钮拖进来(空槽跳过)")]
    [SerializeField] private Button[] backButtons;

    private void Awake()
    {
        if (backButtons == null) return;

        for (int i = 0; i < backButtons.Length; i++)
        {
            Button button = backButtons[i];
            if (button == null) continue;
            button.onClick.AddListener(OnBackClicked);
        }
    }

    /// <summary>退一层:与 ESC 返回完全等价</summary>
    public void OnBackClicked()
    {
        PanelManager.Instance?.CloseTopPanel();
    }
}
