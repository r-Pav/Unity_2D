using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 音乐标点表格工具 — 把「在 Inspector 手写标点数组」换成「编辑一份 CSV 表格」。
///
/// 同文件两个类:
///   · MusicSheetCsv          —— 纯逻辑静态类(只依赖 UnityEngine + System,不引用 UnityEditor):表格文本 ↔ 数据的
///                               Build / TryParse / ReadFileText / WriteFileText。抽出来是为了能离线自检。
///   · MusicSheetEditorWindow —— 编辑器窗口:左侧曲资产列表、右侧表格路径 + 导出/导入/打开/刷新四个按钮 +
///                               Inspector 对照区 + 导入摘要与警告。骨架照抄 Assets/Editor/EnemyEditorWindow.cs。
///
/// 打开方式: Tools → 音乐标点表
/// 一曲一表: Assets/Data/Music/Sheet/&lt;曲资产名&gt;.csv(选中曲资产自动定位同名表,不手选文件)。
/// 导入为整表覆盖:该曲 points / introPoints / pointGroups 全部按表格重建 —— 以后标点只在表格里改,
/// 别再在 Inspector 手改数组(改了下次导入会被覆盖)。
///
/// 运行时链路零改动:标点仍读 MusicTrackData(SO) 的数组,本文件不碰任何运行时脚本。
/// </summary>
public static class MusicSheetCsv
{
    /// <summary>表头字面量(第一行必须是表头;列顺序可调,按列名找列)</summary>
    public const string HeaderLine = "section,group,time,note";

    /// <summary>升序去重容差(秒),与 MusicPointManager 的点表去重口径一致</summary>
    public const float DedupeTolerance = 0.001f;

    /// <summary>项目约定组名(白名单,仅用于导入时给警告,不阻断导入)</summary>
    private static readonly string[] KnownGroupNames =
    {
        "BossHeavy", "BossHeavySound",
        "BossOrb1", "BossOrb2", "BossOrb3", "BossOrb4", "BossOrb5",
        "PlayerCombo", "PlayerBackstab",
    };

    // ============================================================
    // 数据结构
    // ============================================================

    /// <summary>解析出的命名标点组(按表格首次出现顺序)</summary>
    public class ParsedGroup
    {
        /// <summary>组名</summary>
        public string groupName;

        /// <summary>组内标点(升序、已去重)</summary>
        public float[] points;

        /// <summary>该组在表格里只有占位行(有组名但一个点都没有)</summary>
        public bool placeholderOnly;
    }

    /// <summary>整表解析结果(导入时整表覆盖该曲)</summary>
    public class ParsedSheet
    {
        /// <summary>main 段 + group 留空 → 整曲 points</summary>
        public float[] points = new float[0];

        /// <summary>intro 段 → 前奏 introPoints</summary>
        public float[] introPoints = new float[0];

        /// <summary>main 段 + 有组名 → 命名标点组(按首次出现顺序)</summary>
        public List<ParsedGroup> groups = new List<ParsedGroup>();
    }

    /// <summary>该组名是否在项目约定名单内(仅用于警告)</summary>
    public static bool IsKnownGroupName(string groupName)
    {
        if (string.IsNullOrEmpty(groupName)) return false;
        for (int i = 0; i < KnownGroupNames.Length; i++)
        {
            if (KnownGroupNames[i] == groupName) return true;
        }
        return false;
    }

    // ============================================================
    // 生成:MusicTrackData → 表格文本
    // ============================================================

    /// <summary>
    /// 把该曲现有全部内容铺成表格文本。
    /// 行顺序:main 段在前(intro 段在后);main 段内先整曲 points,再 pointGroups 按 track.pointGroups 的原顺序;
    /// 每组内升序;空组输出「组名 + 空 time」一行(组名不会因为暂时没点而丢失)。time 格式化 F2。
    /// </summary>
    public static string Build(MusicTrackData track)
    {
        StringBuilder sb = new StringBuilder();
        sb.Append(HeaderLine).Append('\n');
        if (track == null) return sb.ToString();

        // ① main 段:整曲 points(组名留空)
        foreach (float p in SortedCopy(track.points))
            sb.Append("main,,").Append(FormatTime(p)).Append(",\n");

        // ② main 段:命名组(保持 SO 里的原顺序;空组只留组名)
        if (track.pointGroups != null)
        {
            foreach (MusicPointGroup g in track.pointGroups)
            {
                if (g == null) continue;
                string name = g.groupName == null ? string.Empty : g.groupName.Trim();
                if (name.Length == 0) continue;   // 无名组无法用表格表达,跳过

                if (g.points == null || g.points.Length == 0)
                {
                    sb.Append("main,").Append(EscapeCsv(name)).Append(",,\n");   // 空组:组名占位,time 留空
                    continue;
                }
                foreach (float p in SortedCopy(g.points))
                    sb.Append("main,").Append(EscapeCsv(name)).Append(',').Append(FormatTime(p)).Append(",\n");
            }
        }

        // ③ intro 段:前奏点(无分组结构,组名列必须留空)
        foreach (float p in SortedCopy(track.introPoints))
            sb.Append("intro,,").Append(FormatTime(p)).Append(",\n");

        return sb.ToString();
    }

    // ============================================================
    // 解析:表格文本 → 数据
    // ============================================================

    /// <summary>
    /// 解析表格文本。
    /// 返回 false 时 sheet 为 null、fatalError 带行号,调用方**不要**写资产(整表不导入)。
    /// 返回 true 时输出 points / introPoints / 按首次出现顺序的组列表,每段每组升序 + 容差 0.001 去重。
    /// </summary>
    public static bool TryParse(string text, out ParsedSheet sheet, out string fatalError, out List<string> warnings)
    {
        sheet = null;
        fatalError = null;
        warnings = new List<string>();

        if (string.IsNullOrEmpty(text))
        {
            fatalError = "表格内容为空:找不到表头行。";
            return false;
        }

        // 统一换行;顺手吃掉可能残留的 BOM 字符
        string[] lines = text.Replace("\r\n", "\n").Replace('\r', '\n').TrimStart('\uFEFF').Split('\n');

        // ── 表头:第一个非空非注释行 ──
        int headerIndex = -1;
        List<string> headerFields = null;
        for (int i = 0; i < lines.Length; i++)
        {
            if (IsIgnorableLine(lines[i])) continue;
            headerIndex = i;
            headerFields = SplitCsvLine(lines[i]);
            break;
        }
        if (headerIndex < 0)
        {
            fatalError = "表格没有任何内容:找不到表头行。";
            return false;
        }

        int colSection = IndexOfColumn(headerFields, "section");
        int colGroup = IndexOfColumn(headerFields, "group");
        int colTime = IndexOfColumn(headerFields, "time");
        int colNote = IndexOfColumn(headerFields, "note");   // note 是纯备注列,缺了也能解析

        List<string> missing = new List<string>();
        if (colSection < 0) missing.Add("section");
        if (colGroup < 0) missing.Add("group");
        if (colTime < 0) missing.Add("time");
        if (missing.Count > 0)
        {
            fatalError = string.Format("第 {0} 行表头缺少列: {1}(表头必须含 section / group / time,列顺序任意)。",
                headerIndex + 1, string.Join(" / ", missing.ToArray()));
            return false;
        }

        // ── 累加器 ──
        List<float> pointsAcc = new List<float>();
        List<float> introAcc = new List<float>();
        List<string> groupNames = new List<string>();          // 组名,按首次出现顺序
        List<List<float>> groupPoints = new List<List<float>>();
        HashSet<string> warnedUnknownGroups = new HashSet<string>();
        int dataLines = 0;      // 表头之后的非空非注释行数
        int validSectionLines = 0;

        for (int i = headerIndex + 1; i < lines.Length; i++)
        {
            string line = lines[i];
            if (IsIgnorableLine(line)) continue;
            dataLines++;
            int lineNo = i + 1;

            List<string> fields = SplitCsvLine(line);
            string section = GetField(fields, colSection).Trim().ToLowerInvariant();
            string groupName = GetField(fields, colGroup).Trim();
            string timeText = GetField(fields, colTime).Trim();
            // note 列 / 多余的列一律忽略(纯备注)

            // ① section 只认 main / intro
            if (section != "main" && section != "intro")
            {
                warnings.Add(string.Format("第 {0} 行:section 非法(\"{1}\"),该行已忽略(只允许 main / intro)。",
                    lineNo, GetField(fields, colSection).Trim()));
                continue;
            }
            validSectionLines++;

            // ② intro 段没有分组结构,带组名 = 警告并忽略
            if (section == "intro" && groupName.Length > 0)
            {
                warnings.Add(string.Format("第 {0} 行:intro 段不支持组名(\"{1}\"),该行已忽略。", lineNo, groupName));
                continue;
            }

            bool hasTime = timeText.Length > 0;
            float time = 0f;
            if (hasTime)
            {
                if (!TryParseSeconds(timeText, out time))
                {
                    // 致命:整表不导入,调用方不写资产
                    fatalError = string.Format("第 {0} 行 time 不是有效秒数:\"{1}\"(需为非负数字,允许 1~2 位小数)。整表未导入。",
                        lineNo, timeText);
                    sheet = null;
                    return false;
                }
                int decimals = CountDecimals(timeText);
                if (decimals > 2)
                    warnings.Add(string.Format("第 {0} 行:time 小数超过两位(\"{1}\"),已按原值导入。", lineNo, timeText));
            }
            else if (groupName.Length == 0)
            {
                // time 空 = 只登记组名占位行;组名也空就没有可登记的东西
                warnings.Add(string.Format("第 {0} 行:{1} 段组名为空且 time 为空,无可登记内容,该行已忽略。", lineNo, section));
                continue;
            }

            // ③ 分流:main → 整曲 points / 命名组;intro → introPoints
            if (section == "main")
            {
                if (groupName.Length == 0)
                {
                    pointsAcc.Add(time);
                }
                else
                {
                    int gi = EnsureGroup(groupNames, groupPoints, groupName);
                    if (hasTime) groupPoints[gi].Add(time);

                    if (!IsKnownGroupName(groupName) && warnedUnknownGroups.Add(groupName))
                        warnings.Add(string.Format("第 {0} 行:组名\"{1}\"不在项目约定名单内,已按新组建出(仅提示)。", lineNo, groupName));
                }
            }
            else
            {
                introAcc.Add(time);
            }
        }

        // section 列全非法 = 致命(避免"以为导入了其实整表被忽略")
        if (dataLines > 0 && validSectionLines == 0)
        {
            fatalError = "表格没有任何合法数据行:section 列只允许 main / intro。整表未导入。";
            return false;
        }
        if (dataLines == 0)
            warnings.Add("表格没有数据行:导入后该曲的 points / introPoints / pointGroups 会被清空。");

        // ── 收尾:每段每组升序 + 容差去重 ──
        sheet = new ParsedSheet();
        sheet.points = SortAndDedupe(pointsAcc, "整曲 points", warnings);
        sheet.introPoints = SortAndDedupe(introAcc, "introPoints", warnings);
        for (int i = 0; i < groupNames.Count; i++)
        {
            ParsedGroup g = new ParsedGroup();
            g.groupName = groupNames[i];
            g.placeholderOnly = groupPoints[i].Count == 0;   // 全是占位行 → 空组(仍要建出来,组名不能丢)
            g.points = SortAndDedupe(groupPoints[i], "组 " + groupNames[i], warnings);
            sheet.groups.Add(g);
        }
        return true;
    }

    // ============================================================
    // 文件读写(编码兜底)
    // ============================================================

    /// <summary>
    /// 读表格文件。有 UTF-8 BOM 按 UTF-8;无 BOM 先按 UTF-8 读,结果含替换字符(0xFFFD)则按 GBK 重读。
    /// 目的:兼容 Excel 的两种另存方式(「CSV UTF-8」带 BOM / 老版本另存 GBK)。
    /// </summary>
    public static string ReadFileText(string path)
    {
        byte[] bytes = File.ReadAllBytes(path);

        // UTF-8 BOM
        if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
            return new UTF8Encoding(false).GetString(bytes, 3, bytes.Length - 3);

        // 无 BOM:先按 UTF-8(非法字节会变成 0xFFFD,不抛异常)
        string utf8Text = new UTF8Encoding(false).GetString(bytes);
        if (utf8Text.IndexOf('\uFFFD') < 0) return utf8Text;

        // 出现替换字符 → 判定为 GBK 文本,重读一次
        try
        {
            return Encoding.GetEncoding(936).GetString(bytes);
        }
        catch (Exception)
        {
            return utf8Text;   // 该运行环境没有 936 代码页时退回 UTF-8 结果(不抛给调用方)
        }
    }

    /// <summary>写表格文件:UTF-8 with BOM + CRLF(Excel 双击不乱码、记事本不显示成一行)</summary>
    public static void WriteFileText(string path, string text)
    {
        string normalized = (text ?? string.Empty).Replace("\r\n", "\n").Replace("\n", "\r\n");
        File.WriteAllText(path, normalized, new UTF8Encoding(true));
    }

    /// <summary>把时间秒格式化成两位小数(InvariantCulture,不吃本地化小数点)</summary>
    public static string FormatTime(float seconds)
    {
        return seconds.ToString("F2", CultureInfo.InvariantCulture);
    }

    // ============================================================
    // 内部工具
    // ============================================================

    /// <summary>空行、以及以 # 开头的整行 = 忽略(注释行)</summary>
    private static bool IsIgnorableLine(string line)
    {
        if (line == null) return true;
        string trimmed = line.Trim();
        return trimmed.Length == 0 || trimmed.StartsWith("#", StringComparison.Ordinal);
    }

    /// <summary>按表头名找列(大小写不敏感、去空格;找不到返回 -1)</summary>
    private static int IndexOfColumn(List<string> headerFields, string columnName)
    {
        for (int i = 0; i < headerFields.Count; i++)
        {
            if (string.Equals(headerFields[i].Trim(), columnName, StringComparison.OrdinalIgnoreCase)) return i;
        }
        return -1;
    }

    private static string GetField(List<string> fields, int index)
    {
        if (index < 0 || index >= fields.Count) return string.Empty;
        return fields[index] ?? string.Empty;
    }

    /// <summary>
    /// 手写 CSV 拆行:支持双引号包裹的字段(字段内可含逗号 / 换行前的内容),"" 表示一个引号字符。
    /// </summary>
    public static List<string> SplitCsvLine(string line)
    {
        List<string> fields = new List<string>();
        if (line == null)
        {
            fields.Add(string.Empty);
            return fields;
        }

        StringBuilder sb = new StringBuilder();
        bool inQuotes = false;
        for (int i = 0; i < line.Length; i++)
        {
            char c = line[i];
            if (inQuotes)
            {
                if (c == '"')
                {
                    if (i + 1 < line.Length && line[i + 1] == '"') { sb.Append('"'); i++; }   // 转义引号
                    else inQuotes = false;                                                     // 闭合
                }
                else sb.Append(c);
            }
            else
            {
                if (c == '"') inQuotes = true;
                else if (c == ',') { fields.Add(sb.ToString()); sb.Length = 0; }
                else sb.Append(c);
            }
        }
        fields.Add(sb.ToString());
        return fields;
    }

    /// <summary>需要时给字段加引号(组名一般不含逗号,顺手做全)</summary>
    private static string EscapeCsv(string field)
    {
        if (string.IsNullOrEmpty(field)) return string.Empty;
        if (field.IndexOf(',') < 0 && field.IndexOf('"') < 0 &&
            field.IndexOf('\n') < 0 && field.IndexOf('\r') < 0) return field;
        return "\"" + field.Replace("\"", "\"\"") + "\"";
    }

    /// <summary>严格按 InvariantCulture 解析秒数:拒绝千分位、指数写法、NaN / 无穷 / 负数</summary>
    private static bool TryParseSeconds(string text, out float seconds)
    {
        seconds = 0f;
        const NumberStyles style = (NumberStyles.Float & ~NumberStyles.AllowExponent) | NumberStyles.AllowLeadingSign;
        if (!float.TryParse(text, style, CultureInfo.InvariantCulture, out float value)) return false;
        if (float.IsNaN(value) || float.IsInfinity(value) || value < 0f) return false;
        seconds = value;
        return true;
    }

    /// <summary>小数点后的位数(用于「超过两位」警告)</summary>
    private static int CountDecimals(string text)
    {
        int dot = text.IndexOf('.');
        if (dot < 0) return 0;
        int count = 0;
        for (int i = dot + 1; i < text.Length; i++)
        {
            if (text[i] < '0' || text[i] > '9') break;
            count++;
        }
        return count;
    }

    /// <summary>升序排序 + 容差 0.001 去重(与 MusicPointManager 的点表去重口径一致)</summary>
    private static float[] SortAndDedupe(List<float> source, string label, List<string> warnings)
    {
        if (source == null || source.Count == 0) return new float[0];

        List<float> sorted = new List<float>(source);
        sorted.Sort();

        List<float> result = new List<float>(sorted.Count);
        foreach (float v in sorted)
        {
            if (result.Count > 0 && Mathf.Abs(v - result[result.Count - 1]) < DedupeTolerance)
                continue;   // 容差内视为同一个点
            result.Add(v);
        }

        if (result.Count < sorted.Count)
            warnings.Add(string.Format("{0}:有 {1} 个重复点(容差 {2} 秒)已去重。", label, sorted.Count - result.Count, DedupeTolerance));

        return result.ToArray();
    }

    /// <summary>升序副本(导出时按升序铺表)</summary>
    private static float[] SortedCopy(float[] source)
    {
        if (source == null || source.Length == 0) return new float[0];
        float[] copy = (float[])source.Clone();
        Array.Sort(copy);
        return copy;
    }

    /// <summary>按组名找组,没有就按首次出现顺序追加一个空组</summary>
    private static int EnsureGroup(List<string> groupNames, List<List<float>> groupPoints, string groupName)
    {
        for (int i = 0; i < groupNames.Count; i++)
        {
            if (groupNames[i] == groupName) return i;
        }
        groupNames.Add(groupName);
        groupPoints.Add(new List<float>());
        return groupNames.Count - 1;
    }
}

/// <summary>
/// 音乐标点表编辑器窗口(骨架照抄 EnemyEditorWindow:Tools 菜单 + 左侧资产列表 + 右侧复用 Inspector)。
/// 左侧曲资产列表 → 右侧导出/导入/打开/刷新 + Inspector 对照区 + 导入摘要与警告。
/// </summary>
public class MusicSheetEditorWindow : EditorWindow
{
    private const string MusicRootDir = "Assets/Data/Music";
    private const string SheetDir = "Assets/Data/Music/Sheet";

    private readonly List<MusicTrackData> tracks = new List<MusicTrackData>();
    private int selectedIndex = -1;

    private Vector2 listScroll;
    private Vector2 detailScroll;
    private Vector2 warningScroll;

    private Editor trackInspector;          // Inspector 对照区(复用同一个 Editor 实例)
    private MusicTrackData inspectorTarget;

    private string resultSummary = string.Empty;
    private readonly List<string> resultWarnings = new List<string>();

    [MenuItem("Tools/音乐标点表")]
    public static void Open()
    {
        MusicSheetEditorWindow window = GetWindow<MusicSheetEditorWindow>("音乐标点表");
        window.minSize = new Vector2(760, 520);
        window.RefreshTracks();
    }

    private void OnDisable()
    {
        DestroyTrackInspector();
    }

    // ============================================================
    // 数据刷新
    // ============================================================

    /// <summary>扫 Assets/Data/Music 下的 MusicTrackData 资产(AudioLibrary.asset 不是该类型,按类型过滤自动排除)</summary>
    private void RefreshTracks()
    {
        // 刷新前记住当前选中的曲,刷完还原(否则每次刷新都跳回第一首)
        string keepName = SelectedTrack != null ? SelectedTrack.name : null;

        tracks.Clear();
        selectedIndex = -1;

        if (!Directory.Exists(MusicRootDir)) return;

        string[] guids = AssetDatabase.FindAssets("t:MusicTrackData", new[] { MusicRootDir });
        foreach (string guid in guids)
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            MusicTrackData track = AssetDatabase.LoadAssetAtPath<MusicTrackData>(path);
            if (track != null) tracks.Add(track);
        }
        tracks.Sort((a, b) => string.Compare(a.name, b.name, StringComparison.Ordinal));

        int restored = -1;
        if (keepName != null)
        {
            for (int i = 0; i < tracks.Count; i++)
            {
                if (tracks[i] != null && tracks[i].name == keepName) { restored = i; break; }
            }
        }
        if (restored >= 0) Select(restored);
        else if (tracks.Count > 0) Select(0);
    }

    private void Select(int index)
    {
        selectedIndex = index;
        MusicTrackData track = SelectedTrack;
        Selection.activeObject = track;
        if (track != null) EditorGUIUtility.PingObject(track);
    }

    private MusicTrackData SelectedTrack
    {
        get { return (selectedIndex >= 0 && selectedIndex < tracks.Count) ? tracks[selectedIndex] : null; }
    }

    private static string SheetPathFor(MusicTrackData track)
    {
        return SheetDir + "/" + track.name + ".csv";
    }

    // ============================================================
    // GUI
    // ============================================================

    private void OnGUI()
    {
        GUILayout.BeginHorizontal();
        DrawTrackList();
        DrawDetail();
        GUILayout.EndHorizontal();
    }

    private void DrawTrackList()
    {
        GUILayout.BeginVertical(GUI.skin.box, GUILayout.Width(240));
        GUILayout.Label(string.Format("曲资产 ({0})", tracks.Count), EditorStyles.boldLabel);

        listScroll = GUILayout.BeginScrollView(listScroll);
        for (int i = 0; i < tracks.Count; i++)
        {
            MusicTrackData track = tracks[i];
            if (track == null) continue;
            GUIStyle style = (i == selectedIndex) ? "SelectionRect" : "Label";
            if (GUILayout.Button(track.name, style, GUILayout.Height(24)))
                Select(i);
        }
        GUILayout.EndScrollView();

        GUILayout.Space(4);
        if (GUILayout.Button("刷新")) RefreshTracks();
        GUILayout.Label("目录: " + MusicRootDir, EditorStyles.miniLabel);
        GUILayout.EndVertical();
    }

    private void DrawDetail()
    {
        GUILayout.BeginVertical(GUI.skin.box);
        MusicTrackData track = SelectedTrack;
        if (track == null)
        {
            GUILayout.Label("← 从左侧选择一个曲资产", EditorStyles.centeredGreyMiniLabel);
            GUILayout.EndVertical();
            return;
        }

        detailScroll = GUILayout.BeginScrollView(detailScroll);

        string assetPath = AssetDatabase.GetAssetPath(track);
        string sheetPath = SheetPathFor(track);
        bool sheetExists = File.Exists(sheetPath);

        GUILayout.Label(track.name, EditorStyles.boldLabel);
        GUILayout.Label("曲资产: " + assetPath, EditorStyles.miniLabel);
        GUILayout.Label("表格: " + sheetPath + (sheetExists ? string.Format("  (存在,{0} 行)", CountFileLines(sheetPath)) : "  (不存在)"),
            EditorStyles.miniLabel);
        GUILayout.Space(4);

        // ── 四个按钮 ──
        GUILayout.BeginHorizontal();
        if (GUILayout.Button("导出到表格", GUILayout.Width(110))) ExportToSheet(track);
        if (GUILayout.Button("从表格导入", GUILayout.Width(110))) ImportFromSheet(track);
        if (GUILayout.Button("打开表格", GUILayout.Width(90))) OpenSheetFile(sheetPath);
        if (GUILayout.Button("刷新", GUILayout.Width(70))) RefreshTracks();
        GUILayout.EndHorizontal();

        GUILayout.Label("导入为整表覆盖:该曲 points / introPoints / pointGroups 全部按表格重建。以后标点只在表格里改。",
            EditorStyles.miniLabel);
        GUILayout.Space(6);

        // ── 导入结果摘要 ──
        if (!string.IsNullOrEmpty(resultSummary))
        {
            GUILayout.Label("导入结果", EditorStyles.boldLabel);
            GUILayout.Label(resultSummary, EditorStyles.wordWrappedMiniLabel);

            if (resultWarnings.Count > 0)
            {
                GUILayout.Label(string.Format("警告 ({0})", resultWarnings.Count), EditorStyles.boldLabel);
                warningScroll = GUILayout.BeginScrollView(warningScroll, GUILayout.Height(90));
                for (int i = 0; i < resultWarnings.Count; i++)
                    GUILayout.Label("· " + resultWarnings[i], EditorStyles.wordWrappedMiniLabel);
                GUILayout.EndScrollView();
            }
            GUILayout.Space(6);
        }

        // ── Inspector 对照区(复用默认 Inspector,看导入后的数组) ──
        GUILayout.Label("Inspector 对照区", EditorStyles.boldLabel);
        DrawInspectorCompare(track);

        GUILayout.EndScrollView();
        GUILayout.EndVertical();
    }

    /// <summary>Inspector 对照区:同一曲复用同一个 Editor 实例,换曲时销毁重建</summary>
    private void DrawInspectorCompare(MusicTrackData track)
    {
        if (inspectorTarget != track)
        {
            DestroyTrackInspector();
            inspectorTarget = track;
            if (track != null) trackInspector = Editor.CreateEditor(track);
        }
        if (trackInspector != null)
            trackInspector.OnInspectorGUI();
    }

    private void DestroyTrackInspector()
    {
        if (trackInspector != null)
        {
            DestroyImmediate(trackInspector);
            trackInspector = null;
        }
        inspectorTarget = null;
    }

    // ============================================================
    // 导出 / 导入 / 打开
    // ============================================================

    /// <summary>导出到表格:Build + WriteFileText(覆盖前确认;目录不存在自动创建)</summary>
    private void ExportToSheet(MusicTrackData track)
    {
        string sheetPath = SheetPathFor(track);
        if (File.Exists(sheetPath))
        {
            if (!EditorUtility.DisplayDialog("音乐标点表 — 导出",
                    "表格已存在,确定覆盖?\n" + sheetPath + "\n\n(表格里未导入的改动会丢失)", "覆盖", "取消"))
                return;
        }

        string dir = Path.GetDirectoryName(sheetPath);
        if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir)) Directory.CreateDirectory(dir);

        MusicSheetCsv.WriteFileText(sheetPath, MusicSheetCsv.Build(track));
        AssetDatabase.Refresh();

        resultSummary = string.Format("已导出到 {0}\npoints {1} 个 / introPoints {2} 个 / 命名组 {3} 个。",
            sheetPath, Count(track.points), Count(track.introPoints), track.pointGroups == null ? 0 : track.pointGroups.Length);
        resultWarnings.Clear();
    }

    /// <summary>从表格导入:ReadFileText + TryParse + 整表写回资产(致命错误时弹窗且不写资产)</summary>
    private void ImportFromSheet(MusicTrackData track)
    {
        string sheetPath = SheetPathFor(track);
        if (!File.Exists(sheetPath))
        {
            EditorUtility.DisplayDialog("音乐标点表 — 导入",
                "表格不存在:\n" + sheetPath + "\n\n先点「导出到表格」生成一份。", "确定");
            return;
        }

        string text = MusicSheetCsv.ReadFileText(sheetPath);
        MusicSheetCsv.ParsedSheet sheet;
        string fatalError;
        List<string> warnings;
        if (!MusicSheetCsv.TryParse(text, out sheet, out fatalError, out warnings))
        {
            resultSummary = "导入失败:该曲资产未被修改。";
            resultWarnings.Clear();
            resultWarnings.AddRange(warnings);
            resultWarnings.Add(fatalError);
            EditorUtility.DisplayDialog("音乐标点表 — 导入失败", fatalError + "\n\n该曲资产未被修改。", "确定");
            return;
        }

        ApplySheet(track, sheet);

        int groupPointTotal = 0;
        for (int i = 0; i < sheet.groups.Count; i++) groupPointTotal += Count(sheet.groups[i].points);

        resultSummary = string.Format(
            "已从 {0} 导入(整表覆盖):points {1} 个 / introPoints {2} 个 / 命名组 {3} 个(共 {4} 点)。",
            sheetPath, Count(sheet.points), Count(sheet.introPoints), sheet.groups.Count, groupPointTotal);

        resultWarnings.Clear();
        resultWarnings.AddRange(warnings);
    }

    /// <summary>整表写回资产:清空 points / introPoints / pointGroups 后按解析结果重建</summary>
    private static void ApplySheet(MusicTrackData track, MusicSheetCsv.ParsedSheet sheet)
    {
        Undo.RecordObject(track, "从表格导入音乐标点");

        track.points = sheet.points != null ? sheet.points : new float[0];
        track.introPoints = sheet.introPoints != null ? sheet.introPoints : new float[0];

        MusicPointGroup[] groups = new MusicPointGroup[sheet.groups.Count];
        for (int i = 0; i < sheet.groups.Count; i++)
        {
            groups[i] = new MusicPointGroup();
            groups[i].groupName = sheet.groups[i].groupName;
            groups[i].points = sheet.groups[i].points != null ? sheet.groups[i].points : new float[0];
        }
        track.pointGroups = groups;

        EditorUtility.SetDirty(track);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
    }

    /// <summary>打开表格:资源管理器里定位该文件(不存在则提示先导出)</summary>
    private void OpenSheetFile(string sheetPath)
    {
        if (!File.Exists(sheetPath))
        {
            EditorUtility.DisplayDialog("音乐标点表 — 打开表格",
                "表格不存在:\n" + sheetPath + "\n\n先点「导出到表格」生成一份。", "确定");
            return;
        }
        EditorUtility.RevealInFinder(Path.GetFullPath(sheetPath));
    }

    private static int Count(float[] array)
    {
        return array == null ? 0 : array.Length;
    }

    private static int CountFileLines(string path)
    {
        try
        {
            return File.ReadAllLines(path).Length;
        }
        catch (Exception)
        {
            return 0;
        }
    }
}
