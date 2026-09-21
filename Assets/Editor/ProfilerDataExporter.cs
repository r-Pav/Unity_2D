using System.Globalization;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.Profiling;
using UnityEditorInternal;
using UnityEngine;

/// <summary>
/// Profiler 采集(.data / .raw)导出 CSV —— 供离线排序找热点。只读工具,不改工程任何资产。
///
/// 菜单:Tools/性能/Profiler 采集导出 CSV
/// 产物:输出到采集文件同目录的 &lt;文件名&gt;_csv/ 下
///   frames.csv   每帧一行:frameIndex, cpuMs, fps, gpuMs, threadCount, sampleCount, playerLoopMs, editorLoopMs
///   samples.csv  每个线程每个样本一行:frameIndex, threadGroup, threadName, sampleIndex, name,
///                categoryIndex, totalMs, startMs, childrenCount
///                (父样本在前、childrenCount 描述紧随其后的直接子样本数,层级可离线重建)
///
/// 实现:ProfilerDriver.LoadProfile 载入采集 + ProfilerDriver.GetRawFrameDataView 逐帧逐线程遍历样本,
/// 不依赖第三方库、不落任何临时资产。
/// 注意:LoadProfile 会替换 Profiler 窗口当前显示的采集,想留的采集先存盘。
/// </summary>
public class ProfilerDataExporter : EditorWindow
{
    private const string LastPathKey = "ProfilerDataExporter_LastPath";
    private const string DefaultPath = @"F:/1-Output_All/Unity/0921.data";

    private string dataPath = "";

    [MenuItem("Tools/性能/Profiler 采集导出 CSV")]
    private static void Open()
    {
        ProfilerDataExporter w = GetWindow<ProfilerDataExporter>(true, "Profiler 导出 CSV");
        w.minSize = new Vector2(620f, 190f);
        w.dataPath = EditorPrefs.GetString(LastPathKey, DefaultPath);
        w.Show();
    }

    private void OnGUI()
    {
        EditorGUILayout.LabelField("Profiler 采集文件(.data / .raw)", EditorStyles.boldLabel);
        using (new EditorGUILayout.HorizontalScope())
        {
            dataPath = EditorGUILayout.TextField(dataPath);
            if (GUILayout.Button("浏览", GUILayout.Width(64f)))
            {
                string dir = string.IsNullOrEmpty(dataPath) ? "" : Path.GetDirectoryName(dataPath);
                string p = EditorUtility.OpenFilePanel("选择 Profiler 采集文件", dir, "data,raw");
                if (!string.IsNullOrEmpty(p)) dataPath = p;
            }
        }

        EditorGUILayout.HelpBox(
            "输出到采集文件同目录的 <文件名>_csv/:\n" +
            "  frames.csv   每帧 CPU / FPS / 顶层 PlayerLoop、EditorLoop 耗时\n" +
            "  samples.csv  全部线程全部样本(帧号 / 名字 / 总耗时 / 起始 / 子样本数)\n\n" +
            "导出会替换 Profiler 窗口当前显示的采集。",
            MessageType.Info);

        using (new EditorGUI.DisabledScope(string.IsNullOrEmpty(dataPath)))
        {
            if (GUILayout.Button("加载并导出 CSV", GUILayout.Height(32f)))
                Export(dataPath);
        }
    }

    private static void Export(string path)
    {
        if (!File.Exists(path))
        {
            EditorUtility.DisplayDialog("导出失败", "文件不存在:\n" + path, "好");
            return;
        }

        string outDir = Path.Combine(Path.GetDirectoryName(path) ?? "", Path.GetFileNameWithoutExtension(path) + "_csv");
        Directory.CreateDirectory(outDir);
        EditorPrefs.SetString(LastPathKey, path);

        try
        {
            EditorUtility.DisplayProgressBar("Profiler 导出", "加载采集…", 0f);
            ProfilerDriver.ClearAllFrames();
            ProfilerDriver.LoadProfile(path, false);

            int first = ProfilerDriver.firstFrameIndex;
            int last = ProfilerDriver.lastFrameIndex;
            if (first < 0 || last < first)
            {
                EditorUtility.ClearProgressBar();
                EditorUtility.DisplayDialog("导出失败",
                    "加载后拿不到帧数据。确认选的是 Profiler 采集的 .data/.raw 文件。", "好");
                return;
            }

            int frameCount = last - first + 1;
            string framesPath = Path.Combine(outDir, "frames.csv");
            string samplesPath = Path.Combine(outDir, "samples.csv");
            int writtenFrames = 0;

            using (StreamWriter framesW = new StreamWriter(framesPath, false, new UTF8Encoding(false)))
            using (StreamWriter samplesW = new StreamWriter(samplesPath, false, new UTF8Encoding(false)))
            {
                framesW.WriteLine("frameIndex,cpuMs,fps,gpuMs,threadCount,sampleCount,playerLoopMs,editorLoopMs");
                samplesW.WriteLine("frameIndex,threadGroup,threadName,sampleIndex,name,categoryIndex,totalMs,startMs,childrenCount");

                for (int f = first; f <= last; f++)
                {
                    if (EditorUtility.DisplayCancelableProgressBar("Profiler 导出 CSV",
                            string.Format("帧 {0}/{1}", f - first + 1, frameCount),
                            (f - first) / (float)frameCount))
                        break;

                    float cpuMs = 0f, fps = 0f, gpuMs = 0f, playerLoopMs = 0f, editorLoopMs = 0f;
                    int threadCount = 0, sampleCount = 0;

                    for (int t = 0; ; t++)
                    {
                        using (RawFrameDataView view = ProfilerDriver.GetRawFrameDataView(f, t))
                        {
                            if (!view.valid) break;

                            threadCount++;
                            sampleCount += view.sampleCount;

                            string group = view.threadGroupName;
                            string tname = view.threadName;

                            // 主线程的帧时间/FPS/GPU 时间(帧级数据取一次)
                            if (cpuMs <= 0f && group == "Main Thread" && view.frameTimeMs > 0f)
                            {
                                cpuMs = view.frameTimeMs;
                                fps = view.frameFps;
                                gpuMs = view.frameGpuTimeMs;
                            }

                            for (int i = 0; i < view.sampleCount; i++)
                            {
                                string name = view.GetSampleName(i);
                                float total = view.GetSampleTimeMs(i);

                                if (group == "Main Thread")
                                {
                                    if (name == "PlayerLoop") playerLoopMs += total;
                                    else if (name == "EditorLoop") editorLoopMs += total;
                                }

                                samplesW.Write(f); samplesW.Write(',');
                                samplesW.Write(Csv(group)); samplesW.Write(',');
                                samplesW.Write(Csv(tname)); samplesW.Write(',');
                                samplesW.Write(i); samplesW.Write(',');
                                samplesW.Write(Csv(name)); samplesW.Write(',');
                                samplesW.Write(view.GetSampleCategoryIndex(i)); samplesW.Write(',');
                                samplesW.Write(total.ToString("F4", CultureInfo.InvariantCulture)); samplesW.Write(',');
                                samplesW.Write(view.GetSampleStartTimeMs(i).ToString("F4", CultureInfo.InvariantCulture)); samplesW.Write(',');
                                samplesW.Write(view.GetSampleChildrenCount(i));
                                samplesW.Write('\n');
                            }
                        }
                    }

                    framesW.Write(f); framesW.Write(',');
                    framesW.Write(cpuMs.ToString("F4", CultureInfo.InvariantCulture)); framesW.Write(',');
                    framesW.Write(fps.ToString("F2", CultureInfo.InvariantCulture)); framesW.Write(',');
                    framesW.Write(gpuMs.ToString("F4", CultureInfo.InvariantCulture)); framesW.Write(',');
                    framesW.Write(threadCount); framesW.Write(',');
                    framesW.Write(sampleCount); framesW.Write(',');
                    framesW.Write(playerLoopMs.ToString("F4", CultureInfo.InvariantCulture)); framesW.Write(',');
                    framesW.Write(editorLoopMs.ToString("F4", CultureInfo.InvariantCulture));
                    framesW.Write('\n');
                    writtenFrames++;
                }
            }

            EditorUtility.ClearProgressBar();
            Debug.Log(string.Format("[ProfilerDataExporter] 导出完成:帧 {0}~{1}({2} 帧)\n{3}\n{4}",
                first, last, writtenFrames, framesPath, samplesPath));
            EditorUtility.DisplayDialog("导出完成",
                string.Format("帧 {0}~{1},已写 {2} 帧\n\n{3}\n{4}", first, last, writtenFrames, framesPath, samplesPath), "好");
            EditorUtility.RevealInFinder(framesPath);
        }
        finally
        {
            EditorUtility.ClearProgressBar();
        }
    }

    /// <summary>CSV 字段转义(名字里可能带逗号/引号)</summary>
    private static string Csv(string s)
    {
        if (string.IsNullOrEmpty(s)) return "";
        if (s.IndexOf(',') < 0 && s.IndexOf('"') < 0 && s.IndexOf('\n') < 0) return s;
        return "\"" + s.Replace("\"", "\"\"") + "\"";
    }
}
