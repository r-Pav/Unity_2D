using System;
using UnityEngine;

/// <summary>
/// 音乐曲目数据(ScriptableObject)— 每曲一个资产(场景曲/Boss 曲同结构)。
/// loopPoint:0 = 普通循环(场景曲:单源 loop=true,播完重复);>0 = 交叠循环(Boss 曲:循环内容 0→loopPoint,结尾段与开头段交叠)。
/// points:音乐点时间秒,升序,手工标。场景曲整曲范围;Boss 曲 0→loopPoint 区间内(区间外为交叠结尾段,不标点)。每圈重复生效。
/// 两段式(Boss 曲):introClip 第一首(前奏),播到 introSwitchTime 交叠切到 clip(第二首/主体循环段);introPoints 第一首的音乐点。
/// pointGroups:命名标点组(多数组),如 BossHeavy(重击)/BossOrb1~5(法球)/PlayerCombo(玩家连击)/BossHeavySound(重击音)/PlayerBackstab(连音背刺)。
///
/// 【背刺标点组约定 — PlayerBackstab】groupName 固定 "PlayerBackstab":
///   · 组内标点 = 单点背刺标点(秒,升序),每点各自一个判定窗口,踩中打一刀(孤立点)。
///   · chainGroups = 手工连音组(比标点高一层的分组):每组一串时刻(升序)。组内点自动并入背刺标点集合,
///     不用在 PlayerBackstab 里重复标;踩中组内任意一点后,组内后面的点由 PlayerBackstabState 按各自拍点
///     自动打完(替玩家踩点,每刀都是完整背刺),期间 F 无效;已过去的点不补。
///     与其它标点同一时刻重合时按连音处理(判定只走连音)。
///   · 启用标点/连音时必须把本曲 barIntervalSeconds 设为 0:否则自动重音窗口与标点窗口两路同时开,
///     背刺判定会出现两条来源不同的窗口,行为不可预期。
///   · 未配置该组(或组为空)的曲 = 未启用连音:背刺判定回退自动重音窗口,行为与改前一致。
///   · 该组只服务玩家背刺判定;禁止接进 PlayerBeatJudge(Boss 判定链走 BossHeavySound)。
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
    [Tooltip("命名标点组:BossHeavy(重击)/BossOrb1~5(法球)/PlayerCombo(玩家连击)/BossHeavySound(重击音)/PlayerBackstab(连音背刺),秒数两位小数。" +
             "配 PlayerBackstab 的曲须把 barIntervalSeconds 设为 0")]
    public MusicPointGroup[] pointGroups;

    [Header("连音组(连音背刺;标点之上一层,手工标)")]
    [Tooltip("手工连音组:每项 = 一组连音(组内时刻升序,各组按首点递增、不要交错)。" +
             "组内点自动并入背刺标点集合(不用在 PlayerBackstab 里重复标);踩中组内任意一点后," +
             "组内后面的点按各自拍点自动打完(替玩家踩点),期间 F 无效;与其它标点同一时刻重合时按连音处理")]
    public MusicChainGroup[] chainGroups;

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
    [Tooltip("组名(代码按名查,固定约定:BossHeavy/BossOrb1~5/PlayerCombo/BossHeavySound/PlayerBackstab)")]
    public string groupName;

    [Tooltip("标点时间秒,升序,两位小数")]
    public float[] points;
}

/// <summary>连音组:一组连音标点(组内升序,手工标,不再按间隔自动切组)。
/// 组内点等同背刺标点(代码自动并入标点表),区别只是组内后面的点会被自动打完。</summary>
[Serializable]
public class MusicChainGroup
{
    [Tooltip("本组连音标点时刻(秒,升序,两位小数)")]
    public float[] points;
}
