using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 技能编辑器窗口 — 集中查看/编辑所有技能 SO 资产。
/// 打开方式: Tools → 技能编辑器
/// 左侧: 技能列表(按类型分页签);右侧: 选中技能的完整字段编辑。
/// 只改 SO 数据,不碰运行时逻辑。
/// </summary>
public class SkillEditorWindow : EditorWindow
{
    // ============================================================
    // 页签类型
    // ============================================================
    private enum TabType { Active, Passive, Weapon, Combination, Boss }

    private const string ActiveDir = "Assets/Resources/Skills/Active";
    private const string PassiveDir = "Assets/Resources/Skills/Passive";
    private const string WeaponDir = "Assets/Resources/Skills/Weapon";
    private const string ComboDir = "Assets/Resources/Skills/Combo"; // 组合技能(CombinationSkillData)实际存在此目录
    private const string BossDir = "Assets/Data/BossSkills/FirstBoss";

    private TabType currentTab = TabType.Active;
    private Vector2 listScroll;
    private Vector2 detailScroll;
    private int selectedIndex = -1;
    private List<Object> currentAssets = new List<Object>();

    [MenuItem("Tools/技能编辑器")]
    public static void Open()
    {
        SkillEditorWindow window = GetWindow<SkillEditorWindow>("技能编辑器");
        window.minSize = new Vector2(700, 450);
        window.RefreshList();
    }

    // ============================================================
    // 资产列表
    // ============================================================

    private string CurrentDir
    {
        get
        {
            switch (currentTab)
            {
                case TabType.Active: return ActiveDir;
                case TabType.Passive: return PassiveDir;
                case TabType.Weapon: return WeaponDir;
                case TabType.Combination: return ComboDir;
                case TabType.Boss: return BossDir;
                default: return ActiveDir;
            }
        }
    }

    private void RefreshList()
    {
        currentAssets.Clear();
        selectedIndex = -1;
        if (!Directory.Exists(CurrentDir)) return;

        string[] guids = AssetDatabase.FindAssets("t:ScriptableObject", new[] { CurrentDir });
        foreach (string guid in guids)
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            Object asset = AssetDatabase.LoadAssetAtPath<Object>(path);
            if (asset != null)
                currentAssets.Add(asset);
        }
        currentAssets.Sort((a, b) => string.Compare(a.name, b.name, System.StringComparison.Ordinal));
    }

    // ============================================================
    // GUI
    // ============================================================

    private void OnGUI()
    {
        DrawTabs();

        GUILayout.BeginHorizontal();
        DrawListPanel();
        DrawDetailPanel();
        GUILayout.EndHorizontal();
    }

    private void DrawTabs()
    {
        GUILayout.BeginHorizontal();
        foreach (TabType tab in System.Enum.GetValues(typeof(TabType)))
        {
            bool isActive = currentTab == tab;
            if (GUILayout.Toggle(isActive, tab.ToString(), "Button", GUILayout.Width(100)))
            {
                if (currentTab != tab)
                {
                    currentTab = tab;
                    RefreshList();
                }
            }
        }
        GUILayout.FlexibleSpace();
        if (GUILayout.Button("刷新", GUILayout.Width(60)))
            RefreshList();
        GUILayout.EndHorizontal();
        GUILayout.Space(4);
    }

    private void DrawListPanel()
    {
        GUILayout.BeginVertical(GUI.skin.box, GUILayout.Width(220));
        GUILayout.Label($"技能列表 ({currentAssets.Count})", EditorStyles.boldLabel);

        listScroll = GUILayout.BeginScrollView(listScroll);
        for (int i = 0; i < currentAssets.Count; i++)
        {
            Object asset = currentAssets[i];
            if (asset == null) continue;
            bool selected = (i == selectedIndex);
            GUIStyle style = selected ? "SelectionRect" : "Label";
            if (GUILayout.Button(asset.name, style, GUILayout.Height(24)))
            {
                selectedIndex = i;
                Selection.activeObject = asset;
            }
        }
        GUILayout.EndScrollView();

        GUILayout.Space(4);
        if (GUILayout.Button("新建技能..."))
            CreateNewSkill();
        GUILayout.EndVertical();
    }

    private void DrawDetailPanel()
    {
        GUILayout.BeginVertical(GUI.skin.box);
        if (selectedIndex < 0 || selectedIndex >= currentAssets.Count || currentAssets[selectedIndex] == null)
        {
            GUILayout.Label("← 从左侧选择一个技能", EditorStyles.centeredGreyMiniLabel);
            GUILayout.EndVertical();
            return;
        }

        Object asset = currentAssets[selectedIndex];
        detailScroll = GUILayout.BeginScrollView(detailScroll);

        // 资产路径 + 类型
        string path = AssetDatabase.GetAssetPath(asset);
        GUILayout.Label($"路径: {path}", EditorStyles.miniLabel);
        GUILayout.Space(4);

        // 用默认 Inspector 绘制 SO 全部字段(包含所有继承字段,自动分组)
        Editor editor = Editor.CreateEditor(asset);
        if (editor != null)
        {
            editor.OnInspectorGUI();
            DestroyImmediate(editor);
        }

        GUILayout.Space(8);
        GUILayout.BeginHorizontal();
        if (GUILayout.Button("选中此资产", GUILayout.Width(100)))
        {
            Selection.activeObject = asset;
            EditorGUIUtility.PingObject(asset);
        }
        if (GUILayout.Button("删除技能", GUILayout.Width(100)))
        {
            if (EditorUtility.DisplayDialog("删除技能", $"确定删除 {asset.name} ?", "删除", "取消"))
                DeleteSelectedSkill(asset);
        }
        GUILayout.EndHorizontal();

        GUILayout.EndScrollView();
        GUILayout.EndVertical();
    }

    // ============================================================
    // 新建 / 删除
    // ============================================================

    private void CreateNewSkill()
    {
        System.Type assetType = currentTab switch
        {
            TabType.Active => typeof(ActiveSkillData),
            TabType.Passive => typeof(PassiveSkillData),
            TabType.Weapon => typeof(WeaponSkillData),
            TabType.Combination => typeof(CombinationSkillData),
            TabType.Boss => typeof(BossSkillData),
            _ => typeof(SkillData),
        };

        string dir = CurrentDir;
        if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);

        // 生成不重名文件名
        string baseName = currentTab switch
        {
            TabType.Active => "Skill_Active_New",
            TabType.Passive => "Passive_LX_LX",
            TabType.Weapon => "Skill_Weapon_New",
            TabType.Combination => "Skill_Combination_New",
            TabType.Boss => "BossSkill_New",
            _ => "Skill_New",
        };
        string path = AssetDatabase.GenerateUniqueAssetPath($"{dir}/{baseName}.asset");

        ScriptableObject asset = ScriptableObject.CreateInstance(assetType);
        AssetDatabase.CreateAsset(asset, path);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        RefreshList();
        // 选中新创建的
        for (int i = 0; i < currentAssets.Count; i++)
        {
            if (AssetDatabase.GetAssetPath(currentAssets[i]) == path)
            {
                selectedIndex = i;
                Selection.activeObject = currentAssets[i];
                break;
            }
        }
    }

    private void DeleteSelectedSkill(Object asset)
    {
        string path = AssetDatabase.GetAssetPath(asset);
        AssetDatabase.DeleteAsset(path);
        RefreshList();
        selectedIndex = -1;
    }
}
