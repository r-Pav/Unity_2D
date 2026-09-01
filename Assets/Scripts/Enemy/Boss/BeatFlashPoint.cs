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
    private bool _spriteInitiallyEnabled = true;   // 初始 SpriteRenderer 显隐(闪完恢复;支持"初始隐藏,闪时显示")
    private bool _initiallyActive = true;          // 初始 GameObject 激活态(闪完恢复;支持整个物体 inactive 的隐藏方式)

    private void Awake()
    {
        _initiallyActive = gameObject.activeSelf;
        if (flashSprite == null)
            flashSprite = GetComponent<SpriteRenderer>();
        if (flashSprite != null)
        {
            _originalColor = flashSprite.color;
            _spriteInitiallyEnabled = flashSprite.enabled;
        }
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

    /// <summary>手动触发闪烁(EnemyBeatIndicator 在自动重音窗口调用;不依赖自动订阅)。
    /// 物体初始 inactive 也能闪:先激活让协程能跑,闪完恢复初始激活态。</summary>
    public void Flash()
    {
        if (flashSprite == null) return;
        if (!gameObject.activeSelf)
            gameObject.SetActive(true);   // 整个物体被隐藏:闪时激活(协程需要 active 才能跑)
        if (_flashRoutine != null)
            StopCoroutine(_flashRoutine);
        _flashRoutine = StartCoroutine(FlashRoutine());
    }

    private IEnumerator FlashRoutine()
    {
        if (!flashSprite.enabled)
            flashSprite.enabled = true;   // 初始隐藏的标识:闪时显示
        flashSprite.color = flashColor;
        yield return new WaitForSeconds(flashDuration);
        ResetToInitial();
        _flashRoutine = null;
    }

    /// <summary>主动消失(恢复初始显隐态,停掉闪烁协程)。两个调用时机:
    /// 1. 窗口正常结束(EnemyBeatIndicator.OnWindowPassed);2. 背刺命中帧(PlayerBackstabState.OnBackstabHitFrame)。</summary>
    public void Hide()
    {
        if (_flashRoutine != null)
        {
            StopCoroutine(_flashRoutine);
            _flashRoutine = null;
        }
        ResetToInitial();
    }

    /// <summary>恢复初始态(闪烁自然结束与主动消失共用)。
    /// 普通敌人(autoSubscribe=false)标识一律隐藏:防 BeatPoint 以 GameObject inactive 初始隐藏时
    /// Awake 延迟到 Flash 激活才执行、记录的初始状态失真导致闪完不消失;Boss 恢复初始激活态保持原行为。</summary>
    private void ResetToInitial()
    {
        if (flashSprite != null)
        {
            flashSprite.color = _originalColor;
            flashSprite.enabled = _spriteInitiallyEnabled;
        }
        if (!autoSubscribe)
            gameObject.SetActive(false);   // 普通 enemy 标识:闪完/消失一律隐藏,下次 Flash 再激活
        else
            gameObject.SetActive(_initiallyActive);   // Boss:恢复初始激活态(颜色闪,不隐藏)
    }
}
