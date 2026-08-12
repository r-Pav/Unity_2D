using UnityEngine;

/// <summary>
/// Boss 攻击状态 — 启动攻击循环协程（技能选择+执行），结束后回追击。
/// </summary>
public class BossAttackState : EntityState
{
    private Coroutine attackCoroutine;

    public BossAttackState(CharacterBase owner, StateMachine stateMachine, Animator anim = null)
        : base(owner, stateMachine, anim)
    {
    }

    public override void OnEnter()
    {
        var boss = (FirstBoss)owner;
        boss.moveInput = 0f;

        // 面朝玩家
        float dir = boss.DirectionToPlayer();
        if (dir != 0f)
            boss.UpdateFacing(dir);

        // 启动攻击循环（由 BossControllerBase.ExecuteBossSkillCycle 处理选择+执行）
        attackCoroutine = boss.StartCoroutine(AttackFlow(boss));
    }

    private System.Collections.IEnumerator AttackFlow(FirstBoss boss)
    {
        // 执行技能循环（技能选择 → SO驱动执行 → 或 fallback 普攻）
        yield return boss.ExecuteBossSkillCycle();

        // 攻击结束后切回追击
        if (!boss.IsDead)
            boss.Fsm.ChangeState(boss.CreateChaseState());
    }

    public override void OnUpdate() { }

    public override void OnExit()
    {
        var boss = (FirstBoss)owner;
        if (attackCoroutine != null)
            boss.StopCoroutine(attackCoroutine);
        boss.moveInput = 0f;
    }
}
