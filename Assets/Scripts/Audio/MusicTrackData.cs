using UnityEngine;

/// <summary>
/// 音乐曲目数据(ScriptableObject)— 每曲一个资产(场景曲/Boss 曲同结构)。
/// loopPoint:0 = 普通循环(场景曲:单源 loop=true,播完重复);>0 = 交叠循环(Boss 曲:循环内容 0→loopPoint,结尾段与开头段交叠)。
/// points:音乐点时间秒,升序,手工标。场景曲整曲范围;Boss 曲 0→loopPoint 区间内(区间外为交叠结尾段,不标点)。每圈重复生效。
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
}
