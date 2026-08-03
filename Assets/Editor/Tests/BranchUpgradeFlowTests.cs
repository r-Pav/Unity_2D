using System.Reflection;
using NUnit.Framework;
using UnityEngine;

public sealed class BranchUpgradeFlowTests
{
    private GameObject playerObject;
    private SkillPointManager points;
    private BranchUpgradeSystem branches;
    private ActiveSkillData skill;
    private int[] levels;

    [SetUp]
    public void SetUp()
    {
        playerObject = new GameObject("BranchUpgradeFlowTests_Player");
        points = playerObject.AddComponent<SkillPointManager>();
        typeof(SkillPointManager).GetMethod("Awake", BindingFlags.Instance | BindingFlags.NonPublic)
            ?.Invoke(points, null);

        skill = ScriptableObject.CreateInstance<ActiveSkillData>();
        skill.skillName = "TestSkill";
        skill.skillLevel = 1;
        skill.maxLevel = 3;
        skill.lv1Data = new ActiveSkillData.ActiveBranchData { branchName = "Lv1" };
        skill.lv2Left = new ActiveSkillData.ActiveBranchData { branchName = "Lv2Left" };
        skill.lv2Right = new ActiveSkillData.ActiveBranchData { branchName = "Lv2Right" };
        skill.lv3Left = new ActiveSkillData.ActiveBranchData { branchName = "Lv3Left" };
        skill.lv3Right = new ActiveSkillData.ActiveBranchData { branchName = "Lv3Right" };

        levels = new[] { 0 };
        branches = new BranchUpgradeSystem();
        branches.Initialize(null, points, levels, new[] { new SkillSlot { data = skill } });
    }

    [TearDown]
    public void TearDown()
    {
        Object.DestroyImmediate(skill);
        Object.DestroyImmediate(playerObject);
    }

    [Test]
    public void UnlockLevel1_StartsLocked_AndConsumesOnePoint()
    {
        int before = points.CurrentSkillPoints;

        Assert.That(branches.UnlockLevel1(0), Is.True);
        Assert.That(levels[0], Is.EqualTo(1));
        Assert.That(points.CurrentSkillPoints, Is.EqualTo(before - 1));
    }

    [Test]
    public void ChooseLevel2_RecordsClickedBranch_AndConsumesOnePoint()
    {
        branches.UnlockLevel1(0);
        int before = points.CurrentSkillPoints;

        Assert.That(branches.ChooseLevel2(0, "Right"), Is.True);
        Assert.That(levels[0], Is.EqualTo(2));
        Assert.That(skill.chosenBranch, Is.EqualTo("Right"));
        Assert.That(points.CurrentSkillPoints, Is.EqualTo(before - 1));
    }

    [Test]
    public void UpgradeLevel3_OnlyAcceptsTheChosenBranch()
    {
        points.SetPoints(10);
        branches.UnlockLevel1(0);
        branches.ChooseLevel2(0, "Left");

        Assert.That(branches.UpgradeLevel3(0, "Right"), Is.False);
        Assert.That(levels[0], Is.EqualTo(2));
        Assert.That(branches.UpgradeLevel3(0, "Left"), Is.True);
        Assert.That(levels[0], Is.EqualTo(3));
    }
}
