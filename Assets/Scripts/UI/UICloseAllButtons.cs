using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 「返回游戏」按钮集中入口(2026-09-23 saika 定)— 挂常驻物体(推荐 Canvas),Inspector 里把各界面
/// 的关闭/退出按钮拖进 closeButtons 数组即可。
///
/// 语义 = 暂停菜单的「返回游戏」:不管当前叠了几层界面,一次全关回游戏。
/// 底层走 PanelManager.CloseAllPanels —— 面板栈与 FullScreen 历史一起清,不会在关闭过程中把某个
/// 旧面板恢复出来;统一淡出时长在 PanelManager 的「批量关闭(返回游戏)」分组里配(一处设置,
/// 各面板不用各自配动画)。
///
/// 注意:这些按钮若挂了 UIButtonFeedback,Click 音建议设 None(面板关闭音已由 PanelManager 统一播)。
/// 同一个按钮不要同时拖进本脚本与 UIEscBackButtons 的数组。
/// </summary>
public class UICloseAllButtons : MonoBehaviour
{
    [Tooltip("点一下 = 关掉所有面板直接回游戏。所有界面的「关闭/退出」按钮拖进来(空槽跳过)")]
    [SerializeField] private Button[] closeButtons;

    private void Awake()
    {
        if (closeButtons == null) return;

        for (int i = 0; i < closeButtons.Length; i++)
        {
            Button button = closeButtons[i];
            if (button == null) continue;
            button.onClick.AddListener(OnCloseAllClicked);
        }
    }

    /// <summary>全部关闭:不管几层,直接回游戏</summary>
    public void OnCloseAllClicked()
    {
        PanelManager.Instance?.CloseAllPanels();
    }
}
