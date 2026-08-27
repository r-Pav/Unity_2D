using System.Collections;
using UnityEngine;

/// <summary>
/// 技能 1:双火墙夹击(Wall Pincer)。
/// 流程:Boss 移动到固定位置(拖场景 Transform)→ 屏幕左右两端生成火焰墙(视觉 + 实心 collider 物理阻挡)
/// → 暂停 waitSeconds → 墙朝 Boss 移动至 stopDistance 停住 → 墙范围内(子 obj 大小)有 player → Boss 攻击(data 结算);
/// 无 player → 墙继续沿原方向移动至屏幕边缘消失。
/// 范围/特效/挂点都在技能 prefab 子 obj 上,data 只提供伤害/击退等通用状态。
/// </summary>
public class BossSkill_FireWall : BossSkillExecutor
{
    [Header("Boss 位移")]
    [Tooltip("Boss 移动到的固定位置(拖场景 Transform,禁手填坐标)")]
    public Transform bossMoveTarget;
    [Tooltip("Boss 移动到目标的速度")]
    public float bossMoveSpeed = 6f;

    [Header("火焰墙")]
    [Tooltip("火焰墙 prefab(根上视觉 + 实心 BoxCollider2D 物理阻挡;子 obj 挂 MeleeRangeIndicator 定范围)")]
    public GameObject wallPrefab;
    [Tooltip("墙生成后暂停秒数(再朝 Boss 移动)")]
    public float waitSeconds = 1f;
    [Tooltip("墙停住时距 Boss 的距离")]
    public float stopDistance = 3f;
    [Tooltip("墙朝 Boss 移动速度")]
    public float wallMoveSpeed = 4f;
    [Tooltip("墙到屏幕边缘速度(无 player 时)")]
    public float edgeMoveSpeed = 6f;

    public override IEnumerator ExecuteSkill(BossSkillContext ctx)
    {
        var boss = ctx.boss;
        if (boss == null) yield break;
        PlaySkillAnim(ctx.animator);

        // 1. Boss 移动到固定位置(技能执行中 ChaseState 站桩,直接移 transform)
        if (bossMoveTarget != null)
        {
            yield return MoveTransform(boss.transform, bossMoveTarget.position, bossMoveSpeed);
        }

        // 2. 屏幕左右两端生成火焰墙(挂执行器 prefab 下,中断时随 prefab 一起销毁)
        Camera cam = Camera.main;
        Vector3 leftEdge = boss.transform.position;
        Vector3 rightEdge = boss.transform.position;
        if (cam != null)
        {
            leftEdge = cam.ViewportToWorldPoint(new Vector3(0f, 0.5f, 0f));
            rightEdge = cam.ViewportToWorldPoint(new Vector3(1f, 0.5f, 0f));
        }
        GameObject leftWall = wallPrefab != null ? Instantiate(wallPrefab, transform) : null;
        GameObject rightWall = wallPrefab != null ? Instantiate(wallPrefab, transform) : null;
        if (leftWall != null) leftWall.transform.position = leftEdge;
        if (rightWall != null) rightWall.transform.position = rightEdge;

        // 3. 暂停
        yield return new WaitForSeconds(waitSeconds);

        // 4. 墙朝 Boss 移动至 stopDistance
        Vector3 bossPos = boss.transform.position;
        if (leftWall != null)
            yield return MoveWall(leftWall, bossPos, stopDistance, wallMoveSpeed);
        if (rightWall != null)
            yield return MoveWall(rightWall, bossPos, stopDistance, wallMoveSpeed);

        // 5. 范围检测:有 player → Boss 攻击;无 → 墙继续到屏幕边缘消失
        bool caught = (leftWall != null && WallContainsPlayer(leftWall, ctx.player))
                   || (rightWall != null && WallContainsPlayer(rightWall, ctx.player));
        if (caught)
        {
            AttackPlayer(ctx);
        }
        else
        {
            if (leftWall != null)
                yield return MoveWallToEdge(leftWall, bossPos, edgeMoveSpeed);
            if (rightWall != null)
                yield return MoveWallToEdge(rightWall, bossPos, edgeMoveSpeed);
        }
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

    /// <summary>Boss 攻击墙内 player(data 统一结算)</summary>
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
        while (t != null && Vector2.Distance(t.position, target) > 0.05f)
        {
            t.position = Vector3.MoveTowards(t.position, target, speed * Time.deltaTime);
            yield return null;
        }
    }

    /// <summary>墙朝 Boss 移动,停在距离 boss 为 stopDist 的位置(左墙停 boss 左侧,右墙停右侧)</summary>
    private IEnumerator MoveWall(GameObject wall, Vector3 bossPos, float stopDist, float speed)
    {
        if (wall == null) yield break;
        float dir = Mathf.Sign(bossPos.x - wall.transform.position.x);
        if (dir == 0f) dir = 1f;
        while (wall != null && Mathf.Abs(wall.transform.position.x - bossPos.x) > stopDist)
        {
            wall.transform.position += Vector3.right * dir * speed * Time.deltaTime;
            yield return null;
        }
        if (wall != null)
            wall.transform.position = new Vector3(bossPos.x - dir * stopDist, wall.transform.position.y, wall.transform.position.z);
    }

    /// <summary>墙沿原方向(相对 boss 向外)继续移动至屏幕边缘外</summary>
    private IEnumerator MoveWallToEdge(GameObject wall, Vector3 bossPos, float speed)
    {
        if (wall == null) yield break;
        Camera cam = Camera.main;
        if (cam == null) yield break;
        float dir = Mathf.Sign(wall.transform.position.x - bossPos.x);
        if (dir == 0f) dir = 1f;
        float edgeX = cam.ViewportToWorldPoint(new Vector3(dir > 0f ? 1f : 0f, 0.5f, 0f)).x + dir * 2f;
        while (wall != null && Mathf.Abs(wall.transform.position.x - edgeX) > 0.1f)
        {
            wall.transform.position += Vector3.right * dir * speed * Time.deltaTime;
            yield return null;
        }
    }
}
