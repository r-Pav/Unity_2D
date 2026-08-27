using Cinemachine;
using UnityEngine;

/// <summary>
/// Boss 房相机接管 — Cinemachine 虚拟相机方案。
/// 进 Boss 房:玩家 VCam Priority 降为休眠,boss VCam Priority 拉高 → CinemachineBrain 自动 blend 平滑切换。
/// Boss VCam 自身配置:Follow 玩家、OrthoSize 独立、CinemachineConfiner2D 绑 Boss 房范围。
/// Boss 死亡:恢复两个 VCam 原优先级 → blend 平滑回玩家相机。
/// 玩家死亡:相机也回玩家(复活后不残留 Boss 相机)。
/// 注意:Confiner2D 只支持 PolygonCollider2D / CompositeCollider2D,BoxCollider2D 不生效。
/// </summary>
public class BossRoomCamera : MonoBehaviour
{
    [Header("虚拟相机")]
    [Tooltip("玩家虚拟相机(跟随玩家的 VCam,进房时降优先级休眠)")]
    [SerializeField] private CinemachineVirtualCamera playerVCam;
    [Tooltip("Boss 房虚拟相机(Follow 玩家 + Confiner2D 限 Boss 房,OrthoSize 独立)")]
    [SerializeField] private CinemachineVirtualCamera bossVCam;

    [Header("优先级")]
    [Tooltip("Boss VCam 激活优先级(高于玩家即接管,默认 20)")]
    [SerializeField] private int bossActivePriority = 20;
    [Tooltip("休眠优先级(切换时玩家降到这个值)")]
    [SerializeField] private int inactivePriority = 5;

    private int _playerOriginalPriority;
    private int _bossOriginalPriority;
    private bool _active;

    private void Awake()
    {
        if (playerVCam != null) _playerOriginalPriority = playerVCam.Priority;
        if (bossVCam != null) _bossOriginalPriority = bossVCam.Priority;

        // Confiner 检查:BoxCollider2D 不生效,提前警告
        var confiner = bossVCam != null ? bossVCam.GetComponent<CinemachineConfiner2D>() : null;
        if (confiner != null && confiner.m_BoundingShape2D != null
            && !(confiner.m_BoundingShape2D is PolygonCollider2D)
            && !(confiner.m_BoundingShape2D is CompositeCollider2D))
        {
            Debug.LogWarning("[BossRoomCamera] Boss VCam 的 Confiner 需要 PolygonCollider2D(或带 Polygon 的 CompositeCollider2D),BoxCollider2D 不生效,相机不会受范围约束");
        }
    }

    private void OnEnable()
    {
        EventBus.Subscribe<PlayerDeathEvent>(OnPlayerDeath);
    }

    private void OnDisable()
    {
        EventBus.Unsubscribe<PlayerDeathEvent>(OnPlayerDeath);
    }

    /// <summary>玩家死亡:相机回玩家(复活后不残留 Boss 相机;Boss 战若继续,下次进房再接管)</summary>
    private void OnPlayerDeath(PlayerDeathEvent _)
    {
        if (_active)
            ExitBossRoom();
    }

    /// <summary>进房:降玩家 VCam、拉高 Boss VCam → Brain 平滑 blend 切换</summary>
    public void EnterBossRoom()
    {
        if (bossVCam == null) return;
        _active = true;
        if (playerVCam != null)
        {
            _playerOriginalPriority = playerVCam.Priority;
            playerVCam.Priority = inactivePriority;
        }
        bossVCam.Priority = bossActivePriority;
    }

    /// <summary>出房(Boss 死亡/玩家死亡):恢复两个 VCam 原优先级 → blend 回玩家相机</summary>
    public void ExitBossRoom()
    {
        _active = false;
        if (bossVCam != null)
            bossVCam.Priority = _bossOriginalPriority;
        if (playerVCam != null)
            playerVCam.Priority = _playerOriginalPriority;
    }
}
