using System;
using UnityEngine;

/// <summary>
/// 区域音乐表(ScriptableObject)— 音乐配置的唯一收口:一行 = 一个地区(areaId)+ 场景曲 + 战斗曲(可空)。
///
/// 用途:
/// - 场景曲:玩家进入某区(管道换区 / 石碑传送)时按 areaId 查表做淡入淡出切换
///   (入口 = MusicPointManager.CrossFadeToArea,内部复用现有三段式 CrossFadeTo);
/// - 战斗曲:该区进战斗时切它、脱战接回场景曲(P2 用;本表 P1 只提供查询接口,战斗切换逻辑不在此)。
///
/// 约定:
/// - areaId 必须与场景里 Area 根上 AreaIdentity.areaId 一字不差(如 "Area_default");
/// - 战斗曲允许留空(存档房/安全屋 = 进战斗不切曲,继续播场景曲);
/// - 表里不列 Boss 房 = 天然排除(Boss 房仍走 MusicSwitchTrigger 的 Boss 模式,本表不介入);
/// - 查询一律"空 id / 找不到 / 字段为空 → null",不做告警不做兜底,由调用方决定回退;
/// - 旧配置(Area 根上的 AreaMusicSlot)保留为回退路径:表还没配好的区按老路走,行为不变。
/// </summary>
[CreateAssetMenu(fileName = "AreaMusicTable", menuName = "Audio/Area Music Table")]
public class AreaMusicTable : ScriptableObject
{
    /// <summary>
    /// 表内一行(地区音乐条目)。
    /// 用 class 而不是 struct:Inspector 数组里 struct 条目的字段拖不动(Unity 序列化数组元素的已知限制)。
    /// </summary>
    [Serializable]
    public class Entry
    {
        [Tooltip("地区 id(与场景里 AreaIdentity.areaId 一字不差,如 Area_default)")]
        public string areaId;

        [Tooltip("本区场景曲(进入本区时淡入淡出切到它;空 = 该区不切,维持当前音乐)")]
        public MusicTrackData sceneMusic;

        [Tooltip("本区战斗曲(该区进战斗时切它;空 = 该区进战斗不切曲,继续播场景曲)")]
        public MusicTrackData battleMusic;
    }

    [Tooltip("区域音乐条目:一行 = 地区 id + 场景曲 + 战斗曲(战斗曲可空)")]
    [SerializeField] private Entry[] entries = null;

    /// <summary>取该区场景曲(空 id / 表为空 / 找不到该区 / 字段为空 → null)</summary>
    public MusicTrackData GetSceneMusic(string areaId)
    {
        Entry entry = Find(areaId);
        return entry != null ? entry.sceneMusic : null;
    }

    /// <summary>取该区战斗曲(空 id / 表为空 / 找不到该区 / 字段为空 → null;空 = 该区进战斗不切曲)</summary>
    public MusicTrackData GetBattleMusic(string areaId)
    {
        Entry entry = Find(areaId);
        return entry != null ? entry.battleMusic : null;
    }

    /// <summary>按地区 id 找条目(空 id / 表为空 / 条目 id 为空 / 找不到 → null)</summary>
    private Entry Find(string areaId)
    {
        if (string.IsNullOrEmpty(areaId) || entries == null) return null;
        for (int i = 0; i < entries.Length; i++)
        {
            Entry e = entries[i];
            if (e != null && !string.IsNullOrEmpty(e.areaId) && e.areaId == areaId) return e;
        }
        return null;
    }
}
