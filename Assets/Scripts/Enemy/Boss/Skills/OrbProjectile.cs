using UnityEngine;

/// <summary>
/// 法球投射物 — 由 BossSkill_Orb 在「发射那一刻」生成,OrbManager 统一管理。
/// 发射时按「法球 → 玩家方向」画线延长至地面/墙壁为终点(只快照一次),
/// 之后以 Initialize 传入的速度匀速直线飞行,到达终点即对 player 造成伤害(data 结算)后销毁。
/// 时长 = 起点到终点距离 / 速度,进度由 Time.deltaTime 累加(与音乐时钟/标点无关,帧率波动不漂移)。
/// </summary>
public class OrbProjectile : MonoBehaviour
{
    private BossSkillContext _ctx;
    private BossSkillData _data;
    private float _duration;         // 飞到终点的总时长(秒) = 距离 / 速度
    private float _elapsed;          // 已飞行时长(秒)
    private Vector2 _start;
    private Vector2 _end;
    private LayerMask _groundLayer;
    private float _rayMaxDistance;
    private bool _initialized;       // 未 Initialize 前不推进(防被提前实例化时在 (0,0) 误命中)
    private bool _done;

    /// <summary>由 BossSkill_Orb 在发射那一刻调用:快照起点/终点并算出飞行时长</summary>
    public void Initialize(BossSkillContext ctx, BossSkillData data, float speed,
        LayerMask groundLayer, float rayMaxDistance)
    {
        _ctx = ctx;
        _data = data;
        _groundLayer = groundLayer;
        _rayMaxDistance = rayMaxDistance;

        _start = transform.position;
        _end = ComputeEndPoint();
        _duration = Vector2.Distance(_start, _end) / Mathf.Max(0.1f, speed);
        _elapsed = 0f;
        _done = false;
        _initialized = true;
    }

    /// <summary>终点:法球 → player 方向画线,延长至地面/墙壁;没命中 = 方向 × 最大距离</summary>
    private Vector2 ComputeEndPoint()
    {
        Vector2 orbPos = transform.position;
        Vector2 dir = _ctx != null && _ctx.player != null
            ? ((Vector2)_ctx.player.position - orbPos).normalized
            : Vector2.right;
        if (dir == Vector2.zero) dir = Vector2.right;

        RaycastHit2D hit = Physics2D.Raycast(orbPos, dir, _rayMaxDistance, _groundLayer);
        return hit.collider != null ? hit.point : orbPos + dir * _rayMaxDistance;
    }

    private void Update()
    {
        if (_done || !_initialized) return;

        // 纯时序:累加真实时间 → 进度(到 1 即到达终点)
        _elapsed += Time.deltaTime;
        float progress = _duration > 0.0001f ? Mathf.Clamp01(_elapsed / _duration) : 1f;
        transform.position = Vector2.Lerp(_start, _end, progress);

        if (progress >= 1f)
        {
            _done = true;
            HitPlayer();
            OrbManager.Instance?.Unregister(this);
            Destroy(gameObject);
        }
    }

    private void HitPlayer()
    {
        if (_ctx == null || _ctx.player == null || _data == null || _ctx.boss == null) return;
        var ph = _ctx.player.GetComponent<PlayerHealth>();
        if (ph == null) return;
        Vector2 faceDir = _ctx.player.position.x > _ctx.boss.transform.position.x ? Vector2.right : Vector2.left;
        var info = _data.BuildDamageInfo(_ctx.boss, _ctx.boss.transform.position, faceDir);
        CombatResolver.Resolve(_ctx.boss, ph, info);
        if (_data.hitVFXPrefab != null)
            VFXSpawner.SpawnOnPlayer(_data.hitVFXPrefab, _ctx.player.position);
    }
}
