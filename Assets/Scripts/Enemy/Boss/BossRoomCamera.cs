using Cinemachine;
using UnityEngine;

/// <summary>
/// Boss 房相机接管 — Cinemachine 虚拟相机方案。
/// 进 Boss 房:玩家 VCam Priority 降为休眠,boss VCam Priority 拉高 → CinemachineBrain 自动 blend 平滑切换。
/// Boss VCam 自身配置:Follow 玩家、OrthoSize 独立、CinemachineConfiner2D 绑 Boss 房范围。
/// Boss 死亡:恢复两个 VCam 原优先级 → blend 平滑回玩家相机。
/// 主相机始终 enabled(Brain 驱动),不需要关玩家相机;CameraFollow 保持禁用勿动。
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

    private void Awake()
    {
        if (playerVCam != null) _playerOriginalPriority = playerVCam.Priority;
        if (bossVCam != null) _bossOriginalPriority = bossVCam.Priority;
    }

    /// <summary>进房:降玩家 VCam、拉高 Boss VCam → Brain 平滑 blend 切换</summary>
    public void EnterBossRoom()
    {
        if (bossVCam == null) return;
        if (playerVCam != null)
        {
            _playerOriginalPriority = playerVCam.Priority;
            playerVCam.Priority = inactivePriority;
        }
        bossVCam.Priority = bossActivePriority;
    }

    /// <summary>出房(Boss 死亡):恢复两个 VCam 原优先级 → blend 回玩家相机</summary>
    public void ExitBossRoom()
    {
        if (bossVCam != null)
            bossVCam.Priority = _bossOriginalPriority;
        if (playerVCam != null)
            playerVCam.Priority = _playerOriginalPriority;
    }
}
