using UnityEngine;

/// <summary>
/// 近战攻击范围指示器 — 挂在攻击范围子 GameObject 上
/// Transform（Position + Scale）就是攻击范围，攻击脚本直接读范围做检测
/// </summary>
public class MeleeRangeIndicator : MonoBehaviour
{
    [Header("组件引用")]
    [Tooltip("攻击范围的 SpriteRenderer（拖入自身）")]
    [SerializeField] private SpriteRenderer rangeSprite;

    [Header("闪烁")]
    [Tooltip("闪烁颜色")]
    [SerializeField] private Color flashColor = Color.white;
    [Tooltip("闪烁时长（秒）")]
    [SerializeField] private float flashDuration = 0.15f;

    private Color _originalColor;
    private Coroutine _flashRoutine;

    /// <summary>世界空间攻击矩形中心</summary>
    public Vector2 Center => transform.position;
    /// <summary>世界空间攻击矩形尺寸（取 Sprite 实际渲染大小）</summary>
    public Vector2 Size => rangeSprite != null
        ? new Vector2(rangeSprite.bounds.size.x, rangeSprite.bounds.size.y)
        : new Vector2(Mathf.Abs(transform.lossyScale.x), Mathf.Abs(transform.lossyScale.y));

    private void Awake()
    {
        if (rangeSprite != null)
            _originalColor = rangeSprite.color;
    }

    private void OnEnable()
    {
        if (rangeSprite != null)
            rangeSprite.color = _originalColor;
    }

    /// <summary>触发攻击闪烁</summary>
    public void Flash()
    {
        if (rangeSprite == null) return;
        if (!gameObject.activeInHierarchy) return;
        if (_flashRoutine != null)
            StopCoroutine(_flashRoutine);
        _flashRoutine = StartCoroutine(FlashRoutine());
    }

    private System.Collections.IEnumerator FlashRoutine()
    {
        rangeSprite.color = flashColor;
        yield return new WaitForSeconds(flashDuration);
        rangeSprite.color = _originalColor;
        _flashRoutine = null;
    }
}
