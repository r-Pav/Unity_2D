using System.Collections.Generic;
using DG.Tweening;
using UnityEngine;

/// <summary>
/// 冲刺残影 DashGhostTrail v2(待办1)— 运行时克隆玩家视觉物体,冲刺时沿路径留残影。
///
/// 背景:v1(预生成 SpriteRenderer 数组 + 运行时脱离父级 + lossyScale 手动翻转)
/// 被 saika 实测否决:朝左无残影 + 材质反复出问题,已作废。v2 改为运行时克隆:每次 SpawnOnce
/// 新建一个独立 GameObject + SpriteRenderer(出生即在世界根,无父级),只拷贝渲染所需数据
/// (当前帧 sprite/flip/排序/共享材质),不克隆整棵 Anim → 克隆体不会自己播动画,只显示生成
/// 瞬间那一帧,半透明原地淡出后 Destroy。
///
/// 翻转(修朝左 bug):实际渲染是否镜像 = 自身 flipX XOR 父链 scale.x 为负。克隆体自身 scale
/// 恒正(取 lossyScale 绝对值保尺寸),镜像全靠 flipX 表达 → 朝右/朝左天然一致,不再依赖父级
/// scale 翻转(杜绝 v1 朝左无残影)。
///
/// 触发:PlayerDashState 按 SpawnInterval 节奏调 SpawnOnce(无 Update 轮询)。
/// 接线(编辑器):组件挂 Player 物体(sourceSprite 拖 Anim 的 SpriteRenderer;
/// ghostMaterial 拖 DashGhostMat,URP Transparent 材质,不代码 new,规避 URP shader 名/构建剔除坑)。
/// </summary>
public class DashGhostTrail : MonoBehaviour
{
    [Header("来源")]
    [Tooltip("玩家视觉当前帧来源 SpriteRenderer(拖 Anim 上的 SpriteRenderer;残影逐帧拷贝它的 sprite/形态)")]
    [SerializeField] private SpriteRenderer sourceSprite;

    [Tooltip("残影共享透明材质(拖 DashGhostMat;不代码建材质)")]
    [SerializeField] private Material ghostMaterial;

    [Header("残影参数")]
    [Tooltip("残影生成间隔(秒);0.15s 冲刺 0.05s ≈ 3 个,越小越密")]
    [SerializeField] private float spawnInterval = 0.05f;

    [Tooltip("残影出生透明度(越小越淡;透明度逐克隆体用 SpriteRenderer.color 控制,材质本身不变)")]
    [SerializeField] private float startAlpha = 0.5f;

    [Tooltip("淡出时长(秒);长 = 拖尾余韵久")]
    [SerializeField] private float fadeDuration = 0.25f;

    [Tooltip("可选染色,默认白(只取 rgb,alpha 由 startAlpha/淡出接管)")]
    [SerializeField] private Color tint = Color.white;

    [Tooltip("同时存活残影上限(防对象堆积;满了丢弃本次生成,旧的自然淡完销毁)")]
    [SerializeField] private int maxGhosts = 6;

    /// <summary>存活克隆体 → 各自淡出 tween(克隆体淡完 OnComplete 自 Destroy,条目留待下次生成前清理)</summary>
    private readonly Dictionary<GameObject, Tween> _ghostTweens = new();

    /// <summary>清理循环复用缓冲(收集已销毁克隆体键,字典迭代中不能直接删)</summary>
    private readonly List<GameObject> _deadKeys = new();

    private void OnDestroy()
    {
        // 组件/场景卸载时统一收尾:先 Kill 未完成淡出 —— 通用 DOTween.To 不绑定目标对象,
        // 不 Kill 会在目标销毁后继续回调(空引用/报错);再销毁仍存活的克隆体(独立物体不随
        // Player 走,防泄漏到场景),最后清列表
        foreach (Tween tween in _ghostTweens.Values)
        {
            if (tween != null && tween.IsActive())
                tween.Kill();
        }
        foreach (GameObject go in _ghostTweens.Keys)
        {
            if (go != null)
                Destroy(go);
        }
        _ghostTweens.Clear();
    }

    /// <summary>残影生成间隔(PlayerDashState 按此累计节奏调 SpawnOnce)</summary>
    public float SpawnInterval => spawnInterval;

    /// <summary>
    /// 生成一个残影:运行时克隆独立 GameObject+SpriteRenderer(只拷贝渲染数据),
    /// 半透明原地淡出后销毁。空引用安全:sourceSprite/ghostMaterial 未拖 → 静默 return。
    /// </summary>
    public void SpawnOnce()
    {
        if (sourceSprite == null || ghostMaterial == null)
            return; // 空引用安全:来源/材质未拖 → 静默跳过

        // 存活管理:先清理已销毁的克隆体(淡完 OnComplete Destroy 后键为 Unity null)
        _deadKeys.Clear();
        foreach (GameObject deadGo in _ghostTweens.Keys)
        {
            if (deadGo == null)
                _deadKeys.Add(deadGo);
        }
        if (_deadKeys.Count > 0)
        {
            foreach (GameObject deadGo in _deadKeys)
                _ghostTweens.Remove(deadGo);
            _deadKeys.Clear();
        }

        // 存活上限:当前活动克隆体 >= maxGhosts → 丢弃本次生成(旧的自然淡完销毁)
        if (_ghostTweens.Count >= maxGhosts)
            return;

        Transform sourceT = sourceSprite.transform;

        // ── 克隆:只建渲染器,不克隆整棵 Anim(避免 Animator/脚本/子物体垃圾,克隆体不会自己播动画)──
        // new GameObject 默认无父级 = 出生即在世界根(天然独立于移动中的 Player,无需父级搬运)
        var go = new GameObject("DashGhost");
        SpriteRenderer sr = go.AddComponent<SpriteRenderer>();
        go.transform.SetPositionAndRotation(sourceT.position, sourceT.rotation);

        // 尺寸:玩家 Anim scale=1.4 → 克隆体 scale = lossyScale 绝对值(|lossyScale.x|, |lossyScale.y|, 1),
        // 保证渲染尺寸一致;自身 scale 恒正(不引入负缩放镜像,镜像交给 flipX 表达)
        Vector3 lossy = sourceT.lossyScale;
        go.transform.localScale = new Vector3(Mathf.Abs(lossy.x), Mathf.Abs(lossy.y), 1f);

        // 拷贝当前帧渲染数据:sprite/flip/排序层级与 source 一致;材质共享 ghostMaterial(透明度用克隆体 color 控制)
        sr.sprite = sourceSprite.sprite;
        sr.sortingLayerID = sourceSprite.sortingLayerID;
        sr.sortingOrder = sourceSprite.sortingOrder;
        sr.material = ghostMaterial;

        // 翻转(关键,修朝左 bug):source 实际渲染是否镜像 = 自身 flipX(自身翻转) XOR 父链 scale.x 为负。
        // 克隆体无父级负缩放,镜像全靠 flipX 表达 → 朝右/朝左天然正确,不再依赖父级 scale
        bool parentNegScaleX = lossy.x < 0f; // 父链负缩放(Anim 根/中途节点 scale.x < 0)
        sr.flipX = sourceSprite.flipX ^ parentNegScaleX; // XOR:负缩放会抵消一次 flipX
        sr.flipY = sourceSprite.flipY;

        // 出生透明度(tint 只取 rgb;alpha 固定 startAlpha)
        sr.color = new Color(tint.r, tint.g, tint.b, startAlpha);

        // 淡出 alpha → 0,完成后销毁克隆体。
        // 用核心 DOTween.To 而非 SpriteRenderer.DOFade:项目 DOTween 以 DOTWEEN_NOSPRITES 编译,sprite 模块扩展不可用
        Tween fade = DOTween.To(
                () => sr.color.a,
                a =>
                {
                    Color c = sr.color;
                    c.a = a;
                    sr.color = c;
                },
                0f, fadeDuration)
            .OnComplete(() =>
            {
                if (go != null)
                    Destroy(go);
            });
        _ghostTweens.Add(go, fade);
    }
}
