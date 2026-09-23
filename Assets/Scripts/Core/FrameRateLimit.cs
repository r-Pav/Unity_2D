using UnityEngine;

/// <summary>
/// 帧率档位统一入口（设置面板的「帧率」选项用）。
/// 关键：Unity 在 vSyncCount &gt; 0 时会忽略 targetFrameRate，两者只能生效一个。
/// 所以垂直同步档走 vSyncCount，30/60/120 档把 vSyncCount 归 0 再走 targetFrameRate。
/// </summary>
public static class FrameRateLimit
{
    public const int ModeVSync = 0;   // 垂直同步：帧率跟显示器刷新率
    public const int Mode30 = 1;
    public const int Mode60 = 2;
    public const int Mode120 = 3;

    /// <summary>档位 → 目标帧率；垂直同步档返回 -1（不限帧）</summary>
    public static int ToFrameRate(int mode)
    {
        switch (mode)
        {
            case Mode30: return 30;
            case Mode60: return 60;
            case Mode120: return 120;
            default: return -1;
        }
    }

    /// <summary>档位显示名（设置面板与日志共用）</summary>
    public static string ToLabel(int mode)
    {
        int fps = ToFrameRate(mode);
        return fps < 0 ? "垂直同步" : fps + " 帧";
    }

    /// <summary>应用档位。限帧档关垂直同步换 targetFrameRate，垂直同步档反过来</summary>
    public static void Apply(int mode)
    {
        int fps = ToFrameRate(mode);
        if (fps < 0)
        {
            QualitySettings.vSyncCount = 1;
            Application.targetFrameRate = -1;
        }
        else
        {
            QualitySettings.vSyncCount = 0;
            Application.targetFrameRate = fps;
        }
    }

    /// <summary>按当前运行值反推档位（存档缺失时的兜底）</summary>
    public static int Current
    {
        get
        {
            if (QualitySettings.vSyncCount > 0) return ModeVSync;
            switch (Application.targetFrameRate)
            {
                case 30: return Mode30;
                case 60: return Mode60;
                case 120: return Mode120;
                default: return ModeVSync;
            }
        }
    }
}
