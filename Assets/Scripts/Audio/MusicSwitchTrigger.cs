using UnityEngine;

/// <summary>
/// 音乐切换触发器 — 挂场景 collider 上(管道进出口 / Boss 房门口),玩家进入触发切音乐。
/// 与 AreaChannelTrigger(场景加载)平行:触发器放哪,音乐切哪。
/// Scene 模式:CrossFadeTo(目标曲);Boss 模式:EnterBossMusic(交叠循环) + 订阅 BossDefeatedEvent 死亡切回场景曲。
/// 管道移动中(player.InputEnabled=false)自动跳过,防管道往返时来回切曲。
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

    private void OnTriggerEnter2D(Collider2D other)
    {
        if (!other.CompareTag("Player")) return;
        var player = other.GetComponentInParent<PlayerController>();
        if (player == null) return;
        if (!player.InputEnabled) return;   // 管道移动中(输入被锁)跳过,防往返抖动

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
