using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Video;

/// <summary>
/// 主菜单视频背景(2026-09-11,单视频循环版)。
/// 一个视频循环播放:VideoPlayer 内部回绕(isLooping),同文件循环是无缝的,不需要黑场也不需要过渡。
/// 画面输出到指定的 RenderTexture,再由全屏 RawImage 显示,整个视频层在 UI 层内。
/// 视频不出声(Audio Output 关掉),音乐走 BgmLoopPlayer,互不干涉。
/// 没拖视频或没拖 RT 时不显示视频层,露出底下的原背景图;准备完成后再显示,避免闪黑帧。
///
/// 用法:挂在带 VideoPlayer 的物体上,Target 拖全屏 RawImage、Rt 拖 RenderTexture、Clip 拖素材。
/// </summary>
[RequireComponent(typeof(VideoPlayer))]
public class MenuVideoBackground : MonoBehaviour
{
    [Header("引用")]
    [Tooltip("显示视频的全屏 RawImage")]
    [SerializeField] private RawImage target;

    [Tooltip("视频输出的 RenderTexture(Assets 里的 .renderTexture 资产)")]
    [SerializeField] private RenderTexture rt;

    [Tooltip("循环播放的视频")]
    [SerializeField] private VideoClip clip;

    private VideoPlayer _player;

    private void Awake()
    {
        _player = GetComponent<VideoPlayer>();

        if (target != null)
        {
            target.texture = rt;
            target.enabled = false;   // 准备完成后再显示,避免露出未初始化的黑帧
        }

        if (_player == null)
            return;

        _player.playOnAwake = false;                       // 起播由本脚本控制
        _player.isLooping = true;                          // 单视频无缝循环
        _player.audioOutputMode = VideoAudioOutputMode.None;
        _player.renderMode = VideoRenderMode.RenderTexture;
        _player.targetTexture = rt;
        _player.prepareCompleted += OnPrepared;
    }

    private void Start()
    {
        if (_player == null || rt == null || clip == null)
        {
            Debug.LogWarning("[MenuVideoBackground] RT 或视频没拖齐,视频背景不播放", this);
            return;
        }

        _player.source = VideoSource.VideoClip;   // 显式声明片源模式,防止 Inspector 被改成 URL
        _player.clip = clip;
        _player.Prepare();   // 准备好后由 OnPrepared 起播并显示
    }

    private void OnDestroy()
    {
        if (_player != null)
        {
            _player.prepareCompleted -= OnPrepared;
            if (_player.isPlaying)
                _player.Stop();
        }
    }

    private void OnPrepared(VideoPlayer source)
    {
        if (target != null)
        {
            target.texture = rt;
            target.enabled = true;
        }
        source.Play();
    }
}
