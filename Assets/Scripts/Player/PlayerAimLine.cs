using UnityEngine;

/// <summary>
/// 玩家瞄准虚线 — 从玩家指向鼠标位置，用 LineRenderer 虚线显示
/// 提供 AimDirection 供 PlayerCombat 读取子弹方向
/// 虚线在 XY 平面自由移动（横板游戏中可上下左右瞄准）
/// </summary>
[RequireComponent(typeof(LineRenderer))]
public class PlayerAimLine : MonoBehaviour
{
    [SerializeField] private float maxLength = 10f;
    [SerializeField] private Color lineColor = Color.white;
    [SerializeField] private float lineWidth = 0.05f;

    private LineRenderer line;
    private Camera mainCamera;
    private static Material cachedMaterial;

    /// <summary>当前瞄准方向（单位向量，XY 平面），供 PlayerCombat 读取</summary>
    public Vector2 AimDirection { get; private set; } = Vector2.right;

    /// <summary>隐藏瞄准线（近战模式调用）</summary>
    public void Hide()
    {
        line.positionCount = 0;
        enabled = false;
    }

    private void Awake()
    {
        mainCamera = Camera.main;

        // 设置 LineRenderer
        line = GetComponent<LineRenderer>();
        line.positionCount = 2;
        line.startWidth = lineWidth;
        line.endWidth = lineWidth;
        line.startColor = lineColor;
        line.endColor = lineColor;
        line.useWorldSpace = true;

        // 使用静态缓存避免 Material 泄漏（Player disable/enable 复用）
        if (cachedMaterial != null)
        {
            line.material = cachedMaterial;
            return;
        }

        // 创建虚线纹理（白点 + 透明间隔）
        Texture2D tex = new Texture2D(8, 1, TextureFormat.ARGB32, false);
        for (int i = 0; i < 8; i++)
        {
            if (i < 4)
                tex.SetPixel(i, 0, Color.white);
            else
                tex.SetPixel(i, 0, new Color(0, 0, 0, 0));
        }
        tex.Apply();
        tex.wrapMode = TextureWrapMode.Repeat;

        // 使用 Sprites/Default（几乎所有项目都有，兼容 URP/BIRP）
        Shader shader = Shader.Find("Sprites/Default");
        if (shader == null)
            shader = Shader.Find("Unlit/Transparent");
        cachedMaterial = new Material(shader);
        cachedMaterial.mainTexture = tex;
        cachedMaterial.mainTextureScale = new Vector2(30f, 1f);
        line.material = cachedMaterial;
    }

    private void Update()
    {
        // 鼠标屏幕坐标 → 世界坐标（XY 平面）
        Vector3 mouseScreen = Input.mousePosition;
        mouseScreen.z = Mathf.Abs(mainCamera.transform.position.z - transform.position.z);
        Vector3 mouseWorld = mainCamera.ScreenToWorldPoint(mouseScreen);

        // 计算方向（XY 平面，不锁定 Y 轴）
        Vector2 dir = (mouseWorld - transform.position).normalized;
        if (dir.sqrMagnitude < 0.01f)
            dir = Vector2.right * (transform.localScale.x > 0 ? 1 : -1);

        AimDirection = dir;

        // 更新 LineRenderer
        Vector3 start = transform.position + Vector3.up * 0.3f;
        Vector3 end = start + (Vector3)dir * maxLength;
        line.SetPosition(0, start);
        line.SetPosition(1, end);

        // 【注释保留】朝向翻转逻辑 — 2026-08-04 取消远程攻击后,朝向统一按近战规则
        // (PlayerCombat.AttackDir / UpdateFacing)驱动,不再由鼠标瞄准控制。
        // 若后续技能需要"瞄准朝向翻转角色",取消注释即可。
        // if (Mathf.Abs(dir.x) > 0.1f)
        // {
        //     float facing = dir.x > 0 ? 1 : -1;
        //     Vector3 scale = transform.root.localScale;
        //     scale.x = Mathf.Abs(scale.x) * facing;
        //     transform.root.localScale = scale;
        // }
    }
}
