using UnityEngine;

/// <summary>
/// 音乐切换器 — 挂管道口 / Boss 房门口的 collider 物体上,配置"目标音乐"。
/// 不自己监听 collider(与区域触发器存在时序竞争:进管道瞬间输入已锁定,OnTriggerEnter 会被误判跳过),
/// 由区域触发器(AreaChannelTrigger 进管道 / BossRoomTrigger 进房)在玩家进入时调用 TriggerSwitch()。
/// Scene 模式:CrossFadeTo(目标曲);Boss 模式:EnterBossMusic(交叠循环) + 订阅 BossDefeatedEvent 死亡切回场景曲。
/// </summary>
public class MusicSwitchTrigger : MonoBehaviour
{
    public enum Mode { Scene, Boss }

    [Tooltip("切换模式:Scene = 普通换曲(管道进出口);Boss = 进 Boss 房(交叠循环 + 死亡切回)")]
    [SerializeField] private Mode mode;

    [Tooltip("目标音乐(Scene 模式:对面地区曲;Boss 模式:Boss 曲)")]
    [SerializeField] private MusicTrackData targetMusic;

    [Tooltip("Boss 模式:关联的 Boss(击败时切回进房前的场景曲)")]
    [SerializeField] private BossControllerBase boss;

    private bool _triggered;   // Boss 模式一次性,防重复进房重复切入

    private void OnEnable()
    {
        if (mode == Mode.Boss)
            EventBus.Subscribe<BossDefeatedEvent>(OnBossDefeated);
    }

    private void OnDisable()
    {
        if (mode == Mode.Boss)
            EventBus.Unsubscribe<BossDefeatedEvent>(OnBossDefeated);
    }

    /// <summary>
    /// 执行切换(由区域触发器在玩家进入时调用,不监听 collider,避免进管道瞬间输入锁定导致不触发)。
    /// </summary>
    public void TriggerSwitch()
    {
        var mgr = MusicPointManager.Instance;
        if (mgr == null) return;

        if (mode == Mode.Boss)
        {
            if (_triggered) return;
            _triggered = true;
            mgr.EnterBossMusic(targetMusic);
        }
        else
        {
            mgr.CrossFadeTo(targetMusic);
        }
    }

    /// <summary>Boss 击败:切回进房前的场景曲(仅本触发器关联的 Boss)</summary>
    private void OnBossDefeated(BossDefeatedEvent e)
    {
        if (e.boss != null && e.boss == boss)
            MusicPointManager.Instance?.ExitBossMusic();
    }
}
