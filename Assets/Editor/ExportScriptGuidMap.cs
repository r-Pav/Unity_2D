using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 导出脚本 GUID 映射（P1 应急预案数据源）
/// 菜单: Tools/导出脚本GUID映射
/// 读取当前项目所有 .cs 脚本的真实 meta guid(内存中),导出为 JSON 存档。
/// 用途: Library 损坏后,场景/prefab 引用断裂时,用此 JSON 恢复映射。
/// </summary>
public static class ExportScriptGuidMap
{
    private const string MenuPath = "Tools/导出脚本GUID映射";
    private const string OutputPath = "F:/2-Project/Unity/Docs/script_guid_map.json";

    [MenuItem(MenuPath)]
    public static void Export()
    {
        // 1. 收集所有 .cs 脚本的路径、类名、当前 guid
        var scripts = new List<ScriptEntry>();
        string[] csGuids = AssetDatabase.FindAssets("t:MonoScript", new[] { "Assets" });
        foreach (string guid in csGuids)
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            if (!path.EndsWith(".cs")) continue;

            MonoScript script = AssetDatabase.LoadAssetAtPath<MonoScript>(path);
            if (script == null) continue;

            // 当前 meta guid（AssetDatabase 持有的真实 guid，可能是 base64 编码的）
            string metaGuid = AssetDatabase.AssetPathToGUID(path);

            // 脚本类名
            string className = script.name; // MonoScript.name 通常是类名

            scripts.Add(new ScriptEntry
            {
                className = className,
                scriptPath = path,
                metaGuid = metaGuid,
                fileName = Path.GetFileName(path),
            });
        }

        // 2. 序列化 JSON
        var wrapper = new GuidMapWrapper
        {
            exportedAt = System.DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
            unityVersion = Application.unityVersion,
            scripts = scripts,
        };

        string json = JsonUtility.ToJson(wrapper, prettyPrint: true);
        Directory.CreateDirectory(Path.GetDirectoryName(OutputPath));
        File.WriteAllText(OutputPath, json, Encoding.UTF8);

        Debug.Log($"[GuidMap] 导出完成: {scripts.Count} 个脚本 -> {OutputPath}");
        EditorUtility.DisplayDialog("脚本GUID映射", $"导出完成: {scripts.Count} 个脚本\n{OutputPath}", "OK");
    }

    [System.Serializable]
    private class GuidMapWrapper
    {
        public string exportedAt;
        public string unityVersion;
        public List<ScriptEntry> scripts;
    }

    [System.Serializable]
    private class ScriptEntry
    {
        public string className;
        public string scriptPath;
        public string metaGuid;
        public string fileName;
    }
}
