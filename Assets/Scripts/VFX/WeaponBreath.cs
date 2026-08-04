using UnityEngine;

/// <summary>
/// 武器待机呼吸效果：在基准位置附近做全方向轻微浮动（双正弦叠加出椭圆轨迹）。
/// 挂在武器子物体上（如 w1weapon / w1_transparent），只动自身 localPosition，不影响父级。
/// </summary>
public class WeaponBreath : MonoBehaviour
{
    [Tooltip("浮动幅度（世界单位），0.01~0.05 比较自然")]
    [SerializeField, Min(0f)] private float amplitude = 0.03f;

    [Tooltip("呼吸频率（Hz），1~2 慢呼吸，2~3 急促")]
    [SerializeField, Min(0f)] private float frequency = 1.5f;

    private Vector3 _baseLocalPos;
    private float _phaseOffset;

    private void Awake()
    {
        _baseLocalPos = transform.localPosition;
        // 随机起始相位：多把武器一起挂时不会完全同步
        _phaseOffset = Random.Range(0f, Mathf.PI * 2f);
    }

    private void Update()
    {
        float t = Time.time * frequency + _phaseOffset;
        // x/y 不同频率与相位差 → 全方向椭圆浮动，视觉上像呼吸/漂浮
        float x = Mathf.Sin(t) * amplitude;
        float y = Mathf.Sin(t * 0.5f + 1.2f) * amplitude;
        transform.localPosition = _baseLocalPos + new Vector3(x, y, 0f);
    }
}
