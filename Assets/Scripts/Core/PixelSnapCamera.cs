using UnityEngine;

/// <summary>
/// 相机像素对齐(正式版,2026-09-03 由临时验证转正):
/// 消除 2D 瓦片接缝在相机移动时的闪线/爬纹。
/// 原理:Cinemachine Brain(LateUpdate 执行顺序 1000)每帧把 VCam 状态写入 Main Camera,
/// 相机位置是连续小数 → 16px Point 纹理的纹素与屏幕像素相位持续漂移,
/// 瓦片接缝处高对比边缘被周期性点亮 → 走路闪白线。
/// 本组件执行顺序 2000(晚于 Brain),把相机最终位置 snap 到"1 屏幕像素对应的世界格",
/// 相位不再漂移,接缝线静止不闪。
///
/// 用法:挂 Main Camera(与 CinemachineBrain 同物体),Inspector 勾选"像素对齐"。
/// 兼容:ortho 动态变化(过场缩放)时 px 每帧重算;震屏/位移幅度远大于像素格,
/// 量化误差 <0.5 屏幕像素,肉眼无感;BossVCam/区域相机切换都走 Brain,统一生效。
/// 注意:只 snap 平移,不动 z(-10)与旋转。
/// </summary>
[DefaultExecutionOrder(2000)]
public class PixelSnapCamera : MonoBehaviour
{
    [Header("像素对齐")]
    [Tooltip("关闭 = 相机恢复连续小数跟随(接缝闪线会回来)")]
    [SerializeField] private bool pixelSnapEnabled = true;

    private Camera _cam;

    private void Awake()
    {
        _cam = GetComponent<Camera>();
    }

    private void LateUpdate()
    {
        if (!pixelSnapEnabled || _cam == null) return;

        // 1 屏幕像素 = 2*ortho / 屏幕高(世界单位);相机位置取整到像素格
        float px = 2f * _cam.orthographicSize / Screen.height;
        if (px <= 0f) return;

        Vector3 p = transform.position;
        p.x = Mathf.Round(p.x / px) * px;
        p.y = Mathf.Round(p.y / px) * px;
        transform.position = p;
    }
}
