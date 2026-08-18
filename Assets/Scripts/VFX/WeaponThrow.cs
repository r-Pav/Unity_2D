using System.Collections;
using UnityEngine;

/// <summary>
/// 单次攻击的完整配置(三连击各自一份,完全独立):
/// 路径 / 旋转 / 缩放 / 飞行时间 / 溶解时间
/// </summary>
[System.Serializable]
public class WeaponAttackConfig
{
    [Tooltip("路径(相对武器当前位置的偏移,Scene 里选中可拖)")]
    public Vector3[] pathPoints =
    {
        new Vector3(1.5f, 0f, 0f),
        new Vector3(4f, 0.5f, 0f),
    };

    [Tooltip("飞行固定旋转角度(度)。朝右时 -90 = 水平朝右,0 = 竖直;朝左自动镜像")]
    public float rotationZ = -90f;

    [Tooltip("出现时的放大倍数,1 = 原大小")]
    public float scaleMultiplier = 1f;

    [Tooltip("沿路径飞行全程耗时(秒),速度由快变慢")]
    public float travelDuration = 1f;

    [Tooltip("缓出强度:越大前段甩得越快(2=平方,3=立方,4=四次方)")]
    public float easeOutPower = 2f;

    [Tooltip("飞行结束后溶解消失时长(秒)")]
    public float dissolveInDuration = 1.2f;

    [Tooltip("飞行中碰到墙时插在墙上固定住(第三击用)。false = 正常飞行+溶解")]
    public bool stickToWall = false;

    [Tooltip("插墙后停留时长(秒),随后溶解消失")]
    public float stickHoldDuration = 0.6f;

    [Tooltip("插入墙内深度:检测到墙后沿飞行方向后退此距离再固定(控制剑插进墙多少)")]
    public float stickDepth = 0.2f;

    [Tooltip("该击武器命中敌人时的击退向量(x=水平,按朝向自动镜像;y=垂直击飞)。与 PlayerCombat 基础击退(第三段)向量相加。(0,0) = 该击不附加击退")]
    public Vector2 knockbackForce = Vector2.zero;

    [Tooltip("该击玩家自身攻击位移向量(x=水平前冲,按朝向自动镜像;y=垂直)。命中帧动画事件时施加,与击退同构。(0,0) = 该击无位移")]
    public Vector2 attackShift = Vector2.zero;

    [Tooltip("该击投掷时额外跟随剑飞行的粒子特效 prefab(叠加在武器子级默认特效之上)。留空 = 该击只有默认特效")]
    public GameObject attackVFX;

    [Tooltip("该击特效的显示时长(秒)。>0 时启用:到点停止粒子发射,已发射粒子按自身 Lifetime 自然消亡(循环粒子也能淡出),随后自动销毁。0 = 不启用,走 VFXAutoDestruct 默认逻辑(粒子时长/1.1s)")]
    public float vfxDisplayDuration = 0f;
}

/// <summary>
/// 武器投掷框架(本体 = 呼吸位置的那把剑,只做开关控制):
/// 由攻击动画事件驱动:OnAttackStart1/2/3(三条轨迹) 触发投掷,OnAttackEnd() 触发重生判定。
/// 每次触发 → ① 隐藏本体 ② 呼吸位置生成溶解残影(原位消散)
///       ③ 克隆 weaponTemplate 在曲线第一点出现 ④ clone 沿路径飞行(由快变慢)+ 溶解消失
///       ⑤ 攻击 end 后,若 respawnDelay 秒内没有新的攻击 start,剑重新出现
/// 视觉设置(拖尾/材质/子物体)在 weaponTemplate 上配置,clone 直接继承。
/// </summary>
public class WeaponThrow : MonoBehaviour
{
    [Header("投掷模板")]
    [Tooltip("投掷 clone 的模板。拖尾等视觉设置直接在这个物体上配,投掷时克隆它。留空 = 用自身")]
    [SerializeField] private GameObject weaponTemplate;

    [Header("三连击配置(每击独立,各自的时间/路径/姿态)")]
    [SerializeField] private WeaponAttackConfig attack1 = new WeaponAttackConfig();
    [SerializeField] private WeaponAttackConfig attack2 = new WeaponAttackConfig();
    [SerializeField] private WeaponAttackConfig attack3 = new WeaponAttackConfig();

    [Tooltip("空中攻击轨迹(独立于三连击,空中攻击动画事件调用)")]
    [SerializeField] private WeaponAttackConfig airAttack = new WeaponAttackConfig();

    [Header("重生")]
    [Tooltip("攻击 end 后,若此秒数内没有新的攻击 start,剑才重新出现(连击中断判定)")]
    [SerializeField] private float respawnDelay = 1f;

    [Tooltip("剑重生时的出现时长(秒):从下往上显现。0 = 直接出现")]
    [SerializeField] private float respawnAppearDuration = 0.3f;

    [Header("溶解(三击共用)")]
    [Tooltip("呼吸位置残影溶解消散时长(秒)")]
    [SerializeField] private float dissolveOutDuration = 0.4f;

    [Tooltip("溶解材质(留空自动用 Custom/SpriteDissolve 生成)")]
    [SerializeField] private Material dissolveMaterial;

    [Tooltip("墙 Layer(插墙判定用,stickToWall=true 时生效)")]
    [SerializeField] private LayerMask wallLayer = 1 << 8;

    // ============================================================
    // 运行时状态
    // ============================================================

    private SpriteRenderer _sr;
    private WeaponBreath _breath;
    private Vector3 _breathOrigin;   // 呼吸位置(背后剑的原位,投掷瞬间的世界坐标)

    private int _activeClones;       // 当前在飞的 clone 数量
    private bool _breathHidden;      // 本体是否已隐藏(控制残影只在真正隐藏时生成一次)
    private Coroutine _respawnRoutine;  // 重生等待协程(可被新攻击打断)

    /// <summary>当前在飞 clone 的 BoxCollider2D(攻击范围延伸用,PlayerCombat 命中检测读取)</summary>
    public BoxCollider2D ActiveCloneCollider { get; private set; }

    /// <summary>
    /// 按当前攻击段取武器击退向量(每击独立配置,airAttack 走空中)。PlayerCombat 命中时调用,
    /// x 分量由调用方按朝向镜像。返回 (0,0) 表示该击不附加击退。
    /// </summary>
    public Vector2 GetKnockbackBonus(int comboIndex, bool isAir)
    {
        if (isAir) return airAttack != null ? airAttack.knockbackForce : Vector2.zero;
        switch (comboIndex)
        {
            case 1: return attack1 != null ? attack1.knockbackForce : Vector2.zero;
            case 2: return attack2 != null ? attack2.knockbackForce : Vector2.zero;
            case 3: return attack3 != null ? attack3.knockbackForce : Vector2.zero;
            default: return Vector2.zero;
        }
    }

    /// <summary>
    /// 按当前攻击段取玩家自身攻击位移向量(每击独立配置,airAttack 走空中)。
    /// PlayerCombat 命中帧调用,x 分量由调用方按朝向镜像。返回 (0,0) 表示该击无位移。
    /// </summary>
    public Vector2 GetAttackShift(int comboIndex, bool isAir)
    {
        if (isAir) return airAttack != null ? airAttack.attackShift : Vector2.zero;
        switch (comboIndex)
        {
            case 1: return attack1 != null ? attack1.attackShift : Vector2.zero;
            case 2: return attack2 != null ? attack2.attackShift : Vector2.zero;
            case 3: return attack3 != null ? attack3.attackShift : Vector2.zero;
            default: return Vector2.zero;
        }
    }

    private void Awake()
    {
        _sr = GetComponent<SpriteRenderer>();
        _breath = GetComponent<WeaponBreath>();
    }

    private void Update()
    {
        // 调试用键盘触发(正式版由攻击动画事件调用 OnAttackStart1/2/3)
        if (Input.GetKeyDown(KeyCode.T)) OnAttackStart1();
        else if (Input.GetKeyDown(KeyCode.Y)) OnAttackStart2();
        else if (Input.GetKeyDown(KeyCode.U)) OnAttackStart3();
        else if (Input.GetKeyDown(KeyCode.K)) OnAttackEnd();
    }

    // ============================================================
    // 事件接口(供攻击动画事件调用,三个方法对应三条轨迹)
    // ============================================================

    /// <summary>第一击:用第一条轨迹生成 clone(动画事件选这个方法)</summary>
    public void OnAttackStart1()
    {
        StartAttack(attack1);
    }

    /// <summary>第二击:用第二条轨迹生成 clone(动画事件选这个方法)</summary>
    public void OnAttackStart2()
    {
        StartAttack(attack2);
    }

    /// <summary>第三击:用第三条轨迹生成 clone(动画事件选这个方法)</summary>
    public void OnAttackStart3()
    {
        StartAttack(attack3);
    }

    /// <summary>空中攻击:用空中轨迹生成 clone(空中攻击动画命中帧调用)</summary>
    public void OnAirAttackStart()
    {
        StartAttack(airAttack);
    }

    /// <summary>攻击结束:启动重生判定,respawnDelay 秒内没有新的攻击 start 则剑重新出现</summary>
    public void OnAttackEnd()
    {
        // 无条件启动重生计时(不依赖 clone 计数):
        // 若 clone 已飞完计数归零但动画 end 才到,直接 return 会导致剑永远不回来
        if (_respawnRoutine == null)
        {
            _respawnRoutine = StartCoroutine(RespawnAfterDelay());
        }
    }

    // ============================================================
    // 总调度:各阶段独立方法,后续单独调整
    // ============================================================

    private void StartAttack(WeaponAttackConfig config)
    {
        // 新攻击打断待定的重生
        CancelRespawn();

        _breathOrigin = transform.position;

        // 只有本体还没隐藏时才隐藏+残影(用状态标志,不用 clone 计数):
        // 连发时上一发 clone 飞完计数归零,若用 _activeClones==0 判断会导致残影重复生成 → 背后闪一下
        if (!_breathHidden)
        {
            HideBreathWeapon();
            StartCoroutine(SpawnDissolveEchoAtBreath());
            _breathHidden = true;
        }
        _activeClones++;

        StartCoroutine(ThrowSequence(config));
    }

    private IEnumerator ThrowSequence(WeaponAttackConfig config)
    {
        // 阶段 2:创建 clone 在曲线第一点,独立飞行+溶解
        GameObject projectile = SpawnProjectileAtPathStart(config);

        // 阶段 3:等待 clone 走完全程(飞行+溶解+自毁)
        while (projectile != null)
        {
            yield return null;
        }

        // 这发结束,计数减一;若没有在飞的 clone 了,清掉碰撞引用(避免引用已销毁对象)
        _activeClones--;
        if (_activeClones <= 0)
        {
            ActiveCloneCollider = null;
        }
    }

    // ============================================================
    // 阶段方法(可单独调整)
    // ============================================================

    /// <summary>隐藏本体(关闭):背后的剑消失,拖尾也停(防编辑器访问已停用的 TrailRenderer)</summary>
    private void HideBreathWeapon()
    {
        if (_sr != null) _sr.enabled = false;
        if (_breath != null) _breath.enabled = false;
        var trail = GetComponentInChildren<TrailRenderer>();
        if (trail != null) trail.enabled = false;
    }

    /// <summary>重新显示本体(打开):剑回到呼吸位置,拖尾恢复</summary>
    private void ShowBreathWeapon()
    {
        if (_sr != null) _sr.enabled = true;
        if (_breath != null) _breath.enabled = true;
        var trail = GetComponentInChildren<TrailRenderer>();
        if (trail != null) trail.enabled = true;
    }

    /// <summary>取消待定的重生等待(新攻击开始或已重生时调用)</summary>
    private void CancelRespawn()
    {
        if (_respawnRoutine != null)
        {
            StopCoroutine(_respawnRoutine);
            _respawnRoutine = null;
        }
    }

    /// <summary>重生等待:攻击 end 后间隔 respawnDelay,期间无新攻击则剑从下往上显现回呼吸位置</summary>
    private IEnumerator RespawnAfterDelay()
    {
        yield return new WaitForSeconds(respawnDelay);

        _respawnRoutine = null;
        _activeClones = 0;
        _breathHidden = false;   // 重生后允许下次攻击重新走"隐藏+残影"

        // 本体出现:从下往上显现(溶解量 1 → 0,方向固定上→下,视觉为从下往上长出来)
        if (_sr != null && respawnAppearDuration > 0f)
        {
            // 保存原材质,换溶解材质播出现动画,播完恢复
            Material originalMat = _sr.material;
            Material appearMat = new Material(Shader.Find("Custom/SpriteDissolve"));
            if (appearMat != null && appearMat.shader != null)
            {
                appearMat.SetFloat("_DissolveDir", 1f);
                appearMat.SetFloat("_DissolveAmount", 1f);
                if (_sr.sprite != null) appearMat.mainTexture = _sr.sprite.texture;
                _sr.material = appearMat;
            }
            _sr.enabled = true;

            float appearElapsed = 0f;
            while (appearElapsed < respawnAppearDuration)
            {
                appearElapsed += Time.deltaTime;
                float appear = 1f - Mathf.Clamp01(appearElapsed / respawnAppearDuration);
                if (appearMat != null) appearMat.SetFloat("_DissolveAmount", appear);
                yield return null;
            }
            if (appearMat != null) appearMat.SetFloat("_DissolveAmount", 0f);

            // 恢复原材质
            if (_sr != null && originalMat != null) _sr.material = originalMat;
        }

        ShowBreathWeapon();
    }

    /// <summary>呼吸位置溶解残影:剑离开后,原位留下一道溶解消散的特效</summary>
    private IEnumerator SpawnDissolveEchoAtBreath()
    {
        if (_sr == null || _sr.sprite == null) yield break;

        GameObject echo = new GameObject("WeaponDissolveEcho");
        echo.transform.position = _breathOrigin;
        echo.transform.rotation = transform.rotation;
        echo.transform.localScale = transform.lossyScale;

        SpriteRenderer echoSr = echo.AddComponent<SpriteRenderer>();
        echoSr.sprite = _sr.sprite;
        echoSr.sortingOrder = _sr.sortingOrder;

        Material mat = new Material(Shader.Find("Custom/SpriteDissolve"));
        echoSr.material = mat;

        float elapsed = 0f;
        while (elapsed < dissolveOutDuration)
        {
            elapsed += Time.deltaTime;
            mat.SetFloat("_DissolveAmount", Mathf.Clamp01(elapsed / dissolveOutDuration));
            yield return null;
        }
        Destroy(echo);
    }

    /// <summary>克隆模板生成投掷物在曲线第一点,返回引用(用于等待其自毁)</summary>
    private GameObject SpawnProjectileAtPathStart(WeaponAttackConfig config)
    {
        // 模板:优先用 weaponTemplate,留空用自身(默认剑)
        GameObject template = weaponTemplate != null ? weaponTemplate : gameObject;
        GameObject proj = Instantiate(template);
        proj.name = "WeaponThrowClone";

        // 清掉克隆来的控制组件:clone 只保留视觉(渲染/拖尾),不响应输入/不呼吸
        var clonedThrow = proj.GetComponent<WeaponThrow>();
        if (clonedThrow != null) Destroy(clonedThrow);
        var clonedBreath = proj.GetComponent<WeaponBreath>();
        if (clonedBreath != null) Destroy(clonedBreath);

        // 关键:Instantiate 复制的是模板当前状态,而本体已被 HideBreathWeapon 禁用,
        // clone 的 SpriteRenderer/TrailRenderer 也是禁用状态 → 强制启用,否则画面没东西
        var projSr = proj.GetComponent<SpriteRenderer>();
        if (projSr != null) projSr.enabled = true;
        var projTrail = proj.GetComponentInChildren<TrailRenderer>();
        if (projTrail != null) projTrail.enabled = true;

        // 攻击范围延伸:clone 上的 BoxCollider2D 强制启用(本体保持 disabled),
        // 并记录引用供 PlayerCombat 命中检测读取
        var projCol = proj.GetComponent<BoxCollider2D>();
        if (projCol != null)
        {
            projCol.enabled = true;
            ActiveCloneCollider = projCol;
        }

        // 创建在呼吸位置基准点;WeaponProjectile 内部 _origin = 此位置,
        // 飞行时 _origin + 路径偏移,起点即曲线第一点。若创建时就偏移,会双重偏移。
        proj.transform.position = _breathOrigin;

        // 关键:模板在玩家身上是缩小状态(w1 的 scale=0.04),投掷必须恢复标准大小,
        // 否则 clone 继承 0.04 → 剑和拖尾都小到看不见(之前 new GameObject 方案 scale 默认 1 才正常)
        proj.transform.localScale = Vector3.one;

        // 该击独立特效:直接实例化到 PlayerVFX 容器(不挂 clone 子级),用跟随组件每帧同步位置。
        // 播放生命周期独立——剑销毁后跟随停止,粒子按自身 Lifetime 播完,由 VFXAutoDestruct 自动销毁。
        // 与武器子级默认特效(模板继承,PlayOnAwake 自动播)叠加。
        if (config.attackVFX != null)
        {
            GameObject vfx = VFXSpawner.SpawnOnPlayer(config.attackVFX, proj.transform.position);
            if (vfx != null)
            {
                vfx.name = "AttackVFX";

                // prefab 根物体若是 inactive 状态,Instantiate 出来也是 inactive,Play 不生效 → 强制激活
                vfx.SetActive(true);

                // Instantiate 复制禁用状态 → 所有粒子系统强制播放(团结引擎 ParticleSystem 无 enabled 属性,Play 即可)
                var particleSystems = vfx.GetComponentsInChildren<ParticleSystem>(true);
                foreach (var ps in particleSystems)
                {
                    ps.Play();
                }

                // 跟随剑运动:每帧把特效位置同步到剑当前位置(武器位移驱动特效位置);
                // 剑销毁后 _target == null,同步自动停止,特效原地残留至粒子播完
                var follower = vfx.GetComponent<VFXFollowTarget>();
                if (follower == null) follower = vfx.AddComponent<VFXFollowTarget>();
                follower.Init(proj.transform, Vector3.zero, followRotation: true);

                // 显示时长控制:vfxDisplayDuration > 0 时用 VFXTimedFade 定时淡出,
                // 并移除 VFXSpawner 自动挂的 VFXAutoDestruct(否则它会按粒子时长/1.1s 提前销毁,冲突)
                if (config.vfxDisplayDuration > 0f)
                {
                    var autoDestruct = vfx.GetComponent<VFXAutoDestruct>();
                    if (autoDestruct != null) Destroy(autoDestruct);

                    var timedFade = vfx.GetComponent<VFXTimedFade>();
                    if (timedFade == null) timedFade = vfx.AddComponent<VFXTimedFade>();
                    timedFade.Init(config.vfxDisplayDuration);
                }
            }
        }

        // 依据世界翻转方向决定旋转(父级 flip 后仍是朝右)
        float face = Mathf.Sign(transform.lossyScale.x);
        float rotZ = face >= 0f ? config.rotationZ : -config.rotationZ;
        proj.transform.rotation = Quaternion.Euler(0f, 0f, rotZ);

        // 路径镜像:轨迹锚点是"朝右"配置的相对偏移,朝左时 x 取反,
        // 否则剑会沿朝右的路径飞到身后。复制数组,不改序列化配置。
        Vector3[] path = config.pathPoints;
        if (face < 0f && path != null && path.Length > 0)
        {
            path = new Vector3[path.Length];
            for (int i = 0; i < config.pathPoints.Length; i++)
            {
                Vector3 p = config.pathPoints[i];
                path[i] = new Vector3(-p.x, p.y, p.z);
            }
        }

        // 应用放大倍数(基于标准大小 1,不是模板的 0.04)
        if (config.scaleMultiplier != 1f)
        {
            Vector3 s = proj.transform.localScale;
            proj.transform.localScale = new Vector3(s.x * config.scaleMultiplier, s.y * config.scaleMultiplier, s.z);
        }

        // 溶解方向:固定 1 = 上→下(剑从顶部开始往下消退)
        // 竖直方向不受左右翻转影响,无需按 face 区分
        float dissolveDir = 0f;

        WeaponProjectile comp = proj.AddComponent<WeaponProjectile>();
        comp.Init(
            pathPoints: path,
            travelDuration: config.travelDuration,
            easeOutPower: config.easeOutPower,
            dissolveDuration: config.dissolveInDuration,
            dissolveDirection: dissolveDir,
            dissolveMaterial: dissolveMaterial,
            stickToWall: config.stickToWall,
            stickHoldDuration: config.stickHoldDuration,
            stickDepth: config.stickDepth,
            wallLayer: wallLayer,
            followTarget: transform);

        return proj;
    }

    // ============================================================
    // 编辑器可视化:选中武器时画三条路径曲线,拖点即见
    // 第一击黄 / 第二击青 / 第三击品红
    // ============================================================

    private void OnDrawGizmosSelected()
    {
        DrawPathGizmo(attack1.pathPoints, new Color(1f, 0.85f, 0.3f, 0.9f));
        DrawPathGizmo(attack2.pathPoints, new Color(0.3f, 0.85f, 1f, 0.9f));
        DrawPathGizmo(attack3.pathPoints, new Color(1f, 0.4f, 0.8f, 0.9f));
        DrawPathGizmo(airAttack.pathPoints, new Color(0.4f, 1f, 0.6f, 0.9f));  // 空中轨迹:绿色
    }

    private void DrawPathGizmo(Vector3[] path, Color color)
    {
        if (path == null || path.Length == 0) return;

        Vector3 origin = transform.position;
        Gizmos.color = color;

        // 沿 Catmull-Rom 曲线采样 32 段,画出平滑曲线
        Vector3 prev = origin + ProjectilePathPoint(path, 0f);
        for (int k = 1; k <= 32; k++)
        {
            Vector3 cur = origin + ProjectilePathPoint(path, k / 32f);
            Gizmos.DrawLine(prev, cur);
            prev = cur;
        }

        // 锚点
        Gizmos.color = new Color(color.r, color.g, color.b, 1f);
        for (int i = 0; i < path.Length; i++)
        {
            Gizmos.DrawWireSphere(origin + path[i], 0.08f);
        }
    }

    // 编辑器画线用的 Catmull-Rom(与 WeaponProjectile 相同算法)
    private Vector3 ProjectilePathPoint(Vector3[] path, float t)
    {
        int n = path.Length;
        if (n == 0) return Vector3.zero;
        if (n == 1) return path[0];

        float scaled = t * (n - 1);
        int i = Mathf.Clamp(Mathf.FloorToInt(scaled), 0, n - 2);
        float s = scaled - i;

        Vector3 p0 = path[Mathf.Max(i - 1, 0)];
        Vector3 p1 = path[i];
        Vector3 p2 = path[i + 1];
        Vector3 p3 = path[Mathf.Min(i + 2, n - 1)];

        float s2 = s * s;
        float s3 = s2 * s;

        return 0.5f * (
            (2f * p1) +
            (-p0 + p2) * s +
            (2f * p0 - 5f * p1 + 4f * p2 - p3) * s2 +
            (-p0 + 3f * p1 - 3f * p2 + p3) * s3
        );
    }
}
