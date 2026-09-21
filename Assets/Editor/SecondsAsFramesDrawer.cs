using System.Globalization;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 「秒.帧@30」文本 ↔ 秒 的互转(标点时间统一写法:9.08 = 9 秒 08 帧 = 9.2667 秒,帧号 00~29)。
/// 表格工具(MusicSheetEditorWindow)与 Inspector 绘制器共用这一份,别再各写一套。
/// </summary>
public static class MusicTimeText
{
    public const int FramesPerSecond = 30;

    /// <summary>秒 → 秒.帧 文本:9.2667 → "9.08"(帧号两位,不足补零)</summary>
    public static string ToFramesText(float seconds)
    {
        int sec = Mathf.FloorToInt(seconds);
        int frames = Mathf.RoundToInt((seconds - sec) * FramesPerSecond);
        if (frames >= FramesPerSecond) { sec += 1; frames -= FramesPerSecond; }
        return sec.ToString(CultureInfo.InvariantCulture) + "." + frames.ToString("00", CultureInfo.InvariantCulture);
    }

    /// <summary>秒.帧 文本 → 秒:9.08 → 9.2667。一位小数按两位读(Excel/手敲会吃掉尾零:12.1 = 12 秒 10 帧)。
    /// 只收非负、无符号无指数、帧号 0~29 的写法;不合法返回 false(调用方保持原值,不猜)。</summary>
    public static bool TryParseFramesText(string text, out float seconds)
    {
        seconds = 0f;
        string s = (text ?? string.Empty).Trim();
        if (s.Length == 0) return false;

        int dot = s.IndexOf('.');
        string secPart = dot < 0 ? s : s.Substring(0, dot);
        string framePart = dot < 0 ? "00" : s.Substring(dot + 1);
        if (framePart.Length == 1) framePart += "0";                    // 一位小数按两位读:12.1 = 12 秒 10 帧
        if (framePart.Length == 0 || framePart.Length > 2) return false; // 两位以上小数不是标点写法

        if (!int.TryParse(secPart, NumberStyles.None, CultureInfo.InvariantCulture, out int sec)) return false;
        if (!int.TryParse(framePart, NumberStyles.None, CultureInfo.InvariantCulture, out int frames)) return false;
        if (frames >= FramesPerSecond) return false;                     // 帧号 00~29

        seconds = sec + frames / (float)FramesPerSecond;
        return true;
    }
}

/// <summary>
/// 带 [SecondsAsFrames] 的 float[] 数组:Inspector 里按「秒.帧」显示与输入,存进去仍是秒。
/// 每行一个点(帧号两位);表头可改长度;鼠标悬停显示真实的秒值。
/// 解析不合法 = 保持原值(不写脏数据);写法见 MusicTimeText。
/// </summary>
[CustomPropertyDrawer(typeof(SecondsAsFramesAttribute))]
public class SecondsAsFramesDrawer : PropertyDrawer
{
    private const float Pad = 2f;

    public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
    {
        float line = EditorGUIUtility.singleLineHeight;
        float gap = EditorGUIUtility.standardVerticalSpacing;
        if (!property.isArray) return line;
        if (!property.isExpanded) return line;
        return line + gap + (property.arraySize + 1) * (line + gap) + Pad;
    }

    public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
    {
        float line = EditorGUIUtility.singleLineHeight;
        float gap = EditorGUIUtility.standardVerticalSpacing;

        if (!property.isArray)     // 理论上不会:这个标记只挂在轮询数组上
        {
            EditorGUI.PropertyField(position, property, label);
            return;
        }

        Rect head = new Rect(position.x, position.y, position.width, line);
        property.isExpanded = EditorGUI.Foldout(head, property.isExpanded,
            new GUIContent(label.text + "(秒.帧)", "内部存秒,这里按 秒.帧@30 显示与输入:9.08 = 9 秒 08 帧 = 9.2667 秒。悬停某一行可看它真实存的秒值"));
        if (!property.isExpanded) return;

        Rect sizeRect = new Rect(position.x + 12f, head.y + line + gap, position.width - 12f, line);
        EditorGUI.BeginChangeCheck();
        int newSize = EditorGUI.IntField(sizeRect, "长度", property.arraySize);
        if (EditorGUI.EndChangeCheck() && newSize != property.arraySize)
            property.arraySize = Mathf.Max(0, newSize);

        float y = sizeRect.y + line + gap;
        for (int i = 0; i < property.arraySize; i++)
        {
            SerializedProperty child = property.GetArrayElementAtIndex(i);
            Rect r = new Rect(position.x + 12f, y, position.width - 12f, line);
            y += line + gap;

            var gc = new GUIContent(" " + i, "这一行真实存的秒值:" + child.floatValue.ToString("F4", CultureInfo.InvariantCulture) + " 秒");
            EditorGUI.BeginChangeCheck();
            string edited = EditorGUI.DelayedTextField(r, gc, MusicTimeText.ToFramesText(child.floatValue));
            if (EditorGUI.EndChangeCheck()
                && MusicTimeText.TryParseFramesText(edited, out float sec)
                && Mathf.Abs(sec - child.floatValue) > 1e-6f)
                child.floatValue = sec;      // 只写合法的差量;写不进去就保持原值(不猜)
        }

        if (property.arraySize == 0)
        {
            Rect empty = new Rect(position.x + 12f, y, position.width - 12f, line);
            EditorGUI.LabelField(empty, "(空)", EditorStyles.miniLabel);
        }
    }
}
