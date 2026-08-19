using UnityEngine;

/// <summary>幻象类型 — 计数/顶替/事件按类型区分（决策 N3：每类独立计数，上限 2）</summary>
public enum IllusionType
{
    /// <summary>嘲讽型 — 生成时嘲讽半径内 enemy，仇恨拉向自身（阶段 4 落地）</summary>
    Taunt,
    /// <summary>攻击型 — 自动攻击敌人（阶段 6 落地，本阶段不生成）</summary>
    Attack
}

/// <summary>
/// 幻象实体基类（阶段 4）— 寿命计时、外观（player 贴图半透明）、类型标识。
/// 可被攻击开关：本阶段先做【不可被攻击】——不挂受击组件、不参与 enemy 受击层，
/// enemy 的追踪/出招会指向幻象（嘲讽牵引）但伤害打空（enemy 攻击只结算带 PlayerController 的目标）。
/// </summary>
public class IllusionController : MonoBehaviour
{
    [Header("寿命")]
    [Tooltip("幻象存活时长（秒），到点由 IllusionManager 统一销毁")]
    [SerializeField] private float lifetime = 5f;

    [Header("外观")]
    [Tooltip("半透明度 0~1（1=完全不透明；叠加到 player 贴图上）")]
    [SerializeField] private float alpha = 0.5f;
    [Tooltip("幻象缩放倍率（相对 player 根缩放；占位图偏小时调大）")]
    [SerializeField] private float illusionScale = 1.2f;

    /// <summary>幻象类型（IllusionManager 生成时写入）</summary>
    public IllusionType Type { get; private set; } = IllusionType.Taunt;

    /// <summary>寿命（秒；Initialize 传入覆盖序列化默认）</summary>
    public float Lifetime => lifetime;

    /// <summary>剩余寿命（秒）</summary>
    public float RemainingLifetime => lifeTimer;

    private float lifeTimer;
    private bool initialized;
    private bool appearanceApplied;

    /// <summary>
    /// 初始化 — 由 IllusionManager.SpawnIllusion 调用（一次性）。
    /// type：幻象类型；lifetimeOverride ≤ 0 时使用序列化默认。
    /// </summary>
    public void Initialize(IllusionType type, float lifetimeOverride = -1f)
    {
        Type = type;
        if (lifetimeOverride > 0f) lifetime = lifetimeOverride;
        lifeTimer = lifetime;
        initialized = true;
        ApplyTranslucentAppearance();
    }

    protected virtual void Update()
    {
        if (!initialized) return;

        lifeTimer -= Time.deltaTime;
        if (lifeTimer <= 0f)
        {
            lifeTimer = 0f;
            // 统一销毁（管理器计数/顶替；未初始化完成时不主动销毁，防 OnEnable 误杀）
            var mgr = IllusionManager.Instance;
            if (mgr != null) mgr.Despawn(this);
            else Destroy(gameObject);
        }
    }

    /// <summary>
    /// 外观半透明化：自身已有 SpriteRenderer → 直接压 alpha；
    /// 没有（预制体未建，程序生成路径）→ 复制 player 的贴图/朝向/排序层，再压 alpha。
    /// 幂等：仅生效一次。
    /// </summary>
    private void ApplyTranslucentAppearance()
    {
        if (appearanceApplied) return;
        appearanceApplied = true;

        SpriteRenderer sr = GetComponent<SpriteRenderer>();
        if (sr == null)
        {
            // 程序生成外观：复制 player 第一个 SpriteRenderer 的贴图（预制体由 saika 编辑器建时跳过）
            PlayerController pc = PlayerController.Instance;
            SpriteRenderer playerSr = pc != null ? pc.GetComponentInChildren<SpriteRenderer>() : null;
            if (playerSr != null && playerSr.sprite != null)
            {
                sr = gameObject.AddComponent<SpriteRenderer>();
                sr.sprite = playerSr.sprite;
                sr.flipX = playerSr.flipX;
                sr.sortingLayerID = playerSr.sortingLayerID;
                sr.sortingOrder = playerSr.sortingOrder;
                if (pc != null)
                    transform.localScale = pc.transform.localScale * illusionScale;
            }
        }

        if (sr != null)
        {
            Color c = sr.color;
            c.a = Mathf.Clamp01(alpha);
            sr.color = c;
            Debug.Log($"[DEBUG-Illusion] 外观生成 OK: sprite={(sr.sprite != null ? sr.sprite.name : "NULL")}, alpha={c.a}, pos={transform.position}");
        }
        else
        {
            Debug.LogWarning("[DEBUG-Illusion] 外观生成失败: 无 SpriteRenderer 且 player 无可用贴图");
        }
    }
}
