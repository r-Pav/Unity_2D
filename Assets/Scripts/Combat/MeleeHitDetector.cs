using UnityEngine;

/// <summary>
/// 近战命中检测工具 — Player 和 Enemy 共用。
/// </summary>
public static class MeleeHitDetector
{
    public static Collider2D[] Detect(MeleeRangeIndicator indicator, LayerMask mask)
    {
        if (indicator == null) return new Collider2D[0];
        return Physics2D.OverlapBoxAll(indicator.Center, indicator.Size, 0f, mask);
    }
}
