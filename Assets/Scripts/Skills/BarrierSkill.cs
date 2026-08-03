using UnityEngine;

/// <summary>
/// 障碍球技能 — 按热键放出球形障碍物，沿瞄准线方向飞行
/// 碰到墙壁或敌人停下，停下后阻挡敌人攻击和移动
/// 由 SkillManager 统一管理热键/冷却/法力，BarrierSkill 只负责执行
/// </summary>
public class BarrierSkill : MonoBehaviour, ISkill
{
    [SerializeField] private BarrierSkillData barrierData;

    public SkillData Data => barrierData;
    public float CooldownTimer => 0f;
    public bool IsOnCooldown => false;
    public bool IsActive => false;

    private PlayerController owner;
    private PlayerAimLine aimLine;
    private int ballsCreated;

    void Awake()
    {
        owner = GetComponent<PlayerController>();
        aimLine = GetComponent<PlayerAimLine>();
    }

    void OnEnable()
    {
        EventBus.Subscribe<SkillActivatedEvent>(OnSkillActivated);
    }

    void OnDisable()
    {
        EventBus.Unsubscribe<SkillActivatedEvent>(OnSkillActivated);
    }

    public void OnSkillUpdate(PlayerController pc) { }

    void OnSkillActivated(SkillActivatedEvent e)
    {
        if (barrierData == null) return;
        if (e.skillName != barrierData.skillName) return;
        Activate(owner, e.skillLevel);
    }

    public bool CanActivate(PlayerController pc) => true;

    public void Activate(PlayerController pc)
    {
        Activate(pc, 1);
    }

    public void Activate(PlayerController pc, int skillLevel)
    {
        if (barrierData == null || barrierData.obstacleBallPrefab == null)
        {
            Debug.LogWarning("[BarrierSkill] barrierData 或 obstacleBallPrefab 未赋值");
            return;
        }

        float levelScale = 1f + (skillLevel - 1) * 0.15f;  // 每级 +15% 效果
        float speed = barrierData.ballSpeed * levelScale;
        float knockback = barrierData.knockbackForce * levelScale;

        Vector3 aimDir = aimLine != null ? aimLine.AimDirection : Vector3.right;
        Vector3 spawnPos = transform.position + aimDir * barrierData.spawnOffset + Vector3.up * 0.5f;

        GameObject ball = Instantiate(barrierData.obstacleBallPrefab, spawnPos, Quaternion.identity);
        ObstacleBall ballScript = ball.GetComponent<ObstacleBall>();
        if (ballScript != null)
            ballScript.Launch((Vector2)aimDir, speed, barrierData.maxDistance, knockback);

        ballsCreated++;
        // Debug.Log($"[BarrierSkill] 障碍球 #{ballsCreated} 释放 (Lv{skillLevel})");
    }

    public void Deactivate(PlayerController pc) { }
}
