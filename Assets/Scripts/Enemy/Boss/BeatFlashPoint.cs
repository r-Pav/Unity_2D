using System.Collections;
using UnityEngine;

/// <summary>
/// 重音闪烁点 — 挂 Boss 预制体子 obj(可复用框架:任意 Boss 加这个子 obj 即生效)。
/// player 触发重音(音乐窗口开启)时,在子 obj 位置闪烁(SpriteRenderer 颜色闪一下)。
/// </summary>
public class BeatFlashPoint : MonoBehaviour
{
    [Header("闪烁")]
    [Tooltip("闪烁的 SpriteRenderer(拖本物体或子物体上的)")]
    public SpriteRenderer flashSprite;
    [Tooltip("闪烁颜色")]
    public Color flashColor = new Color(1f, 0.92f, 0.4f);
    [Tooltip("闪烁时长(秒)")]
    public float flashDuration = 0.25f;

    [Header("订阅")]
    [Tooltip("自动订阅全局窗口(仅 Boss 用;普通敌人由 EnemyBeatIndicator 手动触发)")]
    public bool autoSubscribe = true;

    private Color _originalColor;
    private Coroutine _flashRoutine;
    private bool _subscribed;

    private void Awake()
    {
        if (flashSprite == null)
            flashSprite = GetComponent<SpriteRenderer>();
        if (flashSprite != null)
            _originalColor = flashSprite.color;
    }

    private void Start()
    {
        if (!autoSubscribe) return;
        var mgr = MusicPointManager.Instance;
        if (mgr != null)
        {
            mgr.OnWindowEnter += OnWindowEnter;
            _subscribed = true;
        }
    }

    private void OnDestroy()
    {
        var mgr = MusicPointManager.Instance;
        if (mgr != null && _subscribed)
            mgr.OnWindowEnter -= OnWindowEnter;
    }

    /// <summary>重音窗口开启 → 闪烁(防重:连续窗口只重启协程)</summary>
    private void OnWindowEnter(float pointTime)
    {
        Flash();
    }

    /// <summary>手动触发闪烁(EnemyBeatIndicator 在自动重音窗口调用;不依赖自动订阅)</summary>
    public void Flash()
    {
        if (flashSprite == null || !gameObject.activeInHierarchy) return;
        if (_flashRoutine != null)
            StopCoroutine(_flashRoutine);
        _flashRoutine = StartCoroutine(FlashRoutine());
    }

    private IEnumerator FlashRoutine()
    {
        flashSprite.color = flashColor;
        yield return new WaitForSeconds(flashDuration);
        if (flashSprite != null)
            flashSprite.color = _originalColor;
        _flashRoutine = null;
    }
}
