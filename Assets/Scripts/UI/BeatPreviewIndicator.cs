using UnityEngine;

/// <summary>
/// 音乐点预告指示器(Canvas 圆环)— 距下一点 ≤ previewLead 时显示,按剩余时间收缩,点过隐藏。
/// 表现层:激活判定读 MusicPointManager(TimeToNextPoint/PreviewLead),收缩按剩余比例每帧设 scale,不独立排程。
/// </summary>
public class BeatPreviewIndicator : MonoBehaviour
{
    [Tooltip("圆环 RectTransform(收缩目标)")]
    [SerializeField] private RectTransform ring;

    [Tooltip("激活时初始缩放(大)")]
    [SerializeField] private float startScale = 1f;

    [Tooltip("点到前最小缩放(小)")]
    [SerializeField] private float minScale = 0.1f;

    private MusicPointManager _mgr;

    private void Awake()
    {
        _mgr = MusicPointManager.Instance;
        if (ring != null) ring.gameObject.SetActive(false);
    }

    private void Update()
    {
        if (_mgr == null)
        {
            _mgr = MusicPointManager.Instance;
            if (_mgr == null) return;
        }
        if (ring == null) return;

        float remain = _mgr.TimeToNextPoint;
        bool show = _mgr.CurrentTrack != null && remain > 0f && remain <= _mgr.PreviewLead;

        if (!show)
        {
            if (ring.gameObject.activeSelf) ring.gameObject.SetActive(false);
            return;
        }

        if (!ring.gameObject.activeSelf) ring.gameObject.SetActive(true);
        float t = Mathf.Clamp01(remain / Mathf.Max(0.01f, _mgr.PreviewLead));   // 1→0
        ring.localScale = Vector3.one * Mathf.Lerp(minScale, startScale, t);
    }
}
