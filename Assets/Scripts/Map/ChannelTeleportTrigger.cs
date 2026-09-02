using Cinemachine;
using UnityEngine;

/// <summary>
/// 传送触发器 — 截断管道深处的瞬移点(无缝传送管道用):
/// 玩家被管口 AreaChannelTrigger 自动推送(InputEnabled=false)经过本 trigger 时,
/// 无缝瞬移到对侧管道的对应 trigger 位置(落点 = otherSide.transform.position,含 y),
/// 随后 AreaChannelTrigger 的移动协程继续推送,把玩家推出对侧管道到达 B 区。
/// 粒子是管道子物体随管道 SetActive 显隐,瞬移瞬间无视觉穿帮;
/// 相机由 VCam.OnTargetObjectWarped 按 delta 同步瞬移(不滑行、不扫虚空)。
/// 本期只配单向:A 侧 otherSide → B 侧、sourceArea → A 区;B 侧 trigger 只作落点,不接 otherSide。
/// 不做反向传送(反向后续再做,留 otherSide 对称结构即可)。
/// </summary>
public class ChannelTeleportTrigger : MonoBehaviour
{
    [Header("瞬移")]
    [Tooltip("对侧管道的传送 trigger(瞬移落点 = 它的 transform.position,含 y,位置推导不手填坐标)")]
    [SerializeField] private ChannelTeleportTrigger otherSide;

    [Tooltip("来源区域(可选):瞬移瞬间隐藏来源区——玩家已到对侧,原区域继续显示无意义")]
    [SerializeField] private GameObject sourceArea;

    /// <summary>
    /// 防重:本实例已瞬移过一次后不再触发。
    /// 生命周期:触发后保持 true;玩家离开本 collider(OnTriggerExit2D)时复位,
    /// 保证下次旅程(玩家再次从本侧进管道)仍能触发——不能永久锁死。
    /// </summary>
    private bool _teleported;

    /// <summary>
    /// 落点保护:本实例刚被对侧用作瞬移落点(玩家此刻重叠在本 collider 内)。
    /// 若此时自己也触发 OnTriggerEnter2D 会立刻把玩家弹回对侧(双向互指时两边互弹)。
    /// 保护期间忽略触发,直到玩家离开本 collider 才清除。
    /// </summary>
    private bool _landingProtected;

    private void OnTriggerEnter2D(Collider2D other)
    {
        // 仅玩家;未接线(纯落点,如本期 B 侧)不动作;已瞬移过不动作
        if (!other.CompareTag("Player")) return;
        if (otherSide == null) return;
        if (_teleported) return;
        if (_landingProtected) return;   // 落点保护:刚被对侧传送过来,忽略自身触发,防互弹

        var player = other.GetComponentInParent<PlayerController>();
        if (player == null) return;

        // 只处理管道自动移动中:管口 AreaChannelTrigger 已锁输入(InputEnabled=false)。
        // 玩家自由行走(InputEnabled=true)路过本 trigger 不瞬移——防正常走路被拉走;
        // 战斗空气墙等场景(输入仍可用)同样不会误触。
        if (player.InputEnabled) return;

        _teleported = true;
        TeleportPlayer(player);
    }

    private void OnTriggerExit2D(Collider2D other)
    {
        if (!other.CompareTag("Player")) return;
        // 玩家离开本 collider:复位防重 + 清除落点保护,下次旅程可再次触发
        _teleported = false;
        _landingProtected = false;
    }

    /// <summary>执行瞬移:位置突变 → 相机 warp → 通知移动协程重计计时 → (可选)隐藏来源区。</summary>
    private void TeleportPlayer(PlayerController player)
    {
        // 1. 位置突变:玩家 → 对侧 trigger 位置(含 y,不手填坐标)。
        //    记录 delta = 对侧位置 - 原位置,供相机按位移修正。
        Vector3 oldPos = player.transform.position;
        Vector3 newPos = otherSide.transform.position;
        Vector3 delta = newPos - oldPos;
        player.transform.position = newPos;

        // 2. 相机瞬移不滑行:Cinemachine 按 delta 修正内部跟踪状态,画面直接切到新落点,不扫过虚空。
        //    VCam 驱动相机(CameraFollow 禁用勿改);Confiner 单大 collider 全区共用,不会被 clamp 拉回。
        FindObjectOfType<CinemachineVirtualCamera>()?.OnTargetObjectWarped(player.transform, delta);

        // 3. 通知管道移动协程:玩家位置突变,AreaChannelTrigger.AutoMoveChannel 的 elapsed 需重计
        //    (否则瞬移前累计的 elapsed 会对侧剩余路程提前撞 MaxMoveTime=5s 上限 → 半路停+输入锁死)。
        AreaChannelTrigger.NotifyPlayerTeleported();

        // 4. 给对侧落点打保护:玩家此刻重叠在 otherSide 的 collider 内,
        //    otherSide 若也接了 otherSide(双向互指)会在 Enter 时把玩家弹回——置保护标记忽略,
        //    玩家离开其 collider 后 OnTriggerExit2D 自动清除。
        if (otherSide != null)
            otherSide._landingProtected = true;

        // 5. (可选)隐藏来源区:玩家已在对侧,原区域即时隐藏(AreaChannelTrigger 到达后也会 HideArea,双保险)
        if (sourceArea != null)
            ZoneManager.Instance?.HideArea(sourceArea);
    }
}
