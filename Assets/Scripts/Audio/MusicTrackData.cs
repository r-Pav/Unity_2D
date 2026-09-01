using System;
using UnityEngine;

/// <summary>
/// 音乐曲目数据(ScriptableObject)— 每曲一个资产(场景曲/Boss 曲同结构)。
/// loopPoint:0 = 普通循环(场景曲:单源 loop=true,播完重复);>0 = 交叠循环(Boss 曲:循环内容 0→loopPoint,结尾段与开头段交叠)。
/// points:音乐点时间秒,升序,手工标。场景曲整曲范围;Boss 曲 0→loopPoint 区间内(区间外为交叠结尾段,不标点)。每圈重复生效。
/// 两段式(Boss 曲):introClip 第一首(前奏),播到 introSwitchTime 交叠切到 clip(第二首/主体循环段);introPoints 第一首的音乐点。
/// pointGroups:命名标点组(多数组),如 BossHeavy(重击)/BossOrb1~5(法球)/PlayerCombo(玩家连击)/BossHeavySound(重击音)。
/// </summary>
[CreateAssetMenu(fileName = "MusicTrack_", menuName = "Data/MusicTrack")]
public class MusicTrackData : ScriptableObject
{
    [Tooltip("本曲音频")]
    public AudioClip clip;

    [Tooltip("0 = 普通循环(场景曲);>0 = 交叠循环(Boss 曲):循环内容 0→loopPoint,结尾段与开头段交叠")]
    public float loopPoint;

    [Tooltip("音乐点时间秒,升序手工标。场景曲整曲范围;Boss 曲 0→loopPoint 内")]
    public float[] points;

    [Header("自动重音(普通场景曲)")]
    [Tooltip("自动重音间隔(秒):普通场景曲每隔此秒数开一个重音窗口(背刺判定/头顶标识用);0 = 不启用(用 points/标点组)")]
    public float barIntervalSeconds = 0f;

    [Header("两段式(Boss 曲可选)")]
    [Tooltip("第一首(前奏,可空)。空=Boss 模式退化为单曲交叠循环,场景曲不受影响")]
    public AudioClip introClip;

    [Tooltip("第一首切到第二首的秒数(两位小数);introClip 非空时生效,同时是转阶段点")]
    public float introSwitchTime;

    [Tooltip("第一首的音乐点时间秒,升序,两位小数")]
    public float[] introPoints;

    [Header("标点组(命名多数组)")]
    [Tooltip("命名标点组:BossHeavy(重击)/BossOrb1~5(法球)/PlayerCombo(玩家连击)/BossHeavySound(重击音),秒数两位小数")]
    public MusicPointGroup[] pointGroups;

    /// <summary>按组名取标点组(未配置返回 null)</summary>
    public MusicPointGroup GetGroup(string groupName)
    {
        if (string.IsNullOrEmpty(groupName) || pointGroups == null) return null;
        foreach (var g in pointGroups)
        {
            if (g != null && g.groupName == groupName) return g;
        }
        return null;
    }
}

/// <summary>命名标点组:一个组名 + 升序标点秒数(组内每个标点可被独立消费)</summary>
[Serializable]
public class MusicPointGroup
{
    [Tooltip("组名(代码按名查,固定约定:BossHeavy/BossOrb1~5/PlayerCombo/BossHeavySound)")]
    public string groupName;

    [Tooltip("标点时间秒,升序,两位小数")]
    public float[] points;
}
