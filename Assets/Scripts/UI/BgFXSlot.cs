using UnityEngine;

/// <summary>
/// UI 背景特效槽位(通用):手动引用 BG 下已摆好的特效子物体(粒子 prefab 实例)。
/// 特效的位置/大小/层级完全由编辑器里该子物体决定,脚本只负责:
/// 1. BG 激活时播放、隐藏时停止(随父级显隐联动);
/// 2. 面板暂停(timeScale=0)时粒子默认继续流动。
/// 层级说明:Overlay Canvas 下粒子渲染于 UI 之上;想夹在 BG 与内容之间,拖子物体 sibling 顺序。
/// </summary>
public class BgFXSlot : MonoBehaviour
{
    [Header("特效槽位")]
    [Tooltip("BG 下已摆好的特效根物体(粒子 prefab 实例)。位置/大小以编辑器里摆放为准,脚本不改 Transform")]
    [SerializeField] private GameObject[] fxObjects;

    [Tooltip("面板暂停(timeScale=0)时粒子是否继续流动;关闭则粒子随暂停冻结")]
    [SerializeField] private bool useUnscaledOnPause = true;

    private void OnEnable()
    {
        if (fxObjects == null)
            return;

        for (int i = 0; i < fxObjects.Length; i++)
        {
            GameObject fx = fxObjects[i];
            if (fx == null)
            {
                Debug.LogWarning($"[BgFXSlot] {name}:第 {i + 1} 个特效物体为空,已跳过", this);
                continue;
            }

            ParticleSystem[] systems = fx.GetComponentsInChildren<ParticleSystem>(true);
            for (int j = 0; j < systems.Length; j++)
            {
                ParticleSystem ps = systems[j];
                if (useUnscaledOnPause)
                {
                    ParticleSystem.MainModule main = ps.main;
                    main.useUnscaledTime = true;
                }
                ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
                ps.Play();
            }
        }
    }

    private void OnDisable()
    {
        if (fxObjects == null)
            return;

        for (int i = 0; i < fxObjects.Length; i++)
        {
            GameObject fx = fxObjects[i];
            if (fx == null)
                continue;

            ParticleSystem[] systems = fx.GetComponentsInChildren<ParticleSystem>(true);
            for (int j = 0; j < systems.Length; j++)
            {
                systems[j].Stop(true, ParticleSystemStopBehavior.StopEmitting);
            }
        }
    }
}
