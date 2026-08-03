using UnityEngine;
using System.Collections;

/// <summary>
/// Boss 房间触发器 — 挂在场景静态 Trigger 上。
/// Player 进入时：锁输入 → 自动走向 Boss → 启用空气墙+过场 → 激活 Boss。
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
        StartCoroutine(BossEntrySequence(other.transform));
    }

    private IEnumerator BossEntrySequence(Transform player)
    {
        PlayerController pc = player.GetComponent<PlayerController>();
        Rigidbody2D playerRb = player.GetComponent<Rigidbody2D>();

        // 1. 锁输入
        if (pc != null) pc.InputEnabled = false;

        // 2. 自动走向 Boss
        float dir = boss.transform.position.x > player.position.x ? 1f : -1f;
        player.localScale = new Vector3(dir, 1f, 1f);
        float t = 0f;
        while (t < 1f)
        {
            t += Time.deltaTime;
            player.position += Vector3.right * dir * 3f * Time.deltaTime;
            yield return null;
        }
        if (playerRb != null) playerRb.velocity = Vector2.zero;

        // 3. 过场：空气墙 + 相机聚焦
        foreach (var wall in airWalls)
        {
            if (wall != null) wall.enabled = true;
        }

        if (cameraFollow != null && boss != null)
        {
            Vector2 midpoint = (player.position + boss.transform.position) * 0.5f;
            cameraFollow.FocusOnPoint(midpoint, cutsceneZoom);
        }

        yield return new WaitForSeconds(1.5f);

        if (cameraFollow != null)
            cameraFollow.RestoreFollow();

        // 4. 恢复 + 激活
        if (pc != null) pc.InputEnabled = true;
        boss.ActivateBoss();
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
