using UnityEditor;
using UnityEngine;

/// <summary>
/// LootTableSO 自定义 Inspector
/// 数组增减自动均分权重，拖滑块时其他项按比例配平，总和恒为 100
/// </summary>
[CustomEditor(typeof(LootTableSO))]
public class LootTableSOEditor : Editor
{
    private SerializedProperty entriesProp;
    private int _prevArraySize = -1;

    private void OnEnable()
    {
        entriesProp = serializedObject.FindProperty("entries");
        _prevArraySize = entriesProp.arraySize;
    }

    public override void OnInspectorGUI()
    {
        serializedObject.Update();

        if (entriesProp != null)
        {
            DrawDefaultInspectorExceptEntries();

            EditorGUILayout.Space(8);
            EditorGUILayout.LabelField("掉落条目", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox("权重总和恒为 100。增减条目自动均分，拖滑块自动配平。", MessageType.Info);

            int n = entriesProp.arraySize;

            // 数组大小变化 → 自动均分
            if (n > 0 && n != _prevArraySize)
            {
                AutoBalanceWeights(n);
                _prevArraySize = n;
            }

            // 记录拖动前的权重快照
            float[] oldWeights = new float[n];
            for (int i = 0; i < n; i++)
            {
                var entry = entriesProp.GetArrayElementAtIndex(i);
                var weightProp = entry.FindPropertyRelative("weight");
                oldWeights[i] = weightProp.floatValue;
            }

            // 绘制每个条目
            for (int i = 0; i < n; i++)
            {
                var entry = entriesProp.GetArrayElementAtIndex(i);
                var itemProp = entry.FindPropertyRelative("item");
                var weightProp = entry.FindPropertyRelative("weight");

                EditorGUILayout.BeginHorizontal();

                // 物品引用
                EditorGUILayout.PropertyField(itemProp, GUIContent.none, GUILayout.MinWidth(160));

                // 权重滑块 + 百分比标签
                EditorGUI.BeginChangeCheck();
                float newWeight = EditorGUILayout.Slider(weightProp.floatValue, 0f, 100f, GUILayout.MinWidth(100));
                GUILayout.Label($"{newWeight:F0}%", GUILayout.Width(40));

                // 删除按钮
                if (GUILayout.Button("✕", GUILayout.Width(24), GUILayout.Height(18)))
                {
                    entriesProp.DeleteArrayElementAtIndex(i);
                    serializedObject.ApplyModifiedProperties();
                    _prevArraySize = entriesProp.arraySize;
                    AutoBalanceWeights(entriesProp.arraySize);
                    serializedObject.ApplyModifiedProperties();
                    return; // 数组已变，重绘
                }

                EditorGUILayout.EndHorizontal();

                if (EditorGUI.EndChangeCheck())
                {
                    float delta = newWeight - oldWeights[i];
                    if (Mathf.Abs(delta) > 0.001f)
                    {
                        weightProp.floatValue = newWeight;
                        RedistributeDelta(i, delta, n);
                    }
                }
            }

            // 添加按钮
            EditorGUILayout.Space(4);
            if (GUILayout.Button("+ 添加掉落条目"))
            {
                entriesProp.arraySize++;
                serializedObject.ApplyModifiedProperties();
                _prevArraySize = entriesProp.arraySize;
                AutoBalanceWeights(entriesProp.arraySize);
            }

            // 强制归一化按钮
            if (n > 0 && GUILayout.Button("归一化（重算为 100%）"))
            {
                AutoBalanceWeights(n);
            }
        }

        serializedObject.ApplyModifiedProperties();
    }

    private void DrawDefaultInspectorExceptEntries()
    {
        SerializedProperty prop = serializedObject.GetIterator();
        bool enterChildren = true;
        while (prop.NextVisible(enterChildren))
        {
            enterChildren = false;
            if (prop.name == "entries") continue;
            EditorGUILayout.PropertyField(prop, true);
        }
    }

    /// <summary>均分权重，余数加到末尾</summary>
    private void AutoBalanceWeights(int n)
    {
        if (n == 0) return;
        int each = 100 / n;
        int remainder = 100 - each * n;
        for (int i = 0; i < n; i++)
        {
            var entry = entriesProp.GetArrayElementAtIndex(i);
            var weightProp = entry.FindPropertyRelative("weight");
            weightProp.floatValue = each + (i == n - 1 ? remainder : 0);
        }
    }

    /// <summary>条目 i 改变了 delta，从其他条目按比例分摊</summary>
    private void RedistributeDelta(int changedIdx, float delta, int n)
    {
        if (n <= 1) return;

        // 计算其他条目的旧权重之和
        float othersOldSum = 0f;
        for (int j = 0; j < n; j++)
        {
            if (j == changedIdx) continue;
            var entry = entriesProp.GetArrayElementAtIndex(j);
            var w = entry.FindPropertyRelative("weight");
            othersOldSum += w.floatValue;
        }

        if (othersOldSum <= 0.001f)
        {
            // 其他项权重均为 0：均摊 delta
            float share = -delta / (n - 1);
            for (int j = 0; j < n; j++)
            {
                if (j == changedIdx) continue;
                var entry = entriesProp.GetArrayElementAtIndex(j);
                var w = entry.FindPropertyRelative("weight");
                w.floatValue = Mathf.Max(0f, w.floatValue + share);
            }
            return;
        }

        // 按比例分摊
        for (int j = 0; j < n; j++)
        {
            if (j == changedIdx) continue;
            var entry = entriesProp.GetArrayElementAtIndex(j);
            var w = entry.FindPropertyRelative("weight");
            float ratio = w.floatValue / othersOldSum;
            w.floatValue = Mathf.Max(0f, w.floatValue - delta * ratio);
        }
    }
}
