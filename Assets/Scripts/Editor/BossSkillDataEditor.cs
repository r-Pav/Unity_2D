using System.Collections.Generic;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

/// <summary>
/// BossSkillData 自定义 Inspector — animState(动画状态)用 Boss Animator Controller 的状态名下拉选择。
/// 技能编辑器(Tools → 技能编辑器 → Boss 页签)右侧用默认 Inspector 绘制时会自动走这里。
/// 用法:选中技能 data 资产,拖入 Boss 的 Animator Controller,从下拉选动画状态(一个动画可被多个技能复用)。
/// </summary>
[CustomEditor(typeof(BossSkillData))]
public class BossSkillDataEditor : Editor
{
    private AnimatorController _controller;
    private string[] _stateNames = new string[0];

    public override void OnInspectorGUI()
    {
        var data = (BossSkillData)target;
        serializedObject.Update();

        _controller = (AnimatorController)EditorGUILayout.ObjectField(
            "Boss Animator(动画控制器)", _controller, typeof(AnimatorController), false);

        if (_controller != null)
        {
            if (_stateNames.Length == 0)
                _stateNames = CollectStateNames(_controller);

            if (_stateNames.Length > 0)
            {
                int idx = System.Array.IndexOf(_stateNames, data.animState);
                int sel = EditorGUILayout.Popup("动画状态(AnimState)", idx < 0 ? 0 : idx, _stateNames);
                if (data.animState != _stateNames[sel])
                {
                    data.animState = _stateNames[sel];
                    EditorUtility.SetDirty(data);
                }
            }
            else
            {
                EditorGUILayout.HelpBox("控制器中没有状态,请手动填写状态名", MessageType.Warning);
                data.animState = EditorGUILayout.TextField("动画状态(AnimState)", data.animState);
            }
        }
        else
        {
            data.animState = EditorGUILayout.TextField("动画状态(AnimState)", data.animState);
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

    /// <summary>收集控制器所有层/子状态机的状态名(扁平列表)</summary>
    private string[] CollectStateNames(AnimatorController controller)
    {
        var list = new List<string>();
        if (controller == null) return list.ToArray();
        foreach (var layer in controller.layers)
        {
            CollectFromStateMachine(layer.stateMachine, list);
        }
        return list.ToArray();
    }

    private void CollectFromStateMachine(AnimatorStateMachine sm, List<string> list)
    {
        if (sm == null) return;
        foreach (var child in sm.states)
        {
            if (child.state != null && !string.IsNullOrEmpty(child.state.name))
                list.Add(child.state.name);
        }
        foreach (var child in sm.stateMachines)
        {
            if (child.stateMachine != null)
                CollectFromStateMachine(child.stateMachine, list);
        }
    }
}
