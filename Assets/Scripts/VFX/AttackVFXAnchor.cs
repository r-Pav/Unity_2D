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
/// 背刺刀光(PlayBackstabVfx / PlayBackstabSingle)生成后保留世界变换脱离锚点、固定在本刀落点:
/// 连打每刀都瞬移,留在锚点下会把上一刀没播完的刀光一起搬到下一格(2026-09-22)。
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

    // ── 内置音高批(写死;单位 = 半音偏移,加在背刺槽基准 sfxSemitone 上)──
    // 由 MusicTrackData / MusicPointManager 的拍数选批:3 → 三连音批,4/未填 → 四连音批。
    // 连音组每进一组抽一批,组内按刀序【循环】取音(见 BackstabChainPitch);单点背刺走批的循环游标。

    /// <summary>3 拍批(3/4):组内按刀序循环取音</summary>
    private static readonly int[][] PitchSets3 =
    {
        new[] { 0, 4, 7 },      // 大三和弦 do mi sol
        new[] { 0, 2, 4 },      // 全音递增 do re mi
        new[] { 0, 5, 7 },      // 四五度
    };

    /// <summary>4 拍批(4/4):组内按刀序循环取音;曲子没填拍数也走这批</summary>
    private static readonly int[][] PitchSets4 =
    {
        new[] { 0, 4, 7, 12 },  // 大三和弦收八度 do mi sol do'
        new[] { 0, 2, 4, 7 },   // 五声上行 do re mi sol
        new[] { 0, 5, 7, 12 },  // 四度上行收八度
    };

    private int[] _backstabPitchSet;    // 当前音高批(半音数组;null = 还没抽过)
    private int _backstabSetLast = -1;  // 上一次抽到的批内索引(相邻两次避开同一批)
    private int _backstabLoopIndex;     // 单点背刺的循环游标(do → mi → sol → do…)


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

    /// <summary>背刺刀光(→ backstab 槽),只出特效:整组卡点音已在进组时排好(见 ScheduleBackstabGroup)。
    /// 刀光固定在本刀落点(脱离锚点),不被后续连打瞬移搬走。</summary>
    public void PlayBackstabVfx(float vfxDelay = 0f) => PlayComboSlot(backstab, 0, null, 0.0, false, true, vfxDelay, detachFromAnchor: true);

    /// <summary>背刺动作音效(→ backstab 槽),单点路径(自动重音 / Boss 判定链 / 手按单点背刺):
    /// 音高 = 批的循环游标取一个音(do → mi → sol → do…,起点为槽基准半音);
    /// scheduleDsp &gt; 0 = 排到该 dspTime 播,0 = 立即播。</summary>
    public void PlayBackstabSingle(double scheduleDsp = 0.0)
        => PlayComboSlot(backstab, 0, NextLoopPitch(), scheduleDsp, false, false, 0f, detachFromAnchor: true);

    /// <summary>只播背刺音效、不动特效(不 spawn、也不 Hide 上一组):连音路径整组排程用。
    /// hitStep ≥ 0 = 本组第几个音(0 起,从「本次进组那个点」算)→ 组内循环取批里的音;
    /// 传 -1 = 单点(长度 1 的孤立标点) → 走批的循环游标,连续几次单点背刺 do → mi → sol 轮着来。</summary>
    public void PlayBackstabSfxOnly(double scheduleDsp = 0.0, int hitStep = -1)
        => PlayComboSlot(backstab, 0, hitStep >= 0 ? (float?)BackstabChainPitch(hitStep) : NextLoopPitch(), scheduleDsp, true, false);

    /// <summary>
    /// 连音组进入时调(PlayerBackstabState.BindChainGroup):把整组卡点音一次性排到各自标点上。
    /// 2026-09-21 定稿口径:点表就是时间轴 —— 到点自然响,不再靠「提前出刀」去抢排程位置。
    /// 已经过去的点(中途进组 / 换组)跳过;目标 dsp 落在过去 = Unity 立即播,不特判。
    /// </summary>
    public void ScheduleBackstabGroup(float[] points, int fromIndex = 0)
    {
        if (points == null || points.Length == 0) return;
        var mgr = MusicPointManager.Instance;
        if (mgr == null) return;

        // 从「本次进组的那个点」开始排:中途进组时前面的点不补响(那一刀没出手);
        // 进组这一点自己可能刚好在现在附近,目标落在过去 = Unity 立即播,不再用时间阈值把它们也跳过。
        int start = Mathf.Clamp(fromIndex, 0, points.Length);
        if (start >= points.Length) return;

        // 单点组(长度 1 的孤立标点):音高走批的循环游标。
        // 不特判的话每个孤立点都自成一组 → hitStep 恒为 0 → 永远取批里第一个音 = 单点背刺全是原调
        // (2026-09-22 saika 报「连音没问题但被刺还是全是原调」)。
        if (points.Length - start <= 1)
        {
            PlayBackstabSfxOnly(mgr.DspTimeForPoint(points[start]), -1);
            return;
        }

        // 真连音组(长度 > 1):抽一批(相邻两组避开同一批)+ 循环游标归零(换调性后从组内第一个音重新起)
        DrawBackstabPitchSet();
        _backstabLoopIndex = 0;

        // 音高:组内按刀序【循环】取批里的音(do mi sol → do mi sol…);
        // 序号按「本次进组」重算 → 中途进组也从组内第一个音起。
        for (int i = start; i < points.Length; i++)
            PlayBackstabSfxOnly(mgr.DspTimeForPoint(points[i]), i - start);
    }

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

    /// <summary>抽一批音高:拍数 3 → 三连音批,其余(4/未填/非法)→ 四连音批;批内随机,相邻两次避开同一批。
    /// 连音组进入时抽(ScheduleBackstabGroup),单点背刺首次用时懒抽(不会没音高)。</summary>
    private void DrawBackstabPitchSet()
    {
        var mgr = MusicPointManager.Instance;
        int[][] bank = mgr != null && mgr.BeatsPerBar == 3 ? PitchSets3 : PitchSets4;

        int idx = Random.Range(0, bank.Length);
        for (int i = 0; i < 8 && bank.Length > 1 && idx == _backstabSetLast; i++)
            idx = Random.Range(0, bank.Length);
        _backstabSetLast = idx;
        _backstabPitchSet = bank[idx];
    }

    /// <summary>连音组内第 hitStep 个卡点音的音高:组内按刀序【循环】取批里的音(do mi sol → do mi sol…)。
    /// 不用「到顶夹最后一个」——那样 3 音批下 11 刀会响成 do mi sol sol sol…(2026-09-21 那句「3 个一组」的来源)。
    /// 起点 = 背刺槽基准半音(sfxSemitone),Inspector 里能整体升降调。</summary>
    private float BackstabChainPitch(int hitStep)
    {
        if (_backstabPitchSet == null || _backstabPitchSet.Length == 0) DrawBackstabPitchSet();
        int baseSemi = backstab != null ? backstab.sfxSemitone : 0;
        int idx = Mathf.Max(0, hitStep) % _backstabPitchSet.Length;   // 循环取音
        return AudioManager.PitchFromSemitone(baseSemi + _backstabPitchSet[idx]);
    }

    /// <summary>单点背刺的音高倍率:批的循环游标取一个音,并把游标推到下一个(do → mi → sol → do…)。
    /// 一轮走完(游标回到 0)时为下一轮重抽一批(相邻避开同一批)—— 单点背刺不会一直循环同一组
    /// (2026-09-22 saika 定;真连音组不重抽,组内固定用进组抽到的那批)。</summary>
    private float NextLoopPitch()
    {
        if (_backstabPitchSet == null || _backstabPitchSet.Length == 0) DrawBackstabPitchSet();
        int baseSemi = backstab != null ? backstab.sfxSemitone : 0;
        int len = _backstabPitchSet.Length;
        int idx = _backstabLoopIndex % len;
        _backstabLoopIndex = (idx + 1) % len;

        float pitch = AudioManager.PitchFromSemitone(baseSemi + _backstabPitchSet[idx]);
        if (_backstabLoopIndex == 0) DrawBackstabPitchSet();   // 本轮走完 → 下一轮换一批
        return pitch;
    }


    /// <summary>播放一个玩家分组槽(VFX / 音效各自判空,两个都空则静默跳过;自动收上一组)。
    /// scheduleDsp &gt; 0 = 音效排到该 dspTime 播(背刺卡点:与 BGM 走同一个音频时钟);0 = 立即播(普通攻击槽的默认行为)。
    /// sfxOnly = 只出声、不 spawn 特效也不收上一组特效:连音自动连打的空挥刀用。
    /// detachFromAnchor = 特效生成后保留世界变换脱离锚点(背刺刀光:固定在本刀落点,不跟随瞬移)。</summary>
    private void PlayComboSlot(ComboSlot slot, int comboIndex, float? pitchOverride = null, double scheduleDsp = 0.0, bool sfxOnly = false, bool vfxOnly = false, float vfxDelay = 0f, bool detachFromAnchor = false)
    {
        if (slot == null) return;

        bool hasVfx = slot.prefab != null;
        bool hasSfx = slot.sfx != null;
        if (!hasVfx && !hasSfx) return;   // 未配置:不播不警告
        if (sfxOnly && !hasSfx) return;   // 只要出声但这条槽没配音效:静默跳过

        if (!sfxOnly) Hide();  // 收上一组(与是否配 VFX 无关,无 VFX 时内部空转)

        // 挥刀音效(showDelay 只作用于 VFX 延迟生成,不影响音效手感)
        // comboIndex = 连击段号(1 起)→ 半音偏移 → pitch(段1 原调 / 段2 大三度 / 段3 纯五度);0 = 不变调
        if (hasSfx && !vfxOnly)
        {
            float pitch = pitchOverride ?? ComboPitch(slot, comboIndex);

            var am = AudioManager.Instance;

            if (scheduleDsp > 0.0)
            {
                am?.PlaySfxScheduled(slot.sfx, slot.sfxVolume, scheduleDsp, pitch);
            }
            else
            {
                am?.PlaySfx(slot.sfx, slot.sfxVolume, pitch);
            }
        }

        if (sfxOnly || !hasVfx) return;   // 只出声 / 只配了音效:不动特效,也不启动特效超时保险

        if (_delayedRoutine != null) { StopCoroutine(_delayedRoutine); _delayedRoutine = null; }
        if (_lifeRoutine != null) { StopCoroutine(_lifeRoutine); _lifeRoutine = null; }

        float spawnDelay = slot.showDelay + vfxDelay;   // 槽自带延迟 + 本刀要求的延迟(刀光等回打击帧)
        if (spawnDelay > 0f)
            _delayedRoutine = StartCoroutine(ShowDelayedPrefab(slot.prefab, spawnDelay, detachFromAnchor));
        else
            SpawnPrefab(slot.prefab, detachFromAnchor);

        _lifeRoutine = StartCoroutine(LifetimeGuard());
    }


    private IEnumerator ShowDelayedPrefab(GameObject prefab, float delay, bool detachFromAnchor = false)
    {
        yield return new WaitForSeconds(delay);
        _delayedRoutine = null;
        SpawnPrefab(prefab, detachFromAnchor);
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

    /// <summary>玩家分组槽:特效实例挂 attack_VFX(锚点)原点,prefab 内部自带相对位置。
    /// detachFromAnchor = 生成后保留世界变换脱离父级(背刺刀光用,见 SpawnPrefabTo)</summary>
    private void SpawnPrefab(GameObject prefab, bool detachFromAnchor = false)
        => SpawnPrefabTo(prefab, detachFromAnchor ? null : transform);

    /// <summary>池化取实例挂到指定父节点(localPosition=0),清残留后全粒子 Play。
    /// parent 为 null = 保留生成那一刻的世界变换脱离父级:实例固定在这一刀的落点与朝向,
    /// 之后玩家再瞬移(连打每刀左右交替)不会把刚生成 / 还在播的刀光一起搬走。
    /// 先在锚点原点对齐再脱离,所以 prefab 内部偏移与朝向镜像与挂在锚点下完全一致。</summary>
    private void SpawnPrefabTo(GameObject prefab, Transform parent)
    {
        GameObject go = Acquire(prefab);
        if (parent == null)
        {
            // 背刺刀光:先在锚点原点对齐(与常规挂法完全等价,prefab 内部偏移不变),再保留世界变换脱离父级。
            float facing = transform.lossyScale.x < 0f ? -1f : 1f;   // 玩家朝向(父链 scale 的符号)
            go.transform.SetParent(transform, false);
            go.transform.localPosition = Vector3.zero;
            go.transform.SetParent(null, true);   // worldPositionStays:位置 / 旋转 / 缩放留在本刀落点
            // Unity 的粒子系统不吃负缩放:父级 scale.x = -1 时 SpriteRenderer 会镜像,粒子却不会
            // (负 scale 下粒子朝向不翻,部分材质甚至直接不渲染 —— 这是粒子系统的已知行为)。
            // 所以这里不继承那个负号:scale 取绝对值,用「绕 Y 轴 180°」表达朝左
            // (2D 里等价于 flipX,子物体位置会一起镜像)。
            Vector3 s = go.transform.localScale;
            go.transform.localScale = new Vector3(Mathf.Abs(s.x), Mathf.Abs(s.y), Mathf.Abs(s.z));
            if (facing < 0f) go.transform.rotation = Quaternion.Euler(0f, 180f, 0f) * go.transform.rotation;
        }
        else
        {
            go.transform.SetParent(parent, false);
            go.transform.localPosition = Vector3.zero;
        }

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

        // 复用实例可能有残留的变换(背刺刀光脱离父级时会写 scale/rotation)→ 按 prefab 原始值复位
        go.transform.localRotation = prefab.transform.localRotation;
        go.transform.localScale = prefab.transform.localScale;

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
