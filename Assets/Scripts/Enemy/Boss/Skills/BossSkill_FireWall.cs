using System.Collections;
using UnityEngine;

/// <summary>
/// 技能 1:双火墙夹击(Wall Pincer,定版)。
/// 流程:左右墙在场景配置位置生成 → 两墙同时朝 player 移动,到距 player stopDistance 停下
/// → 停留 wallLifetime 秒 → 左墙向右、右墙向左交叉穿过,一直到屏幕外消失。
/// 伤害:单次碰到触发(进入墙范围触发一次,离开再进可再触发;不持续刷伤害)。
/// 墙为场景根独立物体,不受 Boss 层级影响;空中战位期间关 Boss 重力,结束/中断恢复。
/// </summary>
public class BossSkill_FireWall : BossSkillExecutor
{
    [Header("Boss 位移(位置在场景 BossSkillSceneConfig,这里只留速度)")]
    [Tooltip("Boss 移动到目标的速度")]
    public float bossMoveSpeed = 6f;

    [Header("火焰墙(资源与数值)")]
    [Tooltip("墙 prefab(视觉 + 子 obj 挂 MeleeRangeIndicator 定范围;不加实心 collider,墙不推人)")]
    public GameObject wallPrefab;
    [Tooltip("墙向 player 移动速度(最大速度基准)")]
    public float wallMoveSpeed = 4f;
    [Tooltip("墙距 player 的停靠距离(到距离后停)")]
    public float stopDistance = 2f;
    [Tooltip("墙停靠后停留秒数(之后向两边分开并消失)")]
    public float wallLifetime = 2f;

    [Header("第一阶段(朝 player,缓出减速)")]
    [Tooltip("朝 player 移动减速节奏(秒):此时间内从快变慢,到停靠点停")]
    public float wallInDuration = 1.5f;
    [Tooltip("缓出强度:越大前段甩得越快(2=平方,3=立方,4=四次方)")]
    public float wallInEasePower = 2f;

    [Header("最后阶段(交叉穿过,缓入加速)")]
    [Tooltip("交叉穿过全程耗时(秒),速度由慢变快")]
    public float wallExitDuration = 2f;
    [Tooltip("缓入强度:越大后段甩得越快(2=平方,3=立方,4=四次方)")]
    public float wallExitEasePower = 2f;

    // 生成的墙(场景根独立物体,不挂 Boss 下;中断时 OnDestroy 清理)
    private readonly System.Collections.Generic.List<GameObject> _spawnedWalls = new();

    // 空中战位兼容:技能执行期间关 Boss 重力(移动+悬浮),结束/中断恢复
    private Rigidbody2D _savedBossRb;
    private float _savedGravityScale = 1f;

    public override IEnumerator ExecuteSkill(BossSkillContext ctx)
    {
        var boss = ctx.boss;
        if (boss == null) yield break;
        PlaySkillAnim(ctx.animator);

        // 兼容空中战位:执行期间关重力,技能结束恢复(中断由 OnDestroy 兜底)
        _savedBossRb = boss.GetComponent<Rigidbody2D>();
        if (_savedBossRb != null)
        {
            _savedGravityScale = _savedBossRb.gravityScale;
            _savedBossRb.gravityScale = 0f;
        }

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

        // 3. 阶段 A:两墙同时朝 player 移动,缓出(由快变慢),到距 player stopDistance 停下(超时 10 秒兜底)
        bool hitOnce = false;
        float moveElapsed = 0f;
        const float moveTimeout = 10f;
        while (moveElapsed < moveTimeout)
        {
            float k = Mathf.Clamp01(moveElapsed / wallInDuration);
            float ease = 1f - Mathf.Pow(1f - k, wallInEasePower);   // 缓出:前段快后段慢
            float stepSpeed = wallMoveSpeed * ease;

            if (ctx.player != null)
            {
                if (leftWall != null) MoveWallTowardPlayer(leftWall, ctx.player, stopDistance, stepSpeed);
                if (rightWall != null) MoveWallTowardPlayer(rightWall, ctx.player, stopDistance, stepSpeed);
            }

            TickHitOnce(ctx, leftWall, rightWall, ref hitOnce);

            // 两墙都停靠(或未生成)→ 进入停留阶段
            bool leftDone = leftWall == null || ctx.player == null
                || Mathf.Abs(leftWall.transform.position.x - ctx.player.position.x) <= stopDistance;
            bool rightDone = rightWall == null || ctx.player == null
                || Mathf.Abs(rightWall.transform.position.x - ctx.player.position.x) <= stopDistance;
            if (leftDone && rightDone) break;

            moveElapsed += Time.deltaTime;
            yield return null;
        }

        // 4. 阶段 B:停留 wallLifetime 秒(单次碰到伤害)
        float stayElapsed = 0f;
        while (stayElapsed < wallLifetime)
        {
            TickHitOnce(ctx, leftWall, rightWall, ref hitOnce);
            stayElapsed += Time.deltaTime;
            yield return null;
        }

        // 5. 阶段 C:左墙向右、右墙向左交叉穿过,缓入加速(开始慢越来越快),到屏幕外消失
        Camera cam = Camera.main;
        Vector3 leftStart = leftWall != null ? leftWall.transform.position : Vector3.zero;
        Vector3 rightStart = rightWall != null ? rightWall.transform.position : Vector3.zero;
        float exitRightX = cam != null ? cam.ViewportToWorldPoint(new Vector3(1f, 0.5f, 0f)).x + 2f : leftStart.x + 30f;
        float exitLeftX = cam != null ? cam.ViewportToWorldPoint(new Vector3(0f, 0.5f, 0f)).x - 2f : rightStart.x - 30f;
        float exitT = 0f;
        while (exitT < wallExitDuration)
        {
            exitT += Time.deltaTime;
            float k = Mathf.Pow(Mathf.Clamp01(exitT / wallExitDuration), wallExitEasePower);   // 缓入:0→1 加速
            if (leftWall != null)
                leftWall.transform.position = Vector3.LerpUnclamped(leftStart, new Vector3(exitRightX, leftStart.y, leftStart.z), k);
            if (rightWall != null)
                rightWall.transform.position = Vector3.LerpUnclamped(rightStart, new Vector3(exitLeftX, rightStart.y, rightStart.z), k);
            TickHitOnce(ctx, leftWall, rightWall, ref hitOnce);   // 交叉穿过时碰到 player 也触发伤害
            yield return null;
        }

        if (leftWall != null) Destroy(leftWall);
        if (rightWall != null) Destroy(rightWall);

        // 恢复重力(技能结束,空中战位 → 正常落地)
        if (_savedBossRb != null)
        {
            _savedBossRb.gravityScale = _savedGravityScale;
            _savedBossRb = null;
        }
    }

    private GameObject SpawnWall(Transform spawn)
    {
        if (wallPrefab == null || spawn == null) return null;
        GameObject wall = Instantiate(wallPrefab);   // 场景根:不挂 Boss 下,不受 Boss 层级/物理影响
        wall.transform.position = spawn.position;
        _spawnedWalls.Add(wall);
        return wall;
    }

    /// <summary>技能中断(prefab 被销毁)时恢复重力并清理墙,避免残留</summary>
    private void OnDestroy()
    {
        if (_savedBossRb != null)
        {
            _savedBossRb.gravityScale = _savedGravityScale;
            _savedBossRb = null;
        }
        foreach (var w in _spawnedWalls)
        {
            if (w != null) Destroy(w);
        }
        _spawnedWalls.Clear();
    }

    /// <summary>墙朝 player 移动,距 player x 到 stopDist 停靠(两墙夹击)</summary>
    private void MoveWallTowardPlayer(GameObject wall, Transform player, float stopDist, float speed)
    {
        if (wall == null || player == null) return;
        Vector3 pos = wall.transform.position;
        float dx = player.position.x - pos.x;
        if (Mathf.Abs(dx) <= stopDist) return;   // 已停靠
        float step = speed * Time.deltaTime;
        pos.x += Mathf.Sign(dx) * Mathf.Min(step, Mathf.Abs(dx) - stopDist);
        wall.transform.position = pos;
    }

    /// <summary>单次碰到伤害:player 进入任一墙范围触发一次,离开范围可再次触发(不持续刷)</summary>
    private void TickHitOnce(BossSkillContext ctx, GameObject leftWall, GameObject rightWall, ref bool hitOnce)
    {
        if (ctx.player == null) return;
        bool inRange = (leftWall != null && WallContainsPlayer(leftWall, ctx.player))
                    || (rightWall != null && WallContainsPlayer(rightWall, ctx.player));
        if (inRange && !hitOnce)
        {
            AttackPlayer(ctx);
            hitOnce = true;
        }
        else if (!inRange)
        {
            hitOnce = false;
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
        float timeout = 5f;   // 目标不可达(被地形/墙卡住)时兜底,防技能永久卡死
        while (t != null && Vector2.Distance(t.position, target) > 0.05f && timeout > 0f)
        {
            Vector3 next = Vector3.MoveTowards(t.position, target, speed * Time.deltaTime);
            if (rb != null) rb.MovePosition(next);   // 物理移动,不与 Rigidbody 冲突
            else t.position = next;
            timeout -= Time.deltaTime;
            yield return null;
        }
    }
}
