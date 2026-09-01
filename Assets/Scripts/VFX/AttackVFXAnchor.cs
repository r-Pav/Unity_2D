using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 攻击 VFX 锚点 — 挂在攻击者(玩家/Boss/敌人)的 attack_VFX 子物体上。
/// 槽 = 一组叠加特效 prefab,按槽名 Show/Hide,由攻击开始/结束事件驱动。
/// 特效实例化到同名槽子物体下(localPosition=0),槽子物体位置 = 特效出现位置;无同名子物体则挂锚点下。
/// 命中类一次性特效不走本组件(继续 VFXSpawner)。
/// </summary>
public class AttackVFXAnchor : MonoBehaviour
{
    [System.Serializable]
    public class VFXSlot
    {
        [Tooltip("槽名,Show(string) 按此查找;同时按此名找同名字物体作为特效挂点(位置=特效出现位置)。约定:玩家 slot_1/slot_2/slot_3/slot_Air;Boss 普攻 slot_attack/重击 slot_heavy/技能 slot_<skillName>;敌人 slot_attack")]
        public string slotName;

        [Tooltip("本槽叠加的特效 prefab,可多个同时播放(一组同生共死)")]
        public List<GameObject> vfxPrefabs = new List<GameObject>();

        [Tooltip("出现延迟(秒):Show 后等这么久才实例化播放;0 = 立即")]
        public float showDelay = 0f;
    }

    [Header("槽位")]
    [Tooltip("全部槽位,Show(slotName) 按 slotName 查找")]
    public List<VFXSlot> slots = new List<VFXSlot>();

    [Header("保险")]
    [Tooltip("Show 后超过此秒数未 Hide 自动清理(防事件丢失残留)")]
    public float maxLifetime = 10f;

    // 当前活动实例组(整组同生共死)
    private readonly List<GameObject> _active = new List<GameObject>();
    private Coroutine _lifeRoutine;

    /// <summary>显示指定槽(自动先收起上一组;找不到槽只警告不崩)</summary>
    public void Show(string slotName)
    {
        var slot = FindSlot(slotName);
        if (slot == null)
        {
            Debug.LogWarning($"[AttackVFXAnchor] 未找到槽 '{slotName}' (物体: {name})");
            return;
        }

        Hide();  // 收上一组(淡出,不阻塞)

        // 清残留协程:未到时的延迟生成(showDelay 未走完就 Show 新槽)必须停掉,否则旧槽会延迟冒出
        StopAllCoroutines();
        _lifeRoutine = null;

        if (slot.showDelay > 0f)
            StartCoroutine(ShowDelayed(slot, slot.showDelay));
        else
            SpawnGroup(slot);

        _lifeRoutine = StartCoroutine(LifetimeGuard());
    }

    /// <summary>攻击结束:整组停发射,已发射粒子按自身 startLifetime 自然消亡后销毁(淡出)</summary>
    public void Hide()
    {
        // 攻击在 showDelay 未走完时结束:停掉延迟生成协程,避免特效在攻击结束后才冒出
        StopAllCoroutines();
        _lifeRoutine = null;

        if (_active.Count == 0) return;

        foreach (var go in _active)
        {
            if (go == null) continue;
            foreach (var ps in go.GetComponentsInChildren<ParticleSystem>(true))
                ps.Stop();   // 只停发射,粒子继续飞 = 淡出
            Destroy(go, MaxRemainingLifetime(go));
        }
        _active.Clear();
    }

    /// <summary>立即清空不淡出(对象死亡/场景切换/强制打断)</summary>
    public void KillAll()
    {
        StopAllCoroutines();
        _lifeRoutine = null;

        foreach (var go in _active)
            if (go != null) Destroy(go);
        _active.Clear();
    }

    private VFXSlot FindSlot(string slotName)
    {
        if (slots == null) return null;
        foreach (var s in slots)
            if (s != null && s.slotName == slotName) return s;
        return null;
    }

    private IEnumerator ShowDelayed(VFXSlot slot, float delay)
    {
        yield return new WaitForSeconds(delay);
        SpawnGroup(slot);
    }

    /// <summary>实例化整组 prefab 到同名槽子物体下,全部激活 + 强制 Play</summary>
    private void SpawnGroup(VFXSlot slot)
    {
        if (slot.vfxPrefabs == null) return;
        Transform slotRoot = FindSlotRoot(slot.slotName);

        foreach (var prefab in slot.vfxPrefabs)
        {
            if (prefab == null) continue;
            GameObject go = Instantiate(prefab, slotRoot);
            go.transform.localPosition = Vector3.zero;   // 特效中心 = 槽子物体位置;prefab 内部偏移用子物体
            go.name = prefab.name + "_VFX";

            // 团结引擎:Instantiate 复制 prefab 激活状态,inactive 则 Play 不生效 → 强制激活
            go.SetActive(true);

            // Instantiate 复制未播放状态 → 强制全部粒子 Play
            foreach (var ps in go.GetComponentsInChildren<ParticleSystem>(true))
                ps.Play();

            _active.Add(go);
        }
    }

    /// <summary>按槽名找同名字物体作挂点;找不到 = 挂锚点自身</summary>
    private Transform FindSlotRoot(string slotName)
    {
        foreach (Transform child in transform)
            if (child.name == slotName) return child;
        return transform;
    }

    /// <summary>取整组剩余最长粒子寿命(Hide 销毁延迟用;循环粒子停发射后不计)</summary>
    private float MaxRemainingLifetime(GameObject go)
    {
        float max = 0f;
        foreach (var ps in go.GetComponentsInChildren<ParticleSystem>(true))
        {
            if (ps == null || ps.main.loop) continue;
            float l = ps.main.startLifetime.constantMax;
            if (l > max) max = l;
        }
        return max + 0.1f;
    }

    /// <summary>超时保险:Show 后 maxLifetime 秒未 Hide → 自动清理(检查攻击结束事件是否接入)</summary>
    private IEnumerator LifetimeGuard()
    {
        yield return new WaitForSeconds(maxLifetime);
        Hide();
        Debug.LogWarning($"[AttackVFXAnchor] 槽超时未 Hide,自动清理(检查攻击结束事件) {name}");
    }
}
