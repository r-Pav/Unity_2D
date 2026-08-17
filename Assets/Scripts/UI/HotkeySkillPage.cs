using UnityEngine;

/// <summary>
/// 快捷键打开技能合并页 — 挂任意常 active 节点（Canvas / 场景根空物体）。
/// keys 数组：任一按键按下都打开 targetPanel（合并页 SkillPages）。
/// 打开走 PanelManager.OpenPanel，FullScreen 互斥 + ESC 回退与其它页面一致。
/// </summary>
public class HotkeySkillPage : MonoBehaviour
{
    [Tooltip("触发按键列表，任一按下打开技能页")]
    [SerializeField] private KeyCode[] keys = new KeyCode[] { KeyCode.P, KeyCode.K };

    [Tooltip("要打开的合并页（SkillPages，挂 SkillPanelController）")]
    [SerializeField] private GameObject targetPanel;

    private void Update()
    {
        if (targetPanel == null) return;
        for (int i = 0; i < keys.Length; i++)
        {
            if (keys[i] != KeyCode.None && Input.GetKeyDown(keys[i]))
            {
                PanelManager.Instance?.OpenPanel(targetPanel);
                return;
            }
        }
    }
}
