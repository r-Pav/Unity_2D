using System.Collections.Generic;
using Coffee.UIExtensions;
using UnityEngine;

/// <summary>
/// 鼠标拖尾锚点:每帧把鼠标屏幕坐标写进自己的 anchoredPosition,让挂在它下面的 UIParticle 粒子跟着鼠标走。
/// 挂在 Canvas 下、和 UIParticle 同一个 RectTransform 上(粒子作为子物体,localPosition 保持 0)。
/// 只做坐标跟随,不碰粒子参数(粒子在 prefab 上调)。
/// 锚点物体保持居中锚(新建 UI 物体默认就是),anchoredPosition 才是相对父级的正确坐标。
/// </summary>
[DisallowMultipleComponent]
public class MouseTrailFollower : MonoBehaviour
{
    [Tooltip("跟随鼠标的 UI 物体,留空 = 本物体")]
    [SerializeField] private RectTransform followTarget;

    [Tooltip("坐标参照的父级,留空 = followTarget 的父级(必须在 Canvas 下)")]
    [SerializeField] private RectTransform referenceRect;

    [Tooltip("为假时只在启用那一下摆一次,之后不动(给别的界面接管留口子)")]
    [SerializeField] private bool followEveryFrame = true;

    private Canvas _canvas;
    private RectTransform _self;

    private void Awake()
    {
        _self = followTarget != null ? followTarget : transform as RectTransform;
        _canvas = GetComponentInParent<Canvas>();
        if (referenceRect == null && _self != null)
            referenceRect = _self.parent as RectTransform;

        // anchoredPosition 语义:锚点在中心才等于"相对父级中心的偏移"
        if (_self != null && _self.anchorMin != _self.anchorMax)
            _self.anchorMin = _self.anchorMax = new Vector2(0.5f, 0.5f);
    }

    private void OnEnable()
    {
        UpdatePosition();
    }

    private void LateUpdate()
    {
        if (!followEveryFrame) return;
        UpdatePosition();
    }

    private void Start()
    {
        // UIParticle 的 Particles 列表里存的可能是预制体资产里的那份粒子(不是场景里的实例)。
        // 那样烘焙作用在世界原点的资产对象上,和跟着鼠标走的烘焙相机差出去几千单位,超出烘焙视野,
        // 网格就是空的,表现是"完全不显示"。这里用场景层级里的实例重建一次列表,资产对象用 scene.IsValid() 滤掉。
        UIParticle uip = GetComponent<UIParticle>();
        if (uip == null) return;

        List<ParticleSystem> sceneParticles = new List<ParticleSystem>();
        foreach (ParticleSystem ps in GetComponentsInChildren<ParticleSystem>(true))
        {
            if (ps == null || !ps.gameObject.scene.IsValid()) continue;

            // 拖尾材质槽(材质数组第 2 项)被清空时,拖尾会拿空材质画成紫色,这里补回主材质
            ParticleSystemRenderer rend = ps.GetComponent<ParticleSystemRenderer>();
            if (rend != null && rend.sharedMaterial != null && rend.trailMaterial == null)
                rend.trailMaterial = rend.sharedMaterial;

            sceneParticles.Add(ps);
        }

        if (sceneParticles.Count == 0)
        {
            Debug.LogWarning("[MouseTrailFollower] 场景里没找到粒子系统,UIParticle 不会显示,检查粒子预制体是不是拖进来了", this);
            return;
        }

        uip.particles.Clear();
        uip.particles.AddRange(sceneParticles);
        uip.RefreshParticles(uip.particles);
    }

    private void UpdatePosition()
    {
        if (_self == null || referenceRect == null) return;

        // 屏幕空间覆盖模式(Overlay)相机会传 null,传了反而算错
        Camera cam = (_canvas != null && _canvas.renderMode != RenderMode.ScreenSpaceOverlay)
            ? _canvas.worldCamera
            : null;

        if (RectTransformUtility.ScreenPointToLocalPointInRectangle(
                referenceRect, Input.mousePosition, cam, out Vector2 local))
        {
            _self.anchoredPosition = local;
        }
    }
}
