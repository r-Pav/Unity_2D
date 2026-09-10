using System.Collections;
using UnityEngine;

/// <summary>
/// 重音标识点 — 挂 enemy/Boss 子 obj(可复用框架:任意角色加这个子 obj 即生效)。
/// 双模式:
/// ① 拖 aimPrefab(背刺标识 BackstabAim_Template)= 挂点 + prefab 槽生成器:Flash 时在其位置实例化 prefab
///    (BackstabAimIndicator.Show 重置粒子从头播:圆环收缩穿过固定判定圈),Hide 收起实例。enemy 背刺预告用。
/// ② 没拖 = 原 SpriteRenderer 闪烁(flashSprite 颜色闪一下),player 触发重音(音乐窗口开启)时闪烁。Boss 用。
/// autoSubscribe=true 自动订阅全局窗口;普通 enemy 由 EnemyBeatIndicator 手动触发。
/// </summary>
public class BeatFlashPoint : MonoBehaviour
{
    [Header("标识 prefab 槽(拖了 = 挂点生成器模式;不拖 = 下方 SpriteRenderer 闪烁)")]
    [Tooltip("背刺标识 prefab(Assets/Prefab/VFX/BackstabAim_Template):Flash 时实例化到自己位置并播放,重复 Flash 复用实例")]
    public GameObject aimPrefab;

    [Header("闪烁(SpriteRenderer 模式,Boss 用)")]
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
    private GameObject _aimInstance;               // 标识 prefab 实例(首次 Flash 生成,复用显隐;随本物体销毁自动销毁)

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

    /// <summary>手动触发(EnemyBeatIndicator 在自动重音窗口前调用;不依赖自动订阅)。
    /// 物体初始 inactive 也能闪:先激活让协程能跑,闪完恢复初始激活态。
    /// 拖了 aimPrefab = 启动标识实例(隐藏自身 SpriteRenderer 防双视觉);没拖 = 原 SpriteRenderer 闪烁。
    /// secondsToWindowStart = 触发时距窗口起点的真实剩余秒数;windowSeconds = 判定窗口时长(内环适配用)。</summary>
    public void Flash(float secondsToWindowStart = 1f, float windowSeconds = 0.3f)
    {
        if (aimPrefab != null)
        {
            // ── 模式①:挂点 + prefab 槽(背刺标识)──
            if (flashSprite != null) flashSprite.enabled = false;   // 旧闪点不显示,防头顶双视觉
            if (!gameObject.activeSelf)
                gameObject.SetActive(true);                          // 挂点初始 inactive:激活才能承载实例
            ShowAimInstance(secondsToWindowStart, windowSeconds);
            return;
        }
        if (flashSprite == null) return;
        if (!gameObject.activeSelf)
            gameObject.SetActive(true);   // 整个物体被隐藏:闪时激活(协程需要 active 才能跑)
        if (_flashRoutine != null)
            StopCoroutine(_flashRoutine);
        _flashRoutine = StartCoroutine(FlashRoutine());
    }

    /// <summary>生成/复用标识实例并从头播一轮(实例挂本物体下,位置 = 挂点;重复 Flash 只重置播放不重复生成)</summary>
    private void ShowAimInstance(float secondsToWindowStart, float windowSeconds)
    {
        if (_aimInstance == null && aimPrefab != null)
            _aimInstance = Instantiate(aimPrefab, transform, false);
        if (_aimInstance == null) return;
        var ctrl = _aimInstance.GetComponentInChildren<BackstabAimIndicator>(true);
        if (ctrl != null) ctrl.Show(secondsToWindowStart, windowSeconds);
        else _aimInstance.SetActive(true);   // 无组件兜底:激活让 playOnAwake 播
    }

    /// <summary>收起标识实例(BackstabAimIndicator.Hide = 停粒子 + 实例失活;无组件直接失活)</summary>
    private void HideAimInstance()
    {
        if (_aimInstance == null) return;
        var ctrl = _aimInstance.GetComponentInChildren<BackstabAimIndicator>(true);
        if (ctrl != null) ctrl.Hide();
        else _aimInstance.SetActive(false);
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
    /// 1. 窗口正常结束(EnemyBeatIndicator.OnWindowPassed);2. 背刺命中帧(PlayerBackstabState.OnBackstabHitFrame)。
    /// 拖了 aimPrefab = 收起标识实例并恢复挂点初始态。</summary>
    public void Hide()
    {
        if (aimPrefab != null)
        {
            // ── 模式①:挂点 + prefab 槽(背刺标识)──
            HideAimInstance();
            if (flashSprite != null) flashSprite.enabled = _spriteInitiallyEnabled;   // 恢复旧闪点初始显隐
            if (!autoSubscribe)
                gameObject.SetActive(false);   // 普通 enemy 挂点:收起后恢复隐藏(与 ResetToInitial 同策略)
            else
                gameObject.SetActive(_initiallyActive);
            return;
        }
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
