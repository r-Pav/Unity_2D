using System.Collections.Generic;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

/// <summary>
/// BossSkillData 自定义 Inspector — animState(动画开关)用 Boss Animator Controller 的 Bool 参数下拉选择。
/// 技能编辑器(Tools → 技能编辑器 → Boss 页签)右侧用默认 Inspector 绘制时会自动走这里。
/// 用法:选中技能 data 资产,拖入 Boss 的 Animator Controller,从下拉选 Bool 参数(如 IsMagic;一个开关可被多个技能复用)。
/// 技能开始时该参数置真、结束/中断时置假,动画器 Entry 路由按参数决定进哪个技能状态。
/// </summary>
[CustomEditor(typeof(BossSkillData))]
public class BossSkillDataEditor : Editor
{
    private AnimatorController _controller;
    private string[] _paramNames = new string[0];

    public override void OnInspectorGUI()
    {
        var data = (BossSkillData)target;
        serializedObject.Update();

        _controller = (AnimatorController)EditorGUILayout.ObjectField(
            "Boss Animator(动画控制器)", _controller, typeof(AnimatorController), false);

        if (_controller != null)
        {
            if (_paramNames.Length == 0)
                _paramNames = CollectBoolParamNames(_controller);

            if (_paramNames.Length > 0)
            {
                int idx = System.Array.IndexOf(_paramNames, data.animState);
                int sel = EditorGUILayout.Popup("动画开关(Bool 参数)", idx < 0 ? 0 : idx, _paramNames);
                if (data.animState != _paramNames[sel])
                {
                    data.animState = _paramNames[sel];
                    EditorUtility.SetDirty(data);
                }
            }
            else
            {
                EditorGUILayout.HelpBox("控制器里没有 Bool 参数,请先建参数,再手动填写参数名", MessageType.Warning);
                data.animState = EditorGUILayout.TextField("动画开关(Bool 参数)", data.animState);
            }
        }
        else
        {
            data.animState = EditorGUILayout.TextField("动画开关(Bool 参数)", data.animState);
        }

        EditorGUILayout.Space(4);

        // 绘制其余字段(跳过 m_Script 与 animState,避免重复)
        var prop = serializedObject.GetIterator();
        bool first = true;
        while (prop.NextVisible(true))
        {
            if (first) { first = false; continue; }   // m_Script
            if (prop.name == "animState") continue;
            EditorGUILayout.PropertyField(prop, true);
        }
        serializedObject.ApplyModifiedProperties();
    }

    /// <summary>收集控制器所有层的 Bool 参数名(技能动画开关)</summary>
    private string[] CollectBoolParamNames(AnimatorController controller)
    {
        var list = new List<string>();
        if (controller == null) return list.ToArray();
        foreach (var p in controller.parameters)
        {
            if (p.type == AnimatorControllerParameterType.Bool && !string.IsNullOrEmpty(p.name))
                list.Add(p.name);
        }
        return list.ToArray();
    }
}
