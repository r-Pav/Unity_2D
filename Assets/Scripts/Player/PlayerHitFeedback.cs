using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 玩家受击反馈组件 — 全屏闪红遮罩 + 相机震动
/// 挂在 Player 上，由 PlayerController.TakeDamage() 直接调用
/// </summary>
public class PlayerHitFeedback : MonoBehaviour
{
    [SerializeField] private Image damageOverlay;   // Canvas 全屏红色 Image（Inspector 拖入）
    private CameraFollow cachedCam;                  // 缓存的相机引用

    private void Awake()
    {
        cachedCam = CameraFollow.Instance;
    }

    /// <summary>受击时调用：闪红 0.15s（alpha 0.3→0）+ 震屏</summary>
    public void OnTakeDamage()
    {
        // 闪红遮罩
        if (damageOverlay != null)
            StartCoroutine(FlashRoutine());

        // 震屏
        cachedCam?.Shake(0.1f, 0.15f);
    }

    private System.Collections.IEnumerator FlashRoutine()
    {
        // 设置初始红色半透明
        damageOverlay.color = new Color(1f, 0f, 0f, 0.3f);
        damageOverlay.raycastTarget = false;

        float duration = 0.15f;
        float elapsed = 0f;
        while (elapsed < duration)
        {
            elapsed += Time.unscaledDeltaTime;
            float alpha = Mathf.Lerp(0.3f, 0f, elapsed / duration);
            damageOverlay.color = new Color(1f, 0f, 0f, alpha);
            yield return null;
        }

        damageOverlay.color = new Color(1f, 0f, 0f, 0f);
    }
}
