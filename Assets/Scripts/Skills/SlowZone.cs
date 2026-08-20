using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 减速圈（A02B02 技能改造,2026-08-20）— 运行时生成：敌人进入圈内移速 × slowFactor，离开恢复 1f。
/// - CircleCollider2D(isTrigger) + 圆环 Sprite（运行时生成贴图,半径跟随 localScale）
/// - 只检测 Enemy 层（玩家/其他不受影响）
/// - 存在 slowZoneDuration 秒后自动销毁（Time.deltaTime 计时：卡帧 timeScale=0 期间不消耗）
/// - 静态工厂 SlowZone.Spawn(position, radius, duration, slowFactor)
/// - 活跃圈注册表（Spawn 注册 / OnDestroy 移除）+ 查询方法 FindNearestExcludingPlayer
///   （A02B02 传送选目标圈：排除玩家所在圈后取距离玩家最近的圈,无可用返回 null）
/// </summary>
public class SlowZone : MonoBehaviour
{
    private static readonly LayerMask EnemyMask = LayerMask.GetMask("Enemy");
    private static readonly List<SlowZone> Active = new List<SlowZone>();

    private float remaining;
    private float slowFactor;
    private float radius; // 世界半径（查询玩家是否在圈内用）
    private readonly HashSet<EnemyControllerBase> affected = new HashSet<EnemyControllerBase>();

    /// <summary>生成减速圈（静态工厂；自动注册进活跃圈列表）</summary>
    public static void Spawn(Vector2 position, float radius, float duration, float slowFactor)
    {
        GameObject go = new GameObject("ComboLv3_SlowZone");
        go.transform.position = position;
        SlowZone zone = go.AddComponent<SlowZone>();
        zone.Init(radius, duration, slowFactor);
        Active.Add(zone);
    }

    /// <summary>
    /// 查询传送目标圈（A02B02 左键2）：排除玩家当前所在圈（玩家位置在圈半径内）后,选距离玩家最近的圈；
    /// 无可用圈返回 null（调用方取消传送,保持原逻辑回 Idle）。
    /// </summary>
    public static SlowZone FindNearestExcludingPlayer(Vector2 playerPosition)
    {
        SlowZone best = null;
        float bestSqr = float.MaxValue;
        for (int i = 0; i < Active.Count; i++)
        {
            SlowZone zone = Active[i];
            if (zone == null) continue; // Unity 伪空防御（销毁尚未移除的瞬间）
            Vector2 center = (Vector2)zone.transform.position;
            float sqrDist = (center - playerPosition).sqrMagnitude;
            if (sqrDist <= zone.radius * zone.radius) continue; // 玩家在圈内 → 排除（避免原地传送）
            if (sqrDist < bestSqr)
            {
                bestSqr = sqrDist;
                best = zone;
            }
        }
        return best;
    }

    /// <summary>
    /// 玩家位置是否已在任意圈内（A02B02 点技能时已在圈内则不重复生成自身圈）。
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

    private void Init(float radius, float duration, float slowFactor)
    {
        this.slowFactor = slowFactor;
        this.radius = Mathf.Max(0.1f, radius);
        remaining = Mathf.Max(0.1f, duration);

        // 触发碰撞体：局部半径 0.5，配合 localScale = radius*2 → 世界半径 = radius
        CircleCollider2D col = gameObject.AddComponent<CircleCollider2D>();
        col.isTrigger = true;
        col.radius = 0.5f;

        // 圆环视觉：运行时生成单位圆环贴图（1 世界单位），scale 跟随半径
        SpriteRenderer sr = gameObject.AddComponent<SpriteRenderer>();
        sr.sprite = CreateRingSprite();
        sr.color = new Color(0.3f, 0.85f, 1f, 0.75f); // 冰蓝色,与传送弹紫色区分
        sr.sortingOrder = 5; // 地面之上、传送弹(10)之下

        transform.localScale = Vector3.one * (Mathf.Max(0.1f, radius) * 2f);
    }

    private void Update()
    {
        // 按缩放时间计时：慢动作期间走慢,卡帧 timeScale=0 期间不走
        remaining -= Time.deltaTime;
        if (remaining <= 0f)
            Destroy(gameObject);
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

    /// <summary>销毁兜底：圈到点消失时仍在圈内的敌人恢复移速（Destroy 不触发 OnTriggerExit2D）；注销活跃圈注册表</summary>
    private void OnDestroy()
    {
        Active.Remove(this);
        foreach (EnemyControllerBase enemy in affected)
        {
            if (enemy != null) // 已销毁的 enemy 走 Unity 重载 == 判空
                enemy.speedMultiplier = 1f;
        }
        affected.Clear();
    }

    /// <summary>生成单位圆环贴图（外圈实心、内圈镂空；Sprite 世界尺寸 = 1 单位）</summary>
    private static Sprite CreateRingSprite()
    {
        int size = 64;
        Texture2D tex = new Texture2D(size, size);
        Color[] pixels = new Color[size * size];
        float center = (size - 1) / 2f;
        float outer = center;      // 外径贴合圆边缘
        float inner = center - 6f; // 环宽 6px
        for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float d = Vector2.Distance(new Vector2(x, y), Vector2.one * center);
                pixels[y * size + x] = (d <= outer && d >= inner) ? Color.white : Color.clear;
            }
        tex.SetPixels(pixels);
        tex.Apply();
        return Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), size);
    }
}
