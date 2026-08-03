using UnityEngine;

/// <summary>
/// 掉落条目：物品 + 权重，供 LootTableSO 使用。
/// 必须独立文件，否则 Unity Inspector 数组拖拽可能不生效。
/// </summary>
[System.Serializable]
public class DropEntry
{
    [Tooltip("掉落物品模板")]
    public ItemSO item;

    [Tooltip("权重，值越大掉落概率越高。总和恒为100，百分比语义")]
    [Range(0f, 100f)]
    public float weight = 10f;
}
