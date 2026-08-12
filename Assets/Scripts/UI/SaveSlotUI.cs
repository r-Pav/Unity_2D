using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 存档槽条目组件 — 显示存档时间 + 摘要，以及删除按钮（Save 模式显示 / Load 模式隐藏）。
/// 槽主体 Button（槽位根上的 Button）由 SaveLoadPanel 在 OnEnable 绑定点击（保存/读档）。
/// </summary>
public class SaveSlotUI : MonoBehaviour
{
    [Tooltip("存档时间（有存档时）/ \"空存档位\"（空槽）")]
    [SerializeField] private TMP_Text timeText;

    [Tooltip("摘要：章节/技能点/三属性（有存档时）")]
    [SerializeField] private TMP_Text summaryText;

    [Tooltip("删除按钮 — Save 模式显示，Load 模式隐藏；点击由 SaveLoadPanel 绑定")]
    [SerializeField] public Button deleteButton;

    /// <summary>填充存档数据（时间 + 摘要），并控制删除按钮显隐</summary>
    public void SetData(SaveSystem.SlotMeta meta, bool showDelete)
    {
        if (timeText != null)
            timeText.text = meta.saveTime;
        if (summaryText != null)
            summaryText.text = string.Format("章节{0}  技能点{1}  STR {2} INT {3} AGI {4}",
                meta.chapter, meta.skillPoints, meta.str, meta.@int, meta.agi);
        if (deleteButton != null)
            deleteButton.gameObject.SetActive(showDelete);
    }

    /// <summary>显示空槽位</summary>
    public void SetEmpty()
    {
        if (timeText != null)
            timeText.text = "空存档位";
        if (summaryText != null)
            summaryText.text = "";
        if (deleteButton != null)
            deleteButton.gameObject.SetActive(false);
    }
}
