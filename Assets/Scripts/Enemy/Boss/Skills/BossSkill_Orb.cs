using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 技能 2:法球排程(Orb Barrage)。
/// 前置:Boss 曲 MusicTrackData 配 BossOrb1~BossOrb5 标点组(每组 5 个标点,saika 手工填)。
/// 流程:随机到技能 2 → 按释放次数轮换取组(BossOrb1→...→BossOrb5→循环)
/// → 攒着:该组后续 lookAheadWindow 秒内无标点则等待,条件满足才释放(超时放弃)
/// → 召唤 5 个法球,弧形排列在 Boss 头顶上方(相对 Boss 位置,恒定)
/// → 5 个法球对应组内接下来 5 个标点(第一个法球 ↔ 第一个标点,依此类推)
/// → 每个法球以 player 为目标画线延长至地面/墙壁为终点,匀速按音乐时钟移动,
///   到达终点时 = 对应标点(重音)响起 → 造成伤害(data 结算)。
/// 法球由 OrbManager 统一管理生命周期。
/// </summary>
public class BossSkill_Orb : BossSkillExecutor
{
    [Header("法球")]
    [Tooltip("法球 prefab(视觉 + 可选范围;挂 OrbProjectile 自动加)")]
    public GameObject orbPrefab;
    [Tooltip("检查后续标点的秒数窗口(窗口内有标点才释放法球)")]
    public float lookAheadWindow = 4f;
    [Tooltip("地面/墙壁层(法球终点射线命中用)")]
    public LayerMask groundLayer;
    [Tooltip("射线最大距离(没命中地面/墙壁时终点=此距离处)")]
    public float rayMaxDistance = 50f;

    [Header("生成位置(弧形,相对 Boss 头顶)")]
    [Tooltip("Boss 头顶高度偏移(生成弧形的中心高度)")]
    public float arcHeight = 3f;
    [Tooltip("弧形半径(Boss 头顶 x 距离)")]
    public float arcRadius = 2f;
    [Tooltip("弧形角度范围(度),如 120 = 从左上到右上")]
    public float arcAngleRange = 120f;

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

        // 组:预约时由 BossAttackDirector 指定(不消耗轮换计数);手动测试/未预约 = 自行轮换
        string group = !string.IsNullOrEmpty(ctx.reservedOrbGroup) ? ctx.reservedOrbGroup : GetOrbGroup();

        // 预检查:当前轮换组没配/没标点 → 立即放弃(不等,防 Boss 卡技能)
        var groupPoints = GetGroupPoints(mgr, group);
        if (groupPoints.Length == 0)
        {
            Debug.LogWarning($"[BossSkill_Orb] 音乐未配置标点组 {group},技能 2 空放(不卡)");
            yield break;
        }

        // 立即检查:lookAheadWindow 秒窗口内有标点才释放;无 → 空放(不攒着等)
        float candidate = mgr.NextPointInGroup(group);
        if (candidate < 0f || candidate - mgr.TrackTime > lookAheadWindow)
        {
            Debug.LogWarning($"[BossSkill_Orb] 组 {group} 后续 {lookAheadWindow} 秒内无标点,技能 2 空放(不等)");
            yield break;
        }

        // 取组内接下来 5 个标点(回绕循环)
        var beats = GetNextBeats(mgr, group, 5);
        if (beats.Count == 0) yield break;

        // 5 个法球弧形排列在 Boss 头顶
        var spawns = ComputeArcSpawns(ctx.boss.transform.position, beats.Count);
        var orbManager = OrbManager.Instance;
        if (orbManager == null)
        {
            var go = new GameObject("OrbManager");
            orbManager = go.AddComponent<OrbManager>();
        }
        for (int i = 0; i < beats.Count; i++)
        {
            var orb = orbManager.Spawn(orbPrefab, spawns[i]);
            orb.Initialize(ctx, Data, beats[i], groundLayer, rayMaxDistance);
        }

        // 后摇:技能本体短播(法球由 OrbProjectile 各自到达后销毁)
        yield return new WaitForSeconds(0.5f);
    }

    /// <summary>取组内从下一个标点开始的 count 个标点(不足回绕到组开头)</summary>
    private List<float> GetNextBeats(MusicPointManager mgr, string groupName, int count)
    {
        var result = new System.Collections.Generic.List<float>();
        var points = GetGroupPoints(mgr, groupName);
        if (points.Length == 0) return result;

        float t = mgr.TrackTime;
        int idx = 0;
        while (idx < points.Length && points[idx] <= t + 0.001f) idx++;
        if (idx >= points.Length) idx = 0;   // 一圈已过,回绕

        for (int i = 0; i < count; i++)
            result.Add(points[(idx + i) % points.Length]);
        return result;
    }

    /// <summary>弧形生成点:以 Boss 头顶为中心,count 个点从 -angle/2 到 +angle/2 分布</summary>
    private Vector3[] ComputeArcSpawns(Vector3 bossPos, int count)
    {
        var result = new Vector3[count];
        Vector2 center = (Vector2)bossPos + Vector2.up * arcHeight;
        for (int i = 0; i < count; i++)
        {
            float t = count <= 1 ? 0.5f : (float)i / (count - 1);
            float angle = Mathf.Lerp(-arcAngleRange * 0.5f, arcAngleRange * 0.5f, t);
            Vector2 dir = Quaternion.Euler(0f, 0f, angle) * Vector2.up;
            result[i] = center + dir * arcRadius;
        }
        return result;
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
