using UnityEngine;

/// <summary>
/// 音频库(ScriptableObject)— 全局音效配置单点。
/// 所有系统音效(UI / 玩家 / 战斗 / 技能 / 敌人 / 场景)的 clip 引用集中在本资产,
/// 各场景的 AudioManager 实例引同一个资产,配置只维护一处,
/// 后续加音效不碰场景、不碰挂点。
/// 新增音效分组时在本类加 [Header] 分组字段,AudioManager 从本资产取值;
/// 本类不预加空分组字段,等有素材再加。
/// </summary>
[CreateAssetMenu(fileName = "AudioLibrary", menuName = "Audio/Audio Library")]
public class AudioLibrary : ScriptableObject
{
    // ============================================================
    // UI 音效
    // ============================================================

    [Header("UI 音效")]

    /// <summary>按钮悬停音 — UIButtonFeedback 指针进入时播(素材:UI/悬停.flac)</summary>
    [Tooltip("按钮悬停音(素材:UI/悬停.flac)")]
    public AudioClip uiHover;

    /// <summary>按钮点击音 — 所有默认按钮按下时播(素材:UI/Ok.flac)</summary>
    [Tooltip("按钮点击音(素材:UI/Ok.flac)")]
    public AudioClip uiClick;

    /// <summary>面板关闭音 — PanelManager 关闭面板时播一次(素材:UI/cancel.flac)</summary>
    [Tooltip("面板关闭音(素材:UI/cancel.flac)")]
    public AudioClip uiClose;

    /// <summary>面板打开 / 页签切换音 — PanelManager 打开面板时播(素材待补,留空 = 该类静默)</summary>
    [Tooltip("面板打开 / 页签切换音(素材待补,留空 = 该类静默)")]
    public AudioClip uiOpen;

    /// <summary>UI 音效相对音量 — 最终响度 = SFX 组音量 × 该值</summary>
    [Tooltip("UI 音效相对音量(最终响度 = SFX 组音量 × 该值)")]
    [Range(0f, 1f)] public float uiVolume = 1f;
}
