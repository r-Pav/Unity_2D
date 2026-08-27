using System.Collections;
using UnityEngine;

/// <summary>
/// Boss 重击 — 独立机制,由 BossHeavy 标点组驱动。
/// 每个重击标点前 prepareLead 秒:发现标点 → 检查技能优先级(技能执行中则让位等下一个)
/// → Boss 闪现到 player 面前固定距离(面朝墙/空气墙则闪背后)→ 到标点处执行重击攻击。
/// 全程霸体:受击不掉硬直/击退、不中断(照常掉血);被 player 攻击命中 → 该次重击被抵消(不造成伤害)。
/// 重击动画/特效/伤害独立配置(不复用普攻)。
/// </summary>
public class BossHeavyAttack : MonoBehaviour
{
    [Header("标点")]
    [Tooltip("重击标点组名(MusicTrackData Point Groups 里的组名)")]
    public string groupName = "BossHeavy";
    [Tooltip("提前准备秒数(发现标点后提前多久闪现)")]
    public float prepareLead = 2f;

    [Header("闪现")]
    [Tooltip("闪现到 player 面前的距离")]
    public float teleportDistance = 2f;
    [Tooltip("墙/空气墙层(player 面朝方向有墙则闪背后)")]
    public LayerMask wallLayer;

    [Header("重击动画/表现")]
    [Tooltip("重击动画状态名(独立动画)")]
    public string animState = "Heavy";
    [Tooltip("重击特效 prefab 槽(标点攻击时生成)")]
    public GameObject heavyVFXPrefab;

    [Header("重击伤害")]
    [Tooltip("重击伤害值")]
    public float damage = 30f;
    [Tooltip("重击击退力度")]
    public float knockbackForce = 6f;
    [Tooltip("重击击退上挑(0 = 水平)")]
    public float knockbackUp = 1f;

    // ============================================================
    // 运行时
    // ============================================================

    private BossControllerBase _boss;
    private BossSkillSlots _slots;
    private Animator _animator;
    private Transform _player;
    private Coroutine _loopRoutine;

    private bool _heavyActive;   // 重击施放中(霸体)
    private bool _cancelled;     // 被 player 攻击抵消(该次重击不造成伤害)

    public bool IsActive => _heavyActive;

    private void Awake()
    {
        _boss = GetComponent<BossControllerBase>();
        _slots = GetComponent<BossSkillSlots>();
        _animator = GetComponentInChildren<Animator>();
    }

    private void Start()
    {
        _player = PlayerController.Instance?.transform;
    }

    private void OnEnable()
    {
        _loopRoutine = StartCoroutine(HeavyLoop());
    }

    private void OnDisable()
    {
        if (_loopRoutine != null) StopCoroutine(_loopRoutine);
        _heavyActive = false;
    }

    /// <summary>重击被 player 攻击命中:标记抵消(该次重击不造成伤害),Boss 照常掉血</summary>
    public void NotifyHit()
    {
        if (_heavyActive)
            _cancelled = true;
    }

    // ============================================================
    // 标点监听循环(事件驱动等待,不每帧轮询业务)
    // ============================================================

    private IEnumerator HeavyLoop()
    {
        var mgr0 = MusicPointManager.Instance;
        if (mgr0 != null)
        {
            var track = mgr0.CurrentTrack;
            var group = track != null ? track.GetGroup(groupName) : null;
            Debug.Log($"[BossHeavy] 监听启动 track={(track != null ? track.name : "null")} 组[{groupName}]存在={(group != null)} 点数={(group != null && group.points != null ? group.points.Length : 0)}");
        }

        while (_boss != null && !_boss.IsDead)
        {
            var mgr = MusicPointManager.Instance;
            if (mgr == null)
            {
                yield return null;
                continue;
            }

            float next = mgr.NextPointInGroup(groupName);
            float toNext = next >= 0f ? next - mgr.TrackTime : -1f;

            if (toNext >= 0f && toNext <= prepareLead)
            {
                Debug.Log($"[BossHeavy] 发现重击标点 {next:F2} 秒,还有 {toNext:F2} 秒(组={groupName})");
                // 不打断技能:技能执行中重击让位,等技能结束后再查下一个标点
                if (_slots != null && _slots.IsExecuting)
                {
                    Debug.Log("[BossHeavy] 技能执行中,重击让位(不打断技能)");
                    while (mgr.TrackTime < next) yield return null;
                    continue;
                }
                yield return StartCoroutine(ExecuteHeavy(next));
                continue;
            }
            yield return null;
        }
    }

    /// <summary>执行一次重击:闪现 → 等标点 → 攻击 → 解除霸体</summary>
    private IEnumerator ExecuteHeavy(float beatTime)
    {
        _heavyActive = true;
        _cancelled = false;
        Debug.Log($"[BossHeavy] 重击开始(标点 {beatTime:F2}),闪现+霸体");

        TeleportToPlayer();

        // 播放动画:优先重击动画,未配置则用普攻 Attack 代替(测试用)
        string state = string.IsNullOrEmpty(animState) ? "Attack" : animState;
        if (_animator != null)
        {
            _animator.Play(state);
            Debug.Log($"[BossHeavy] 播放动画 {state}");
        }

        // 等到重音标点
        var mgr = MusicPointManager.Instance;
        while (mgr != null && mgr.TrackTime < beatTime)
            yield return null;
        if (_boss == null || _boss.IsDead) yield break;

        // 到标点:未被抵消 → 重击攻击
        if (_cancelled)
        {
            Debug.Log("[BossHeavy] 重击被玩家攻击抵消,本次不造成伤害");
        }
        else
        {
            Debug.Log($"[BossHeavy] 重音到达,重击攻击 damage={damage}");
            PerformHeavyHit();
        }

        // 短后摇(动画播完由动画器接管,这里给个最小间隔)
        yield return new WaitForSeconds(0.3f);

        _heavyActive = false;
    }

    /// <summary>闪现到 player 面前(面朝墙/空气墙则闪背后)</summary>
    private void TeleportToPlayer()
    {
        if (_player == null || _boss == null) return;
        float dir = _player.position.x > _boss.transform.position.x ? 1f : -1f;

        // player 面朝方向是否有墙/空气墙
        bool playerFacingWall = Physics2D.Raycast(_player.position, Vector2.right * dir, teleportDistance + 1f, wallLayer).collider != null;

        Vector3 target = playerFacingWall
            ? _player.position - Vector3.right * dir * teleportDistance   // 闪背后
            : _player.position + Vector3.right * dir * teleportDistance;  // 闪面前

        _boss.transform.position = new Vector3(target.x, _boss.transform.position.y, _boss.transform.position.z);
    }

    /// <summary>重击攻击:对 player 结算伤害 + 特效</summary>
    private void PerformHeavyHit()
    {
        if (_player == null || _boss == null) return;
        var ph = _player.GetComponent<PlayerHealth>();
        if (ph == null) return;

        Vector2 faceDir = _player.position.x > _boss.transform.position.x ? Vector2.right : Vector2.left;
        Vector2 kbDir = new Vector2(faceDir.x, knockbackUp);
        var info = new DamageInfo
        {
            amount = damage,
            source = _boss,
            sourcePosition = _boss.transform.position,
            attackLabel = "BossHeavy",
            knockback = new Knockback
            {
                direction = kbDir.normalized,
                force = knockbackForce,
                duration = 0.2f,
                ignoreResistance = false
            }
        };
        CombatResolver.Resolve(_boss, ph, info);

        if (heavyVFXPrefab != null)
            VFXSpawner.SpawnOnPlayer(heavyVFXPrefab, _player.position);
    }
}
