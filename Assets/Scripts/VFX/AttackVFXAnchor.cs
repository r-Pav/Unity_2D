using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 攻击 VFX 锚点 / 统一管理器 — 挂在攻击者的 attack_VFX 子物体上。
/// 玩家侧(2026-09-07 saika 拍板结构):Inspector 直接三块大组,不嵌套数组、无槽名字符串 ——
///   地面攻击:段1/段2/段3 各一个特效 prefab 槽(ground1/2/3)
///   空中攻击:段1/段2/段3 各一个特效 prefab 槽(air1/2/3)
///   被刺:一个背刺特效 prefab 槽(backstab)
///   地图元素冲刺:一个冲刺特效 prefab 槽(mapDash)
///   特效 prefab 位置/大小在 prefab 内调好(相对 attack_VFX 原点,挂 attack_VFX 子物体下,localPosition=0)。
///   统一入口(攻击开始/切段/结束事件调用):PlayGround(1~3) / PlayAir(1~3) / PlayBackstab() / PlayMapDash() / Stop()。
/// 通用槽(Boss/敌人用,玩家不填):slots + Show("slot_xxx"),保留原按名查找/同名槽子物体挂点逻辑。
/// 实例管理收拢:同 prefab 池化复用;Hide = 停发射 + 粒子飞完延迟回池(保留淡出);KillAll = 立即回池。
/// 命中类一次性特效不走本组件(继续 VFXSpawner)。
/// </summary>
public class AttackVFXAnchor : MonoBehaviour
{
    /// <summary>通用槽(旧,Show 按名字;Boss/敌人用,玩家不填)</summary>
    [System.Serializable]
    public class VFXSlot
    {
        [Tooltip("槽名,Show(string) 按此查找;同时按此名找同名字物体作为特效挂点。Boss slot_attack/slot_heavy/slot_<技能名>;敌人 slot_attack")]
        public string slotName;

        [Tooltip("本槽叠加的特效 prefab,可多个同时播放(一组同生共死)")]
        public List<GameObject> vfxPrefabs = new List<GameObject>();

        [Tooltip("出现延迟(秒):Show 后等这么久才实例化播放;0 = 立即")]
        public float showDelay = 0f;
    }

    /// <summary>玩家分组槽:一段攻击一个特效 prefab + 一个挥刀音效(位置/大小在 prefab 内调好;多个粒子效果放同一 prefab 子物体)</summary>
    [System.Serializable]
    public class ComboSlot
    {
        [Tooltip("本段攻击特效 prefab(空 = 未配置,播放静默跳过)")]
        public GameObject prefab;

        [Tooltip("出现延迟(秒):播放后等这么久才实例化;0 = 立即")]
        public float showDelay = 0f;

        [Tooltip("本段挥刀音效(空 = 不播)。与特效同槽配置,攻击起手/切段瞬间立即播放")]
        public AudioClip sfx;

        [Tooltip("挥刀音效相对音量(最终响度 = 设置面板 SFX 音量 × 此值)")]
        [Range(0f, 1f)] public float sfxVolume = 1f;

        [Tooltip("挥刀音基准变调(半音,滑动条):0 = 原调,4 = 大三度,7 = 纯五度,12 = 八度")]
        [Range(-12, 12)] public int sfxSemitone = 0;

        [Tooltip("本槽连击每段递增半音(第 2 段起):0 = 每段同音,4 = do/mi/升sol")]
        [Range(0, 12)] public int sfxRisePerStep = 0;
    }

    [Header("通用槽(Show 按名字 — Boss/敌人用,玩家不填)")]
    [Tooltip("全部通用槽,Show(slotName) 按 slotName 查找;实例挂到同名字物体下")]
    public List<VFXSlot> slots = new List<VFXSlot>();

    [Header("玩家 · 地面攻击(PlayGround 1~3)")]
    public ComboSlot ground1 = new ComboSlot();
    public ComboSlot ground2 = new ComboSlot();
    public ComboSlot ground3 = new ComboSlot();

    [Header("玩家 · 空中攻击(PlayAir 1~3)")]
    public ComboSlot air1 = new ComboSlot();
    public ComboSlot air2 = new ComboSlot();
    public ComboSlot air3 = new ComboSlot();

    [Header("玩家 · 被刺(PlayBackstab)")]
    public ComboSlot backstab = new ComboSlot();

    [Header("玩家 · 背刺命中音的音高(素材与音量在敌人身上,音高在攻击者侧调)")]
    [Tooltip("勾上 = 背刺每一刀的音高从下面的和谐音程池里随机取一个(相邻两刀避开同音);不勾 = 背刺槽的基准 + 每段递增滑条")]
    public bool backstabRandomHarmony = false;

    [Tooltip("背刺和谐音程池(半音偏移,加在背刺槽基准上):0=do 3=降mi 4=mi 5=fa 7=sol 9=la 12=do'")]
    public int[] backstabHarmonySemitones = { 0, 4, 7, 12 };

    /// <summary>上一刀抽到的和谐音程(运行时,避开相邻重复用;不序列化)</summary>
    private int _lastBackstabHarmony = int.MinValue;

    [Header("玩家 · 地图元素冲刺(PlayMapDash)")]
    public ComboSlot mapDash = new ComboSlot();

    [Header("保险")]
    [Tooltip("播放后超过此秒数未 Stop 自动清理(防事件丢失残留)")]
    public float maxLifetime = 10f;

    // ── 播放中的实例记录 ──
    private class ActiveVFX
    {
        public GameObject instance;
        public GameObject prefab;          // 回池 key
        public Coroutine recycleRoutine;   // 延迟回池协程句柄(null = 未启动)
    }

    private readonly List<ActiveVFX> _active = new List<ActiveVFX>();     // 当前可见组
    private readonly List<ActiveVFX> _recycling = new List<ActiveVFX>();  // Stop 后等粒子飞完、延迟回池中
    private readonly Dictionary<GameObject, Stack<GameObject>> _pool = new Dictionary<GameObject, Stack<GameObject>>();
    private Coroutine _lifeRoutine;     // 超时保险句柄
    private Coroutine _delayedRoutine;  // showDelay 延迟生成句柄

    // ============================================================
    // 玩家侧统一入口(攻击开始/切段/结束事件调用;空槽/未配置 = 静默跳过)
    // ============================================================

    /// <summary>地面连击段特效(1~3 → ground1/2/3;越界自动钳)</summary>
    public void PlayGround(int comboIndex) => PlayComboSlot(GetSlot(ground1, ground2, ground3, comboIndex), comboIndex);

    /// <summary>空中连击段特效(1~3 → air1/2/3;越界自动钳)</summary>
    public void PlayAir(int comboIndex) => PlayComboSlot(GetSlot(air1, air2, air3, comboIndex), comboIndex);

    /// <summary>背刺特效(→ backstab)</summary>
    /// <summary>背刺动作音效(→ backstab 槽)。hitStep = 组内刀序(0 起),音高走 BackstabSfxPitch(随机和谐音程 / 基准 + 递增)</summary>
    public void PlayBackstab(int hitStep = 0) => PlayComboSlot(backstab, 0, BackstabSfxPitch(hitStep));

    /// <summary>地图元素冲刺特效(→ mapDash)</summary>
    public void PlayMapDash() => PlayComboSlot(mapDash, 0);

    /// <summary>攻击结束:收起当前组(与 Hide 同义,语义化别名)</summary>
    public void Stop() => Hide();

    private static ComboSlot GetSlot(ComboSlot s1, ComboSlot s2, ComboSlot s3, int comboIndex)
    {
        switch (comboIndex)
        {
            case 1: return s1;
            case 2: return s2;
            default: return s3;
        }
    }

    /// <summary>
    /// 挥刀音音高倍率 = 本槽基准半音(sfxSemitone) + 每段递增(sfxRisePerStep) × (段号 - 1),第 1 段不加递增。
    /// 段号 ≤ 0(背刺/冲刺)= 只取本槽基准;基准 0 + 递增 4 → 第1段 do / 第2段 mi / 第3段 升sol。
    /// </summary>
    private float ComboPitch(ComboSlot slot, int comboIndex)
    {
        int step = comboIndex > 1 ? comboIndex - 1 : 0;
        return AudioManager.PitchFromSemitone(slot.sfxSemitone + slot.sfxRisePerStep * step);
    }

    /// <summary>
    /// 背刺那一刀的命中音音高倍率(命中音素材/音量仍在敌人身上,音高在攻击者侧调)。
    /// 勾了随机 → 从 backstabHarmonySemitones 池里随机取一个和谐音程加在背刺槽基准上,相邻两刀避开同音
    /// (抽到与上一刀相同的值就重抽,最多 8 次;池里只有一个值或全是同一个值时无从避开);
    /// 没勾 → 背刺槽基准 + 每段递增 × 刀序(hitStep 为组内刀序,0 起,第 1 刀不加递增)。
    /// </summary>
    public float BackstabSfxPitch(int hitStep)
    {
        int baseSemi = backstab != null ? backstab.sfxSemitone : 0;

        if (backstabRandomHarmony && backstabHarmonySemitones != null && backstabHarmonySemitones.Length > 0)
        {
            int len = backstabHarmonySemitones.Length;
            int semi = backstabHarmonySemitones[Random.Range(0, len)];
            for (int i = 0; i < 8 && semi == _lastBackstabHarmony; i++)
                semi = backstabHarmonySemitones[Random.Range(0, len)];   // 避开上一刀
            _lastBackstabHarmony = semi;
            return AudioManager.PitchFromSemitone(baseSemi + semi);
        }

        int step = hitStep > 0 ? hitStep : 0;
        int rise = backstab != null ? backstab.sfxRisePerStep : 0;
        return AudioManager.PitchFromSemitone(baseSemi + rise * step);
    }

    /// <summary>播放一个玩家分组槽(VFX / 音效各自判空,两个都空则静默跳过;自动收上一组)</summary>
    private void PlayComboSlot(ComboSlot slot, int comboIndex, float? pitchOverride = null)
    {
        if (slot == null) return;

        bool hasVfx = slot.prefab != null;
        bool hasSfx = slot.sfx != null;
        if (!hasVfx && !hasSfx) return;   // 未配置:不播不警告

        Hide();  // 收上一组(与是否配 VFX 无关,无 VFX 时内部空转)

        // 挥刀音效立即播(showDelay 只作用于 VFX 延迟生成,不影响音效手感)
        // comboIndex = 连击段号(1 起)→ 半音偏移 → pitch(段1 原调 / 段2 大三度 / 段3 纯五度);0 = 不变调
        if (hasSfx)
            AudioManager.Instance?.PlaySfx(slot.sfx, slot.sfxVolume, pitchOverride ?? ComboPitch(slot, comboIndex));

        if (!hasVfx) return;   // 只配了音效:不动特效,也不启动特效超时保险

        if (_delayedRoutine != null) { StopCoroutine(_delayedRoutine); _delayedRoutine = null; }
        if (_lifeRoutine != null) { StopCoroutine(_lifeRoutine); _lifeRoutine = null; }

        if (slot.showDelay > 0f)
            _delayedRoutine = StartCoroutine(ShowDelayedPrefab(slot.prefab, slot.showDelay));
        else
            SpawnPrefab(slot.prefab);

        _lifeRoutine = StartCoroutine(LifetimeGuard());
    }

    private IEnumerator ShowDelayedPrefab(GameObject prefab, float delay)
    {
        yield return new WaitForSeconds(delay);
        _delayedRoutine = null;
        SpawnPrefab(prefab);
    }

    // ============================================================
    // 通用 Show / Hide / KillAll(玩家入口与 Boss/敌人共用)
    // ============================================================

    /// <summary>显示指定通用槽(按名字;自动先收起上一组;找不到槽只警告不崩)</summary>
    public void Show(string slotName)
    {
        var slot = FindSlot(slotName);
        if (slot == null)
        {
            Debug.LogWarning($"[AttackVFXAnchor] 未找到通用槽 '{slotName}' (物体: {name})");
            return;
        }

        Hide();  // 收上一组(停发射,淡出后回池,不阻塞)

        if (_delayedRoutine != null) { StopCoroutine(_delayedRoutine); _delayedRoutine = null; }
        if (_lifeRoutine != null) { StopCoroutine(_lifeRoutine); _lifeRoutine = null; }

        if (slot.showDelay > 0f)
            _delayedRoutine = StartCoroutine(ShowDelayed(slot, slot.showDelay));
        else
            SpawnGroup(slot);

        _lifeRoutine = StartCoroutine(LifetimeGuard());
    }

    /// <summary>攻击结束:当前组停发射,已发射粒子按自身 startLifetime 自然消亡后回池(淡出尾迹)</summary>
    public void Hide()
    {
        // showDelay 未走完时结束:停掉延迟生成,避免特效在攻击结束后才冒出
        if (_delayedRoutine != null) { StopCoroutine(_delayedRoutine); _delayedRoutine = null; }
        // 超时保险句柄停掉(正常结束不再触发)
        if (_lifeRoutine != null) { StopCoroutine(_lifeRoutine); _lifeRoutine = null; }

        if (_active.Count == 0) return;

        foreach (var e in _active)
        {
            StopParticles(e.instance);   // 只停发射,粒子继续飞 = 淡出
            RecycleLater(e);
        }
        _active.Clear();
    }

    /// <summary>立即清空(对象死亡/场景切换/强制打断):不等淡出,全部立刻回池</summary>
    public void KillAll()
    {
        if (_delayedRoutine != null) { StopCoroutine(_delayedRoutine); _delayedRoutine = null; }
        if (_lifeRoutine != null) { StopCoroutine(_lifeRoutine); _lifeRoutine = null; }

        // 可见组 + 延迟回池中(等粒子飞完的)全部立即回收
        var all = new List<ActiveVFX>(_active.Count + _recycling.Count);
        all.AddRange(_active);
        all.AddRange(_recycling);
        foreach (var e in all)
            RecycleNow(e);
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
        _delayedRoutine = null;
        SpawnGroup(slot);
    }

    /// <summary>通用槽整组实例化到同名槽子物体下(池化取,无则新建)</summary>
    private void SpawnGroup(VFXSlot slot)
    {
        if (slot.vfxPrefabs == null) return;
        Transform slotRoot = FindSlotRoot(slot.slotName);

        foreach (var prefab in slot.vfxPrefabs)
        {
            if (prefab == null) continue;
            SpawnPrefabTo(prefab, slotRoot);
        }
    }

    /// <summary>玩家分组槽:特效实例挂 attack_VFX(锚点)原点,prefab 内部自带相对位置</summary>
    private void SpawnPrefab(GameObject prefab) => SpawnPrefabTo(prefab, transform);

    /// <summary>池化取实例挂到指定父节点(localPosition=0),清残留后全粒子 Play</summary>
    private void SpawnPrefabTo(GameObject prefab, Transform parent)
    {
        GameObject go = Acquire(prefab);
        go.transform.SetParent(parent, false);
        go.transform.localPosition = Vector3.zero;

        _active.Add(new ActiveVFX { instance = go, prefab = prefab });
    }

    /// <summary>池化取实例:优先复用同 prefab 空闲实例,否则新建;清残留 + 强制 Play</summary>
    private GameObject Acquire(GameObject prefab)
    {
        GameObject go = null;
        if (_pool.TryGetValue(prefab, out var stack) && stack != null && stack.Count > 0)
            go = stack.Pop();

        if (go == null) go = Instantiate(prefab);

        go.name = prefab.name + "_VFX";
        go.SetActive(true);   // 团结引擎:Instantiate 复制 prefab 激活状态,inactive 则 Play 不生效

        // 复用实例可能有残留:停发射并清空,再统一从头 Play
        foreach (var ps in go.GetComponentsInChildren<ParticleSystem>(true))
        {
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            ps.Play();
        }
        return go;
    }

    /// <summary>Stop 后延迟回池:等最长粒子寿命,自然淡出结束再回收</summary>
    private void RecycleLater(ActiveVFX e)
    {
        _recycling.Add(e);
        e.recycleRoutine = StartCoroutine(RecycleRoutine(e));
    }

    private IEnumerator RecycleRoutine(ActiveVFX e)
    {
        yield return new WaitForSeconds(MaxRemainingLifetime(e.instance));
        e.recycleRoutine = null;
        RecycleNow(e);
    }

    /// <summary>立即回池:停+清粒子、脱离挂点、隐藏备用(从 active/recycling 双列表移除)</summary>
    private void RecycleNow(ActiveVFX e)
    {
        if (e.recycleRoutine != null && _recycling.Contains(e))
        {
            StopCoroutine(e.recycleRoutine);
            e.recycleRoutine = null;
        }
        _recycling.Remove(e);
        _active.Remove(e);

        if (e.instance == null) return;
        foreach (var ps in e.instance.GetComponentsInChildren<ParticleSystem>(true))
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        e.instance.SetActive(false);
        e.instance.transform.SetParent(transform, false);   // 挂回锚点下(隐藏备用,回池)

        if (e.prefab == null) { Destroy(e.instance); return; }
        if (!_pool.TryGetValue(e.prefab, out var stack) || stack == null)
        {
            stack = new Stack<GameObject>();
            _pool[e.prefab] = stack;
        }
        stack.Push(e.instance);
    }

    /// <summary>只停发射(粒子继续飞 = 淡出),不清粒子</summary>
    private static void StopParticles(GameObject go)
    {
        if (go == null) return;
        foreach (var ps in go.GetComponentsInChildren<ParticleSystem>(true))
            ps.Stop(false, ParticleSystemStopBehavior.StopEmitting);
    }

    /// <summary>通用槽:按槽名找同名字物体作挂点;找不到 = 挂锚点自身</summary>
    private Transform FindSlotRoot(string slotName)
    {
        foreach (Transform child in transform)
            if (child.name == slotName) return child;
        return transform;
    }

    /// <summary>取整组剩余最长粒子寿命(延迟回池等待用)</summary>
    private float MaxRemainingLifetime(GameObject go)
    {
        float max = 0f;
        foreach (var ps in go.GetComponentsInChildren<ParticleSystem>(true))
        {
            if (ps == null || ps.main.loop) continue;   // 循环粒子停发射后按粒子寿命自然结束,不额外计
            float l = ps.main.startLifetime.constantMax;
            if (l > max) max = l;
        }
        return max + 0.1f;
    }

    /// <summary>超时保险:播放后 maxLifetime 秒未 Stop → 自动清理(检查攻击结束事件是否接入)</summary>
    private IEnumerator LifetimeGuard()
    {
        yield return new WaitForSeconds(maxLifetime);
        _lifeRoutine = null;
        Hide();
        Debug.LogWarning($"[AttackVFXAnchor] 特效超时未 Stop,自动清理(检查攻击结束事件) {name}");
    }
}
