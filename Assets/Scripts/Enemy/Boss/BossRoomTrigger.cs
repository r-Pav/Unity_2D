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

    [Header("空气墙")]
    [SerializeField] private Collider2D[] airWalls;

    [Header("过场参数")]
    [SerializeField] private float cutsceneZoom = 7f;

    private bool _triggered;

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

        // 音乐:进 Boss 房,场景曲缓出、Boss 曲双源交叠缓入
        MusicPointManager.Instance?.EnterBossMusic();
        yield break;
    }

    private void OnDisable()
    {
        _triggered = false;
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
