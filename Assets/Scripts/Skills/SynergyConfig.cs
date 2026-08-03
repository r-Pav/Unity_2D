using UnityEngine;

/// <summary>协同联动 Bonus 数据</summary>
[System.Serializable]
public class SynergyBonus
{
    public int requiredLevel;
    public string bonusName;
    [Tooltip("冷却倍率，1=不变，0.9=-10%")]
    public float cooldownMultiplier = 1f;
    public float manaRegenBonus;
    [Tooltip("效果倍率（伤害/速度等），1=不变，1.15=+15%")]
    public float effectMultiplier = 1f;
}

/// <summary>协同联动配置（ScriptableObject，拖到 SkillManager 上）</summary>
[CreateAssetMenu(fileName = "SynergyConfig", menuName = "Game/SynergyConfig")]
public class SynergyConfig : ScriptableObject
{
    public SynergyBonus[] bonuses;
}
