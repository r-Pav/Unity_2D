using System.Collections;
using UnityEngine;

/// <summary>
/// 技能 2:法球排程(Orb Barrage)— 纯时序,不再卡音乐标点。
/// 流程:抽到技能 2 → 5 个法球弧形排列在 Boss 头顶(生成点本帧一次算定,相对 Boss 位置恒定)
/// → 按 orbLaunchInterval 错峰发射(第一个立即起飞,之后每球等一个间隔),
///   每个法球在「发射那一刻」才生成:终点 = 此刻的 法球 → 玩家方向画线延长至地面/墙壁(只快照一次)
/// → 每个法球以 orbSpeed 匀速直线飞向自己的终点,到达终点即造成伤害(data 结算)后销毁。
/// 不再依赖音乐标点组:无音乐、未配标点组也能正常释放。
/// 法球由 OrbManager 统一管理生命周期。
/// </summary>
public class BossSkill_Orb : BossSkillExecutor
{
    [Header("法球")]
    [Tooltip("法球 prefab(视觉 + 可选范围;挂 OrbProjectile 自动加)")]
    public GameObject orbPrefab;
    [Tooltip("地面/墙壁层(法球终点射线命中用)")]
    public LayerMask groundLayer;
    [Tooltip("射线最大距离(没命中地面/墙壁时终点=此距离处)")]
    public float rayMaxDistance = 50f;

    [Header("发射(纯时序)")]
    [Tooltip("法球飞行速度(世界单位/秒;飞行时长 = 起点到终点距离 / 速度)")]
    public float orbSpeed = 12f;
    [Tooltip("每球发射间隔(秒);第一球立即发射,之后每球等一个间隔")]
    public float orbLaunchInterval = 0.15f;

    [Header("生成位置(弧形,相对 Boss 头顶)")]
    [Tooltip("Boss 头顶高度偏移(生成弧形的中心高度)")]
    public float arcHeight = 3f;
    [Tooltip("弧形半径(Boss 头顶 x 距离)")]
    public float arcRadius = 2f;
    [Tooltip("弧形角度范围(度),如 120 = 从左上到右上")]
    public float arcAngleRange = 120f;

    /// <summary>法球个数(= 弧形生成点数量)。固定 5,不再由标点组长度决定</summary>
    private const int OrbCount = 5;

    public override IEnumerator ExecuteSkill(BossSkillContext ctx)
    {
        if (ctx == null || ctx.boss == null) yield break;
        SetSkillAnimOn(ctx.animator);

        // 5 个法球弧形排列在 Boss 头顶(生成点本帧一次算定)
        var spawns = ComputeArcSpawns(ctx.boss.transform.position, OrbCount);

        var orbManager = OrbManager.Instance;
        if (orbManager == null)
        {
            var go = new GameObject("OrbManager");
            orbManager = go.AddComponent<OrbManager>();
        }

        for (int i = 0; i < spawns.Length; i++)
        {
            // 错峰发射:第一球立即,之后每球等 orbLaunchInterval
            if (i > 0 && orbLaunchInterval > 0f)
                yield return new WaitForSeconds(orbLaunchInterval);

            // 「发射那一刻」才 Spawn + Initialize:终点射线用此刻的玩家位置快照
            var orb = orbManager.Spawn(orbPrefab, spawns[i]);
            orb.Initialize(ctx, Data, orbSpeed, groundLayer, rayMaxDistance);
        }

        // 后摇:技能本体短播(法球由 OrbProjectile 各自飞完销毁)
        yield return new WaitForSeconds(0.5f);
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
}
