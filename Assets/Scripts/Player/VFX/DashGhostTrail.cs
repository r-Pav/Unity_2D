using System.Collections.Generic;
using DG.Tweening;
using UnityEngine;

/// <summary>
/// 冲刺残影(待办1)— 冲刺时沿路径留残影。
/// 设计(saika 拍板):淡出式残影、取当前帧玩家 sprite 减透明度(alpha startAlpha→0)、
/// 最多 3 个残影循环复用、残影子物体预生成(编辑器摆好,初始 inactive,运行时绝不 Instantiate)。
///
/// 无 Update 轮询:SpawnOnce 由 PlayerDashState 按 spawnInterval 节奏调用;
/// 残影生成后独立停留在生成位置(脱离移动父级,见 SpawnOnce 注释),玩家继续移动 = 路径拖影。
///
/// 接线(编辑器):挂玩家角色 sprite 所在 GameObject;sourceSprite 拖玩家 SpriteRenderer;
/// ghosts 拖 3 个预生成残影子物体的 SpriteRenderer(材质透明由 saika 编辑器建)。
/// </summary>
public class DashGhostTrail : MonoBehaviour
{
    [Header("来源")]
    [Tooltip("玩家角色当前帧来源 SpriteRenderer(拖玩家视觉主体;残影逐帧拷贝它的 sprite/形态)")]
    [SerializeField] private SpriteRenderer sourceSprite;

    [Tooltip("残影 SpriteRenderer 引用(3 个预生成残影子物体,初始 inactive;元素 null 视同不可用)")]
    [SerializeField] private SpriteRenderer[] ghosts;

    [Header("残影参数")]
    [Tooltip("残影生成间隔(秒);0.15s 冲刺 ≈ 3 个(0.03~0.05 范围,越小越密)")]
    [SerializeField] private float spawnInterval = 0.05f;

    [Tooltip("残影出生透明度(当前帧减透明度 = 半透明;越小越淡)")]
    [SerializeField] private float startAlpha = 0.5f;

    [Tooltip("淡出时长(秒);长 = 拖尾余韵久")]
    [SerializeField] private float fadeDuration = 0.25f;

    [Tooltip("可选染色,默认白(只取 rgb,alpha 由 startAlpha/淡出接管)")]
    [SerializeField] private Color tint = Color.white;

    /// <summary>每个残影当前进行的淡出 tween(复用同一残影前先 Kill 旧的,防 alpha 残留/重复回调)</summary>
    private readonly Dictionary<SpriteRenderer, Tween> _ghostTweens = new();

    private void OnDestroy()
    {
        // 场景卸载/组件销毁时清掉未完成残影淡出:通用 DOTween.To 不绑定目标对象,
        // 不手动 Kill 会在目标销毁后继续回调(空引用/报错),这里统一收尾
        foreach (Tween tween in _ghostTweens.Values)
        {
            if (tween != null && tween.IsActive())
                tween.Kill();
        }
        _ghostTweens.Clear();
    }

    /// <summary>残影生成间隔(PlayerDashState 按此累计节奏调 SpawnOnce)</summary>
    public float SpawnInterval => spawnInterval;

    /// <summary>
    /// 生成一个残影:找空闲残影(非 activeSelf 的第一个;全在用 = 3 个上限,静默跳过),
    /// 拷贝当前帧玩家外观后激活,再淡出到 alpha 0 后 SetActive(false),等待下次复用。
    /// 空引用安全:sourceSprite/ghosts 未拖或元素 null 时静默 return,不报错。
    /// </summary>
    public void SpawnOnce()
    {
        if (sourceSprite == null || ghosts == null || ghosts.Length == 0)
            return; // 空引用安全:来源/残影数组未拖 → 静默跳过

        // 找空闲残影:ghosts 中非 activeSelf 的第一个(null 视同不可用跳过)
        int idleIndex = -1;
        for (int i = 0; i < ghosts.Length; i++)
        {
            if (ghosts[i] != null && !ghosts[i].gameObject.activeSelf)
            {
                idleIndex = i;
                break;
            }
        }
        if (idleIndex < 0)
            return; // 3 个残影全在用 → 静默跳过(不覆盖,即静默上限)

        SpriteRenderer ghost = ghosts[idleIndex];

        // 复用同一残影前先 Kill 旧淡出 tween(淡出中途再次生成:防残留,从头淡出)
        if (_ghostTweens.TryGetValue(ghost, out Tween oldTween))
        {
            if (oldTween != null && oldTween.IsActive())
                oldTween.Kill();
            _ghostTweens.Remove(ghost);
        }

        // ── 拷贝当前帧玩家形态 ──
        // sprite/flip/排序层级与 source 一致(材质不拷贝:残影用自己的透明材质,由 saika 编辑器指定)
        ghost.sprite = sourceSprite.sprite;
        ghost.flipX = sourceSprite.flipX;
        ghost.sortingLayerID = sourceSprite.sortingLayerID;
        ghost.sortingOrder = sourceSprite.sortingOrder;

        // 位置/旋转/缩放取 source 当前值(受击形变/缩放时残影跟随当前形态)。
        // 关键:若残影子物体挂在移动中的玩家层级下,会被父级带着走 → 先脱离到世界根
        // (worldPositionStays,生成瞬间不跳变;运行时脱离,编辑器/场景文件不受影响),
        // 此后玩家继续移动,残影独立停在生成位置原地淡出 = 路径拖影。
        Transform ghostT = ghost.transform;
        if (ghostT.parent != null)
            ghostT.SetParent(null);
        Transform sourceT = sourceSprite.transform;
        ghostT.SetPositionAndRotation(sourceT.position, sourceT.rotation);
        ghostT.localScale = sourceT.lossyScale; // 脱离后 localScale==世界缩放,与 source 渲染尺寸一致

        // 激活并设出生透明度(tint 只取 rgb;alpha 固定 startAlpha)
        ghost.gameObject.SetActive(true);
        ghost.color = new Color(tint.r, tint.g, tint.b, startAlpha);

        // 淡出 alpha → 0,完成后关闭残影(下次生成复用同一物体)
        // 用核心 DOTween.To 而非 SpriteRenderer.DOFade:项目 DOTween 以 DOTWEEN_NOSPRITES 编译,sprite 模块扩展不可用
        Tween fade = DOTween.To(
                () => ghost.color.a,
                a =>
                {
                    Color c = ghost.color;
                    c.a = a;
                    ghost.color = c;
                },
                0f, fadeDuration)
            .OnComplete(() =>
            {
                if (ghost != null)
                    ghost.gameObject.SetActive(false); // 淡完关闭,等待复用
            });
        _ghostTweens[ghost] = fade;
    }

    /// <summary>全部残影立即结束淡出并关闭(可选兜底;常规流程不需要——残影各自淡出自灭)</summary>
    public void ResetAll()
    {
        if (ghosts == null)
            return;
        foreach (SpriteRenderer ghost in ghosts)
        {
            if (ghost == null)
                continue;
            if (_ghostTweens.TryGetValue(ghost, out Tween tween))
            {
                if (tween != null && tween.IsActive())
                    tween.Kill();
                _ghostTweens.Remove(ghost);
            }
            if (ghost.gameObject.activeSelf)
                ghost.gameObject.SetActive(false);
        }
    }
}
