using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 法球管理器 — 统一管理技能 2 的所有法球(生成/注册/清理)。
/// 法球到达(标点响起)后由 OrbProjectile 自己 Unregister 并销毁;
/// 场景切换/清场时 Clear() 统一销毁。
/// </summary>
public class OrbManager : MonoBehaviour
{
    public static OrbManager Instance { get; private set; }

    private readonly List<OrbProjectile> _orbs = new List<OrbProjectile>();

    private void Awake()
    {
        if (Instance == null) Instance = this;
        else Destroy(gameObject);
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    /// <summary>生成一个法球(prefab 空时用空物体)并注册</summary>
    public OrbProjectile Spawn(GameObject prefab, Vector3 pos)
    {
        GameObject go = prefab != null ? Instantiate(prefab, pos, Quaternion.identity) : new GameObject("Orb");
        go.transform.position = pos;
        var orb = go.GetComponent<OrbProjectile>();
        if (orb == null) orb = go.AddComponent<OrbProjectile>();
        _orbs.Add(orb);
        return orb;
    }

    public void Unregister(OrbProjectile orb)
    {
        _orbs.Remove(orb);
    }

    /// <summary>统一销毁所有法球(场景切换/Boss 死亡等)</summary>
    public void Clear()
    {
        foreach (var orb in _orbs)
        {
            if (orb != null) Destroy(orb.gameObject);
        }
        _orbs.Clear();
    }
}
