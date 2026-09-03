using UnityEngine;

/// <summary>
/// 区域音乐槽 — 挂 Area 根(与 AreaIdentity 同物体或子物体)。
/// 该区域的场景音乐:玩家进入本区域(管道到达/传送)时,管道/传送侧读此槽做淡入淡出切换。
/// 空 = 该区未配音乐,进入时不切(维持当前)。Boss 房不放此槽(走 MusicSwitchTrigger Boss 模式)。
/// </summary>
public class AreaMusicSlot : MonoBehaviour
{
    [Tooltip("本区域场景音乐(进入时 CrossFadeTo 淡入淡出;空 = 不切)")]
    [SerializeField] private MusicTrackData areaMusic;

    public MusicTrackData AreaMusic => areaMusic;
}
