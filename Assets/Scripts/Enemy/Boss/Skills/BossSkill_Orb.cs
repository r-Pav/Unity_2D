using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 技能 2:法球排程(Orb Barrage)。
/// 前置:Boss 曲 MusicTrackData 配 BossOrb1~BossOrb5 标点组(每组 ≤5 个标点,saika 手工填)。
/// 流程:随机到技能 2 → 按释放次数轮换取组(BossOrb1→...→BossOrb5→循环)
/// → 攒着:该组后续 lookAheadWindow 秒内无标点则等待,条件满足才释放(超时放弃)
/// → 召唤法球,数量 = 后续窗口内标点数(≤5),第一个法球对应第一个标点,依此类推
/// → 每个法球以 player 为目标画线延长至地面/墙壁为终点,匀速按音乐时钟移动,
///   到达终点时 = 对应标点(重音)响起 → 造成伤害(data 结算)。
/// 法球由 OrbManager 统一管理生命周期。
/// </summary>
public class BossSkill_Orb : BossSkillExecutor
{
    [Header("法球")]
    [Tooltip("法球 prefab(视觉 + 可选范围;挂 OrbProjectile 自动加)")]
    public GameObject orbPrefab;
    [Tooltip("检查后续标点的秒数窗口(法球只召唤窗口内的标点)")]
    public float lookAheadWindow = 4f;
    [Tooltip("攒着等待超时秒数(组内一直无标点时放弃,防技能卡死)")]
    public float waitTimeout = 30f;
    [Tooltip("地面/墙壁层(法球终点射线命中用)")]
    public LayerMask groundLayer;
    [Tooltip("射线最大距离(没命中地面/墙壁时终点=此距离处)")]
    public float rayMaxDistance = 50f;

    private static int _useCount;   // 组轮换计数(跨实例累计)

    /// <summary>按释放次数轮换取组名(BossOrb1~BossOrb5)</summary>
    private static string GetOrbGroup()
    {
        int idx = _useCount % 5 + 1;
        _useCount++;
        return "BossOrb" + idx;
    }

    public override IEnumerator ExecuteSkill(BossSkillContext ctx)
    {
        var mgr = MusicPointManager.Instance;
        if (mgr == null || ctx.player == null || ctx.boss == null) yield break;
        PlaySkillAnim(ctx.animator);

        string group = GetOrbGroup();

        // 攒着:等当前组后续窗口内出现标点(条件满足才释放;超时放弃)
        float nextBeat = -1f;
        float waitElapsed = 0f;
        while (waitElapsed < waitTimeout)
        {
            float candidate = mgr.NextPointInGroup(group);
            if (candidate >= 0f && candidate - mgr.TrackTime <= lookAheadWindow)
            {
                nextBeat = candidate;
                break;
            }
            waitElapsed += Time.deltaTime;
            yield return null;
        }
        if (nextBeat < 0f) yield break;   // 超时无标点,放弃(技能结束)

        // 收集后续窗口内的标点(从 nextBeat 开始,最多 5 个)
        var beats = new List<float> { nextBeat };
        var groupPoints = GetGroupPoints(mgr, group);
        foreach (float p in groupPoints)
        {
            if (beats.Count >= 5) break;
            if (p > nextBeat + 0.001f && p - mgr.TrackTime <= lookAheadWindow)
                beats.Add(p);
        }

        // 法球生成位置(场景配置)
        var sceneConfig = FindObjectOfType<BossSkillSceneConfig>();
        Vector3 spawnPos = sceneConfig != null && sceneConfig.orbSpawnPoint != null
            ? sceneConfig.orbSpawnPoint.position
            : ctx.boss.transform.position;

        // 召唤法球(每个对应一个标点)
        var orbManager = OrbManager.Instance;
        if (orbManager == null)
        {
            var go = new GameObject("OrbManager");
            orbManager = go.AddComponent<OrbManager>();
        }
        foreach (float beat in beats)
        {
            var orb = orbManager.Spawn(orbPrefab, spawnPos);
            orb.Initialize(ctx, Data, beat, groundLayer, rayMaxDistance);
        }

        // 后摇:技能本体短播(法球由 OrbProjectile 各自到达后销毁)
        yield return new WaitForSeconds(0.5f);
    }

    /// <summary>取组内标点数组(空组返回空)</summary>
    private float[] GetGroupPoints(MusicPointManager mgr, string groupName)
    {
        var track = mgr.CurrentTrack;
        if (track == null) return System.Array.Empty<float>();
        var group = track.GetGroup(groupName);
        return group != null && group.points != null ? group.points : System.Array.Empty<float>();
    }
}
