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

    public static void ConvertBuiltInToURP2D()
    {
        Debug.Log("[URPMIG] Convert start");
        UnityEditor.Rendering.Universal.Converters.RunInBatchMode(
            UnityEditor.Rendering.Universal.ConverterContainerId.BuiltInToURP2D);
        AssetDatabase.SaveAssets();
        Debug.Log("[URPMIG] Convert done");
    }

    public static void AuditMaterials()
    {
        Debug.Log("[URPMIG] AuditMaterials start");
        var guids = AssetDatabase.FindAssets("t:Material", new[] { "Assets" });
        foreach (var g in guids)
        {
            var p = AssetDatabase.GUIDToAssetPath(g);
            var m = AssetDatabase.LoadAssetAtPath<Material>(p);
            if (m == null || m.shader == null) continue;
            Debug.Log($"[MAT] {p} | shader={m.shader.name}");
        }
        Debug.Log("[URPMIG] AuditMaterials done");
    }

    public static void ConvertLegacyParticles()
    {
        Debug.Log("[URPMIG] ConvertLegacyParticles start");
        var targetShader = Shader.Find("Universal Render Pipeline/Particles/Unlit");
        if (targetShader == null) throw new Exception("[URPMIG] URP Particles/Unlit shader not found");

        var guids = AssetDatabase.FindAssets("t:Material", new[] { "Assets" });
        int converted = 0;
        foreach (var g in guids)
        {
            var p = AssetDatabase.GUIDToAssetPath(g);
            var m = AssetDatabase.LoadAssetAtPath<Material>(p);
            if (m == null || m.shader == null) continue;
            string oldName = m.shader.name;
            int blendMode; // 0=Alpha 2=Additive
            if (oldName == "Legacy Shaders/Particles/Alpha Blended") blendMode = 0;
            else if (oldName == "Legacy Shaders/Particles/Additive") blendMode = 2;
            else if (oldName == "Legacy Shaders/Particles/Additive (Soft)") blendMode = 2;
            else continue;

            // 先拷贝旧属性(换 shader 后仍可读,但先取值更稳)
            var tex = m.HasProperty("_MainTex") ? m.GetTexture("_MainTex") : null;
            var col = m.HasProperty("_TintColor") ? m.GetColor("_TintColor")
                   : m.HasProperty("_Color") ? m.GetColor("_Color") : Color.white;

            m.shader = targetShader;
            m.SetTexture("_BaseMap", tex);
            m.SetColor("_BaseColor", col);
            m.SetFloat("_Surface", 1f);
            m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            m.DisableKeyword("_ALPHAPREMULTIPLY_ON");
            m.DisableKeyword("_ALPHATEST_ON");
            m.SetFloat("_AlphaClip", 0f);
            m.SetFloat("_ColorMode", 0f);
            m.SetFloat("_Cull", 0f);
            m.SetFloat("_ZWrite", 0f);
            m.SetFloat("_Blend", blendMode);
            // BlendMode: SrcAlpha=5, One=1, OneMinusSrcAlpha=10
            float src = 5f;
            float dst = blendMode == 0 ? 10f : 1f;
            m.SetFloat("_SrcBlend", src);
            m.SetFloat("_DstBlend", dst);
            m.SetFloat("_SrcBlendAlpha", src);
            m.SetFloat("_DstBlendAlpha", dst);
            EditorUtility.SetDirty(m);
            Debug.Log($"[MATCONV] {p} | {oldName} -> URP Particles/Unlit blend={(blendMode == 0 ? "Alpha" : "Additive")}");
            converted++;
        }
        AssetDatabase.SaveAssets();
        Debug.Log($"[URPMIG] ConvertLegacyParticles done converted={converted}");
    }
}
