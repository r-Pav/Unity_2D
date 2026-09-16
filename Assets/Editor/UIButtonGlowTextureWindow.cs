using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 按钮辉光贴图生成器(2026-09-14)。
/// 从按钮自己的 sprite 生成一张「距轮廓等距」的描边光贴图,存成 PNG 放到 Assets/Graphics/UI/Glow/。
/// 生成的图是白色 RGB + alpha 表示光强,颜色由 UIButtonGlow 的 Glow Color 染色,图本身可以用美术工具精修。
///
/// 算法:取 sprite 的 alpha 二值化 → 画布按描边宽度四周外扩 → 两遍 chamfer 距离变换 → 按距离写 alpha。
/// 因为用的是等距变换,矩形边出直边光、圆角出圆角光,和按钮轮廓严格一致(不是把图放大那种,放大会让圆角半径一起变大)。
/// </summary>
public class UIButtonGlowTextureWindow : EditorWindow
{
    private const string OUT_DIR = "Assets/Graphics/UI/Glow";
    private const int MaxSide = 4096;

    [SerializeField] private int strokeWidth = 8;
    [SerializeField] private float softness = 0.5f;
    [SerializeField] private List<Button> buttons = new List<Button>();

    private SerializedObject _so;
    private SerializedProperty _buttonsProp;

    [MenuItem("Tools/UI/按钮辉光贴图生成器")]
    private static void Open()
    {
        var w = GetWindow<UIButtonGlowTextureWindow>("按钮辉光贴图");
        w.minSize = new Vector2(380, 300);
    }

    private void OnEnable()
    {
        _so = new SerializedObject(this);
        _buttonsProp = _so.FindProperty("buttons");
    }

    private void OnGUI()
    {
        EditorGUILayout.HelpBox(
            "从按钮自己的 sprite 生成描边光贴图。\n生成后把图拖到 UIButtonGlow 组件里对应按钮的槽。",
            MessageType.Info);

        strokeWidth = EditorGUILayout.IntSlider("描边宽度(像素)", strokeWidth, 1, 64);
        softness = EditorGUILayout.Slider("柔和度(0=实心描边, 1=向外渐淡)", softness, 0f, 1f);
        EditorGUILayout.Space(6);

        _so.Update();
        EditorGUILayout.PropertyField(_buttonsProp, new GUIContent("目标按钮(可多个)"), true);
        _so.ApplyModifiedProperties();

        EditorGUILayout.BeginHorizontal();
        if (GUILayout.Button("用当前选中的按钮填充")) FillFromSelection();
        if (GUILayout.Button("清空")) buttons.Clear();
        EditorGUILayout.EndHorizontal();

        EditorGUILayout.Space(8);
        GUI.enabled = buttons.Count > 0;
        if (GUILayout.Button($"生成({buttons.Count} 个)", GUILayout.Height(28))) Generate();
        GUI.enabled = true;
    }

    private void FillFromSelection()
    {
        buttons.Clear();
        foreach (var go in Selection.gameObjects)
            foreach (var b in go.GetComponentsInChildren<Button>(true))
                if (!buttons.Contains(b)) buttons.Add(b);
        Repaint();
    }

    private void Generate()
    {
        Directory.CreateDirectory(OUT_DIR);

        int ok = 0;
        foreach (var btn in buttons)
        {
            if (btn == null) continue;

            Sprite src = null;
            if (btn.targetGraphic is Image ti) src = ti.sprite;
            if (src == null)
            {
                var own = btn.GetComponent<Image>();
                if (own != null) src = own.sprite;
            }
            if (src == null || src.texture == null)
            {
                Debug.LogWarning($"[按钮辉光] {btn.name} 上找不到 sprite,跳过", btn);
                continue;
            }

            string path = GenerateOne(btn.name, src, strokeWidth, softness);
            if (!string.IsNullOrEmpty(path)) ok++;
        }

        AssetDatabase.Refresh();
        Debug.Log($"[按钮辉光] 完成 {ok}/{buttons.Count} 个。生成在 {OUT_DIR},拖到 UIButtonGlow 对应按钮的槽里。");
    }

    private static string GenerateOne(string buttonName, Sprite src, int width, float soft)
    {
        Rect r = src.textureRect;
        int sw = Mathf.RoundToInt(r.width);
        int sh = Mathf.RoundToInt(r.height);
        if (sw <= 0 || sh <= 0) { Debug.LogWarning($"[按钮辉光] {buttonName} 的 sprite 尺寸无效"); return null; }

        int ow = sw + width * 2;
        int oh = sh + width * 2;
        if (ow > MaxSide || oh > MaxSide)
        {
            Debug.LogWarning($"[按钮辉光] {buttonName} 太大,输出 {ow}x{oh} 超过 {MaxSide},跳过");
            return null;
        }

        // 1. 取 sprite 区域的 alpha(用 RT 中转,不要求纹理 Read/Write Enabled)
        Texture2D full = ReadTexture(src.texture);
        if (full == null) { Debug.LogWarning($"[按钮辉光] {buttonName} 读不到纹理像素"); return null; }

        var px = full.GetPixels();
        int texW = full.width;
        int ox = Mathf.RoundToInt(r.x);
        int oy = Mathf.RoundToInt(r.y);

        bool[] solid = new bool[sw * sh];
        for (int y = 0; y < sh; y++)
            for (int x = 0; x < sw; x++)
                solid[y * sw + x] = px[(oy + y) * texW + (ox + x)].a > 0.4f;

        DestroyImmediate(full);

        // 2. 外扩画布 + 距离变换(到最近实体像素的像素距离)
        float[] dist = new float[ow * oh];
        const float INF = 1e9f;
        for (int y = 0; y < oh; y++)
            for (int x = 0; x < ow; x++)
            {
                int ix = x - width;
                int iy = y - width;
                bool isSolid = ix >= 0 && ix < sw && iy >= 0 && iy < sh && solid[iy * sw + ix];
                dist[y * ow + x] = isSolid ? 0f : INF;
            }
        Chamfer(dist, ow, oh);

        // 3. 写 alpha:solid 内部不出光(0),轮廓外 [0, width] 按柔和度衰减
        var outTex = new Texture2D(ow, oh, TextureFormat.RGBA32, false, true);
        var cols = new Color32[ow * oh];
        for (int y = 0; y < oh; y++)
            for (int x = 0; x < ow; x++)
            {
                float d = dist[y * ow + x];
                byte a = 0;
                if (d > 0f && d <= width)
                {
                    float t = d / width;
                    float al = Mathf.Clamp01(1f - soft * t);
                    a = (byte)Mathf.RoundToInt(al * 255f);
                }
                cols[y * ow + x] = new Color32(255, 255, 255, a);
            }
        outTex.SetPixels32(cols);
        outTex.Apply();

        string path = $"{OUT_DIR}/{buttonName}_Glow.png";
        File.WriteAllBytes(path, outTex.EncodeToPNG());
        DestroyImmediate(outTex);

        AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
        ApplyImportSettings(path);

        Debug.Log($"[按钮辉光] 生成 {path}  按钮={buttonName} 原图 {sw}x{sh} 输出 {ow}x{oh} 描边宽 {width}px 柔和度 {soft:0.##}");
        return path;
    }

    /// <summary>两遍 chamfer 距离变换(3x3 邻域,直边权 1、对角权 √2),单位=像素</summary>
    private static void Chamfer(float[] d, int w, int h)
    {
        const float D1 = 1f;
        const float D2 = 1.41421356f;

        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < w; x++)
            {
                int i = y * w + x;
                float v = d[i];
                if (x > 0) v = Mathf.Min(v, d[i - 1] + D1);
                if (y > 0) v = Mathf.Min(v, d[i - w] + D1);
                if (x > 0 && y > 0) v = Mathf.Min(v, d[i - w - 1] + D2);
                if (x < w - 1 && y > 0) v = Mathf.Min(v, d[i - w + 1] + D2);
                d[i] = v;
            }
        }

        for (int y = h - 1; y >= 0; y--)
        {
            for (int x = w - 1; x >= 0; x--)
            {
                int i = y * w + x;
                float v = d[i];
                if (x < w - 1) v = Mathf.Min(v, d[i + 1] + D1);
                if (y < h - 1) v = Mathf.Min(v, d[i + w] + D1);
                if (x < w - 1 && y < h - 1) v = Mathf.Min(v, d[i + w + 1] + D2);
                if (x > 0 && y < h - 1) v = Mathf.Min(v, d[i + w - 1] + D2);
                d[i] = v;
            }
        }
    }

    /// <summary>把纹理经 RT 中转读成可访问像素(不依赖 Read/Write Enabled)</summary>
    private static Texture2D ReadTexture(Texture src)
    {
        if (src == null) return null;

        RenderTexture rt = RenderTexture.GetTemporary(src.width, src.height, 0,
            RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear);
        Graphics.Blit(src, rt);

        RenderTexture prev = RenderTexture.active;
        RenderTexture.active = rt;

        var tex = new Texture2D(src.width, src.height, TextureFormat.RGBA32, false, true);
        tex.ReadPixels(new Rect(0, 0, src.width, src.height), 0, 0);
        tex.Apply();

        RenderTexture.active = prev;
        RenderTexture.ReleaseTemporary(rt);
        return tex;
    }

    /// <summary>生成的图设成 UI 用的 Sprite:单图、不压 Mip、无压缩、双线性、Clamp</summary>
    private static void ApplyImportSettings(string path)
    {
        var imp = AssetImporter.GetAtPath(path) as TextureImporter;
        if (imp == null) return;

        imp.textureType = TextureImporterType.Sprite;
        imp.spriteImportMode = SpriteImportMode.Single;
        imp.spritePixelsPerUnit = 100f;
        imp.alphaIsTransparency = true;
        imp.mipmapEnabled = false;
        imp.filterMode = FilterMode.Bilinear;
        imp.wrapMode = TextureWrapMode.Clamp;
        imp.textureCompression = TextureImporterCompression.Uncompressed;
        imp.maxTextureSize = MaxSide;
        imp.SaveAndReimport();
    }
}
