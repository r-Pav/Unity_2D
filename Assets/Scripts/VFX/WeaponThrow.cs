using UnityEngine;

/// <summary>
/// 武器投掷:按 T 触发 → 沿路径点飞行(由快变慢)→ Dissolve 溶解消失。
/// 路径点 = 相对武器当前位置的偏移,Inspector 里拖数组,Scene 视图 Gizmos 可视化连线。
/// </summary>
public class WeaponThrow : MonoBehaviour
{
    [Header("路径(相对武器当前位置的偏移,Scene 里选中可拖)")]
    [SerializeField] private Vector3[] pathPoints =
    {
        new Vector3(1.5f, 0f, 0f),
        new Vector3(4f, 0.5f, 0f),
    };

    [Header("飞行")]
    [Tooltip("全程耗时(秒),速度会由快变慢")]
    [SerializeField] private float travelDuration = 1f;

    [Tooltip("转到路径方向的耗时(秒),保留'先横出去'的观感")]
    [SerializeField] private float rotateDuration = 0.15f;

    [Header("溶解")]
    [Tooltip("溶解消失耗时(秒)")]
    [SerializeField] private float dissolveDuration = 1.2f;

    [Tooltip("溶解材质(留空自动用 Custom/SpriteDissolve 生成)")]
    [SerializeField] private Material dissolveMaterial;

    private SpriteRenderer _sr;
    private Material _runtimeMat;
    private WeaponBreath _breath;

    private bool _throwing;
    private float _elapsed;
    private float _startRotationZ;
    private Vector3 _throwOrigin;

    private void Awake()
    {
        _sr = GetComponent<SpriteRenderer>();
        _breath = GetComponent<WeaponBreath>();
    }

    private void Update()
    {
        if (!_throwing && Input.GetKeyDown(KeyCode.T))
        {
            StartThrow();
        }

        if (!_throwing) return;

        _elapsed += Time.deltaTime;

        // 路径进度:平方缓出 → 由快变慢
        float raw = Mathf.Clamp01(_elapsed / travelDuration);
        float t = 1f - (1f - raw) * (1f - raw);

        // 沿路径取位置和切线
        Vector3 pos = GetPathPosition(t, out Vector3 tangent);
        transform.position = _throwOrigin + pos;

        // 平滑转到路径方向,保留"先横出去"的观感
        float targetZ = Mathf.Atan2(tangent.y, tangent.x) * Mathf.Rad2Deg;
        float rotProgress = Mathf.Clamp01(_elapsed / rotateDuration);
        transform.rotation = Quaternion.Euler(0f, 0f, Mathf.LerpAngle(_startRotationZ, targetZ, rotProgress));

        // 溶解消失,进度按时间
        float dissolve = Mathf.Clamp01(_elapsed / dissolveDuration);
        _runtimeMat.SetFloat("_DissolveAmount", dissolve);

        // 走完路径或完全溶解 → 销毁
        if (raw >= 1f || dissolve >= 1f)
        {
            Destroy(gameObject);
        }
    }

    // Catmull-Rom 样条插值:曲线平滑穿过每个锚点,同时算出切线方向
    private Vector3 GetPathPosition(float t, out Vector3 tangent)
    {
        int n = pathPoints.Length;
        if (n == 0)
        {
            tangent = Vector3.right;
            return Vector3.zero;
        }
        if (n == 1)
        {
            tangent = Vector3.right;
            return pathPoints[0];
        }

        float scaled = t * (n - 1);
        int i = Mathf.Clamp(Mathf.FloorToInt(scaled), 0, n - 2);
        float s = scaled - i;

        // 每段取前后各一个点作控制:首尾用端点自身重复
        Vector3 p0 = pathPoints[Mathf.Max(i - 1, 0)];
        Vector3 p1 = pathPoints[i];
        Vector3 p2 = pathPoints[i + 1];
        Vector3 p3 = pathPoints[Mathf.Min(i + 2, n - 1)];

        float s2 = s * s;
        float s3 = s2 * s;

        // 位置
        Vector3 pos = 0.5f * (
            (2f * p1) +
            (-p0 + p2) * s +
            (2f * p0 - 5f * p1 + 4f * p2 - p3) * s2 +
            (-p0 + 3f * p1 - 3f * p2 + p3) * s3
        );

        // 切线(导数)
        tangent = 0.5f * (
            (-p0 + p2) +
            2f * (2f * p0 - 5f * p1 + 4f * p2 - p3) * s +
            3f * (-p0 + 3f * p1 - 3f * p2 + p3) * s2
        );

        if (tangent.sqrMagnitude < 0.0001f) tangent = Vector3.right;
        return pos;
    }

    private void StartThrow()
    {
        _throwing = true;
        _elapsed = 0f;
        _throwOrigin = transform.position;
        _startRotationZ = transform.rotation.eulerAngles.z;

        // 脱离 Player 父级,独立飞行,保持当前世界位置
        transform.SetParent(null, true);

        // 停掉呼吸脚本,避免位置被覆盖
        if (_breath != null) _breath.enabled = false;

        // 生成运行时材质,不动项目里的材质资源
        _runtimeMat = dissolveMaterial != null
            ? new Material(dissolveMaterial)
            : new Material(Shader.Find("Custom/SpriteDissolve"));
        _runtimeMat.SetFloat("_DissolveAmount", 0f);

        if (_sr != null) _sr.material = _runtimeMat;
    }

    // 编辑器可视化:选中武器时沿曲线采样画线,拖点即见
    private void OnDrawGizmosSelected()
    {
        if (pathPoints == null || pathPoints.Length == 0) return;

        Vector3 origin = transform.position;
        Gizmos.color = new Color(1f, 0.85f, 0.3f, 0.9f);

        // 沿 Catmull-Rom 曲线采样 32 段,画出平滑曲线
        Vector3 prev = origin + GetPathPosition(0f, out _);
        for (int k = 1; k <= 32; k++)
        {
            Vector3 cur = origin + GetPathPosition(k / 32f, out _);
            Gizmos.DrawLine(prev, cur);
            prev = cur;
        }

        Gizmos.color = Color.white;
        for (int i = 0; i < pathPoints.Length; i++)
        {
            Gizmos.DrawWireSphere(origin + pathPoints[i], 0.08f);
        }
    }
}
