using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 减速圈（A02B02 技能改造）— 运行时生成：敌人进入圈内移速 × slowFactor，离开恢复 1f。
/// - CircleCollider2D(isTrigger)：局部半径 0.5,localScale = radius*2 → 世界半径 = radius
/// - 视觉 = 技能分支资产配的粒子特效 prefab(vfxPrefab,每技能一槽),替代早期代码生成细环(2026-09-07 saika 拍板):
///   特效实例独立生成(不挂圈子级,避免被圈 localScale 放大),生命周期锚定圈 duration:到点停粒子发射,
///   粒子按自身 startLifetime 淡出后销毁;圈触发效果精确到点结束(立即恢复移速 + 禁用 trigger)
/// - 只检测 Enemy 层(玩家/其他不受影响)
/// - 存在 duration 秒后自动销毁(Time.deltaTime 计时：卡帧 timeScale=0 期间不消耗)
/// - 静态工厂 SlowZone.Spawn(position, radius, duration, slowFactor, vfxPrefab)
/// - 活跃圈注册表(Spawn 注册 / OnDestroy 移除)+ 查询方法 FindNearestExcludingPlayer
///   (A02B02 传送选目标圈:排除玩家所在圈后取距离玩家最近的圈,无可用返回 null)
/// </summary>
public class SlowZone : MonoBehaviour
{
    private static readonly LayerMask EnemyMask = LayerMask.GetMask("Enemy");
    private static readonly List<SlowZone> Active = new List<SlowZone>();

    private float remaining;
    private float slowFactor;
    private float radius;            // 世界半径(查询玩家是否在圈内用)
    private GameObject _vfxInstance; // 技能特效实例(独立物体,不随圈缩放)
    private bool _shutdown;          // 到点已触发收尾(防 Update 重复)
    private readonly HashSet<EnemyControllerBase> affected = new HashSet<EnemyControllerBase>();

    /// <summary>生成减速圈(静态工厂;自动注册进活跃圈列表)。vfxPrefab 空 = 无视觉(纯 trigger)</summary>
    public static void Spawn(Vector2 position, float radius, float duration, float slowFactor, GameObject vfxPrefab = null)
    {
        GameObject go = new GameObject("ComboLv3_SlowZone");
        go.transform.position = position;
        SlowZone zone = go.AddComponent<SlowZone>();
        zone.Init(radius, duration, slowFactor, vfxPrefab);
        Active.Add(zone);
    }

    /// <summary>
    /// 查询传送目标圈(A02B02 左键2):排除玩家当前所在圈(玩家位置在圈半径内)后,选距离玩家最近的圈;
    /// 无可用圈返回 null(调用方取消传送,保持原逻辑回 Idle)。
    /// </summary>
    public static SlowZone FindNearestExcludingPlayer(Vector2 playerPosition)
    {
        SlowZone best = null;
        float bestSqr = float.MaxValue;
        for (int i = 0; i < Active.Count; i++)
        {
            SlowZone zone = Active[i];
            if (zone == null) continue; // Unity 伪空防御(销毁尚未移除的瞬间)
            Vector2 center = (Vector2)zone.transform.position;
            float sqrDist = (center - playerPosition).sqrMagnitude;
            if (sqrDist <= zone.radius * zone.radius) continue; // 玩家在圈内 → 排除(避免原地传送)
            if (sqrDist < bestSqr)
            {
                bestSqr = sqrDist;
                best = zone;
            }
        }
        return best;
    }

    /// <summary>
    /// 玩家位置是否已在任意圈内(A02B02 点技能时已在圈内则不重复生成自身圈)。
    /// </summary>
    public static bool IsPointInAnyZone(Vector2 point)
    {
        for (int i = 0; i < Active.Count; i++)
        {
            SlowZone zone = Active[i];
            if (zone == null) continue;
            Vector2 center = (Vector2)zone.transform.position;
            if ((center - point).sqrMagnitude <= zone.radius * zone.radius)
                return true;
        }
        return false;
    }

    private void Init(float radius, float duration, float slowFactor, GameObject vfxPrefab)
    {
        this.slowFactor = slowFactor;
        this.radius = Mathf.Max(0.1f, radius);
        remaining = Mathf.Max(0.1f, duration);

        // 触发碰撞体:局部半径 0.5,配合 localScale = radius*2 → 世界半径 = radius
        CircleCollider2D col = gameObject.AddComponent<CircleCollider2D>();
        col.isTrigger = true;
        col.radius = 0.5f;

        transform.localScale = Vector3.one * (Mathf.Max(0.1f, radius) * 2f);

        // 视觉 = 技能分支资产特效槽;独立生成不挂子级(避免被圈 localScale=radius*2 放大),位置与圈同点
        if (vfxPrefab != null)
        {
            _vfxInstance = Instantiate(vfxPrefab, transform.position, Quaternion.identity);
            _vfxInstance.SetActive(true);   // 团结引擎:复制 prefab 激活状态,inactive 则 Play 不生效 → 强制激活
            foreach (var ps in _vfxInstance.GetComponentsInChildren<ParticleSystem>(true))
                ps.Play();
        }
    }

    private void Update()
    {
        if (_shutdown) return;
        // 按缩放时间计时:慢动作期间走慢,卡帧 timeScale=0 期间不走
        remaining -= Time.deltaTime;
        if (remaining <= 0f)
            BeginShutdown();
    }

    /// <summary>
    /// 到点收尾:圈效果精确结束(立即恢复圈内敌人移速 + 禁用 trigger),特效停发射按尾迹淡出后销毁。
    /// 减速/传送判定依赖 Active 注册表,圈体延迟销毁期间已从功能上失效(affected 已清、collider 已禁用)。
    /// </summary>
    private void BeginShutdown()
    {
        if (_shutdown) return;
        _shutdown = true;

        // 立即结束减速效果(Destroy 延迟期间敌人不受影响)
        foreach (EnemyControllerBase enemy in affected)
            if (enemy != null)
                enemy.speedMultiplier = 1f;
        affected.Clear();

        var col = GetComponent<Collider2D>();
        if (col != null) col.enabled = false;

        if (_vfxInstance != null)
        {
            // 停发射保留尾迹:粒子按自身 startLifetime 飞完后销毁(循环粒子停发射即不新发)
            foreach (var ps in _vfxInstance.GetComponentsInChildren<ParticleSystem>(true))
                ps.Stop(false, ParticleSystemStopBehavior.StopEmitting);
            float tail = MaxParticleLifetime(_vfxInstance);
            Destroy(_vfxInstance, tail);
            Destroy(gameObject, tail);
        }
        else
        {
            Destroy(gameObject);
        }
    }

    private void OnTriggerEnter2D(Collider2D other)
    {
        // 只检测 Enemy 层
        if ((EnemyMask & (1 << other.gameObject.layer)) == 0) return;
        EnemyControllerBase enemy = other.GetComponentInParent<EnemyControllerBase>();
        if (enemy == null || enemy.IsDead) return;
        if (affected.Add(enemy))
            enemy.speedMultiplier = slowFactor;
    }

    private void OnTriggerExit2D(Collider2D other)
    {
        if ((EnemyMask & (1 << other.gameObject.layer)) == 0) return;
        EnemyControllerBase enemy = other.GetComponentInParent<EnemyControllerBase>();
        if (enemy == null) return;
        if (affected.Remove(enemy))
            enemy.speedMultiplier = 1f;
    }

    /// <summary>销毁兜底:圈消失时仍在圈内的敌人恢复移速(Destroy 不触发 OnTriggerExit2D);注销活跃圈注册表;清特效实例</summary>
    private void OnDestroy()
    {
        Active.Remove(this);
        foreach (EnemyControllerBase enemy in affected)
        {
            if (enemy != null) // 已销毁的 enemy 走 Unity 重载 == 判空
                enemy.speedMultiplier = 1f;
        }
        affected.Clear();
        if (_vfxInstance != null)
            Destroy(_vfxInstance);
    }

    /// <summary>粒子最大单次寿命(淡出销毁延迟用;循环粒子停发射后按 startLifetime 飞完)</summary>
    private static float MaxParticleLifetime(GameObject go)
    {
        float max = 0f;
        foreach (var ps in go.GetComponentsInChildren<ParticleSystem>(true))
        {
            if (ps == null) continue;
            float l = ps.main.startLifetime.constantMax;
            if (l > max) max = l;
        }
        return max + 0.1f;
    }
}
