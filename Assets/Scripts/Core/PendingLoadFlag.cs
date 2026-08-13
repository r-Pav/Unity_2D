/// <summary>
/// 跨场景待读档标记 — 纯静态类（零 MonoBehaviour / 零序列化 / 零 UnityEvent）。
/// 静态字段跨场景存活，用于 TitleScene → SampleScene 传递读档意图：
///   - TitleScene 写：新游戏 slot = -1；读档 slot = N（写入自动置 wasSet = true）
///   - SampleScene 启动读后清（见 Clear()）
/// 跨场景静态传递，不用 PlayerPrefs（不落盘，进程内一次性）。
/// </summary>
public static class PendingLoadFlag
{
    /// <summary>
    /// 标记是否被写入过：
    ///   false = 从未设置（编辑器直接进 SampleScene 调试）→ SceneBootstrap 走调试模式原地不动；
    ///   true  = 已由主菜单写入 → 按 slot 判定读档/新游戏。
    /// 注意：slot 默认 -1 与「新游戏标记 -1」数值相同，必须靠此标志区分「未设置」和「新游戏」。
    /// </summary>
    public static bool wasSet = false;

    private static int _slot = -1;

    /// <summary>待读档槽位：-1 = 新游戏，>=0 = 读档槽位。写入时自动置 wasSet = true</summary>
    public static int slot
    {
        get { return _slot; }
        set
        {
            _slot = value;
            wasSet = true;
        }
    }

    /// <summary>清空标记：SampleScene 启动读完标记后调用，避免后续误触发二次读档</summary>
    public static void Clear()
    {
        _slot = -1;
        wasSet = false;
    }
}
