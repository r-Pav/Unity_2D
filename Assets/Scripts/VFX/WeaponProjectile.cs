using System.Collections;
using UnityEngine;

/// <summary>
/// 投掷武器 clone 的飞行组件(由 WeaponThrow 创建并 Init):
/// 沿 Catmull-Rom 路径飞行(由快变慢) → 溶解消失 → 自毁。
/// 视觉设置(拖尾/材质/子物体)在模板上配置,clone 继承,本组件只驱动飞行。
/// </summary>
public class WeaponProjectile : MonoBehaviour
{
    private Vector3[] _path;
    private float _travelDuration;
    private float _dissolveDuration;
    private float _easeOutPower = 2f;   // 缓出强度:越大前段甩得越快
    private Vector3 _origin;
    private SpriteRenderer _sr;
    private Material _runtimeMat;
    private bool _hasDissolve;   // 是否真的用了溶解 shader(否则只做 alpha 渐隐)

    /// <summary>由 WeaponThrow 调用:传入路径/参数,随后自动飞行+溶解+自毁。
    /// SpriteRenderer / TrailRenderer 等视觉组件由模板继承,这里只换溶解材质。</summary>
    public void Init(
        Vector3[] pathPoints,
        float travelDuration,
        float easeOutPower,
        float dissolveDuration,
        float dissolveDirection,
        Material dissolveMaterial)
    {
        _path = pathPoints;
        _travelDuration = travelDuration;
        _dissolveDuration = dissolveDuration;
        _easeOutPower = Mathf.Max(1f, easeOutPower);
        _origin = transform.position;
        _sr = GetComponent<SpriteRenderer>();

        // 溶解材质:
        // - 拖了 dissolveMaterial 字段 → 用它
        // - 否则 Shader.Find;找不到时降级:不换材质,溶解退化为 alpha 渐隐(飞行不受影响)
        if (dissolveMaterial != null)
        {
            _runtimeMat = new Material(dissolveMaterial);
        }
        else
        {
            Shader sh = Shader.Find("Custom/SpriteDissolve");
            _runtimeMat = sh != null ? new Material(sh) : null;
        }

        if (_runtimeMat != null)
        {
            _hasDissolve = _runtimeMat.HasProperty("_DissolveAmount");
            if (_hasDissolve)
            {
                _runtimeMat.SetFloat("_DissolveAmount", 0f);
                _runtimeMat.SetFloat("_DissolveDir", dissolveDirection);
            }
            // 关键:显式把 sprite 纹理绑到材质,否则 new 出来的材质 _MainTex 是内置白纹理,剑渲染不可见
            if (_sr != null && _sr.sprite != null)
            {
                _runtimeMat.mainTexture = _sr.sprite.texture;
            }
            if (_sr != null) _sr.material = _runtimeMat;
        }
        else
        {
            // shader 找不到:保留模板材质,只做 alpha 渐隐,不阻塞飞行
            if (_sr != null) _runtimeMat = _sr.material;
        }

        // 诊断日志:确认剑不可见的原因
        Debug.Log($"[WeaponProjectile] sr={( _sr != null)} sprite={(_sr != null && _sr.sprite != null)} " +
                  $"shaderFind={(_runtimeMat != null && _runtimeMat.shader != null ? _runtimeMat.shader.name : "NULL")} " +
                  $"hasDissolve={_hasDissolve} mat={( _runtimeMat != null ? _runtimeMat.name : "NULL")}");

        StartCoroutine(FlyAndDissolve());
    }

    // ============================================================
    // 飞行 + 溶解(独立协程)
    // ============================================================

    private IEnumerator FlyAndDissolve()
    {
        // 阶段 1:沿路径飞行,由快变慢(缓出曲线,强度可调)
        float elapsed = 0f;
        while (elapsed < _travelDuration)
        {
            elapsed += Time.deltaTime;

            float raw = Mathf.Clamp01(elapsed / _travelDuration);
            // 缓出:前段甩得快,后段飘。power 越大前段越猛
            float t = 1f - Mathf.Pow(1f - raw, _easeOutPower);

            transform.position = _origin + GetPathPosition(t);
            yield return null;
        }

        // 阶段 2:溶解消失 0 → 1,同时本体渐隐
        elapsed = 0f;
        while (elapsed < _dissolveDuration)
        {
            elapsed += Time.deltaTime;
            float dissolve = Mathf.Clamp01(elapsed / _dissolveDuration);

            if (_hasDissolve)
            {
                _runtimeMat.SetFloat("_DissolveAmount", dissolve);
            }

            // 同步渐隐:alpha 随溶解进度降低,本体和拖尾一起消散
            if (_sr != null) _sr.color = new Color(1f, 1f, 1f, 1f - dissolve);
            yield return null;
        }
        if (_hasDissolve) _runtimeMat.SetFloat("_DissolveAmount", 1f);

        // 自毁:先停用整个物体(让编辑器 Inspector 停止访问 TrailRenderer),再销毁
        gameObject.SetActive(false);
        Destroy(gameObject);
    }

    // ============================================================
    // Catmull-Rom 样条插值:曲线平滑穿过每个锚点
    // ============================================================

    private Vector3 GetPathPosition(float t)
    {
        int n = _path.Length;
        if (n == 0) return Vector3.zero;
        if (n == 1) return _path[0];

        float scaled = t * (n - 1);
        int i = Mathf.Clamp(Mathf.FloorToInt(scaled), 0, n - 2);
        float s = scaled - i;

        // 每段取前后各一个点作控制:首尾用端点自身重复
        Vector3 p0 = _path[Mathf.Max(i - 1, 0)];
        Vector3 p1 = _path[i];
        Vector3 p2 = _path[i + 1];
        Vector3 p3 = _path[Mathf.Min(i + 2, n - 1)];

        float s2 = s * s;
        float s3 = s2 * s;

        return 0.5f * (
            (2f * p1) +
            (-p0 + p2) * s +
            (2f * p0 - 5f * p1 + 4f * p2 - p3) * s2 +
            (-p0 + 3f * p1 - 3f * p2 + p3) * s3
        );
    }
}
