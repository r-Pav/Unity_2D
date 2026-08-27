using UnityEngine;
using System.Collections;

/// <summary>
/// Boss 房间触发器 — 挂在场景静态 Trigger 上。
/// 空气墙默认关闭;Player 进入 Boss 房 Trigger 时:开启空气墙(锁门) + 激活 Boss(直接进入追击,不过场)。
/// 此 GameObject 独立放置，不跟随 Boss。
/// </summary>
public class BossRoomTrigger : MonoBehaviour
{
    [Header("引用")]
    [SerializeField] private BossControllerBase boss;
    [SerializeField] private CameraFollow cameraFollow;

    [Header("相机接管")]
    [Tooltip("Boss 房相机接管组件(挂场景空物体或本物体;留空 = 不接管相机)")]
    [SerializeField] private BossRoomCamera roomCamera;

    [Header("空气墙")]
    [SerializeField] private Collider2D[] airWalls;

    [Header("过场参数")]
    [SerializeField] private float cutsceneZoom = 7f;

    private bool _triggered;

    private void OnEnable()
    {
        EventBus.Subscribe<BossDefeatedEvent>(OnBossDefeated);
    }

    private void OnDisable()
    {
        EventBus.Unsubscribe<BossDefeatedEvent>(OnBossDefeated);
        _triggered = false;
    }

    /// <summary>Boss 死亡:恢复相机(出房)</summary>
    private void OnBossDefeated(BossDefeatedEvent e)
    {
        if (e.boss != null && e.boss == boss && roomCamera != null)
            roomCamera.ExitBossRoom();
    }

    private void OnTriggerEnter2D(Collider2D other)
    {
        if (_triggered) return;
        if (!other.CompareTag("Player")) return;
        if (boss == null) return;

        _triggered = true;
        StartCoroutine(BossEntrySequence());
    }

    private IEnumerator BossEntrySequence()
    {
        // 开启空气墙(锁门)
        foreach (var wall in airWalls)
        {
            if (wall != null) wall.enabled = true;
        }

        // 激活 Boss — 立即进入追击状态(不过场、不锁输入、不自动走)
        boss.ActivateBoss();

        // 相机接管:Boss 房相机锁定到锚点
        if (roomCamera != null)
            roomCamera.EnterBossRoom();

        // 音乐:进 Boss 房,切换器切 Boss 曲(同物体挂 MusicSwitchTrigger 时自动调用)
        GetComponent<MusicSwitchTrigger>()?.TriggerSwitch();
        yield break;
    }

#if UNITY_EDITOR
    private void OnDrawGizmos()
    {
        var col = GetComponent<Collider2D>();
        if (col == null) return;

        Gizmos.color = _triggered
            ? new Color(0.5f, 0.5f, 0.5f, 0.15f)
            : new Color(1f, 0.3f, 0.3f, 0.25f);

        Gizmos.DrawCube(col.bounds.center, col.bounds.size);
        Gizmos.DrawWireCube(col.bounds.center, col.bounds.size);
    }
#endif
}
