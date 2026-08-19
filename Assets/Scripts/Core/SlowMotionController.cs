using UnityEngine;

/// <summary>
/// 全局慢动作控制器（技能组阶段 7,7.3）— 传送后瞄准选点期间的时间减速。
///
/// Enter(float timeScale, float duration)：进入慢动作（真实时长 duration 秒后自动恢复 1）；
/// Exit()：立即退出慢动作。
///
/// 与 HitStopController 的优先级（手册 7.3）：
///   HitStop 会把 Time.timeScale 置 0 冻结 —— 冻结期间挂起慢动作的恢复计时与应用/恢复，
///   HitStop 结束（timeScale 恢复为冻结前保存值）后，本控制器继续按慢动作倍率运行或按时恢复。
///   由于 HitStop 冻结前保存的 timeScale 就是本控制器写入的慢动作倍率，HitStop 结束天然回到慢动作；
///   本控制器只处理两种边界：
///   ① 慢动作 Enter 时恰好处于 HitStop 冻结（timeScale=0）→ 推迟应用倍率，解冻后补写；
///   ② 慢动作 Exit 时恰好处于冻结 → 推迟恢复 1，解冻后补写（防 HitStop 把 1 覆盖成慢动作倍率）。
///
/// 用法：SlowMotionController.EnterSlow(0.3f, 3f) / ExitSlow()（静态入口，自动 EnsureInstance）。
/// </summary>
public class SlowMotionController : MonoBehaviour
{
    public static SlowMotionController Instance { get; private set; }

    // ============================================================
    // 运行时状态
    // ============================================================

    private float _targetScale = 1f;  // 慢动作目标倍率
    private float _remaining;         // 剩余时长（仅 timeScale>0 时递减 = HitStop 冻结期间挂起）
    private bool _active;             // 慢动作进行中
    private bool _pendingApply;       // 处于 HitStop 冻结，倍率应用/恢复被推迟

    // ============================================================
    // 生命周期 / 惰性创建（与 IllusionManager.EnsureInstance 同款：执行器静态上下文可用）
    // ============================================================

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    /// <summary>确保控制器存在（场景已挂直接用；否则惰性创建）。执行器调用。</summary>
    public static SlowMotionController EnsureInstance()
    {
        if (Instance != null) return Instance;
        var go = new GameObject("SlowMotionController");
        return go.AddComponent<SlowMotionController>();
    }

    // ============================================================
    // 公共接口
    // ============================================================

    /// <summary>进入慢动作（重复 Enter 覆盖旧参数）。冻结期间进入 = 解冻后生效。</summary>
    /// <param name="timeScale">慢动作倍率（0.05~1 钳制；1 = 无效果）</param>
    /// <param name="duration">真实时长（秒；≤0 = 立即恢复）</param>
    public void Enter(float timeScale, float duration)
    {
        _targetScale = Mathf.Clamp(timeScale, 0.05f, 1f);
        _remaining = Mathf.Max(0f, duration);
        _active = true;

        // HitStop 冻结（timeScale=0）期间挂起应用：等解冻后由 Update 补写
        if (Time.timeScale <= 0f)
        {
            _pendingApply = true;
            return;
        }
        Time.timeScale = _targetScale;
    }

    /// <summary>退出慢动作（立即恢复 timeScale=1；冻结期间退出 = 解冻后恢复）</summary>
    public void Exit()
    {
        _active = false;
        _remaining = 0f;

        if (Time.timeScale <= 0f)
        {
            _pendingApply = true; // 冻结中：解冻后补恢复 1（防 HitStop 把 1 覆盖成慢动作倍率）
            return;
        }
        Time.timeScale = 1f;
    }

    /// <summary>静态入口：进入慢动作（无实例时惰性创建）</summary>
    public static void EnterSlow(float timeScale, float duration)
        => EnsureInstance().Enter(timeScale, duration);

    /// <summary>静态入口：退出慢动作（无实例时直接恢复 1，兜底清理）</summary>
    public static void ExitSlow()
    {
        if (Instance != null) Instance.Exit();
        // 无实例兜底：仅未冻结（timeScale>0）时恢复 1；冻结期间绝不强制覆盖（HitStop 会自行恢复）
        else if (Time.timeScale > 0f) Time.timeScale = 1f;
    }

    // ============================================================
    // 每帧驱动
    // ============================================================

    private void Update()
    {
        if (!_active && !_pendingApply) return;

        // HitStop 冻结期间：挂起（不递减时长、不应用/恢复），等解冻
        if (Time.timeScale <= 0f) return;

        // 解冻后的补写（Enter/Exit 在冻结期间被调用过）
        if (_pendingApply)
        {
            _pendingApply = false;
            Time.timeScale = _active ? _targetScale : 1f;
            return;
        }

        // 正常慢动作计时（真实时间；卡帧冻结期间已 return，时长天然挂起）
        if (_active)
        {
            _remaining -= Time.unscaledDeltaTime;
            if (_remaining <= 0f)
            {
                _remaining = 0f;
                _active = false;
                Time.timeScale = 1f;
            }
        }
    }
}
