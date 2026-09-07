using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 技能编辑器窗口 — 按技能树形浏览/编辑所有技能 SO 资产。
/// 打开方式: Tools → 技能编辑器
/// 顶部: 类型页签(Active/被动/合成/武器/Boss)
/// 左侧树: 一级 = 技能资产;Active 技能(树A/树B)展开子级 = Lv1/Lv2左/右/Lv3左/右 分支
///   每个分支用 branchName 标注,一个分支一行,不再整体平铺重复字段
/// 右侧: 选中一级 → 编辑 SO 根字段(不含分支数据);选中分支 → 只编辑该分支一组字段
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

    // 分支字段名(ActiveSkillData 内五个分支对象的序列化字段名,按显示顺序)
    private static readonly string[] BranchKeys =
        { "lv1Data", "lv2Left", "lv2Right", "lv3Left", "lv3Right" };
    private static readonly string[] BranchLabels =
        { "Lv1", "Lv2 左", "Lv2 右", "Lv3 左", "Lv3 右" };

    private TabType currentTab = TabType.Active;
    private Vector2 listScroll;
    private Vector2 detailScroll;
    private List<Object> currentAssets = new List<Object>();

    // 树展开状态:当前选中资产 + 展开标记(Active 资产是否展开分支树)
    private Object selectedAsset;      // 当前选中的技能资产
    private string selectedBranch;     // 非空 = 选中该 Active 资产的分支字段名(lv1Data 等);空 = 选根
    private readonly HashSet<Object> expandedAssets = new HashSet<Object>();

    [MenuItem("Tools/技能编辑器")]
    public static void Open()
    {
        SkillEditorWindow window = GetWindow<SkillEditorWindow>("技能编辑器");
        window.minSize = new Vector2(760, 480);
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

    private bool IsActiveSkill(Object asset) => asset is ActiveSkillData;

    private void RefreshList()
    {
        currentAssets.Clear();
        selectedAsset = null;
        selectedBranch = null;
        expandedAssets.Clear();
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
        DrawTreePanel();
        DrawDetailPanel();
        GUILayout.EndHorizontal();
    }

    private void DrawTabs()
    {
        GUILayout.BeginHorizontal();
        foreach (TabType tab in System.Enum.GetValues(typeof(TabType)))
        {
            bool isActive = currentTab == tab;
            if (GUILayout.Toggle(isActive, tab.ToString(), "Button", GUILayout.Width(110)))
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

    /// <summary>一级节点显示名:Active 用 skillName+hotkey,其他用资产名</summary>
    private string RootLabel(Object asset)
    {
        if (asset is SkillData sd)
        {
            string key = sd.hotkey != KeyCode.None ? $" [{sd.hotkey}]" : "";
            return $"{sd.skillName}{key}";
        }
        return asset.name;
    }

    private void DrawTreePanel()
    {
        GUILayout.BeginVertical(GUI.skin.box, GUILayout.Width(280));
        GUILayout.Label($"技能列表 ({currentAssets.Count})", EditorStyles.boldLabel);

        listScroll = GUILayout.BeginScrollView(listScroll);
        for (int i = 0; i < currentAssets.Count; i++)
        {
            Object asset = currentAssets[i];
            if (asset == null) continue;

            bool isActiveSkill = IsActiveSkill(asset);
            bool isExpanded = expandedAssets.Contains(asset);

            GUILayout.BeginHorizontal();
            // Active 技能显示折叠箭头;其他类型无子级不显示
            if (isActiveSkill)
            {
                bool newExpanded = EditorGUILayout.Foldout(isExpanded, "", true, EditorStyles.foldout);
                if (newExpanded != isExpanded)
                {
                    if (newExpanded) expandedAssets.Add(asset);
                    else expandedAssets.Remove(asset);
                }
                // 折叠箭头占位宽度
                GUILayout.Space(-14);
            }
            else
            {
                GUILayout.Space(20);
            }

            bool isSelectedRoot = (selectedAsset == asset && string.IsNullOrEmpty(selectedBranch));
            GUIStyle rootStyle = isSelectedRoot ? "SelectionRect" : "Label";
            if (GUILayout.Button(RootLabel(asset), rootStyle, GUILayout.Height(22)))
            {
                selectedAsset = asset;
                selectedBranch = null;
                Selection.activeObject = asset;
            }
            GUILayout.EndHorizontal();

            // 分支子级(仅 Active 且展开)
            if (isActiveSkill && isExpanded)
            {
                var active = (ActiveSkillData)asset;
                for (int b = 0; b < BranchKeys.Length; b++)
                {
                    string key = BranchKeys[b];
                    string label = BranchLabels[b];
                    var branch = GetBranchByKey(active, key);
                    if (branch != null && !string.IsNullOrEmpty(branch.branchName))
                        label += $" · {branch.branchName}";
                    GUILayout.BeginHorizontal();
                    GUILayout.Space(28);
                    bool isSelectedBranch = (selectedAsset == asset && selectedBranch == key);
                    GUIStyle bStyle = isSelectedBranch ? "SelectionRect" : "Label";
                    if (GUILayout.Button(label, bStyle, GUILayout.Height(20)))
                    {
                        selectedAsset = asset;
                        selectedBranch = key;
                        Selection.activeObject = asset;
                    }
                    GUILayout.EndHorizontal();
                }
            }
        }
        GUILayout.EndScrollView();

        GUILayout.Space(4);
        if (GUILayout.Button("新建技能..."))
            CreateNewSkill();
        GUILayout.EndVertical();
    }

    private static ActiveSkillData.ActiveBranchData GetBranchByKey(ActiveSkillData data, string key)
    {
        switch (key)
        {
            case "lv1Data": return data.lv1Data;
            case "lv2Left": return data.lv2Left;
            case "lv2Right": return data.lv2Right;
            case "lv3Left": return data.lv3Left;
            case "lv3Right": return data.lv3Right;
            default: return null;
        }
    }

    private void DrawDetailPanel()
    {
        GUILayout.BeginVertical(GUI.skin.box);
        if (selectedAsset == null)
        {
            GUILayout.Label("← 从左侧选择一个技能", EditorStyles.centeredGreyMiniLabel);
            GUILayout.EndVertical();
            return;
        }

        Object asset = selectedAsset;
        detailScroll = GUILayout.BeginScrollView(detailScroll);

        // 资产路径
        string path = AssetDatabase.GetAssetPath(asset);
        GUILayout.Label($"路径: {path}", EditorStyles.miniLabel);
        GUILayout.Space(4);

        // 自绘字段:选分支 → 只画该分支一组;选根 → 画根字段(Active 隐藏分支对象,防重复平铺)
        SerializedObject so = new SerializedObject(asset);
        bool isBranchEdit = !string.IsNullOrEmpty(selectedBranch) && IsActiveSkill(asset);
        string headerTitle;

        if (isBranchEdit)
        {
            var active = (ActiveSkillData)asset;
            var branch = GetBranchByKey(active, selectedBranch);
            headerTitle = $"{BranchLabels[System.Array.IndexOf(BranchKeys, selectedBranch)]}"
                + (branch != null && !string.IsNullOrEmpty(branch.branchName) ? $" · {branch.branchName}" : "");
            GUILayout.Label($"分支编辑 — {headerTitle}", EditorStyles.boldLabel);
            GUILayout.Space(4);

            SerializedProperty branchProp = so.FindProperty(selectedBranch);
            if (branchProp != null)
                DrawSerializedChildren(branchProp);
        }
        else
        {
            GUILayout.Label("技能通用字段", EditorStyles.boldLabel);
            GUILayout.Space(4);
            SerializedProperty prop = so.GetIterator();
            bool enterChildren = true;
            while (prop.NextVisible(enterChildren))
            {
                enterChildren = false;
                if (prop.name == "m_Script") continue;
                // Active 根视图跳过分支对象(分支在树里各自编辑)
                if (IsActiveSkill(asset) && IsBranchField(prop.name)) continue;
                EditorGUILayout.PropertyField(prop, true);
            }
        }

        so.ApplyModifiedProperties();
        GUILayout.Space(8);

        // 底部操作
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

    private static bool IsBranchField(string name)
    {
        for (int i = 0; i < BranchKeys.Length; i++)
            if (name == BranchKeys[i]) return true;
        return false;
    }

    /// <summary>绘制一个序列化对象的全部直接子字段(分支对象用它只画一组,越界即停)</summary>
    private static void DrawSerializedChildren(SerializedProperty parent)
    {
        SerializedProperty child = parent.Copy();
        bool enterChildren = true;
        // Copy 后指向父;NextVisible(true) 进入第一个直接子字段。
        // 关键:SerializedProperty 横向遍历超出 parent 子树后会继续走到同级/父级其他字段
        // (如画 lv2Left 时越界画到 lv2Right/lv3Left),必须用 propertyPath 前缀判断离开即停。
        while (child.NextVisible(enterChildren))
        {
            enterChildren = false;
            string p = child.propertyPath;
            if (p == parent.propertyPath) continue;          // 迭代器自身,跳过
            if (!p.StartsWith(parent.propertyPath + ".")) break; // 已离开 parent 子树 → 停止
            EditorGUILayout.PropertyField(child, true);
        }
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
        selectedAsset = asset;
        selectedBranch = null;
        Selection.activeObject = asset;
    }

    private void DeleteSelectedSkill(Object asset)
    {
        string path = AssetDatabase.GetAssetPath(asset);
        AssetDatabase.DeleteAsset(path);
        RefreshList();
        selectedAsset = null;
        selectedBranch = null;
    }
}
