using UnityEngine;
using System.Collections;

/// <summary>
/// 地区管理器（单例）— 负责地区根物体的 SetActive 显隐切换。
/// 所有地区在编辑器中直接摆进场景（普通物体或 prefab 实例均可），
/// 运行时只做显隐控制，无加载/卸载。
/// </summary>
public class ZoneManager : MonoBehaviour
{
    // ============================================================
    // Singleton
    // ============================================================

    private static ZoneManager _instance;
    public static ZoneManager Instance
    {
        get
        {
            if (_instance == null)
                _instance = FindObjectOfType<ZoneManager>();
            return _instance;
        }
    }

    // ============================================================
    // 配置
    // ============================================================

    [Header("地区引用")]
    [Tooltip("当前活跃地区（初始地区）。切换时由触发器目标地区覆盖")]
    [SerializeField] private GameObject currentArea;

    // ============================================================
    // 运行时状态
    // ============================================================

    private bool _isTransitioning;

    public bool IsTransitioning => _isTransitioning;

    /// <summary>触发器是否可响应(切换中不可;无冷却——移动中经过另一侧触发器由 _isTransitioning 挡住,完成后回头应立即能触发反向)</summary>
    public bool CanTrigger => !_isTransitioning;

    // ============================================================
    // 公开 API — 显隐控制
    // ============================================================

    /// <summary>显示目标地区(进入触发器时立即显示,玩家在通道内已可见)</summary>
    public void ShowArea(GameObject area)
    {
        if (area != null)
            area.SetActive(true);
    }

    /// <summary>隐藏地区(移动完成到达落点后关闭来源地区)</summary>
    public void HideArea(GameObject area)
    {
        if (area == null) return;
        area.SetActive(false);
    }

    // ============================================================
    // 切换流程
    // ============================================================

    /// <summary>
    /// 开始地区切换协程。
    /// 由 AreaChannelTrigger.OnTriggerEnter2D 调用。
    /// </summary>
    public void StartTransition(AreaChannelTrigger trigger, Transform player)
    {
        if (_isTransitioning)
        {
            Debug.LogWarning("[ZoneManager] 切换进行中，忽略重复触发");
            return;
        }
        StartCoroutine(TransitionRoutine(trigger, player));
    }

    /// <summary>
    /// 核心切换协程：
    /// 锁输入 → 自动移动 + 镜头缩放 → 显示目标地区/隐藏来源地区 → 解锁
    /// </summary>
    private IEnumerator TransitionRoutine(AreaChannelTrigger trigger, Transform player)
    {
        _isTransitioning = true;

        // ── 缓存组件引用 ──
        var pc = player.GetComponent<PlayerController>();
        var rb = player.GetComponent<Rigidbody2D>();
        var anim = player.GetComponentInChildren<PlayerAnimation>();
        var cam = Camera.main;
        float originalOrtho = cam != null ? cam.orthographicSize : 5f;

        // ── 1. 锁输入 + 清残留水平速度 ──
        // 玩家可能带着跑速进通道:锁输入后 PlayerController 不再更新速度,
        // 残留 velocity.x 会让切换结束后继续播移动动画/滑行,必须清掉
        if (pc != null) pc.InputEnabled = false;
        if (rb != null)
            rb.velocity = new Vector2(0f, rb.velocity.y);   // 保留 y(空中进通道自然落地)

        // 自动移动动画标记(PlayerAnimation 强制播放移动动画)
        if (anim != null)
        {
            anim.AutoMoving = true;
            anim.AutoMoveSpeed = trigger.MoveSpeed;
        }

        // ── 2. 立即显示目标地区(玩家在通道内即可看到出口方向的下一张图) ──
        ShowArea(trigger.TargetArea);

        // ── 2. 自动移动(直接位移,目标是 targetSpawnPoint 的 x,到达即停)+ 镜头缩放 ──
        // 注意:只比较 x 轴(移动是水平的,落点 y/z 不参与判断,防 y/z 差异导致死循环)
        Vector3 target = trigger.TargetSpawnPoint;
        float arrivedEps = 0.05f;
        float timeout = 10f;   // 兜底:配置错误(方向/落点异常)时强制结束,防无限播动画
        float elapsed = 0f;
        while (Mathf.Abs(target.x - player.position.x) > arrivedEps && elapsed < timeout)
        {
            float step = trigger.MoveSpeed * Time.deltaTime;
            elapsed += Time.deltaTime;

            // 移动方向:朝落点(保证收敛);若已在落点 x 上则不会进入循环
            float dirToTarget = Mathf.Sign(target.x - player.position.x);
            player.localScale = new Vector3(dirToTarget, 1f, 1f);

            Vector3 next = player.position + new Vector3(dirToTarget * step, 0f, 0f);
            // 不超过落点
            float remaining = Mathf.Abs(target.x - player.position.x);
            if (step > remaining)
                next = new Vector3(target.x, player.position.y, player.position.z);
            player.position = next;

            // 镜头平滑缩放(直接控制 orthoSize；CameraFollow 不在 isOverriding 时不会干预)
            if (cam != null)
                cam.orthographicSize = Mathf.Lerp(cam.orthographicSize, trigger.ZoomAmount, 3f * Time.deltaTime);

            yield return null;
        }

        // 精确落点(只对齐 x;y/z 保留玩家当前值,防地面高度差异)
        player.position = new Vector3(target.x, player.position.y, player.position.z);

        // ── 2.5 到点即停:立刻复位自动移动动画 + 清水平速度 ──
        // 必须在镜头恢复循环之前(镜头恢复要 1~2 秒,期间玩家已停,动画若还强制播移动=空转)
        if (anim != null)
            anim.AutoMoving = false;
        if (rb != null)
            rb.velocity = new Vector2(0f, rb.velocity.y);

        // ── 3. 移动完成:关闭来源地区,更新当前地区 ──
        HideArea(trigger.SourceArea);
        currentArea = trigger.TargetArea;

        // ── 4. 发送切换完成事件 ──
        EventBus.Trigger(new AreaSwitchEvent(
            currentArea != null ? currentArea.name : null,
            trigger.TargetArea != null ? trigger.TargetArea.name : null));

        // ── 5. 镜头平滑恢复 ──
        if (cam != null)
        {
            while (Mathf.Abs(cam.orthographicSize - originalOrtho) > 0.05f)
            {
                cam.orthographicSize = Mathf.Lerp(cam.orthographicSize, originalOrtho, 3f * Time.deltaTime);
                yield return null;
            }
            cam.orthographicSize = originalOrtho;
        }

        // ── 6. 解锁输入 ──
        // (AutoMoving/速度已在 2.5 步到点时复位,这里只解锁)
        if (pc != null) pc.InputEnabled = true;
        _isTransitioning = false;
    }
}
