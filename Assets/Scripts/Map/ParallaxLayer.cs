using UnityEngine;

/// <summary>
/// 视差背景层 — 挂各地区背景层（Bg_Far / Bg_Mid / Bg_Near）上。
/// LateUpdate 里按 factor 缩放相机位移量，营造纵深。
///
/// factor 语义（视觉移动速度比例）：
///   0   = 视觉完全静止（无限远，贴屏幕）—— 远景山/天空
///   0.5 = 视觉半速 —— 中景树林/建筑
///   1   = 与地面同速（贴地）—— 近景装饰/地面
///
/// 位置公式：pos = 锚点 + (相机位置 - 相机起点) × (1 - factor)
/// 地区显隐由 ZoneManager(ShowArea/HideArea) 控制：OnEnable 重新锚定，
/// 每次进入地区从摆放位置开始计算，无需额外管理。
/// </summary>
public class ParallaxLayer : MonoBehaviour
{
    [Header("视差")]
    [Tooltip("视觉移动速度比例：0=固定不动(最远/贴屏幕)，0.5=半速(中景)，1=与地面同速(最近)")]
    [SerializeField, Range(0f, 1f)] private float factor = 0.5f;

    [Tooltip("仅水平方向视差（横版卷轴默认 true；false = XY 都视差）")]
    [SerializeField] private bool horizontalOnly = true;

    private Transform _cam;
    private Vector3 _anchorPos;   // 本层摆放的初始世界位置（锚点）
    private Vector3 _camOrigin;   // 激活时相机位置基准

    private void Awake()
    {
        if (Camera.main != null)
            _cam = Camera.main.transform;
    }

    private void OnEnable()
    {
        // 每次激活（地区 ShowArea 显示本层）重新锚定：基准=当前摆放位置 + 当前相机位置
        _anchorPos = transform.position;
        if (_cam != null) _camOrigin = _cam.position;
    }

    private void LateUpdate()
    {
        if (_cam == null) return;

        Vector3 delta = _cam.position - _camOrigin;
        Vector3 pos = _anchorPos + delta * (1f - factor);
        if (horizontalOnly)
            pos.y = _anchorPos.y;

        transform.position = pos;
    }
}
