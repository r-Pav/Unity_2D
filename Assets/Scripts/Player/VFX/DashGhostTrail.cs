using System.Collections.Generic;
using DG.Tweening;
using UnityEngine;

/// <summary>
/// 冲刺残影 DashGhostTrail(待办1)— 运行时克隆玩家视觉物体,冲刺时沿路径留残影。
///
/// 每次 SpawnOnce 新建独立 GameObject + SpriteRenderer(世界根,无父级),只拷贝渲染数据
/// (当前帧 sprite/flip/排序/共享材质/含负号 lossyScale),不克隆整棵 Anim → 克隆体不会自己播动画。
/// 残影 = sourceSprite 的精确世界副本:玩家怎么渲染残影就怎么渲染(含朝左负 scale 镜像),
/// 半透明原地淡出后 Destroy。材质复用玩家本体材质(双面渲染 + PNG alpha),不需要独立残影材质。
///
/// 触发:PlayerDashState 按 冲刺时长 ÷ GhostsPerDash 的间隔节奏调 SpawnOnce(无 Update 轮询)。
/// 接线(编辑器):组件挂 Player 物体,sourceSprite 拖 Anim 的 SpriteRenderer。
/// </summary>
public class DashGhostTrail : MonoBehaviour
{
    [Header("来源")]
    [Tooltip("玩家视觉当前帧来源 SpriteRenderer(拖 Anim 上的 SpriteRenderer;残影逐帧拷贝它的 sprite/形态)")]
    [SerializeField] private SpriteRenderer sourceSprite;

    [Header("残影参数")]
    [Tooltip("单次冲刺的残影总数;PlayerDashState 按 冲刺时长 ÷ 残影数 自动算间隔,均匀铺满冲刺路径")]
    [SerializeField] private int ghostsPerDash = 3;

    [Tooltip("残影出生透明度(越小越淡;透明度逐克隆体用 SpriteRenderer.color 控制,材质本身不变)")]
    [SerializeField] private float startAlpha = 0.5f;

    [Tooltip("淡出时长(秒);长 = 拖尾余韵久")]
    [SerializeField] private float fadeDuration = 0.25f;

    [Tooltip("可选染色,默认白(只取 rgb,alpha 由 startAlpha/淡出接管)")]
    [SerializeField] private Color tint = Color.white;

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

    /// <summary>单次冲刺残影总数(PlayerDashState 按 冲刺时长 ÷ 此值 算生成间隔)</summary>
    public int GhostsPerDash => ghostsPerDash;

    /// <summary>
    /// 生成一个残影:运行时克隆独立 GameObject+SpriteRenderer(只拷贝渲染数据),
    /// 半透明原地淡出后销毁。空引用安全:sourceSprite 未拖 → 静默 return。
    /// </summary>
    public void SpawnOnce()
    {
        if (sourceSprite == null)
            return; // 空引用安全:来源未拖 → 静默跳过

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

        Transform sourceT = sourceSprite.transform;

        // ── 克隆:只建渲染器,不克隆整棵 Anim(避免 Animator/脚本/子物体垃圾,克隆体不会自己播动画)──
        // new GameObject 默认无父级 = 出生即在世界根(天然独立于移动中的 Player,无需父级搬运)
        var go = new GameObject("DashGhost");
        SpriteRenderer sr = go.AddComponent<SpriteRenderer>();
        go.transform.SetPositionAndRotation(sourceT.position, sourceT.rotation);

        // 克隆体 = sourceSprite 的精确世界副本:玩家怎么渲染,残影就怎么渲染,方向零手动处理。
        // 材质复用 sourceSprite.sharedMaterial(玩家本体材质,双面渲染 + PNG 透明通道支持 alpha;
        // 不用独立残影材质——URP/Unlit Cull=Back 会把负 scale(朝左)的残影整面剔除,玩家材质没有此问题)。
        // scale 用 lossyScale 原样(含负号):玩家朝左靠父链负 scale 镜像,克隆体同款负 scale 同款镜像。
        Vector3 lossy = sourceT.lossyScale;
        go.transform.localScale = lossy;

        // 拷贝当前帧渲染数据:sprite/flip/排序层级/材质全部与 source 一致
        sr.sprite = sourceSprite.sprite;
        sr.flipX = sourceSprite.flipX;
        sr.flipY = sourceSprite.flipY;
        sr.sortingLayerID = sourceSprite.sortingLayerID;
        sr.sortingOrder = sourceSprite.sortingOrder;
        sr.sharedMaterial = sourceSprite.sharedMaterial;

        // 出生透明度(tint 只取 rgb;alpha 固定 startAlpha,靠玩家材质的顶点色 alpha 生效)
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
