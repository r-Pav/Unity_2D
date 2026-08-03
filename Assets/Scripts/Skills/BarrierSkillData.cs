using UnityEngine;

[CreateAssetMenu(fileName = "Skill_Barrier_", menuName = "Game/SkillData/Barrier")]
public class BarrierSkillData : SkillData
{
    [Header("障碍球参数")]
    public GameObject obstacleBallPrefab;
    public float ballSpeed = 4f;
    public float maxDistance = 8f;
    public float knockbackForce = 5f;
    public float spawnOffset = 2f;
}
