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
    private Vector3 _originalPos;  // 摆放的原始世界位置(Awake 缓存,每次激活重置回这里)
    private Vector3 _anchorPos;    // 本层锚点(激活时 = 原始位置)
    private Vector3 _camOrigin;    // 激活时相机位置基准
    private bool _frozen;          // 冻结:不再跟随相机视差(进管道时旧场景背景停住)
    private bool _hasBounds;       // 是否有移动边界(管道切换时由 ZoneManager 设置)
    private float _minX, _maxX;    // 背景 x 移动范围 [本侧入口, 对侧出口]

    /// <summary>
    /// 设置背景移动边界(动态,每次管道切换由 ZoneManager 传入):
    /// 背景视差只在本侧入口 ↔ 对侧管道出口之间移动,不出场景地盘。
    /// </summary>
    public void SetBounds(float minX, float maxX)
    {
        _minX = Mathf.Min(minX, maxX);
        _maxX = Mathf.Max(minX, maxX);
        _hasBounds = true;
    }

    /// <summary>清除边界(地区隐藏时重置,下次激活重新设置)</summary>
    public void ClearBounds()
    {
        _hasBounds = false;
    }

    /// <summary>冻结/解冻本层视差:冻结时 LateUpdate 不再更新位置(旧场景背景停住,配合淡出消融)</summary>
    public void SetFrozen(bool frozen)
    {
        _frozen = frozen;
    }

    /// <summary>重置回摆放原始位置并停用位置计算(固定原位置,不再视差)——ZoneManager.ShowArea 调用</summary>
    public void ResetToOriginalAndDisable()
    {
        transform.position = _originalPos;
        _frozen = true;
        enabled = false; // 完全停用:不执行 LateUpdate,位置永不变
    }

    private void Awake()
    {
        // 缓存摆放的原始位置:ParallaxLayer 每帧改 transform.position(视差),
        // 若 OnEnable 锚定"当前漂移位置"会越跑越偏(地区切换后背景不在设置的位置)。
        // 必须以 Awake 的原始摆放位置为锚,每次激活重置回去。
        _originalPos = transform.position;
        if (Camera.main != null)
            _cam = Camera.main.transform;
    }

    private void OnEnable()
    {
        // 每次激活(地区 ShowArea 显示本层)重置回原始摆放位置再锚定:
        // 不被上次视差计算留下的漂移位置污染,背景永远从设置的位置开始视差
        transform.position = _originalPos;
        _anchorPos = _originalPos;
        _frozen = false; // 激活默认解冻(跟随视差)
        if (_cam != null) _camOrigin = _cam.position;
    }

    private void LateUpdate()
    {
        if (_cam == null || _frozen) return; // 冻结:保持当前位置(旧场景背景停住)

        Vector3 delta = _cam.position - _camOrigin;
        Vector3 pos = _anchorPos + delta * (1f - factor);
        if (horizontalOnly)
            pos.y = _anchorPos.y;

        // 移动边界 clamp:背景只在 [本侧入口, 对侧出口] 之间视差移动,
        // 相机走再远背景也不出场景地盘(动态边界,由管道切换时设置)
        if (_hasBounds)
            pos.x = Mathf.Clamp(pos.x, _minX, _maxX);

        transform.position = pos;
    }
}
