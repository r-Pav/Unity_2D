using UnityEditor;
using UnityEngine;

/// <summary>
/// 粒子特效模板生成器(Tools/粒子特效)。
/// 一键在 Assets/Prefab/VFX 下生成带 Trails 拖尾的粒子 prefab(飞行光点 + 渐隐拖尾),
/// 生成后用 Particle System 窗口微调视觉。纯编辑器工具,不进游戏、不碰场景。
/// </summary>
public static class ParticleFXGenerator
{
    private const string OutputDir = "Assets/Prefab/VFX";

    [MenuItem("Tools/粒子特效/Trail 拖尾模板")]
    public static void CreateTrailTemplate()
    {
        string path = OutputDir + "/ParticleTrail_Template.prefab";

        if (!AssetDatabase.IsValidFolder(OutputDir))
        {
            Debug.LogError("[ParticleFXGenerator] 输出目录不存在:" + OutputDir);
            return;
        }
        if (AssetDatabase.LoadAssetAtPath<GameObject>(path) != null)
            AssetDatabase.DeleteAsset(path);

        GameObject root = new GameObject("ParticleTrail_Template");
        ParticleSystem ps = root.AddComponent<ParticleSystem>();
        ConfigureTrail(ps);

        ParticleSystemRenderer rend = root.GetComponent<ParticleSystemRenderer>();
        Material mat = AssetDatabase.LoadAssetAtPath<Material>("Assets/Epic Toon FX/Materials/Glows/glow.mat");
        if (mat != null)
        {
            rend.sharedMaterial = mat;
            rend.trailMaterial = mat;
        }

        GameObject prefab = PrefabUtility.SaveAsPrefabAsset(root, path);
        Object.DestroyImmediate(root);

        if (prefab != null)
        {
            EditorGUIUtility.PingObject(prefab);
            Selection.activeObject = prefab;
            Debug.Log("[ParticleFXGenerator] 已生成:" + path, prefab);
        }
        else
        {
            Debug.LogError("[ParticleFXGenerator] prefab 保存失败:" + path);
        }
    }

    [MenuItem("Tools/粒子特效/单体条状拖尾")]
    public static void CreateSingleTrailTemplate()
    {
        string path = OutputDir + "/Particle_SingleTrail_Template.prefab";

        if (!AssetDatabase.IsValidFolder(OutputDir))
        {
            Debug.LogError("[ParticleFXGenerator] 输出目录不存在:" + OutputDir);
            return;
        }
        if (AssetDatabase.LoadAssetAtPath<GameObject>(path) != null)
            AssetDatabase.DeleteAsset(path);

        GameObject root = new GameObject("Particle_SingleTrail_Template");
        ParticleSystem ps = root.AddComponent<ParticleSystem>();
        ConfigureSingleTrail(ps);

        ParticleSystemRenderer rend = root.GetComponent<ParticleSystemRenderer>();
        Material mat = AssetDatabase.LoadAssetAtPath<Material>("Assets/Epic Toon FX/Materials/Glows/glow.mat");
        if (mat != null)
        {
            rend.sharedMaterial = mat;
            rend.trailMaterial = mat;
        }

        GameObject prefab = PrefabUtility.SaveAsPrefabAsset(root, path);
        Object.DestroyImmediate(root);

        if (prefab != null)
        {
            EditorGUIUtility.PingObject(prefab);
            Selection.activeObject = prefab;
            Debug.Log("[ParticleFXGenerator] 已生成:" + path, prefab);
        }
        else
        {
            Debug.LogError("[ParticleFXGenerator] prefab 保存失败:" + path);
        }
    }

    /// <summary>
    /// 单体条状拖尾:不持续喷射,每轮 Burst 只发 1 个粒子,沿 XY 平面内飞行,
    /// 轨迹拖成一条连续的带状尾(柔光材质,条状明显),不是散点也不是沿 Z 飞出画面。
    /// Cone 轴默认沿本地 +Z,绕 Y 转 90° 后指向 +X → 2D 相机(看 XY)可见横向运动。
    /// 想换方向直接旋转 prefab。预览 loop 开着,每 duration 秒飞一颗。
    /// </summary>
    private static void ConfigureSingleTrail(ParticleSystem ps)
    {
        ParticleSystem.MainModule main = ps.main;
        main.duration = 3f;
        main.loop = true;
        main.startLifetime = new ParticleSystem.MinMaxCurve(1.8f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(8f);
        main.startSize = new ParticleSystem.MinMaxCurve(0.4f); // 单个本体要明显
        main.startColor = new Color(1f, 0.8f, 0.4f);
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.maxParticles = 10;

        // 不持续喷,每轮开头只发 1 个
        ParticleSystem.EmissionModule emission = ps.emission;
        emission.enabled = true;
        emission.rateOverTime = new ParticleSystem.MinMaxCurve(0f);
        emission.SetBursts(new[] { new ParticleSystem.Burst(0f, 1) });

        // 点源发射,方向由 shape 决定:Cone 默认 +Z,绕 Y 90° 指向 +X(2D 平面)
        ParticleSystem.ShapeModule shape = ps.shape;
        shape.enabled = true;
        shape.shapeType = ParticleSystemShapeType.Cone;
        shape.radius = 0.02f;
        shape.angle = 0.2f;
        shape.rotation = new Vector3(0f, 90f, 0f);

        // 本体随生命渐隐(尾段即粒子快结束时)
        ParticleSystem.ColorOverLifetimeModule color = ps.colorOverLifetime;
        color.enabled = true;
        color.color = new ParticleSystem.MinMaxGradient(MakeFadeGradient(new Color(1f, 0.9f, 0.6f)));

        // 单条带状拖尾:首端宽、尾端收窄留宽(条状不点状),柔光渐隐
        ParticleSystem.TrailModule trails = ps.trails;
        trails.enabled = true;
        trails.mode = ParticleSystemTrailMode.PerParticle;
        trails.lifetime = 0.8f;
        trails.minVertexDistance = 0.02f;
        trails.widthOverTrail = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(
            new Keyframe(0f, 0.35f), new Keyframe(1f, 0.06f)));
        trails.colorOverTrail = new ParticleSystem.MinMaxGradient(MakeFadeGradient(new Color(1f, 0.95f, 0.75f)));
        trails.dieWithParticles = true;
    }

    [MenuItem("Tools/粒子特效/背刺爆发(短促)")]
    public static void CreateBackstabBurst()
    {
        string path = OutputDir + "/BackstabBurst_Template.prefab";

        if (!AssetDatabase.IsValidFolder(OutputDir))
        {
            Debug.LogError("[ParticleFXGenerator] 输出目录不存在:" + OutputDir);
            return;
        }
        if (AssetDatabase.LoadAssetAtPath<GameObject>(path) != null)
            AssetDatabase.DeleteAsset(path);

        GameObject root = new GameObject("BackstabBurst_Template");
        ConfigureBackstabBurst(root);

        GameObject prefab = PrefabUtility.SaveAsPrefabAsset(root, path);
        Object.DestroyImmediate(root);

        if (prefab != null)
        {
            EditorGUIUtility.PingObject(prefab);
            Selection.activeObject = prefab;
            Debug.Log("[ParticleFXGenerator] 已生成:" + path, prefab);
        }
        else
        {
            Debug.LogError("[ParticleFXGenerator] prefab 保存失败:" + path);
        }
    }

    /// <summary>
    /// 背刺成功短促爆发:一个 prefab 根 + 三个子粒子,全部单次播放、~0.5s 收完。
    /// Ring 冲击环(扩散+快速淡)、Flash 中心闪光(0.2s 亮一下)、Shards 少量碎光溅开。
    /// 素材全部用 Epic Toon FX 现成贴图与 Additive 材质,不新建材质。
    /// </summary>
    private static void ConfigureBackstabBurst(GameObject root)
    {
        // --- Ring 冲击环 ---
        ParticleSystem ring = CreateChildParticle(root.transform, "Ring",
            "Assets/Epic Toon FX/Materials/Misc/ring (additive).mat");
        ParticleSystem.MainModule ringMain = ring.main;
        ringMain.duration = 0.55f;
        ringMain.loop = false;
        ringMain.playOnAwake = true;
        ringMain.startLifetime = new ParticleSystem.MinMaxCurve(0.5f);
        ringMain.startSpeed = new ParticleSystem.MinMaxCurve(0f);
        ringMain.startSize = new ParticleSystem.MinMaxCurve(0.5f);
        ringMain.startColor = new Color(1f, 0.92f, 0.75f);
        ringMain.simulationSpace = ParticleSystemSimulationSpace.World;
        ringMain.maxParticles = 20;

        ParticleSystem.EmissionModule ringEmi = ring.emission;
        ringEmi.rateOverTime = new ParticleSystem.MinMaxCurve(0f);
        ringEmi.SetBursts(new[] { new ParticleSystem.Burst(0f, 1) });

        ParticleSystem.ShapeModule ringShape = ring.shape;
        ringShape.enabled = true;
        ringShape.shapeType = ParticleSystemShapeType.Sphere;
        ringShape.radius = 0.01f;

        // 扩散:尺寸 0.6→2.0,alpha 快速淡出
        ParticleSystem.ColorOverLifetimeModule ringColor = ring.colorOverLifetime;
        ringColor.enabled = true;
        ringColor.color = new ParticleSystem.MinMaxGradient(MakeFadeGradient(new Color(1f, 0.9f, 0.7f)));
        ParticleSystem.SizeOverLifetimeModule ringSize = ring.sizeOverLifetime;
        ringSize.enabled = true;
        ringSize.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(
            new Keyframe(0f, 0.6f), new Keyframe(0.4f, 1.3f), new Keyframe(1f, 2.2f)));

        // --- Flash 中心闪光:0.2s 亮一下 ---
        ParticleSystem flash = CreateChildParticle(root.transform, "Flash",
            "Assets/Epic Toon FX/Materials/Glows/glow.mat");
        ParticleSystem.MainModule flashMain = flash.main;
        flashMain.duration = 0.25f;
        flashMain.loop = false;
        flashMain.playOnAwake = true;
        flashMain.startLifetime = new ParticleSystem.MinMaxCurve(0.22f);
        flashMain.startSpeed = new ParticleSystem.MinMaxCurve(0f);
        flashMain.startSize = new ParticleSystem.MinMaxCurve(0.9f);
        flashMain.startColor = new Color(1f, 0.95f, 0.8f);
        flashMain.simulationSpace = ParticleSystemSimulationSpace.World;
        flashMain.maxParticles = 20;

        ParticleSystem.EmissionModule flashEmi = flash.emission;
        flashEmi.rateOverTime = new ParticleSystem.MinMaxCurve(0f);
        flashEmi.SetBursts(new[] { new ParticleSystem.Burst(0f, 1) });

        ParticleSystem.ShapeModule flashShape = flash.shape;
        flashShape.enabled = true;
        flashShape.shapeType = ParticleSystemShapeType.Sphere;
        flashShape.radius = 0.01f;

        ParticleSystem.ColorOverLifetimeModule flashColor = flash.colorOverLifetime;
        flashColor.enabled = true;
        flashColor.color = new ParticleSystem.MinMaxGradient(MakeFadeGradient(new Color(1f, 0.93f, 0.75f)));
        ParticleSystem.SizeOverLifetimeModule flashSize = flash.sizeOverLifetime;
        flashSize.enabled = true;
        flashSize.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(
            new Keyframe(0f, 0.6f), new Keyframe(1f, 1.4f)));

        // --- Shards 碎光:少量向四周溅开,带轻微重力 ---
        ParticleSystem shards = CreateChildParticle(root.transform, "Shards",
            "Assets/Epic Toon FX/Materials/Sparkle/sparkle_ADD.mat");
        ParticleSystem.MainModule shardMain = shards.main;
        shardMain.duration = 0.45f;
        shardMain.loop = false;
        shardMain.playOnAwake = true;
        shardMain.startLifetime = new ParticleSystem.MinMaxCurve(0.35f);
        shardMain.startSpeed = new ParticleSystem.MinMaxCurve(3f, 7f);
        shardMain.startSize = new ParticleSystem.MinMaxCurve(0.1f, 0.16f);
        shardMain.startColor = new Color(1f, 0.85f, 0.5f);
        shardMain.simulationSpace = ParticleSystemSimulationSpace.World;
        shardMain.gravityModifier = 1.2f; // 轻微下落,像被击溅出的碎屑
        shardMain.maxParticles = 30;

        ParticleSystem.EmissionModule shardEmi = shards.emission;
        shardEmi.rateOverTime = new ParticleSystem.MinMaxCurve(0f);
        shardEmi.SetBursts(new[] { new ParticleSystem.Burst(0f, 5) });

        ParticleSystem.ShapeModule shardShape = shards.shape;
        shardShape.enabled = true;
        shardShape.shapeType = ParticleSystemShapeType.Sphere;
        shardShape.radius = 0.05f;

        ParticleSystem.ColorOverLifetimeModule shardColor = shards.colorOverLifetime;
        shardColor.enabled = true;
        shardColor.color = new ParticleSystem.MinMaxGradient(MakeFadeGradient(new Color(1f, 0.9f, 0.6f)));
        ParticleSystem.SizeOverLifetimeModule shardSize = shards.sizeOverLifetime;
        shardSize.enabled = true;
        shardSize.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(
            new Keyframe(0f, 1f), new Keyframe(1f, 0.3f)));
    }

    [MenuItem("Tools/粒子特效/随机轨迹拖尾点")]
    public static void CreateWanderTrailTemplate()
    {
        string path = OutputDir + "/Particle_WanderTrail_Template.prefab";

        if (!AssetDatabase.IsValidFolder(OutputDir))
        {
            Debug.LogError("[ParticleFXGenerator] 输出目录不存在:" + OutputDir);
            return;
        }
        if (AssetDatabase.LoadAssetAtPath<GameObject>(path) != null)
            AssetDatabase.DeleteAsset(path);

        GameObject root = new GameObject("Particle_WanderTrail_Template");
        ParticleSystem ps = root.AddComponent<ParticleSystem>();
        ConfigureWanderTrail(ps);

        ParticleSystemRenderer rend = root.GetComponent<ParticleSystemRenderer>();
        Material mat = AssetDatabase.LoadAssetAtPath<Material>("Assets/Epic Toon FX/Materials/Glows/glow.mat");
        if (mat != null)
        {
            rend.sharedMaterial = mat;
            rend.trailMaterial = mat;
        }

        GameObject prefab = PrefabUtility.SaveAsPrefabAsset(root, path);
        Object.DestroyImmediate(root);

        if (prefab != null)
        {
            EditorGUIUtility.PingObject(prefab);
            Selection.activeObject = prefab;
            Debug.Log("[ParticleFXGenerator] 已生成:" + path, prefab);
        }
        else
        {
            Debug.LogError("[ParticleFXGenerator] prefab 保存失败:" + path);
        }
    }

    /// <summary>
    /// 随机轨迹拖尾点:单个光点沿 +X 飞行,叠加小幅 Noise 随机摆动(蛇形但不乱飘),
    /// 自带条状拖尾,模拟"能量球飘着飞入"的观感。幅度由 Noise 的 strength 控制,小值=轻微摆动。
    /// 路径方向想变直接旋转 prefab。预览 loop 开着,每 duration 秒飞一颗。
    /// </summary>
    private static void ConfigureWanderTrail(ParticleSystem ps)
    {
        ParticleSystem.MainModule main = ps.main;
        main.duration = 4f;
        main.loop = true;
        main.startLifetime = new ParticleSystem.MinMaxCurve(2.2f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(5f);
        main.startSize = new ParticleSystem.MinMaxCurve(0.4f);
        main.startColor = new Color(1f, 0.85f, 0.45f); // 能量球暖金
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.maxParticles = 10;

        ParticleSystem.EmissionModule emission = ps.emission;
        emission.enabled = true;
        emission.rateOverTime = new ParticleSystem.MinMaxCurve(0f);
        emission.SetBursts(new[] { new ParticleSystem.Burst(0f, 1) });

        // 朝 +X 飞(2D 平面)
        ParticleSystem.ShapeModule shape = ps.shape;
        shape.enabled = true;
        shape.shapeType = ParticleSystemShapeType.Cone;
        shape.radius = 0.02f;
        shape.angle = 0.2f;
        shape.rotation = new Vector3(0f, 90f, 0f);

        // 小幅随机摆动:strength 小 = 轻微蛇形;frequency 决定弯的密度
        ParticleSystem.NoiseModule noise = ps.noise;
        noise.enabled = true;
        noise.strength = new ParticleSystem.MinMaxCurve(0.6f);
        noise.frequency = 1.2f;
        noise.scrollSpeed = 0.5f;
        noise.damping = true;

        // 本体随生命渐隐
        ParticleSystem.ColorOverLifetimeModule color = ps.colorOverLifetime;
        color.enabled = true;
        color.color = new ParticleSystem.MinMaxGradient(MakeFadeGradient(new Color(1f, 0.9f, 0.6f)));

        // 条状拖尾:首端宽、尾端收窄留宽,柔光渐隐
        ParticleSystem.TrailModule trails = ps.trails;
        trails.enabled = true;
        trails.mode = ParticleSystemTrailMode.PerParticle;
        trails.lifetime = 0.7f;
        trails.minVertexDistance = 0.02f;
        trails.widthOverTrail = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(
            new Keyframe(0f, 0.32f), new Keyframe(1f, 0.05f)));
        trails.colorOverTrail = new ParticleSystem.MinMaxGradient(MakeFadeGradient(new Color(1f, 0.95f, 0.75f)));
        trails.dieWithParticles = true;
    }

    [MenuItem("Tools/粒子特效/能量球环绕(中心拖尾)")]
    public static void CreateOrbCluster()
    {
        string path = OutputDir + "/OrbCluster_Template.prefab";

        if (!AssetDatabase.IsValidFolder(OutputDir))
        {
            Debug.LogError("[ParticleFXGenerator] 输出目录不存在:" + OutputDir);
            return;
        }
        if (AssetDatabase.LoadAssetAtPath<GameObject>(path) != null)
            AssetDatabase.DeleteAsset(path);

        GameObject root = new GameObject("OrbCluster_Template");
        ConfigureOrbCluster(root);

        GameObject prefab = PrefabUtility.SaveAsPrefabAsset(root, path);
        Object.DestroyImmediate(root);

        if (prefab != null)
        {
            EditorGUIUtility.PingObject(prefab);
            Selection.activeObject = prefab;
            Debug.Log("[ParticleFXGenerator] 已生成:" + path, prefab);
        }
        else
        {
            Debug.LogError("[ParticleFXGenerator] prefab 保存失败:" + path);
        }
    }

    /// <summary>
    /// 能量球环绕模板(移动出尾):默认 = 中心能量球静止 + 3 小球环绕(OrbitRotator 驱动)。
    /// 中心球挂 TrailRenderer:只有整体被拖动/移动时才画出连续条状尾,静止自动无尾,
    /// 不发射、不循环。3 个环绕小球常转,各自带短尾一直可见。
    /// 素材:中心/小球用现成 magic_orb3.png(Sprite 自带光晕),拖尾材质用 Epic glow。
    /// </summary>
    private static void ConfigureOrbCluster(GameObject root)
    {
        Sprite ballSprite = AssetDatabase.LoadAssetAtPath<Sprite>("Assets/Graphics/深度图/magic_orb3.png");
        Material glowMat = AssetDatabase.LoadAssetAtPath<Material>("Assets/Epic Toon FX/Materials/Glows/glow.mat");
        if (ballSprite == null)
            Debug.LogWarning("[ParticleFXGenerator] magic_orb3.png 未找到或不是 Sprite");
        if (glowMat == null)
            Debug.LogWarning("[ParticleFXGenerator] glow.mat 未找到");

        // --- 中心能量球:视觉(默认 Sprite 材质渲染贴图,自带光晕) + 移动拖尾 ---
        GameObject core = new GameObject("Core");
        core.transform.SetParent(root.transform, false);
        SpriteRenderer coreSr = core.AddComponent<SpriteRenderer>();
        if (ballSprite != null)
            coreSr.sprite = ballSprite;
        coreSr.color = new Color(1f, 0.92f, 0.7f); // 暖金,可调
        coreSr.sortingOrder = 1;

        TrailRenderer coreTrail = core.AddComponent<TrailRenderer>();
        if (glowMat != null)
            coreTrail.sharedMaterial = glowMat;
        coreTrail.time = 0.5f;                 // 尾时长:拖一下 0.5s 内消失
        coreTrail.widthMultiplier = 1f;
        coreTrail.widthCurve = new AnimationCurve(
            new Keyframe(0f, 0.7f), new Keyframe(1f, 0.06f)); // 团结曲线 0=头:头部粗、尾端细
        coreTrail.colorGradient = MakeTrailGradient(1f, 0.9f, 0.6f);
        coreTrail.minVertexDistance = 0.01f; // 小于每帧位移,保证每帧都加顶点(防拖尾一节节跳)
        coreTrail.numCapVertices = 4;
        coreTrail.numCornerVertices = 4;
        coreTrail.emitting = true;

        // --- 轨道容器:绕 Z 转,小球公转 ---
        GameObject orbit = new GameObject("Orbit");
        orbit.transform.SetParent(root.transform, false);
        orbit.AddComponent<OrbitRotator>();

        // --- 3 个环绕小球(120° 均布),各自带短尾 ---
        CreateOrbitBall(orbit.transform, ballSprite, glowMat, "Orb1", 0f, new Color(1f, 0.95f, 0.7f));
        CreateOrbitBall(orbit.transform, ballSprite, glowMat, "Orb2", 120f, new Color(0.85f, 1f, 0.95f));
        CreateOrbitBall(orbit.transform, ballSprite, glowMat, "Orb3", 240f, new Color(1f, 0.8f, 0.7f));
    }

    /// <summary>建一个环绕小球:放在轨道半径 0.9、给定起始角度,带短拖尾。</summary>
    private static void CreateOrbitBall(Transform orbit, Sprite sprite, Material glowMat, string name, float angleDeg, Color tint)
    {
        GameObject ball = new GameObject(name);
        ball.transform.SetParent(orbit, false);

        float rad = angleDeg * Mathf.Deg2Rad;
        ball.transform.localPosition = new Vector3(Mathf.Cos(rad) * 0.9f, Mathf.Sin(rad) * 0.9f, 0f);
        ball.transform.localScale = Vector3.one * 0.3f;

        SpriteRenderer sr = ball.AddComponent<SpriteRenderer>();
        if (sprite != null)
            sr.sprite = sprite;
        sr.color = tint;
        sr.sortingOrder = 0;

        TrailRenderer trail = ball.AddComponent<TrailRenderer>();
        if (glowMat != null)
            trail.sharedMaterial = glowMat;
        trail.time = 0.3f; // 绕行弧的一小段,一直可见
        trail.widthMultiplier = 1f;
        trail.widthCurve = new AnimationCurve(
            new Keyframe(0f, 0.16f), new Keyframe(1f, 0.02f)); // 0=头:头部粗、尾端细
        trail.colorGradient = MakeTrailGradient(tint.r, tint.g, tint.b);
        trail.minVertexDistance = 0.01f; // 小球每帧弧长约 0.037(140°/s、半径0.9),0.01 保证每帧加顶点
        trail.numCapVertices = 4;
        trail.numCornerVertices = 4;
        trail.emitting = true;
    }

    /// <summary>拖尾渐变:团结引擎曲线 x=0 是头部(物体处)、x=1 是尾端(远端)。头实色,尾透明。</summary>
    private static Gradient MakeTrailGradient(float r, float g, float b)
    {
        Gradient grad = new Gradient();
        grad.SetKeys(
            new[] { new GradientColorKey(new Color(r, g, b), 0f), new GradientColorKey(new Color(r, g, b), 1f) },
            new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0f, 1f) });
        return grad;
    }

    [MenuItem("Tools/粒子特效/敌人死亡爆炸")]
    public static void CreateEnemyDeathBurst()
    {
        string path = OutputDir + "/EnemyDeathBurst_Template.prefab";

        if (!AssetDatabase.IsValidFolder(OutputDir))
        {
            Debug.LogError("[ParticleFXGenerator] 输出目录不存在:" + OutputDir);
            return;
        }
        if (AssetDatabase.LoadAssetAtPath<GameObject>(path) != null)
            AssetDatabase.DeleteAsset(path);

        GameObject root = new GameObject("EnemyDeathBurst_Template");
        ConfigureEnemyDeathBurst(root);

        GameObject prefab = PrefabUtility.SaveAsPrefabAsset(root, path);
        Object.DestroyImmediate(root);

        if (prefab != null)
        {
            EditorGUIUtility.PingObject(prefab);
            Selection.activeObject = prefab;
            Debug.Log("[ParticleFXGenerator] 已生成:" + path, prefab);
        }
        else
        {
            Debug.LogError("[ParticleFXGenerator] prefab 保存失败:" + path);
        }
    }

    /// <summary>
    /// 敌人死亡爆炸(短促,~0.6s 收完):Flash 中心闪光 + Ring 冲击环扩散 +
    /// Shards 碎片溅开 + Smoke 烟尘。死亡时拖进 EnemyDeathVFX.enemyDeathVFXPrefab 槽。
    /// </summary>
    private static void ConfigureEnemyDeathBurst(GameObject root)
    {
        // --- Flash 中心闪光 ---
        ParticleSystem flash = CreateChildParticle(root.transform, "Flash",
            "Assets/Epic Toon FX/Materials/Glows/glow.mat");
        ParticleSystem.MainModule fMain = flash.main;
        fMain.duration = 0.3f;
        fMain.loop = false;
        fMain.playOnAwake = true;
        fMain.startLifetime = new ParticleSystem.MinMaxCurve(0.25f);
        fMain.startSpeed = new ParticleSystem.MinMaxCurve(0f);
        fMain.startSize = new ParticleSystem.MinMaxCurve(0.6f);
        fMain.startColor = new Color(1f, 0.95f, 0.8f);
        fMain.simulationSpace = ParticleSystemSimulationSpace.World;
        fMain.maxParticles = 20;

        ParticleSystem.EmissionModule fEmi = flash.emission;
        fEmi.rateOverTime = new ParticleSystem.MinMaxCurve(0f);
        fEmi.SetBursts(new[] { new ParticleSystem.Burst(0f, 1) });
        ParticleSystem.ShapeModule fShape = flash.shape;
        fShape.enabled = true;
        fShape.shapeType = ParticleSystemShapeType.Sphere;
        fShape.radius = 0.01f;
        ParticleSystem.ColorOverLifetimeModule fColor = flash.colorOverLifetime;
        fColor.enabled = true;
        fColor.color = new ParticleSystem.MinMaxGradient(MakeFadeGradient(new Color(1f, 0.95f, 0.8f)));
        ParticleSystem.SizeOverLifetimeModule fSize = flash.sizeOverLifetime;
        fSize.enabled = true;
        fSize.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(
            new Keyframe(0f, 0.7f), new Keyframe(1f, 1.6f)));

        // --- Ring 冲击环(扩散快、快速淡) ---
        ParticleSystem ring = CreateChildParticle(root.transform, "Ring",
            "Assets/Epic Toon FX/Materials/Misc/ring (additive).mat");
        ParticleSystem.MainModule rMain = ring.main;
        rMain.duration = 0.5f;
        rMain.loop = false;
        rMain.playOnAwake = true;
        rMain.startLifetime = new ParticleSystem.MinMaxCurve(0.45f);
        rMain.startSpeed = new ParticleSystem.MinMaxCurve(0f);
        rMain.startSize = new ParticleSystem.MinMaxCurve(0.5f);
        rMain.startColor = new Color(1f, 0.85f, 0.6f);
        rMain.simulationSpace = ParticleSystemSimulationSpace.World;
        rMain.maxParticles = 20;

        ParticleSystem.EmissionModule rEmi = ring.emission;
        rEmi.rateOverTime = new ParticleSystem.MinMaxCurve(0f);
        rEmi.SetBursts(new[] { new ParticleSystem.Burst(0f, 1) });
        ParticleSystem.ShapeModule rShape = ring.shape;
        rShape.enabled = true;
        rShape.shapeType = ParticleSystemShapeType.Sphere;
        rShape.radius = 0.01f;
        ParticleSystem.ColorOverLifetimeModule rColor = ring.colorOverLifetime;
        rColor.enabled = true;
        rColor.color = new ParticleSystem.MinMaxGradient(MakeFadeGradient(new Color(1f, 0.85f, 0.6f)));
        ParticleSystem.SizeOverLifetimeModule rSize = ring.sizeOverLifetime;
        rSize.enabled = true;
        rSize.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(
            new Keyframe(0f, 0.5f), new Keyframe(1f, 2.2f)));

        // --- Shards 碎片溅开(带重力) ---
        ParticleSystem shards = CreateChildParticle(root.transform, "Shards",
            "Assets/Epic Toon FX/Materials/Sparkle/sparkle_ADD.mat");
        ParticleSystem.MainModule sMain = shards.main;
        sMain.duration = 0.6f;
        sMain.loop = false;
        sMain.playOnAwake = true;
        sMain.startLifetime = new ParticleSystem.MinMaxCurve(0.45f);
        sMain.startSpeed = new ParticleSystem.MinMaxCurve(3f, 8f);
        sMain.startSize = new ParticleSystem.MinMaxCurve(0.1f, 0.2f);
        sMain.startColor = new Color(1f, 0.75f, 0.45f);
        sMain.simulationSpace = ParticleSystemSimulationSpace.World;
        sMain.gravityModifier = 2.5f;
        sMain.maxParticles = 40;

        ParticleSystem.EmissionModule sEmi = shards.emission;
        sEmi.rateOverTime = new ParticleSystem.MinMaxCurve(0f);
        sEmi.SetBursts(new[] { new ParticleSystem.Burst(0f, 10) });
        ParticleSystem.ShapeModule sShape = shards.shape;
        sShape.enabled = true;
        sShape.shapeType = ParticleSystemShapeType.Sphere;
        sShape.radius = 0.15f;
        ParticleSystem.ColorOverLifetimeModule sColor = shards.colorOverLifetime;
        sColor.enabled = true;
        sColor.color = new ParticleSystem.MinMaxGradient(MakeFadeGradient(new Color(1f, 0.8f, 0.5f)));
        ParticleSystem.SizeOverLifetimeModule sSize = shards.sizeOverLifetime;
        sSize.enabled = true;
        sSize.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(
            new Keyframe(0f, 1f), new Keyframe(1f, 0.3f)));

        // --- Smoke 烟尘(慢扩散,偏灰,半透明) ---
        ParticleSystem smoke = CreateChildParticle(root.transform, "Smoke",
            "Assets/Epic Toon FX/Materials/Clouds/cloud_2x2_soft.mat");
        ParticleSystem.MainModule mMain = smoke.main;
        mMain.duration = 0.8f;
        mMain.loop = false;
        mMain.playOnAwake = true;
        mMain.startLifetime = new ParticleSystem.MinMaxCurve(0.7f);
        mMain.startSpeed = new ParticleSystem.MinMaxCurve(0.6f, 1.5f);
        mMain.startSize = new ParticleSystem.MinMaxCurve(0.5f, 0.8f);
        mMain.startColor = new Color(0.55f, 0.5f, 0.48f, 0.9f);
        mMain.simulationSpace = ParticleSystemSimulationSpace.World;
        mMain.maxParticles = 20;

        ParticleSystem.EmissionModule mEmi = smoke.emission;
        mEmi.rateOverTime = new ParticleSystem.MinMaxCurve(0f);
        mEmi.SetBursts(new[] { new ParticleSystem.Burst(0f, 5) });
        ParticleSystem.ShapeModule mShape = smoke.shape;
        mShape.enabled = true;
        mShape.shapeType = ParticleSystemShapeType.Sphere;
        mShape.radius = 0.2f;
        ParticleSystem.ColorOverLifetimeModule mColor = smoke.colorOverLifetime;
        mColor.enabled = true;
        mColor.color = new ParticleSystem.MinMaxGradient(MakeFadeGradient(new Color(0.55f, 0.5f, 0.48f)));
        ParticleSystem.SizeOverLifetimeModule mSize = smoke.sizeOverLifetime;
        mSize.enabled = true;
        mSize.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(
            new Keyframe(0f, 0.8f), new Keyframe(1f, 2.4f)));
    }

    /// <summary>在父级下建一个子粒子物体并挂上指定材质。</summary>
    private static ParticleSystem CreateChildParticle(Transform parent, string name, string materialPath)
    {
        GameObject go = new GameObject(name);
        go.transform.SetParent(parent, false);
        ParticleSystem ps = go.AddComponent<ParticleSystem>();
        Material mat = AssetDatabase.LoadAssetAtPath<Material>(materialPath);
        if (mat != null)
            go.GetComponent<ParticleSystemRenderer>().sharedMaterial = mat;
        else
            Debug.LogWarning("[ParticleFXGenerator] 材质未找到:" + materialPath);
        return ps;
    }

    /// <summary>把默认粒子配成"飞行光点 + 每粒子拖尾"的基础形态,参数集中在这便于后续调模板默认。</summary>
    private static void ConfigureTrail(ParticleSystem ps)
    {
        // Main:循环方便挂场景直接看效果;正式用时关 loop、按触发播放
        ParticleSystem.MainModule main = ps.main;
        main.duration = 5f;
        main.loop = true;
        main.startLifetime = new ParticleSystem.MinMaxCurve(1.5f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(6f);
        main.startSize = new ParticleSystem.MinMaxCurve(0.18f);
        main.startColor = new Color(1f, 0.85f, 0.5f); // 暖黄光点
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.maxParticles = 300;

        // Emission
        ParticleSystem.EmissionModule emission = ps.emission;
        emission.enabled = true;
        emission.rateOverTime = new ParticleSystem.MinMaxCurve(25f);

        // Shape:小锥口向前喷射,配合 startSpeed 形成飞行方向;口径越小越像弹道
        ParticleSystem.ShapeModule shape = ps.shape;
        shape.enabled = true;
        shape.shapeType = ParticleSystemShapeType.Cone;
        shape.radius = 0.05f;
        shape.angle = 0.5f;

        // Color over Lifetime:光点随生命渐隐
        ParticleSystem.ColorOverLifetimeModule color = ps.colorOverLifetime;
        color.enabled = true;
        color.color = new ParticleSystem.MinMaxGradient(MakeFadeGradient(new Color(1f, 0.9f, 0.6f)));

        // Size over Lifetime:轻微收小
        ParticleSystem.SizeOverLifetimeModule size = ps.sizeOverLifetime;
        size.enabled = true;
        AnimationCurve sizeCurve = new AnimationCurve(new Keyframe(0f, 1f), new Keyframe(1f, 0.4f));
        size.size = new ParticleSystem.MinMaxCurve(1f, sizeCurve);

        // Trails:每粒子拖尾,宽从头到尾变细、颜色渐隐
        ParticleSystem.TrailModule trails = ps.trails;
        trails.enabled = true;
        trails.mode = ParticleSystemTrailMode.PerParticle;
        trails.lifetime = 0.4f;
        trails.minVertexDistance = 0.05f;
        trails.widthOverTrail = new ParticleSystem.MinMaxCurve(1f, MakeWidthCurve());
        trails.colorOverTrail = new ParticleSystem.MinMaxGradient(MakeFadeGradient(new Color(1f, 0.95f, 0.75f)));
        trails.dieWithParticles = true;
    }

    /// <summary>宽度曲线:头(0)粗 → 尾(1)细到 0,拖尾收尖。</summary>
    private static AnimationCurve MakeWidthCurve()
    {
        return new AnimationCurve(new Keyframe(0f, 0.22f), new Keyframe(1f, 0f));
    }

    /// <summary>颜色随拖尾位置渐隐:头(0)实色 → 尾(1)alpha 0。</summary>
    private static Gradient MakeFadeGradient(Color headColor)
    {
        Gradient g = new Gradient();
        g.SetKeys(
            new[] { new GradientColorKey(headColor, 0f), new GradientColorKey(headColor, 1f) },
            new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0.85f, 0.6f), new GradientAlphaKey(0f, 1f) });
        return g;
    }
}
