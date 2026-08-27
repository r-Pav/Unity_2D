using UnityEngine;

/// <summary>
/// Boss 技能场景配置 — 挂 Boss 房场景里的空物体上,存技能需要的场景位置(拖场景 Transform,禁手填坐标)。
/// 技能 prefab 是资源,不能引用场景物体;运行时由技能执行器(FindObjectOfType)读这里的引用。
/// 字段前缀 = 技能用途,技能 1(双火墙)用 FireWall* 三个。
/// </summary>
public class BossSkillSceneConfig : MonoBehaviour
{
    [Header("技能 1 双火墙")]
    [Tooltip("Boss 放技能 1 时移动到的位置(拖场景空物体)")]
    public Transform fireWallBossTarget;

    [Tooltip("左墙生成位置(拖场景空物体)")]
    public Transform fireWallLeftSpawn;

    [Tooltip("右墙生成位置(拖场景空物体)")]
    public Transform fireWallRightSpawn;

    [Header("技能 2 法球")]
    [Tooltip("法球生成位置(拖场景空物体)")]
    public Transform orbSpawnPoint;
}
