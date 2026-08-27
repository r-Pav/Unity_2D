using System.Collections;
using UnityEngine;

/// <summary>
/// 技能 1:双火墙(Wall Pincer,改版)。
/// 流程:Boss 移动到固定位置(手动指定,留空不移动)→ 左右墙在手动指定位置生成
/// → 墙朝 player 移动(不是朝 Boss)→ 墙范围(子 obj MeleeRangeIndicator)覆盖 player 时造成伤害(带间隔)
/// → 墙距 player 到 stopDistance 停住,停留 wallLifetime 秒后消失。
/// 墙不物理推人(collider 由 prefab 决定,推荐不加实心碰撞),只是经过时受伤害。
/// 所有位置手动指定,无 Camera 兜底。
/// </summary>
public class BossSkill_FireWall : BossSkillExecutor
{
    [Header("Boss 位移(位置在场景 BossSkillSceneConfig,这里只留速度)")]
    [Tooltip("Boss 移动到目标的速度")]
    public float bossMoveSpeed = 6f;

    [Header("火焰墙(资源与数值)")]
    [Tooltip("墙 prefab(视觉 + 子 obj 挂 MeleeRangeIndicator 定范围;不加实心 collider,墙不推人)")]
    public GameObject wallPrefab;
    [Tooltip("墙向 player 移动速度")]
    public float wallMoveSpeed = 4f;
    [Tooltip("墙距 player 的停靠距离(到距离后停)")]
    public float stopDistance = 2f;
    [Tooltip("墙范围持续伤害间隔秒")]
    public float damageInterval = 0.5f;
    [Tooltip("墙停靠后停留秒数(之后消失)")]
    public float wallLifetime = 2f;

    // 生成的墙(场景根独立物体,不挂 Boss 下;中断时 OnDestroy 清理)
    private readonly System.Collections.Generic.List<GameObject> _spawnedWalls = new();

    public override IEnumerator ExecuteSkill(BossSkillContext ctx)
    {
        var boss = ctx.boss;
        if (boss == null) yield break;
        PlaySkillAnim(ctx.animator);

        // 0. 场景配置:位置全部从 BossSkillSceneConfig 读(prefab 不存场景引用)
        var sceneConfig = FindObjectOfType<BossSkillSceneConfig>();
        if (sceneConfig == null)
            Debug.LogWarning("[BossSkill_FireWall] 场景里没有 BossSkillSceneConfig(挂空物体拖位置),技能 1 空放");

        // 1. Boss 移动到固定位置(场景配置;留空不动)
        Transform bossMoveTarget = sceneConfig != null ? sceneConfig.fireWallBossTarget : null;
        if (bossMoveTarget != null)
        {
            Vector3 targetPos = bossMoveTarget.position;   // 快照固定目标
            yield return MoveTransform(boss.transform, targetPos, bossMoveSpeed);
        }

        // 2. 左右墙在场景配置位置生成(挂执行器 prefab 下,随技能结束销毁)
        if (wallPrefab == null)
            Debug.LogWarning("[BossSkill_FireWall] 未配置 Wall Prefab,技能 1 空放");
        GameObject leftWall = SpawnWall(sceneConfig != null ? sceneConfig.fireWallLeftSpawn : null);
        GameObject rightWall = SpawnWall(sceneConfig != null ? sceneConfig.fireWallRightSpawn : null);

        // 3. 阶段 A:墙朝 player 移动,直到两墙都停靠(超时 10 秒兜底)
        float lastDamageTime = -10f;
        float moveElapsed = 0f;
        const float moveTimeout = 10f;
        while (moveElapsed < moveTimeout)
        {
            if (ctx.player != null)
            {
                Vector3 target = ctx.player.position;
                if (leftWall != null) MoveWallToward(leftWall, target, stopDistance, wallMoveSpeed);
                if (rightWall != null) MoveWallToward(rightWall, target, stopDistance, wallMoveSpeed);
            }

            // 两墙都停靠(或未生成)→ 进入停留阶段
            bool leftDone = leftWall == null || ctx.player == null
                || Mathf.Abs(leftWall.transform.position.x - ctx.player.position.x) <= stopDistance;
            bool rightDone = rightWall == null || ctx.player == null
                || Mathf.Abs(rightWall.transform.position.x - ctx.player.position.x) <= stopDistance;
            if (leftDone && rightDone) break;

            moveElapsed += Time.deltaTime;
            yield return null;
        }

        // 4. 阶段 B:停留 wallLifetime 秒,期间持续范围伤害
        float stayElapsed = 0f;
        while (stayElapsed < wallLifetime)
        {
            if (ctx.player != null && Time.time - lastDamageTime >= damageInterval)
            {
                bool caught = (leftWall != null && WallContainsPlayer(leftWall, ctx.player))
                           || (rightWall != null && WallContainsPlayer(rightWall, ctx.player));
                if (caught)
                {
                    AttackPlayer(ctx);
                    lastDamageTime = Time.time;
                }
            }

            stayElapsed += Time.deltaTime;
            yield return null;
        }

        if (leftWall != null) Destroy(leftWall);
        if (rightWall != null) Destroy(rightWall);
    }

    private GameObject SpawnWall(Transform spawn)
    {
        if (wallPrefab == null || spawn == null) return null;
        GameObject wall = Instantiate(wallPrefab);   // 场景根:不挂 Boss 下,不受 Boss 层级/物理影响
        wall.transform.position = spawn.position;
        _spawnedWalls.Add(wall);
        return wall;
    }

    /// <summary>技能中断(prefab 被销毁)时清理墙,避免残留</summary>
    private void OnDestroy()
    {
        foreach (var w in _spawnedWalls)
        {
            if (w != null) Destroy(w);
        }
        _spawnedWalls.Clear();
    }

    /// <summary>墙朝目标移动,距目标 x 到 stopDist 停住(每帧,不物理推挤)</summary>
    private void MoveWallToward(GameObject wall, Vector3 target, float stopDist, float speed)
    {
        if (wall == null) return;
        Vector3 pos = wall.transform.position;
        float dx = target.x - pos.x;
        if (Mathf.Abs(dx) <= stopDist) return;   // 已到停靠距离
        float step = speed * Time.deltaTime;
        pos.x += Mathf.Sign(dx) * Mathf.Min(step, Mathf.Abs(dx) - stopDist);
        wall.transform.position = pos;
    }

    /// <summary>墙范围(子 obj MeleeRangeIndicator,Size=视觉大小)内是否有 player</summary>
    private bool WallContainsPlayer(GameObject wall, Transform player)
    {
        if (wall == null || player == null) return false;
        var indicator = wall.GetComponentInChildren<MeleeRangeIndicator>();
        if (indicator == null) return false;
        Vector2 size = indicator.Size;
        if (size.x <= 0f || size.y <= 0f) return false;
        Vector2 center = indicator.transform.position;
        return Mathf.Abs(player.position.x - center.x) <= size.x * 0.5f
            && Mathf.Abs(player.position.y - center.y) <= size.y * 0.5f;
    }

    /// <summary>Boss 对墙内 player 造成伤害(data 统一结算)</summary>
    private void AttackPlayer(BossSkillContext ctx)
    {
        if (ctx.player == null || Data == null || ctx.boss == null) return;
        var ph = ctx.player.GetComponent<PlayerHealth>();
        if (ph == null) return;
        Vector2 faceDir = ctx.player.position.x > ctx.boss.transform.position.x ? Vector2.right : Vector2.left;
        var info = Data.BuildDamageInfo(ctx.boss, ctx.boss.transform.position, faceDir);
        CombatResolver.Resolve(ctx.boss, ph, info);
        if (Data.hitVFXPrefab != null)
            VFXSpawner.SpawnOnPlayer(Data.hitVFXPrefab, ctx.player.position);
    }

    private IEnumerator MoveTransform(Transform t, Vector3 target, float speed)
    {
        var rb = t != null ? t.GetComponent<Rigidbody2D>() : null;
        while (t != null && Vector2.Distance(t.position, target) > 0.05f)
        {
            Vector3 next = Vector3.MoveTowards(t.position, target, speed * Time.deltaTime);
            if (rb != null) rb.MovePosition(next);   // 物理移动,不与 Rigidbody 冲突
            else t.position = next;
            yield return null;
        }
    }
}
