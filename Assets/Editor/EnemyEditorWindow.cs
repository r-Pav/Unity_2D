using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 敌人编辑器窗口 — 集中查看/编辑场景里已有的普通敌人 + EnemyConfigSO 模板资产。
/// 打开方式: Tools → 敌人编辑器
/// 页签「场景敌人」: 只读场景层级里已摆放的近战/远程敌人（Boss 单独设计，本编辑器不涉及），
///                  不读项目 Prefab 资产；右侧编辑组件字段（改完 Ctrl+S 保存场景）。
///                  支持新增敌人：选类型(近战/远程)+Lv(1/2/3) → 克隆场景同类型模板 →
///                  按 EnemyConfigSO 对应 Lv 档烘焙数值到字段（自包含，不依赖运行时解析）。
/// 页签「配置模板」: EnemyConfigSO 资产列表（一个类型一个 SO，内含 Lv1/2/3 三档），右侧编辑。
/// 与 SkillEditorWindow 同构。只改数据,不碰运行时逻辑。
/// </summary>
public class EnemyEditorWindow : EditorWindow
{
    private enum TabType { SceneEnemies, ConfigTemplates }

    private const string EnemyDir = "Assets/Data/Enemies";
    private const string MeleeSoName = "Melee_SO";
    private const string RangedSoName = "Ranged_SO";

    private TabType currentTab = TabType.SceneEnemies;
    private Vector2 listScroll;
    private Vector2 detailScroll;

    // 场景敌人（页签1）
    private List<EnemyControllerBase> sceneEnemies = new List<EnemyControllerBase>();
    private int selectedSceneIndex = -1;

    // 新增敌人表单
    private int newEnemyType;   // 0=近战 1=远程
    private int newEnemyLv = 1; // 1~3

    // 配置模板（页签2）
    private List<Object> configAssets = new List<Object>();
    private int selectedConfigIndex = -1;

    [MenuItem("Tools/敌人编辑器")]
    public static void Open()
    {
        EnemyEditorWindow window = GetWindow<EnemyEditorWindow>("敌人编辑器");
        window.minSize = new Vector2(700, 500);
        window.RefreshCurrent();
    }

    // ============================================================
    // 数据刷新
    // ============================================================

    private void RefreshCurrent()
    {
        if (currentTab == TabType.SceneEnemies) RefreshSceneEnemies();
        else RefreshConfigs();
    }

    /// <summary>只读场景层级里已创建的近战/远程敌人（Boss 排除，不含 Prefab 资产）</summary>
    private void RefreshSceneEnemies()
    {
        sceneEnemies.Clear();
        selectedSceneIndex = -1;
        foreach (var enemy in FindObjectsOfType<EnemyControllerBase>(true))
        {
            // Boss 单独设计，本编辑器不涉及
            if (enemy is BossControllerBase) continue;
            sceneEnemies.Add(enemy);
        }
        sceneEnemies.Sort((a, b) => string.Compare(a.name, b.name, System.StringComparison.Ordinal));
    }

    private void RefreshConfigs()
    {
        configAssets.Clear();
        selectedConfigIndex = -1;
        if (!Directory.Exists(EnemyDir)) return;

        string[] guids = AssetDatabase.FindAssets("t:EnemyConfigSO", new[] { EnemyDir });
        foreach (string guid in guids)
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            Object asset = AssetDatabase.LoadAssetAtPath<Object>(path);
            if (asset != null)
                configAssets.Add(asset);
        }
        configAssets.Sort((a, b) => string.Compare(a.name, b.name, System.StringComparison.Ordinal));
    }

    // ============================================================
    // GUI
    // ============================================================

    private void OnGUI()
    {
        DrawTabs();

        GUILayout.BeginHorizontal();
        if (currentTab == TabType.SceneEnemies)
        {
            DrawSceneEnemyList();
            DrawSceneEnemyDetail();
        }
        else
        {
            DrawConfigList();
            DrawConfigDetail();
        }
        GUILayout.EndHorizontal();
    }

    private void DrawTabs()
    {
        GUILayout.BeginHorizontal();
        foreach (TabType tab in System.Enum.GetValues(typeof(TabType)))
        {
            bool isActive = currentTab == tab;
            string label = tab == TabType.SceneEnemies ? "场景敌人" : "配置模板";
            if (GUILayout.Toggle(isActive, label, "Button", GUILayout.Width(100)))
            {
                if (currentTab != tab)
                {
                    currentTab = tab;
                    RefreshCurrent();
                }
            }
        }
        GUILayout.FlexibleSpace();
        if (GUILayout.Button("刷新", GUILayout.Width(60)))
            RefreshCurrent();
        GUILayout.EndHorizontal();
        GUILayout.Space(4);
    }

    // ── 场景敌人列表 ──

    private void DrawSceneEnemyList()
    {
        GUILayout.BeginVertical(GUI.skin.box, GUILayout.Width(240));
        GUILayout.Label($"场景敌人 ({sceneEnemies.Count})", EditorStyles.boldLabel);

        listScroll = GUILayout.BeginScrollView(listScroll);
        for (int i = 0; i < sceneEnemies.Count; i++)
        {
            EnemyControllerBase enemy = sceneEnemies[i];
            if (enemy == null) continue;
            bool selected = (i == selectedSceneIndex);
            GUIStyle style = selected ? "SelectionRect" : "Label";
            string label = $"{enemy.name}  [{enemy.GetType().Name}]";
            if (GUILayout.Button(label, style, GUILayout.Height(24)))
            {
                selectedSceneIndex = i;
                Selection.activeObject = enemy.gameObject;
            }
        }
        GUILayout.EndScrollView();

        GUILayout.Space(4);
        DrawNewEnemyForm();

        GUILayout.EndVertical();
    }

    /// <summary>新增敌人表单：类型 + Lv → 克隆场景同类型模板并烘焙 SO 数值</summary>
    private void DrawNewEnemyForm()
    {
        GUILayout.Label("新增敌人", EditorStyles.boldLabel);
        newEnemyType = EditorGUILayout.Popup("类型", newEnemyType, new[] { "近战", "远程" });
        newEnemyLv = EditorGUILayout.IntPopup("等级", newEnemyLv, new[] { "Lv1", "Lv2", "Lv3" }, new[] { 1, 2, 3 });
        if (GUILayout.Button("创建（克隆场景同类型）"))
            CreateNewEnemyObject();
    }

    private void DrawSceneEnemyDetail()
    {
        GUILayout.BeginVertical(GUI.skin.box);
        if (selectedSceneIndex < 0 || selectedSceneIndex >= sceneEnemies.Count || sceneEnemies[selectedSceneIndex] == null)
        {
            GUILayout.Label("← 从左侧选择一个场景敌人", EditorStyles.centeredGreyMiniLabel);
            GUILayout.EndVertical();
            return;
        }

        EnemyControllerBase enemy = sceneEnemies[selectedSceneIndex];
        detailScroll = GUILayout.BeginScrollView(detailScroll);

        GUILayout.Label($"{enemy.name}  ({enemy.GetType().Name})", EditorStyles.boldLabel);
        GUILayout.Label("编辑的是场景对象序列化字段，改完 Ctrl+S 保存场景。", EditorStyles.miniLabel);
        GUILayout.Space(4);

        // 直接编辑该组件字段（场景数据，保存场景持久化）
        Editor editor = Editor.CreateEditor(enemy);
        if (editor != null)
        {
            editor.OnInspectorGUI();
            DestroyImmediate(editor);
        }

        GUILayout.Space(8);
        GUILayout.BeginHorizontal();
        if (GUILayout.Button("在层级中选中", GUILayout.Width(110)))
        {
            Selection.activeObject = enemy.gameObject;
            EditorGUIUtility.PingObject(enemy.gameObject);
        }
        if (GUILayout.Button("应用 SO Lv 值", GUILayout.Width(110)))
        {
            if (BakeLvStatsToEnemy(enemy, enemy.Level, true))
                EditorUtility.DisplayDialog("敌人编辑器", $"{enemy.name} 已按 SO 的 Lv{enemy.Level} 档烘焙数值到字段。", "确定");
        }
        GUILayout.EndHorizontal();

        GUILayout.EndScrollView();
        GUILayout.EndVertical();
    }

    // ── 配置模板列表 ──

    private void DrawConfigList()
    {
        GUILayout.BeginVertical(GUI.skin.box, GUILayout.Width(220));
        GUILayout.Label($"配置模板 ({configAssets.Count})", EditorStyles.boldLabel);

        listScroll = GUILayout.BeginScrollView(listScroll);
        for (int i = 0; i < configAssets.Count; i++)
        {
            Object asset = configAssets[i];
            if (asset == null) continue;
            bool selected = (i == selectedConfigIndex);
            GUIStyle style = selected ? "SelectionRect" : "Label";
            if (GUILayout.Button(asset.name, style, GUILayout.Height(24)))
            {
                selectedConfigIndex = i;
                Selection.activeObject = asset;
            }
        }
        GUILayout.EndScrollView();

        GUILayout.Space(4);
        if (GUILayout.Button("新建敌人配置..."))
            CreateNewEnemy();
        GUILayout.EndVertical();
    }

    private void DrawConfigDetail()
    {
        GUILayout.BeginVertical(GUI.skin.box);
        if (selectedConfigIndex < 0 || selectedConfigIndex >= configAssets.Count || configAssets[selectedConfigIndex] == null)
        {
            GUILayout.Label("← 从左侧选择一个配置模板", EditorStyles.centeredGreyMiniLabel);
            GUILayout.EndVertical();
            return;
        }

        Object asset = configAssets[selectedConfigIndex];
        detailScroll = GUILayout.BeginScrollView(detailScroll);

        // 资产路径
        string path = AssetDatabase.GetAssetPath(asset);
        GUILayout.Label($"路径: {path}", EditorStyles.miniLabel);
        GUILayout.Space(4);

        // 用默认 Inspector 绘制 SO 全部字段(Lv1/Lv2/Lv3 三组;Boss 字段已注释剥离)
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
        if (GUILayout.Button("删除配置", GUILayout.Width(100)))
        {
            if (EditorUtility.DisplayDialog("删除敌人配置", $"确定删除 {asset.name} ?", "删除", "取消"))
                DeleteSelectedEnemy(asset);
        }
        GUILayout.EndHorizontal();

        GUILayout.EndScrollView();
        GUILayout.EndVertical();
    }

    // ============================================================
    // 新增敌人（克隆场景同类型 + 烘焙 SO Lv 档数值）
    // ============================================================

    private void CreateNewEnemyObject()
    {
        // 1. 找场景中同类型模板（第一个匹配）
        EnemyControllerBase template = null;
        foreach (var e in sceneEnemies)
        {
            if (newEnemyType == 0 && e is EnemyMeleeController) { template = e; break; }
            if (newEnemyType == 1 && e is EnemyRangedController) { template = e; break; }
        }
        if (template == null)
        {
            EditorUtility.DisplayDialog("新增敌人",
                $"场景里没有{(newEnemyType == 0 ? "近战" : "远程")}敌人模板。\n请先在场景摆一个同类型敌人，再回来创建。", "确定");
            return;
        }

        // 2. 克隆（复制全部组件/子物体，不读 Prefab 资产）
        string typeName = newEnemyType == 0 ? "Melee" : "Ranged";
        GameObject clone = (GameObject)Object.Instantiate(template.gameObject);
        clone.name = $"Enemy_{typeName}_Lv{newEnemyLv}";
        clone.transform.SetParent(template.transform.parent, true);
        clone.transform.position = template.transform.position + Vector3.right * 2f;
        Undo.RegisterCreatedObjectUndo(clone, "新增敌人");

        // 3. 烘焙：按 SO 对应 Lv 档填数值 + 设 level + 挂 SO 引用
        bool baked = BakeLvStatsToEnemy(clone.GetComponent<EnemyControllerBase>(), newEnemyLv, false);

        RefreshSceneEnemies();
        Selection.activeObject = clone;
        EditorGUIUtility.PingObject(clone);
        if (!baked)
            EditorUtility.DisplayDialog("新增敌人", $"{clone.name} 已创建（复制自 {template.name}），但未找到对应配置模板，数值沿用模板。", "确定");
    }

    /// <summary>
    /// 把 EnemyConfigSO 对应 Lv 档数值烘焙写入 enemy 组件字段（自包含）。
    /// showDialog 为 true 时提示未找到 SO（用于"应用 SO Lv 值"按钮）。
    /// 返回是否成功烘焙（找到 SO 且该 Lv 档数值有效）。
    /// </summary>
    private static bool BakeLvStatsToEnemy(EnemyControllerBase enemy, int lv, bool showDialog)
    {
        if (enemy == null) return false;

        bool isMelee = enemy is EnemyMeleeController;
        string soName = isMelee ? MeleeSoName : RangedSoName;
        EnemyConfigSO so = AssetDatabase.LoadAssetAtPath<EnemyConfigSO>($"{EnemyDir}/{soName}.asset");
        if (so == null)
        {
            if (showDialog)
                EditorUtility.DisplayDialog("敌人编辑器", $"未找到 {soName}（路径 {EnemyDir}/{soName}.asset）。\n先在「配置模板」页签新建并命名。", "确定");
            return false;
        }
        EnemyLvStats stats = so.GetLvStats(lv);
        if (stats == null) return false;

        // controller 字段
        var ctrl = new SerializedObject(enemy);
        SetProp(ctrl, "level", lv);
        SetProp(ctrl, "config", so);
        SetFloat(ctrl, "maxHealth", stats.maxHealth);
        SetFloat(ctrl, "detectionWidth", stats.detectionWidth);
        SetFloat(ctrl, "detectionHeight", stats.detectionHeight);
        SetFloat(ctrl, "attackWidth", stats.attackWidth);
        SetFloat(ctrl, "attackHeight", stats.attackHeight);
        SetFloat(ctrl, "attackCooldownDuration", stats.attackCooldownDuration);
        SetFloat(ctrl, "rangedKnockbackForce", stats.rangedKnockbackForce);
        ctrl.ApplyModifiedProperties();

        // 子组件字段
        if (isMelee)
        {
            var meleeAtk = enemy.GetComponent<EnemyMeleeAttack>();
            if (meleeAtk != null)
            {
                var s = new SerializedObject(meleeAtk);
                SetFloat(s, "damage", stats.meleeDamage);
                s.ApplyModifiedProperties();
            }
            // 巡逻范围在 EnemyMeleeController
            var meleeCtrl = enemy as EnemyMeleeController;
            if (meleeCtrl != null)
            {
                var s = new SerializedObject(meleeCtrl);
                SetFloat(s, "patrolRange", stats.patrolRange);
                s.ApplyModifiedProperties();
            }
        }
        else
        {
            var rangedAtk = enemy.GetComponent<EnemyRangedAttack>();
            if (rangedAtk != null)
            {
                var s = new SerializedObject(rangedAtk);
                SetFloat(s, "damage", stats.rangedDamage);
                SetFloat(s, "bulletSpeed", stats.bulletSpeed);
                SetFloat(s, "bulletRadius", stats.bulletRadius);
                s.ApplyModifiedProperties();
            }
        }

        var contact = enemy.GetComponent<EnemyContactTrigger>();
        if (contact != null)
        {
            var s = new SerializedObject(contact);
            SetFloat(s, "pushForce", stats.contactPushForce);
            SetFloat(s, "cooldown", stats.contactCooldown);
            SetFloat(s, "detectRadius", stats.contactDetectRadius);
            s.ApplyModifiedProperties();
        }

        EditorUtility.SetDirty(enemy.gameObject);
        return true;
    }

    private static void SetFloat(SerializedObject so, string propName, float val)
    {
        var p = so.FindProperty(propName);
        if (p != null) p.floatValue = val;
    }

    private static void SetProp(SerializedObject so, string propName, Object val)
    {
        var p = so.FindProperty(propName);
        if (p != null) p.objectReferenceValue = val;
    }

    private static void SetProp(SerializedObject so, string propName, int val)
    {
        var p = so.FindProperty(propName);
        if (p != null) p.intValue = val;
    }

    // ============================================================
    // 新建 / 删除（配置模板）
    // ============================================================

    private void CreateNewEnemy()
    {
        if (!Directory.Exists(EnemyDir)) Directory.CreateDirectory(EnemyDir);

        // 生成不重名文件名
        string baseName = "EnemyConfig_New";
        string path = AssetDatabase.GenerateUniqueAssetPath($"{EnemyDir}/{baseName}.asset");

        ScriptableObject asset = ScriptableObject.CreateInstance<EnemyConfigSO>();
        AssetDatabase.CreateAsset(asset, path);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        RefreshConfigs();
        // 选中新创建的
        for (int i = 0; i < configAssets.Count; i++)
        {
            if (AssetDatabase.GetAssetPath(configAssets[i]) == path)
            {
                selectedConfigIndex = i;
                Selection.activeObject = configAssets[i];
                break;
            }
        }
    }

    private void DeleteSelectedEnemy(Object asset)
    {
        string path = AssetDatabase.GetAssetPath(asset);
        AssetDatabase.DeleteAsset(path);
        RefreshConfigs();
        selectedConfigIndex = -1;
    }
}
