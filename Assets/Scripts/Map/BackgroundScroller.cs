using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// 无限平铺背景滚动器 — Far/Mid 两层。
/// 
/// 需求:背景图固定在世界坐标(不做视差),当相机/player 移动到边界时,
/// 提前生成 clone 无缝拼接在之后 → 无限延伸的背景,零手动摆放。
/// 
/// 核心算法:相机视野边缘 vs 最外圈图边界的距离判断——
///   相机右缘 - 提前量 &gt; 最右图右边界 → 在右侧补一张 clone
///   相机左缘 + 提前量 &lt; 最左图左边界 → 在左侧补一张 clone
/// 提前量保证"相机到达之前就生成",不穿帮。
/// 
/// 挂载:场景根常驻物体(不随地区显隐)。地区下的 BG 容器可保留(显示静态远景)
/// 或移除——本组件独立生成平铺层,与 ZoneManager 无耦合。
/// </summary>
public class BackgroundScroller : MonoBehaviour
{
    [Header("背景层贴图(必须左右可平铺 seamless)")]
    [Tooltip("远景图(星空/月亮/远山)——排序在最后")]
    [SerializeField] private Sprite farSprite;
    [Tooltip("中景图(树林/建筑剪影)——排序在 Far 前")]
    [SerializeField] private Sprite midSprite;

    [Header("层偏移")]
    [Tooltip("Far 层的 Y 偏移(世界单位,对齐摆放位置)")]
    [SerializeField] private float farYOffset = 0f;
    [Tooltip("Mid 层的 Y 偏移(世界单位,对齐摆放位置)")]
    [SerializeField] private float midYOffset = 0f;

    [Header("图大小(整体缩放)")]
    [Tooltip("整体缩放倍数:1=原始大小,2=宽高都放大2倍,0.5=缩小一半。宽高一起变")]
    [SerializeField] private float tileScale = 1f;

    [Header("生成策略")]
    [Tooltip("初始预铺:相机两侧各铺 N 张(图宽倍数)")]
    [SerializeField] private int initialCount = 2;
    [Tooltip("提前生成量(世界单位):相机视野边缘距最外图边界小于此值就补图")]
    [SerializeField] private float preloadMargin = 8f;
    [Tooltip("回收距离(世界单位):图中心离相机超过此值就销毁(防无限堆积)")]
    [SerializeField] private float recycleDistance = 60f;

    private Transform _cam;
    private Camera _camComp;

    // 每层:父物体 + 图列表(列表按 x 升序,首=最左,尾=最右)
    private Transform _farRoot;
    private Transform _midRoot;
    private readonly List<Transform> _farTiles = new List<Transform>();
    private readonly List<Transform> _midTiles = new List<Transform>();

    private void Awake()
    {
        _cam = Camera.main != null ? Camera.main.transform : null;
        _camComp = Camera.main;
    }

    private void Start()
    {
        if (_cam == null) return;

        // 创建层父物体(排序:Far=-10 < Mid=-9 < Default(0),层次:Far 最远、Mid 中间、游戏物体最前)
        _farRoot = CreateLayerRoot("Far_Tiles", farYOffset);
        _midRoot = CreateLayerRoot("Mid_Tiles", midYOffset);

        // 初始铺图:以相机为中心,左右各 initialCount 张
        if (farSprite != null) Seed(_farRoot, _farTiles, farSprite, -10);
        if (midSprite != null) Seed(_midRoot, _midTiles, midSprite, -9);
    }

    private Transform CreateLayerRoot(string name, float yOffset)
    {
        GameObject go = new GameObject(name);
        go.transform.SetParent(transform);
        go.transform.position = new Vector3(0f, yOffset, 0f);
        return go.transform;
    }

    /// <summary>初始铺图:以相机 x 为中心,左右各 initialCount 张,图宽 = 原始宽 × tileScale</summary>
    private void Seed(Transform root, List<Transform> tiles, Sprite sprite, int sortingOrder)
    {
        float w = GetTileWidth(sprite);
        if (w <= 0f) return;
        float centerX = _cam.position.x;
        for (int i = -initialCount; i <= initialCount; i++)
            tiles.Add(CreateTile(root, sprite, centerX + i * w, sortingOrder));
    }

    /// <summary>创建单张背景图:整体按 tileScale 缩放(宽高一起变),sortingOrder 设层序</summary>
    private Transform CreateTile(Transform root, Sprite sprite, float x, int sortingOrder)
    {
        GameObject go = new GameObject("Tile_" + x.ToString("F1"));
        go.transform.SetParent(root);
        // y 跟随父级 root(已带 farYOffset/midYOffset)——不能写死 0,否则层偏移失效
        go.transform.position = new Vector3(x, root.position.y, 0f);
        var sr = go.AddComponent<SpriteRenderer>();
        sr.sprite = sprite;
        sr.sortingOrder = sortingOrder; // 层序:所有背景 -10,低于 Default(0)的游戏物体

        // 整体缩放:localScale = tileScale(1 = 原始大小,宽高一起变)
        if (tileScale > 0f && Mathf.Abs(tileScale - 1f) > 0.0001f)
            go.transform.localScale = new Vector3(tileScale, tileScale, 1f);
        return go.transform;
    }

    /// <summary>单张图的世界宽度:原始宽 × tileScale</summary>
    private float GetTileWidth(Sprite sprite)
    {
        float baseW = sprite != null ? sprite.bounds.size.x : 0f;
        return baseW * tileScale;
    }

    private void LateUpdate()
    {
        if (_cam == null || _camComp == null) return;
        if (_farTiles.Count == 0 && _midTiles.Count == 0) return;

        // 相机视野左右缘(世界坐标)
        float viewHalfW = _camComp.orthographicSize * _camComp.aspect;
        float rightEdge = _cam.position.x + viewHalfW;
        float leftEdge = _cam.position.x - viewHalfW;

        if (_farTiles.Count > 0) UpdateLayer(_farTiles, farSprite, rightEdge, leftEdge);
        if (_midTiles.Count > 0) UpdateLayer(_midTiles, midSprite, rightEdge, leftEdge);

        RecycleFarAway(_farTiles);
        RecycleFarAway(_midTiles);
    }

    /// <summary>单层延续:右侧不足补图(while 防大位移一次缺多张),左侧对称</summary>
    private void UpdateLayer(List<Transform> tiles, Sprite sprite, float rightEdge, float leftEdge)
    {
        float w = GetTileWidth(sprite);
        if (w <= 0f) return;

        // 右侧延续:最右图右边界 - 提前量 < 相机右缘 → 补图(放在最右图右侧 w 处,无缝)
        Transform rightMost = tiles[tiles.Count - 1];
        int order = rightMost.GetComponent<SpriteRenderer>() != null
            ? rightMost.GetComponent<SpriteRenderer>().sortingOrder : -10;
        float rightMostEdge = rightMost.position.x + w / 2f;
        while (rightMostEdge - preloadMargin < rightEdge)
        {
            Transform t = CreateTile(rightMost.parent, sprite, rightMost.position.x + w, order);
            tiles.Add(t);
            rightMost = t;
            rightMostEdge = rightMost.position.x + w / 2f;
        }

        // 左侧延续:最左图左边界 + 提前量 > 相机左缘 → 补图
        Transform leftMost = tiles[0];
        float leftMostEdge = leftMost.position.x - w / 2f;
        while (leftMostEdge + preloadMargin > leftEdge)
        {
            Transform t = CreateTile(leftMost.parent, sprite, leftMost.position.x - w, order);
            tiles.Insert(0, t);
            leftMost = t;
            leftMostEdge = leftMost.position.x - w / 2f;
        }
    }

    /// <summary>回收:图中心离相机超过 recycleDistance 就销毁(从两端检查,防无限堆积)</summary>
    private void RecycleFarAway(List<Transform> tiles)
    {
        while (tiles.Count > 0 && Mathf.Abs(tiles[0].position.x - _cam.position.x) > recycleDistance)
        {
            if (tiles[0] != null) Destroy(tiles[0].gameObject);
            tiles.RemoveAt(0);
        }
        while (tiles.Count > 0 && Mathf.Abs(tiles[tiles.Count - 1].position.x - _cam.position.x) > recycleDistance)
        {
            if (tiles[tiles.Count - 1] != null) Destroy(tiles[tiles.Count - 1].gameObject);
            tiles.RemoveAt(tiles.Count - 1);
        }
    }

    // ── 编辑器预览:选中本物体时,画出两层贴图的实际铺放范围与每张图边界 ──
    // 运行时生成前(编辑器里没有 tile),用字段值直接模拟铺放位置,方便确认位置/大小
    private void OnDrawGizmosSelected()
    {
        DrawLayerPreview(farSprite, farYOffset, new Color(0.5f, 0.8f, 1f, 0.6f), "Far");
        DrawLayerPreview(midSprite, midYOffset, new Color(1f, 0.8f, 0.5f, 0.6f), "Mid");
    }

    private void DrawLayerPreview(Sprite sprite, float yOffset, Color color, string label)
    {
        if (sprite == null) return;

        // 预览尺寸 = 原始尺寸 × tileScale(与运行时一致)
        Vector2 orig = sprite.bounds.size;
        float w = orig.x * tileScale;
        float h = orig.y * tileScale;
        if (w <= 0f) return;

        // 参考中心:编辑器里没有相机引用时用原点;有相机用相机 x
        float centerX = 0f;
        if (_cam != null) centerX = _cam.position.x;
#if UNITY_EDITOR
        if (_cam == null && Camera.main != null) centerX = Camera.main.transform.position.x;
#endif

        Gizmos.color = color;
        for (int i = -initialCount; i <= initialCount; i++)
        {
            float x = centerX + i * w;
            Vector3 center = new Vector3(x, yOffset, 0f);
            // 每张图边界框
            Gizmos.DrawWireCube(center, new Vector3(w, h, 0f));
        }

        // 范围线(铺放总宽)
        float totalW = w * (initialCount * 2 + 1);
        Vector3 rangeLeft = new Vector3(centerX - totalW / 2f, yOffset, 0f);
        Vector3 rangeRight = new Vector3(centerX + totalW / 2f, yOffset, 0f);
        Gizmos.DrawLine(rangeLeft + Vector3.up * h * 0.6f, rangeRight + Vector3.up * h * 0.6f);
        Gizmos.DrawLine(rangeLeft - Vector3.up * h * 0.6f, rangeRight - Vector3.up * h * 0.6f);
    }
}
