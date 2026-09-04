using System;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

/// <summary>
/// URP 迁移 run2:建 2D URP Asset + 指派 Graphics/Quality 管线 + 打开主场景验证
/// </summary>
public static class SpikeSetup
{
    public static void ConfigureUrp()
    {
        Debug.Log("[URPMIG] ConfigureUrp start");
        const string settingsDir = "Assets/Settings";
        const string assetPath = settingsDir + "/URP_2D_Main.asset";

        // 1) 建 Settings 目录
        if (!AssetDatabase.IsValidFolder(settingsDir))
            AssetDatabase.CreateFolder("Assets", "Settings");

        // 2) 反射调 internal CreateRendererAsset(与菜单 "URP Asset (with 2D Renderer)" 同链路)
        var mi = typeof(UniversalRenderPipelineAsset).GetMethod("CreateRendererAsset",
            BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
        if (mi == null) throw new Exception("[URPMIG] CreateRendererAsset method not found");
        var rendererData = (ScriptableRendererData)mi.Invoke(null,
            new object[] { assetPath, RendererType._2DRenderer, true, "Renderer" });
        Debug.Log($"[URPMIG] rendererData={rendererData.name} path={AssetDatabase.GetAssetPath(rendererData)}");

        // 3) 建 URP Asset 并关联 renderer
        var asset = UniversalRenderPipelineAsset.Create(rendererData);
        AssetDatabase.CreateAsset(asset, assetPath);
        Debug.Log($"[URPMIG] urpAsset created path={assetPath}");

        // 4) GraphicsSettings 指派
        GraphicsSettings.defaultRenderPipeline = asset;
        Debug.Log($"[URPMIG] defaultRenderPipeline set, now={GraphicsSettings.defaultRenderPipeline?.name}");

        // 5) QualitySettings 全等级指派
        var names = QualitySettings.names;
        for (int i = 0; i < names.Length; i++)
        {
            QualitySettings.SetQualityLevel(i, false);
            QualitySettings.renderPipeline = asset;
            Debug.Log($"[URPMIG] quality[{i}]={names[i]} rp={QualitySettings.renderPipeline?.name}");
        }
        QualitySettings.SetQualityLevel(QualitySettings.names.Length - 1, false);

        // 6) 打开两个主场景触发资源导入,检查崩溃/硬错误
        var scene = EditorSceneManager.OpenScene("Assets/Scenes/SampleScene.scene", OpenSceneMode.Single);
        Debug.Log($"[URPMIG] scene opened={scene.name} path={scene.path}");

        EditorUtility.SetDirty(asset);
        AssetDatabase.SaveAssets();
        Debug.Log("[URPMIG] ConfigureUrp done");
    }
}
