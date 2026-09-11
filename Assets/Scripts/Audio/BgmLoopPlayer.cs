using System.Collections;
using UnityEngine;

/// <summary>
/// 带间隔的 BGM 循环播放器(2026-09-11,主菜单 BGM 用)。
/// 播放方式:启动时从 bgmClips 数组随机抽一首 → 整曲播完 → 静音 gapSeconds 秒 → 重播同一首,往复。
/// 本次运行固定播抽中那一首,下次启动重新随机;不做淡入淡出(硬切)。
/// 不用 AudioSource.loop:loop 是无缝紧接,做不到留间隔,所以用协程控制。
///
/// 用法:挂在主菜单场景(TitleScene)的一个独立空物体上,数组里拖候选曲。
/// 不要挂 AudioManager 物体上 —— 那个是 DontDestroyOnLoad 常驻,会把主菜单 BGM 带进游戏场景继续播。
/// AudioSource 运行时自建并注册进 AudioManager 的 Bgm 组,音量跟随设置面板 BGM 滑条;
/// 场景卸载时随物体销毁并自动注销音源。
/// </summary>
public class BgmLoopPlayer : MonoBehaviour
{
    [Tooltip("BGM 候选列表(启动时随机抽一首循环;空槽忽略;全空 = 不播,Console 警告一次)")]
    [SerializeField] private AudioClip[] bgmClips;

    [Tooltip("每遍播完后的静音间隔(秒),默认 2")]
    [SerializeField] private float gapSeconds = 2f;

    private AudioSource _source;
    private AudioClip _clip;       // 本次运行抽中的曲目(启动随机一次,之后固定)
    private Coroutine _routine;

    private void Start()
    {
        _source = gameObject.AddComponent<AudioSource>();
        _source.playOnAwake = false;
        _source.loop = false;        // 循环由协程接管(要留间隔),禁用 loop
        _source.spatialBlend = 0f;   // 2D:全局声,不随距离衰减

        // Start 时机在 AudioManager.Awake 之后,注册即拿到当前 BGM 音量
        if (AudioManager.Instance != null)
            AudioManager.Instance.RegisterSource(AudioManager.AudioGroup.Bgm, _source);

        _clip = PickRandomClip();
        if (_clip == null)
        {
            Debug.LogWarning("[BgmLoopPlayer] BGM 候选列表为空,不会播放", this);
            return;
        }

        _routine = StartCoroutine(LoopRoutine());
    }

    private void OnDestroy()
    {
        if (_routine != null)
            StopCoroutine(_routine);
        if (_source != null && AudioManager.Instance != null)
            AudioManager.Instance.UnregisterSource(AudioManager.AudioGroup.Bgm, _source);
    }

    /// <summary>整曲 → 静音间隔 → 整曲,无限循环(同一首)。用 Realtime 等待,不受 timeScale 影响。</summary>
    private IEnumerator LoopRoutine()
    {
        WaitForSecondsRealtime waitClip = new WaitForSecondsRealtime(_clip.length);
        WaitForSecondsRealtime waitGap = new WaitForSecondsRealtime(Mathf.Max(0f, gapSeconds));

        while (true)
        {
            _source.clip = _clip;
            _source.Play();
            yield return waitClip;   // 整曲播完(不做淡出,直接停)
            _source.Stop();
            yield return waitGap;    // 静音 gapSeconds 秒
        }
    }

    /// <summary>从数组里随机抽一首,跳过空槽。蓄水池抽样:只扫一遍、不额外分配;全空返回 null。</summary>
    private AudioClip PickRandomClip()
    {
        if (bgmClips == null || bgmClips.Length == 0)
            return null;

        AudioClip pick = null;
        int seen = 0;
        foreach (AudioClip c in bgmClips)
        {
            if (c == null)
                continue;
            seen++;
            if (Random.Range(0, seen) == 0)
                pick = c;
        }
        return pick;
    }
}
