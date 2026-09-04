using UnityEngine;

/// <summary>
/// [开屏P3] SampleScene 启动流程控制 — 挂 SampleScene 场景内独立空物体（saika 手动挂载）。
/// Start() 时读取 PendingLoadFlag 判定启动模式：
///   - 标记从未被写入（wasSet=false，编辑器直接进 SampleScene 调试）→ 调试模式：不读档不传送，玩家原地保持现状
///   - wasSet && slot >= 0 → 读档模式：SaveSystem.LoadGame(slot)（内部 RestorePositionNextFrame 延迟一帧恢复位置，此处只调用不重复传送）
///   - wasSet && slot == -1 → 新游戏模式：玩家传送 DefaultSpawnPoint.position
/// 三种模式共用 Start()：等所有组件 Awake 完成后再执行（LoadGame 的协程/玩家引用此时安全）。
/// 处理完统一 PendingLoadFlag.Clear()，避免后续误触发二次读档。
/// 防御：saveSystem/player/defaultSpawnPoint 留空时跳过对应分支并 LogWarning，不崩溃。
/// </summary>
public class SceneBootstrap : MonoBehaviour
{
    [Header("启动流程")]
    [Tooltip("默认出生点（场景里 DefaultSpawnPoint 空物体）：新游戏传送 + PlayerHealth 死亡复活共用")]
    [SerializeField] private Transform defaultSpawnPoint;

    [Tooltip("Player 上的 SaveSystem 组件（读档用）；留空 = 读档模式跳过并 LogWarning")]
    [SerializeField] private SaveSystem saveSystem;

    [Tooltip("玩家根组件（新游戏传送用）；留空 = 新游戏传送跳过并 LogWarning")]
    [SerializeField] private PlayerController player;

    private void Start()
    {
        // 调试模式：标记从未被写入（编辑器直接进 SampleScene）→ 不读档不传送，玩家原地（保持现状调试行为）
        if (!PendingLoadFlag.wasSet)
            return;

        if (PendingLoadFlag.slot >= 0)
        {
            // 读档模式：LoadGame 内部延迟一帧恢复位置，只调用不重复传送
            if (saveSystem == null)
            {
                Debug.LogWarning("[SceneBootstrap] saveSystem 未配置，读档模式跳过（槽位 " + PendingLoadFlag.slot + "）");
            }
            else
            {
                saveSystem.LoadGame(PendingLoadFlag.slot);
            }
        }
        else if (PendingLoadFlag.slot == -1)
        {
            // 新游戏模式：玩家传送默认出生点
            if (player == null)
            {
                Debug.LogWarning("[SceneBootstrap] player 未配置，新游戏传送跳过");
            }
            else if (defaultSpawnPoint == null)
            {
                Debug.LogWarning("[SceneBootstrap] defaultSpawnPoint 未配置，新游戏传送跳过");
            }
            else
            {
                // 只取出生点 x/y,z 保持玩家自身值(防 DefaultSpawnPoint 的 z 偏移污染 2D 玩家深度)
                Vector3 spawn = defaultSpawnPoint.position;
                player.transform.position = new Vector3(spawn.x, spawn.y, player.transform.position.z);
            }
        }
        // else: 非法槽位（理论不会出现），仅清标记

        PendingLoadFlag.Clear();
    }
}
