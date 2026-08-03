using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 章节解锁测试按钮 — 挂 PassivePanel。
/// 自动遍历 5 个 LayerRow(每行一个 Button_test):
///   第 i 行按钮 → SetChapter(i+1)，解锁 TI~Ti 全部被动槽位。
/// 用于场景/章节机制调试，正式版本可移除。
/// </summary>
public class PassiveChapterTestButtons : MonoBehaviour
{
    [Tooltip("5 个 LayerRow（TI~TV），按顺序拖入；空则运行时按名字查找")]
    [SerializeField] private Transform[] layerRows;

    private PassiveEquipManager passiveEquipManager;

    private void Awake()
    {
        passiveEquipManager = GetComponentInParent<PassiveEquipManager>();
        if (passiveEquipManager == null)
            passiveEquipManager = FindObjectOfType<PassiveEquipManager>();

        if (layerRows == null || layerRows.Length == 0)
            layerRows = FindLayerRows();

        BindButtons();
    }

    /// <summary>按 TI~TV 命名查找 5 个行（挂 PassivePanel 时自动生效）</summary>
    private Transform[] FindLayerRows()
    {
        string[] names = { "LayerRow_I", "LayerRow_II", "LayerRow_III", "LayerRow_IV", "LayerRow_V" };
        var rows = new Transform[names.Length];
        for (int i = 0; i < names.Length; i++)
        {
            Transform t = transform.Find(names[i]);
            if (t == null)
                Debug.LogWarning($"[PassiveTest] 找不到 {names[i]}，请手动拖入 layerRows");
            rows[i] = t;
        }
        return rows;
    }

    private void BindButtons()
    {
        for (int i = 0; i < layerRows.Length; i++)
        {
            if (layerRows[i] == null) continue;

            Transform btn = layerRows[i].Find("Button_test");
            if (btn == null)
            {
                Debug.LogWarning($"[PassiveTest] {layerRows[i].name} 下找不到 Button_test");
                continue;
            }

            Button button = btn.GetComponent<Button>();
            if (button == null) button = btn.GetComponentInChildren<Button>();
            if (button == null) continue;

            int chapter = i + 1; // 第 i 行 → 章节 i+1
            button.onClick.AddListener(() =>
            {
                if (passiveEquipManager != null)
                {
                    passiveEquipManager.SetChapter(chapter);
                    Debug.Log($"[PassiveTest] 解锁到第 {chapter} 章（{passiveEquipManager.CurrentChapter}）");
                }
            });
        }
    }
}
