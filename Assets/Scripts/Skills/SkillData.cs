using UnityEngine;

/// <summary>技能触发类型</summary>
public enum SkillType
{
    Active,   // 主动：按键触发，有冷却
    Passive,  // 被动：始终生效，无冷却
    Toggle    // 切换：按键开关，持续消耗
}

/// <summary>技能功能类别</summary>
public enum SkillCategory
{
    Attack,   // 攻击类
    Movement, // 位移类
    Defense,  // 防御类
    Support,  // 辅助 / Buff
    Passive   // 被动
}

/// <summary>
/// 技能配置数据（ScriptableObject）
/// 策划可直接在 Inspector 调整所有参数，无需碰代码
/// </summary>
[CreateAssetMenu(fileName = "Skill_", menuName = "Game/SkillData")]
public class SkillData : ScriptableObject
{
    [Header("基础信息")]
    public string skillName;        // 技能名称
    [TextArea(2, 4)]
    public string description;      // 技能描述
    public Sprite icon;             // 技能图标

    [Header("输入")]
    public KeyCode hotkey;          // 激活快捷键（Active / Toggle 类型使用）

    [Header("消耗与冷却")]
    public float cooldown;          // 冷却时间（秒，0 = 无冷却）
    public float manaCost;          // 法力消耗（0 = 无消耗）

    [Header("充能（阶段7，可选）")]
    [Tooltip("启用充能模型：有充能则消耗并激活，充能各自独立恢复；未启用走原单 CD 路径（零回归）")]
    public bool useCharges;
    [Tooltip("最大充能数（useCharges=true 时生效；默认 1）")]
    public int maxCharges = 1;
    [Tooltip("每充能恢复时间（秒，useCharges=true 时生效；每个已消耗的充能独立计时）")]
    public float chargeRechargeTime = 5f;

    [Header("分类")]
    public SkillType type;          // Active / Passive / Toggle
    public SkillCategory category;  // 攻击 / 位移 / 防御 / 辅助 / 被动

    [Header("进阶")]
    public int unlockLevel;         // 解锁等级（0 = 初始可用）
    public float castTime;          // 施法时间（秒，0 = 瞬发）

    [Header("状态接管（阶段7，B9 出口）")]
    [Tooltip("激活成功后由执行器接管状态（不切 PlayerSkillCastState 固定 0.25s；瞄准选点等需要长时间选点的技能用）")]
    public bool interceptsStateAfterActivate;

    [Header("等级")]
    public int skillLevel = 1;      // 当前等级
    public int maxLevel = 5;        // 最大等级

    [Header("表现（可选）")]
    public GameObject vfxPrefab;    // 特效预制体
    public AudioClip sfxClip;       // 音效
}
