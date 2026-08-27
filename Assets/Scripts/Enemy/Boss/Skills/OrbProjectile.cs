using UnityEngine;

/// <summary>
/// 法球投射物 — 由 BossSkill_Orb 生成,OrbManager 统一管理。
/// 以 player 为目标画线延长至地面/墙壁为终点;按音乐时钟匀速移动,
/// 到达终点时 = 对应标点(重音)响起 → 对 player 造成伤害(data 结算)后销毁。
/// 进度 = 1 - (标点时刻 - TrackTime)/总时长,帧率波动不漂移。
/// </summary>
public class OrbProjectile : MonoBehaviour
{
    private BossSkillContext _ctx;
    private BossSkillData _data;
    private float _targetBeatTime;   // 对应标点时刻(音乐时间轴)
    private float _totalDuration;    // 开始移动到重音的总时长(秒)
    private Vector2 _start;
    private Vector2 _end;
    private LayerMask _groundLayer;
    private float _rayMaxDistance;
    private bool _done;

    public void Initialize(BossSkillContext ctx, BossSkillData data, float targetBeatTime,
        LayerMask groundLayer, float rayMaxDistance)
    {
        _ctx = ctx;
        _data = data;
        _targetBeatTime = targetBeatTime;
        _groundLayer = groundLayer;
        _rayMaxDistance = rayMaxDistance;

        var mgr = MusicPointManager.Instance;
        float now = mgr != null ? mgr.TrackTime : Time.time;
        _totalDuration = Mathf.Max(0.1f, targetBeatTime - now);
        _start = transform.position;
        _end = ComputeEndPoint();
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
        if (_done) return;

        var mgr = MusicPointManager.Instance;
        float trackTime = mgr != null ? mgr.TrackTime : Time.time;
        float remaining = _targetBeatTime - trackTime;

        // 音乐时钟插值:剩余时间比例 → 位置(帧率波动不累积漂移)
        float progress = 1f - Mathf.Clamp01(remaining / _totalDuration);
        transform.position = Vector2.Lerp(_start, _end, progress);

        if (remaining <= 0f)
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
