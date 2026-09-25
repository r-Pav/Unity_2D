using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 合成界面主控（[装备制作 S7]）—— 把 S1~S6 六个组件接到界面物体上（本流水线最后一块）。
///
/// 分工（本组件只做「接线 + 判定 + 原子合成」，不重新实现已有能力）：
///   · 数据层：<see cref="CraftStagingArea"/>（S3，普通类）—— 本组件 new 出来注入两个槽，两槽共享；
///   · 槽交互：<see cref="CraftMakeSlot"/>（S4）—— 只负责拖入 / 右键取出，槽内容一变就回调本组件刷新；
///   · 材料列表：<see cref="CraftMatPickDialog"/>（S5）—— 本组件决定填哪个槽、填多少，真正写入走同一个数据层入口；
///   · 配方列表：<see cref="CraftRecipeListView"/>（S6）—— 本组件订阅 OnRecipePicked 载入配方；
///   · 背包/仓库：<see cref="InventoryManager"/>（S2 的 CanFitFully / TryAddToWarehouse）。
///
/// 关键口径（规格第三节，2026-09-25 定稿）：
///   1. 材料填入 = 暂存区模式：填入时材料从背包/仓库真挪进暂存区；右键槽位取消并归还来源容器；
///   2. 合成是原子操作：材料全扣 + 产物全落地，任一步失败整笔不做，不允许「部分成功」；
///   3. 只走配方驱动：先选配方（列表），再往里填该配方要求的材料；
///   4. 打开方式：批次一用快捷键 Z（正式入口批次二再定）；
///   5. 不做二次确认框（批次二）。
///
/// 合成落地顺序（重要取舍，详见 <see cref="DoCraft"/> 注释）：
///   ①只读校验配方与材料 → ②只读判「产物放得下」（背包优先、仓库兜底）→ ③扣料 → ④产物落地。
///   ②在③之前 = 放不下就一格材料都不扣；③在④之前 = 扣料不会因为产物先落袋、背包被占满而失败。
///
/// 挂载：本组件挂 Canvas/Create 面板根；面板根**必须在场景里设为 inactive** ——
/// 否则 PanelManager.Start 会把已 active 的全屏面板入栈，一开局就暂停 + 锁输入。
/// 字段拖拽清单见交付报告的「编辑器挂载清单」。
/// </summary>
public class CraftMakePanel : MonoBehaviour, IPanel
{
    // ============================================================
    // IPanel（显式实现，与 CraftMatListDialog / CraftMatPickDialog 同款写法）
    // 独占页：与技能面板同口径 —— 暂停游戏 + 锁玩家输入 + 显示光标
    // ============================================================

    PanelType IPanel.PanelType => PanelType.FullScreen;
    bool IPanel.PauseGame => true;
    bool IPanel.LockInput => true;
    bool IPanel.ShowCursor => true;

    // ============================================================
    // 文案 / 颜色口径（公开常量，自测与后续步骤可复用）
    // ============================================================

    /// <summary>「已填数量/需求数量」之间的分隔符</summary>
    public const string QuantitySeparator = "/";

    /// <summary>材料清单前缀（previewStats）</summary>
    public const string MaterialPrefix = "材料:";

    /// <summary>多条材料的连接符（previewStats 里一行内并列）</summary>
    public const string MaterialJoinSeparator = " + ";

    /// <summary>缺少材料的前缀</summary>
    public const string ShortagePrefix = "缺少:";

    /// <summary>多条缺少材料的连接符</summary>
    public const string ShortageJoinSeparator = "、";

    /// <summary>背包与仓库都放不下产物的提示（规格第六节 批次一）</summary>
    public const string StatusNoSpace = "背包与仓库都放不下，无法合成";

    /// <summary>背包数据不可用时的提示（环境缺失，安全失败）</summary>
    public const string StatusNoInventory = "背包数据不可用，无法合成";

    /// <summary>扣料中途失败（已回滚）</summary>
    public const string StatusConsumeFailed = "扣除材料失败，本次合成已放弃";

    /// <summary>产物落地失败（材料已退回）</summary>
    public const string StatusLandingFailed = "产物落地失败，材料已退回";

    /// <summary>关闭面板时材料归还失败（容器满，材料仍留在暂存区）</summary>
    public const string StatusReturnFailed = "材料归还失败，仍留在暂存区";

    /// <summary>填入时目标槽原内容归还失败的提示</summary>
    public const string StatusFillReplaceFailed = "槽内材料归还失败，无法填入";

    /// <summary>填入失败（材料已被别处取走等）的提示</summary>
    public const string StatusFillFailed = "填入失败，材料可能已被取走";

    /// <summary>一键填入时没选配方</summary>
    public const string StatusNoRecipe = "先在配方列表里选一条配方";

    /// <summary>空槽占位（显示配方需求材料、还没放）时的图标颜色 —— 调暗表示「这不是槽里的真材料」</summary>
    public static readonly Color PlaceholderIconColor = new Color(0.35f, 0.35f, 0.35f, 1f);

    /// <summary>槽内真有材料（或无显示）时的图标颜色</summary>
    public static readonly Color NormalIconColor = Color.white;

    // ============================================================
    // 配置（Inspector —— 由 saika 按挂载清单拖）
    // ============================================================

    [Header("槽与注入")]
    [Tooltip("左槽 Slot_Left 上的 CraftMakeSlot（槽位号 slotIndex 填 0）")]
    [SerializeField] private CraftMakeSlot slotLeft = null;

    [Tooltip("右槽 Slot_Right 上的 CraftMakeSlot（槽位号 slotIndex 必须填 1；留 0 会与左槽撞号，脚本只兜底不代填）")]
    [SerializeField] private CraftMakeSlot slotRight = null;

    [Header("槽显示 — 左")]
    [Tooltip("左槽图标 Image（Slot_Left/Icon）")]
    [SerializeField] private Image slotLeftIcon = null;

    [Tooltip("左槽材料名 TMP（Slot_Left/Name）")]
    [SerializeField] private TMP_Text slotLeftName = null;

    [Tooltip("左槽数量 TMP（Slot_Left/Quantity）=「已填数量/需求数量」")]
    [SerializeField] private TMP_Text slotLeftQuantity = null;

    [Tooltip("左槽拥有总数 TMP（Slot_Left/QuantityALL）= 背包 + 仓库 + 暂存区合计")]
    [SerializeField] private TMP_Text slotLeftOwned = null;

    [Header("槽显示 — 右")]
    [Tooltip("右槽图标 Image（Slot_Right/Icon）")]
    [SerializeField] private Image slotRightIcon = null;

    [Tooltip("右槽材料名 TMP（Slot_Right/Name）")]
    [SerializeField] private TMP_Text slotRightName = null;

    [Tooltip("右槽数量 TMP（Slot_Right/Quantity）=「已填数量/需求数量」")]
    [SerializeField] private TMP_Text slotRightQuantity = null;

    [Tooltip("右槽拥有总数 TMP（Slot_Right/QuantityALL）= 背包 + 仓库 + 暂存区合计")]
    [SerializeField] private TMP_Text slotRightOwned = null;

    [Header("产物预览")]
    [Tooltip("产物图标 Image（ResultPreview/Icon）")]
    [SerializeField] private Image previewIcon = null;

    [Tooltip("产物名称 TMP（ResultPreview/Name）")]
    [SerializeField] private TMP_Text previewName = null;

    [Tooltip("产物描述 TMP（ResultPreview/Desc）")]
    [SerializeField] private TMP_Text previewDesc = null;

    [Tooltip("产物/材料状态 TMP（ResultPreview/Stats）：材料清单、「缺少:…」、合成结果提示都写这里")]
    [SerializeField] private TMP_Text previewStats = null;

    [Tooltip("未选配方时显示的占位提示物体（ResultPreview/Placeholder）；选了配方就隐藏")]
    [SerializeField] private GameObject previewPlaceholder = null;

    [Header("按钮")]
    [Tooltip("tianchong_Btn：打开材料列表对话框，点选填入第一个空槽")]
    [SerializeField] private Button tianchongBtn = null;

    [Tooltip("List_Btn：唤出/收起 Description 配方列表页")]
    [SerializeField] private Button listBtn = null;

    [Tooltip("CrateBtn：执行合成（材料不足时不可点）")]
    [SerializeField] private Button crateBtn = null;

    [Tooltip("Description/Btn_Close：收起配方列表页")]
    [SerializeField] private Button descriptionCloseBtn = null;

    [Header("仓库展示页（2026-09-25 saika 新增）")]
    [Tooltip("Btn_list/ware_Btn：开关合成界面里的仓库展示页")]
    [SerializeField] private Button wareBtn = null;

    [Tooltip("Create/WarehousePanel：合成界面里的仓库展示页根物体（挂了 UIPanelMotion 就走它的开关动效）")]
    [SerializeField] private GameObject warehousePage = null;

    [Header("配方列表")]
    [Tooltip("Description 页根物体（打开时 SetActive(true)，并 Rebuild 列表）")]
    [SerializeField] private GameObject descriptionRoot = null;

    [Tooltip("Description 页里的配方列表容器（CraftRecipeListView）")]
    [SerializeField] private CraftRecipeListView recipeListView = null;

    [Header("材料列表对话框")]
    [Tooltip("材料列表对话框（CraftMatPickDialog，自己是个 Dialog 面板）")]
    // [已废弃 2026-09-25] 填充改成一键填入后不再使用材料列表对话框;字段保留只为不丢序列化槽位。
    [SerializeField] private CraftMatPickDialog matPickDialog = null;

    [Header("面板")]
    [Tooltip("面板管理器；留空则 Awake 取 PanelManager.Instance")]
    [SerializeField] private PanelManager panelManager = null;

    [Tooltip("打开/关闭本面板的快捷键（批次一测试用；正式入口批次二再定）")]
    [SerializeField] private KeyCode toggleKey = KeyCode.Z;

    // ============================================================
    // 运行时状态
    // ============================================================

    /// <summary>暂存区（两槽共享）—— S3 是普通 C# 类，不能在 Inspector 拖引用，只能这里 new 出来注入</summary>
    public CraftStagingArea Staging { get; private set; }

    /// <summary>当前载入的配方（null = 还没选配方）</summary>
    public CraftRecipeSO SelectedRecipe => selectedRecipe;

    /// <summary>当前载入的配方（内部可写）</summary>
    private CraftRecipeSO selectedRecipe;

    /// <summary>
    /// 状态行文本（合成成功 / 失败提示）。非空时盖过 previewStats 的默认材料清单，
    /// 换配方或槽内容变化时清掉 —— 避免上一条提示一直挂着。
    /// </summary>
    private string statusMessage;

    /// <summary>面板侧两个槽的组件（下标 0 = 左槽、1 = 右槽；仅本组件内部按下标遍历）</summary>
    private readonly CraftMakeSlot[] slotComponents = new CraftMakeSlot[CraftStagingArea.SLOT_COUNT];

    /// <summary>面板侧两个槽真正的槽位号（下标 0 = 左槽、1 = 右槽）</summary>
    private readonly int[] slotIndices = new int[CraftStagingArea.SLOT_COUNT];

    /// <summary>面板侧两个槽的显示元素（下标 0 = 左槽、1 = 右槽）</summary>
    private readonly SlotView[] slotViews = new SlotView[CraftStagingArea.SLOT_COUNT];

    /// <summary>槽位号异常（右槽漏填 / 非法值）的警告只打一次</summary>
    private bool warnedSlotIndexIssue;

    /// <summary>一个槽的显示元素组合（把「槽序号 → 四个 UI 引用」的映射收在一处）</summary>
    private struct SlotView
    {
        public Image icon;
        public TMP_Text name;
        public TMP_Text quantity;
        public TMP_Text owned;
    }

    /// <summary>扣料计划的一步：从 stagingIndex 这一格扣掉 consume 个</summary>
    private struct ConsumeStep
    {
        public int stagingIndex;
        public int consume;
    }

    /// <summary>已经真扣掉的量（失败回滚用）：材料模板 + 来源容器 + 数量</summary>
    private struct ConsumedRecord
    {
        public ItemSO template;
        public bool fromWarehouse;
        public int count;
    }

    // ============================================================
    // 生命周期
    // ============================================================

    /// <summary>
    /// 接线：建暂存区并注入两个槽（共享同一实例）、订阅槽变化与配方点选、绑四个按钮、收起 Description 页。
    /// 面板根在场景里是 inactive 时，Awake 会在第一次打开（PanelManager.OpenPanel → SetActive(true)）时才跑，
    /// 顺序仍是 Awake → OnEnable，所以 OnEnable 里刷新是安全的。
    /// </summary>
    private void Awake()
    {
        if (panelManager == null) panelManager = PanelManager.Instance;

        Staging = new CraftStagingArea();

        slotComponents[0] = slotLeft;
        slotComponents[1] = slotRight;

        slotViews[0] = new SlotView
        {
            icon = slotLeftIcon,
            name = slotLeftName,
            quantity = slotLeftQuantity,
            owned = slotLeftOwned
        };
        slotViews[1] = new SlotView
        {
            icon = slotRightIcon,
            name = slotRightName,
            quantity = slotRightQuantity,
            owned = slotRightOwned
        };

        slotIndices[0] = ResolveSlotIndex(slotLeft, 0);
        slotIndices[1] = ResolveSlotIndex(slotRight, 1);

        for (int i = 0; i < slotComponents.Length; i++)
        {
            CraftMakeSlot slot = slotComponents[i];
            if (slot == null) continue;

            slot.Staging = Staging;                    // 数据层注入（S4 自己不发事件、不碰显示）
            slot.OnSlotChanged += HandleSlotChanged;   // 拖入 / 右键取出成功 → 本组件刷新
        }

        if (recipeListView != null)
            recipeListView.OnRecipePicked += SetRecipe;

        tianchongBtn?.onClick.AddListener(AutoFillRecipeMaterials);   // 2026-09-25 saika 定:填充 = 一键填入,不弹列表
        listBtn?.onClick.AddListener(ToggleDescription);
        crateBtn?.onClick.AddListener(() => DoCraft());   // DoCraft 有返回值，这里包一层 lambda
        descriptionCloseBtn?.onClick.AddListener(CloseDescription);
        wareBtn?.onClick.AddListener(ToggleWarehousePage);          // 仓库展示页开关

        // Description 页初始收起（编辑器里若已设 inactive，这里等价、无副作用）
        if (descriptionRoot != null) descriptionRoot.SetActive(false);
    }

    /// <summary>
    /// 面板被打开（或首次激活）时刷新一次，保证显示的是当前数据；
    /// 并把背包面板带出来（2026-09-25 saika 定：打开合成面板自动打开背包，已经开着就不做操作）。
    /// </summary>
    private void OnEnable()
    {
        RefreshAll();
        OpenInventoryIfClosed();
    }

    /// <summary>
    /// 打开合成面板时自动打开背包面板；背包已经打开就返回、不做任何操作。
    /// 关闭合成面板时**不动**背包（保持玩家自己的开合状态）。
    /// </summary>
    private void OpenInventoryIfClosed()
    {
        InventoryManager inventory = InventoryManager.Instance;
        if (inventory == null || inventory.IsInventoryOpen) return;

        inventory.OpenInventoryPanel();
    }

    /// <summary>
    /// 关闭收尾（面板从 active 变 inactive 时触发：PanelManager 关闭 / SetActive(false) / 场景卸载）：
    /// 暂存材料必须归还来源容器，不能让材料卡在暂存区（规格第三节第 1 条）。
    /// 归还失败（容器满）时材料留在暂存区，打警告 + 状态行提示，绝不静默丢物品。
    /// </summary>
    private void OnDisable()
    {
        // 仓库展示页是独立面板(自己走 PanelManager 开关),关合成面板时不带着它一起关,
        // 免得出现「按一下 ESC 两个都没了」。它由自己的按钮/ESC 关。

        // 配方列表页是主面板的子页(随主面板一起隐藏):这里把它从面板栈里摘掉,
        // 否则栈里会留一个已隐藏的面板,下一次 ESC 计数会错。它已隐藏,关闭守卫只清栈、不播动效。
        if (descriptionRoot != null && panelManager != null)
            panelManager.ClosePanel(descriptionRoot);

        if (Staging == null) return;

        Staging.ReturnAll();

        bool leftover = false;
        for (int i = 0; i < CraftStagingArea.SLOT_COUNT; i++)
        {
            if (!Staging.IsEmpty(i))
            {
                leftover = true;
                break;
            }
        }

        if (leftover)
        {
            statusMessage = StatusReturnFailed;
            Debug.LogWarning("[CraftMakePanel] 关闭面板时有材料没能归还（背包/仓库放不下），材料仍留在暂存区。", this);
        }
        else
        {
            statusMessage = null;   // 正常收尾：下次打开是干净状态
        }

        RefreshAll();
    }

    /// <summary>
    /// 快捷键只做开关（规格第三节第 8 条：批次一用 Z 测试，正式入口批次二再定）。
    ///
    /// 注意：本组件挂在被 PanelManager 开关的面板根上，面板关着时本物体 inactive → Update 不跑。
    /// 所以「关着的时候按 Z 打开」要靠 Canvas 上的 HotkeyManager（键位 Z + 面板 = Create）来兜；
    /// 两者同时启用会在面板开着时同帧双切换 —— 用 HotkeyManager 时请把本字段设成 None。
    /// </summary>
    private void Update()
    {
        if (toggleKey == KeyCode.None) return;
        if (Input.GetKeyDown(toggleKey)) TogglePanel();
    }

    // ============================================================
    // 打开 / 关闭
    // ============================================================

    /// <summary>
    /// 开关面板：已开则关（关闭收尾走 OnDisable → 材料归还），未开则开。
    /// 走 PanelManager 是为了暂停/锁输入/光标口径统一；面板管理器缺失时退化成直接切自身显隐（收尾照样走 OnDisable）。
    /// </summary>
    public void TogglePanel()
    {
        if (panelManager == null) panelManager = PanelManager.Instance;

        if (panelManager != null)
        {
            if (panelManager.IsPanelOpen(gameObject)) panelManager.ClosePanel(gameObject);
            else panelManager.OpenPanel(gameObject);
            return;
        }

        gameObject.SetActive(!gameObject.activeSelf);
    }

    // ============================================================
    // 配方载入 / 配方列表页
    // ============================================================

    /// <summary>
    /// 载入一条配方（配方列表点选 / 外部调用）：清掉上一条的提示、同步两槽需求、刷新界面。
    /// 传 null = 清空选择（产物预览回到占位提示）。已填入的暂存材料不动 —— 槽是按材料种类匹配配方的。
    /// </summary>
    public void SetRecipe(CraftRecipeSO recipe)
    {
        selectedRecipe = recipe;
        statusMessage = null;
        RefreshAll();
    }

    /// <summary>
    /// List_Btn：唤出 / 收起 Description 配方列表页。
    /// 打开时重建一次列表，保证是当前最新的配方数据（资产可能在别处被改过）。
    /// </summary>
    public void ToggleDescription()
    {
        if (descriptionRoot == null) return;

        if (panelManager == null) panelManager = PanelManager.Instance;

        if (panelManager != null)
        {
            // 子页进栈(Description 上挂了 CraftSubPage):ESC 会先收它,再按才关合成主面板;
            // 重复调用由面板管理器的幂等守卫兜住,不会重播动效。
            bool willShow = !descriptionRoot.activeSelf;
            panelManager.TogglePanel(descriptionRoot);
            if (willShow) recipeListView?.Rebuild();
            return;
        }

        // 兜底:没有面板管理器(单场景调试)时退化成本地显隐
        bool show = !descriptionRoot.activeSelf;
        descriptionRoot.SetActive(show);
        if (show) recipeListView?.Rebuild();
    }

    /// <summary>Description/Btn_Close：收起配方列表页（选择配方后也可以由外部调它收起来）</summary>
    public void CloseDescription()
    {
        if (descriptionRoot == null) return;

        if (panelManager == null) panelManager = PanelManager.Instance;
        if (panelManager != null)
        {
            panelManager.ClosePanel(descriptionRoot);
            return;
        }

        descriptionRoot.SetActive(false);
    }

    // ============================================================
    // 仓库展示页（ware_Btn，2026-09-25 saika 新增）
    // ============================================================

    /// <summary>
    /// ware_Btn：开关合成界面里的仓库展示页（Create/WarehousePanel）。
    ///
    /// 这个面板挂的是 WarehousePanel 脚本，数据直接读 InventoryManager 的那一份仓库
    /// （与真正仓库面板同源），所以这里只负责显隐，不碰任何数据。
    /// 面板根挂了 UIPanelMotion 时走它的开/关动效，没挂就硬切。
    /// </summary>
    public void ToggleWarehousePage()
    {
        if (warehousePage == null) return;

        if (panelManager == null) panelManager = PanelManager.Instance;

        // 走面板管理器:显隐、开关动效、栈(ESC 分层关闭)都由它统一管;
        // 重复调用有幂等守卫,已经开着再按不会重播动效(不会闪)。
        if (panelManager != null)
        {
            panelManager.TogglePanel(warehousePage);
            return;
        }

        // 兜底:没有面板管理器时退化成本地显隐
        warehousePage.SetActive(!warehousePage.activeSelf);
    }

    /// <summary>收起仓库展示页（外部调用 / 收起所有子页时用）</summary>
    public void CloseWarehousePage()
    {
        if (warehousePage == null) return;

        if (panelManager == null) panelManager = PanelManager.Instance;
        if (panelManager != null)
        {
            panelManager.ClosePanel(warehousePage);
            return;
        }

        warehousePage.SetActive(false);
    }

    // ============================================================
    // 填入（tianchong_Btn → 一键填入，2026-09-25 saika 定）
    // ============================================================

    /// <summary>
    /// tianchong_Btn:一键填入 —— 按当前配方把需要的材料全部搬进两个槽。
    ///
    /// 规则:
    ///   · 来源 = 背包 + 仓库,有多少拿多少(不按需求数量截断,不够也照填;多余部分留在槽里);
    ///   · 目标槽 = 配方第 1 条需求 → 左槽,第 2 条 → 右槽(与槽位认领口径一致);
    ///   · 填之前先清空两槽(材料按来源归还;某格归还失败就保留该格内容,同名材料会继续叠上去);
    ///   · 未载入配方时只提示、不动数据。
    ///
    /// 写入走与拖动填入同一条路径(CraftStagingArea.TryStage),同种材料在同一格内累加。
    /// 不再使用材料列表对话框(CraftMatPickDialog),那条路已废弃。
    /// </summary>
    public void AutoFillRecipeMaterials()
    {
        if (Staging == null) return;

        if (selectedRecipe == null || !selectedRecipe.IsValid())
        {
            statusMessage = StatusNoRecipe;
            RefreshAll();
            return;
        }

        InventoryManager inventory = InventoryManager.Instance;
        if (inventory == null)
        {
            statusMessage = StatusNoInventory;
            RefreshAll();
            return;
        }

        // ① 清空两槽(归还失败的那格保留原内容:一键填不硬塞,也不丢材料)
        for (int i = 0; i < CraftStagingArea.SLOT_COUNT; i++)
            Staging.TryUnstage(i);

        // ② 按配方需求逐条搬材料(材料种类 = 配方条数,最多两槽)
        var needs = selectedRecipe.GetValidMaterials();
        for (int i = 0; i < needs.Count && i < CraftStagingArea.SLOT_COUNT; i++)
        {
            ItemSO template = needs[i].item;
            if (template == null) continue;

            int stageIndex = slotIndices[i];

            // 每轮重新扫描:来源容器索引会随扣除变化,一格搬完再搬下一格;
            // guard 只防实现出意外时死循环(正常最多 = 背包格数 + 仓库格数)
            bool moved = true;
            int guard = 0;
            while (moved && guard++ < 64)
            {
                moved = TryStageWholeStack(inventory, template, false, stageIndex)
                     || TryStageWholeStack(inventory, template, true, stageIndex);
            }
        }

        statusMessage = null;
        RefreshAll();
    }

    /// <summary>
    /// 把背包 / 仓库里第一个匹配 template 的格子整格填进指定槽;填到了返回 true。
    /// 扫不到该材料返回 false —— 一键填的循环靠它收敛。
    /// </summary>
    private bool TryStageWholeStack(InventoryManager inventory, ItemSO template, bool fromWarehouse, int stageIndex)
    {
        IReadOnlyList<ItemInstance> list = fromWarehouse ? inventory.WarehouseItems : inventory.PlayerItems;

        for (int i = 0; i < list.Count; i++)
        {
            ItemInstance item = list[i];
            if (item == null || item.template != template || item.stackSize <= 0) continue;

            return Staging.TryStage(stageIndex, fromWarehouse, i, item.stackSize);
        }
        return false;
    }

    // ============================================================
    // 合成（CrateBtn，原子操作）
    // ============================================================

    /// <summary>
    /// 执行合成。顺序：
    ///   ① 只读校验配方有效 + 两槽暂存材料按种类与数量匹配配方需求（忽略槽序；槽内数量可多于需求，
    ///      多出来的部分合成后留在暂存区）；
    ///   ② 只读判「产物放得下」：背包 CanFitFully 为真 → 走背包；否则仓库容量试算为真 → 走仓库；
    ///      两者都不行 → 不扣任何材料，previewStats 提示「背包与仓库都放不下，无法合成」；
    ///   ③ 扣料（按需求数量扣，做法见 ApplyConsumePlan）；任一步失败 → 回滚并放弃；
    ///   ④ 产物落地（背包 AddItem / 仓库 TryAddToWarehouse）。成功 → 槽内刷新 + 结果提示，配方保留（可连续制作）。
    ///
    /// 为什么 ③ 在 ④ 之前（与规格「先确保产物放得下再扣料」的落地顺序一致、但把落地动作放到扣料之后）：
    ///   · 「先判容量」的要求由 ② 承担 —— 容量不够时一格材料都没扣，原子性不受影响；
    ///   · 扣料要把暂存格整格取回容器再精确扣除。若产物先落袋，背包可能刚好被占满，
    ///     那时「整格取出」会因归还失败而整笔放弃 —— 表现成「产物已给、材料没扣」的半笔。
    ///   · 反过来先扣料：扣料过程对容器的净占用为零（取回多少就扣掉多少），
    ///     ② 判过的容量在 ④ 时依然成立（面板打开期间游戏暂停、容器不会被别处改动）。
    /// </summary>
    public bool DoCraft()
    {
        statusMessage = null;   // 每次点合成重新判

        InventoryManager inventory = InventoryManager.Instance;
        if (inventory == null)
        {
            statusMessage = StatusNoInventory;
            RefreshAll();
            return false;
        }

        // ① 材料校验（纯只读；不足时 previewStats 会显示「缺少:…」）
        if (!TryBuildConsumePlan(out List<ConsumeStep> plan, out _))
        {
            RefreshAll();
            return false;
        }

        ItemSO result = selectedRecipe.resultItem;
        int resultCount = Mathf.Max(1, selectedRecipe.resultCount);

        // ② 只读判容量：背包优先，仓库兜底
        bool toBackpack = inventory.CanFitFully(result, resultCount);
        bool toWarehouse = false;
        if (!toBackpack)
        {
            toWarehouse = CanFitWarehouse(result, resultCount);
            if (!toWarehouse)
            {
                statusMessage = StatusNoSpace;
                RefreshAll();
                return false;
            }
        }

        // ③ 扣料（原子：任一步失败 → 回滚 + 放弃）
        if (!ApplyConsumePlan(plan, out List<ConsumedRecord> consumed))
        {
            statusMessage = StatusConsumeFailed;
            RefreshAll();
            return false;
        }

        // ④ 产物落地（② 用的就是这两个 API 的容量口径，正常不会失败；真失败就退款保原子）
        bool landed = toBackpack
            ? inventory.AddItem(result, resultCount)
            : inventory.TryAddToWarehouse(result, resultCount);

        if (!landed)
        {
            Debug.LogWarning("[CraftMakePanel] 产物落地失败（材料已扣除），把扣掉的材料退回来源容器。", this);
            RefundConsumedRecords(consumed);
            statusMessage = StatusLandingFailed;
            RefreshAll();
            return false;
        }

        statusMessage = "合成成功:" + CraftRecipeListEntry.BuildResultLabel(selectedRecipe);
        RefreshAll();
        return true;
    }

    /// <summary>
    /// 材料是否已按「种类 + 数量」满足配方需求（忽略槽序）。供 CrateBtn 可点性与自测使用。
    /// 纯只读，不改动任何数据。
    /// </summary>
    public bool CanCraft()
    {
        return TryBuildConsumePlan(out _, out _);
    }

    // ============================================================
    // 扣料计划（纯只读）
    // ============================================================

    /// <summary>
    /// 生成扣料计划：把配方需求按「材料种类」聚合，再按槽序从两个暂存格里依次认领，
    /// 得到每格要扣多少（同一格可能被多个需求条目共享，用 claimed 防超扣）。
    ///
    /// 匹配口径（规格第三节第 7 条 + 合成一节第 1 条）：忽略槽序，只看两个槽里该材料的总量够不够；
    /// 槽内数量大于需求时，多出来的部分不进计划（合成后留在暂存区）。
    /// 认领不满 = 材料不足 → 返回 false 并把「缺什么」写进 shortageText（纯只读，不动数据）。
    /// </summary>
    private bool TryBuildConsumePlan(out List<ConsumeStep> plan, out string shortageText)
    {
        plan = new List<ConsumeStep>();
        shortageText = string.Empty;

        if (Staging == null) return false;
        if (selectedRecipe == null || !selectedRecipe.IsValid()) return false;

        List<CraftMaterial> materials = selectedRecipe.GetValidMaterials();
        if (materials == null || materials.Count == 0) return false;

        int[] claimed = new int[CraftStagingArea.SLOT_COUNT];
        var visited = new List<ItemSO>();
        var shortages = new List<string>();

        for (int i = 0; i < materials.Count; i++)
        {
            ItemSO item = materials[i].item;
            if (item == null) continue;
            if (visited.Contains(item)) continue;   // 同种材料多条：在第一条里已合并需求，不再重复认领
            visited.Add(item);

            int remaining = GetRecipeCountForTemplate(item);
            if (remaining <= 0) continue;

            for (int s = 0; s < CraftStagingArea.SLOT_COUNT && remaining > 0; s++)
            {
                CraftStagingArea.Entry entry = Staging.GetEntry(s);
                if (entry == null || entry.template != item) continue;

                int available = entry.count - claimed[s];
                if (available <= 0) continue;

                int take = Mathf.Min(remaining, available);
                claimed[s] += take;
                remaining -= take;
                plan.Add(new ConsumeStep { stagingIndex = s, consume = take });
            }

            if (remaining > 0)
                shortages.Add(item.itemName + CraftRecipeListEntry.CountSuffix + remaining);
        }

        if (shortages.Count > 0)
        {
            shortageText = string.Join(ShortageJoinSeparator, shortages);
            plan.Clear();
            return false;
        }

        return true;
    }

    // ============================================================
    // 扣料执行 / 回滚
    // ============================================================

    /// <summary>
    /// 按计划扣料。S3 只有整格 TryUnstage，所以「部分扣除」就地实现（规格第六节 批次一 第 3 条）：
    ///   ① 整格 TryUnstage 取出（材料回到来源容器，容器腾出位置）；
    ///   ② 若该格还有「多余部分」（原量 − 要扣量 &gt; 0），把多余部分 TryStage 放回同一格
    ///      （来源容器与来源格号沿用原记录 → 等价于「只扣掉需求数」，多余部分留在暂存区）；
    ///   ③ 要扣的量从来源容器里精确扣除（逐个格子扣，每格按实际堆叠数取小）。
    ///
    /// 每步都判返回值；任一步失败 → 把已经扣掉的退回来源容器、把已经取出的材料尽力放回暂存格，返回 false。
    /// 净效果不变量：每格只让「容器 + 暂存区」里该材料的合计减少 consume 个，其余条目一条不动。
    /// 失败路径分析：② 之后来源容器里该材料的量 ≥ 要扣量（就是刚归还进去的那一批），
    /// 且容器里必然存在一个堆叠数 ≥ 要扣量的格子（合并进去的会补满、没合并的是新建的整叠），
    /// 所以 ③ 实际不可达 —— 兜底只保证「物品不凭空消失、总量只多不少」。
    /// </summary>
    private bool ApplyConsumePlan(List<ConsumeStep> plan, out List<ConsumedRecord> consumed)
    {
        consumed = new List<ConsumedRecord>();
        if (plan == null || Staging == null) return false;

        for (int i = 0; i < plan.Count; i++)
        {
            ConsumeStep step = plan[i];
            if (step.consume <= 0) continue;

            CraftStagingArea.Entry entry = Staging.GetEntry(step.stagingIndex);
            if (entry == null || entry.template == null)
                return FailConsume(consumed, "暂存格已空");

            ItemSO template = entry.template;
            bool fromWarehouse = entry.fromWarehouse;
            int stagedCount = entry.count;
            int overflow = stagedCount - step.consume;
            if (overflow < 0) return FailConsume(consumed, "扣量超过暂存量");

            // ① 整格取出（材料回到来源容器；失败 = 容器放不下，此时该格原封不动）
            if (!Staging.TryUnstage(step.stagingIndex))
                return FailConsume(consumed, "整格取出失败");

            // ② 多余部分放回同一格
            if (overflow > 0)
            {
                int sourceIndex = FindContainerStackIndex(fromWarehouse, template, overflow);
                bool restored = sourceIndex >= 0 &&
                                Staging.TryStage(step.stagingIndex, fromWarehouse, sourceIndex, overflow);
                if (!restored)
                {
                    // 放不回去 → 尽力把原量重新放回该格（材料本体在来源容器里，不会丢）
                    RestoreStaged(step.stagingIndex, template, fromWarehouse, stagedCount);
                    return FailConsume(consumed, "多余部分放回失败");
                }
            }

            // ③ 从来源容器精确扣除要扣的量
            if (!TakeExact(fromWarehouse, template, step.consume, out int removed))
            {
                // 先把手滑扣掉的退回容器，再把「该留在暂存格的那份」尽力放回（槽非空时放不进去，
                // 材料就留在来源容器里 —— 总量守恒，只是位置变了）
                Refund(template, fromWarehouse, removed);
                RestoreStaged(step.stagingIndex, template, fromWarehouse, step.consume);
                return FailConsume(consumed, "精确扣除失败");
            }

            consumed.Add(new ConsumedRecord
            {
                template = template,
                fromWarehouse = fromWarehouse,
                count = step.consume
            });
        }

        return true;
    }

    /// <summary>扣料失败收尾：把之前几格已经真扣掉的量退回来源容器（总量只多不少），返回 false</summary>
    private bool FailConsume(List<ConsumedRecord> consumed, string reason)
    {
        Debug.LogWarning($"[CraftMakePanel] 扣料中途失败（{reason}），已尽力回滚：扣掉的退回来源容器、可取出的材料放回暂存格。", this);
        RefundConsumedRecords(consumed);
        return false;
    }

    /// <summary>把计划里已经真扣掉的量逐条退回来源容器（仅用于失败善后）</summary>
    private void RefundConsumedRecords(List<ConsumedRecord> consumed)
    {
        if (consumed == null) return;

        for (int i = 0; i < consumed.Count; i++)
        {
            Refund(consumed[i].template, consumed[i].fromWarehouse, consumed[i].count);
        }
    }

    /// <summary>
    /// 退回 count 个 template 到指定容器（仓库走 TryAddToWarehouse、背包先只读判容量再 AddItem）。
    /// 只用于合成失败时的善后；容器满时退不回去会打警告（这一步不会凭空创造物品）。
    /// </summary>
    private static bool Refund(ItemSO template, bool fromWarehouse, int count)
    {
        if (template == null || count <= 0) return true;

        InventoryManager inventory = InventoryManager.Instance;
        if (inventory == null) return false;

        bool refunded;
        if (fromWarehouse)
        {
            refunded = inventory.TryAddToWarehouse(template, count);
        }
        else
        {
            refunded = inventory.CanFitFully(template, count) && inventory.AddItem(template, count);
        }

        if (!refunded)
            Debug.LogWarning($"[CraftMakePanel] 回滚时 {template.itemName} x{count} 退回失败（容器放不下），该材料被消耗。");

        return refunded;
    }

    // ============================================================
    // 容器读写辅助
    // ============================================================

    /// <summary>
    /// 只读试算：仓库能否全量装下 count 个 template。
    ///
    /// 口径逐行照抄 <see cref="InventoryManager.TryAddToWarehouse"/> 的容量试算
    /// （同类剩余堆叠余量 + 剩余槽位数 × template.maxStack）—— S2 只提供「会写入」的 TryAddToWarehouse，
    /// 而合成需要「先只读判容量、后落地」，所以这里复刻一份只读版。
    /// 若将来 S2 的容量口径变了，这里会偏保守（判 false → 本次合成放弃、材料一格不扣），不会凭空出货。
    /// </summary>
    public static bool CanFitWarehouse(ItemSO template, int count)
    {
        if (template == null || count <= 0) return false;

        InventoryManager inventory = InventoryManager.Instance;
        if (inventory == null) return false;

        int capacity = 0;

        IReadOnlyList<ItemInstance> items = inventory.WarehouseItems;
        if (items != null)
        {
            for (int i = 0; i < items.Count; i++)
            {
                ItemInstance item = items[i];
                if (item == null || item.template != template) continue;
                if (!item.CanStack) continue;

                capacity += item.RemainingStackSpace;
            }
        }

        capacity += (InventoryManager.WAREHOUSE_MAX_SLOTS - inventory.WarehouseCount) * template.maxStack;
        return capacity >= count;
    }

    /// <summary>
    /// 在指定容器里找一格「至少装了 minCount 个 template」的格子，返回下标（-1 = 没有）。
    /// 优先返回堆叠数最大的那格 —— 部分扣除、多余部分放回都靠它一次到位（TryStage 不接受已有材料的格子，只放一次）。
    /// 仓库侧扣空一格会被 CleanupZeroStackWarehouse 压掉下标，所以调用方每轮都要重新找。
    /// </summary>
    private static int FindContainerStackIndex(bool fromWarehouse, ItemSO template, int minCount)
    {
        InventoryManager inventory = InventoryManager.Instance;
        if (inventory == null || template == null) return -1;

        IReadOnlyList<ItemInstance> items = fromWarehouse ? inventory.WarehouseItems : inventory.PlayerItems;
        if (items == null) return -1;

        int best = -1;
        int bestSize = 0;

        for (int i = 0; i < items.Count; i++)
        {
            ItemInstance item = items[i];
            if (item == null || item.template != template) continue;
            if (item.stackSize < minCount) continue;

            if (item.stackSize > bestSize)
            {
                bestSize = item.stackSize;
                best = i;
            }
        }

        return best;
    }

    /// <summary>
    /// 从容器里精确扣除 count 个 template：逐个格子取（每格按实际堆叠数取小），仓库侧压掉空条目后重新扫描。
    /// 返回 true 时保证恰好扣掉 count 个（容器里不够则返回 false，并把已扣掉的量从 removed 报出来供回滚）。
    /// </summary>
    private static bool TakeExact(bool fromWarehouse, ItemSO template, int count, out int removed)
    {
        removed = 0;
        if (template == null || count <= 0) return false;

        InventoryManager inventory = InventoryManager.Instance;
        if (inventory == null) return false;

        int remaining = count;
        int guard = 0;   // 防死循环护栏（每轮要么减 remaining 要么返回，正常跑不到）

        while (remaining > 0 && guard++ < 64)
        {
            int index = FindContainerStackIndex(fromWarehouse, template, 1);
            if (index < 0) return false;

            ItemInstance item = fromWarehouse
                ? inventory.GetWarehouseItem(index)
                : inventory.GetPlayerItem(index);
            if (item == null || item.template != template || item.stackSize <= 0) return false;

            int take = Mathf.Min(remaining, item.stackSize);
            bool ok = fromWarehouse
                ? inventory.RemoveWarehouseItem(index, take)
                : inventory.RemoveItem(index, take);
            if (!ok) return false;

            remaining -= take;
            removed += take;
        }

        return remaining <= 0;
    }

    /// <summary>把 count 个 template 重新放回指定暂存格（失败善后；该格已有材料时放不进去，返回 false）</summary>
    private bool RestoreStaged(int stagingIndex, ItemSO template, bool fromWarehouse, int count)
    {
        if (Staging == null || template == null || count <= 0) return false;

        int index = FindContainerStackIndex(fromWarehouse, template, count);
        if (index < 0) return false;

        return Staging.TryStage(stagingIndex, fromWarehouse, index, count);
    }

    /// <summary>统计某材料在暂存区两格里的合计数量（合成判定与「拥有总数」都要算它）</summary>
    private int CountStaged(ItemSO template)
    {
        if (Staging == null || template == null) return 0;

        int total = 0;
        for (int i = 0; i < CraftStagingArea.SLOT_COUNT; i++)
        {
            CraftStagingArea.Entry entry = Staging.GetEntry(i);
            if (entry != null && entry.template == template) total += entry.count;
        }
        return total;
    }

    /// <summary>
    /// 该材料的「总拥有」= 背包 + 仓库 + 暂存区合计（界面的 QuantityALL）。
    /// 暂存区的必须算进来：材料填入时已经从容器真扣走了，不算会显示少。
    /// </summary>
    private int CountTotalOwned(ItemSO template)
    {
        if (template == null) return 0;

        InventoryManager inventory = InventoryManager.Instance;
        int total = 0;

        if (inventory != null)
        {
            total += CountInContainer(inventory.PlayerItems, template);
            total += CountInContainer(inventory.WarehouseItems, template);
        }

        return total + CountStaged(template);
    }

    /// <summary>统计单个容器里某材料的数量（只读）</summary>
    private static int CountInContainer(IReadOnlyList<ItemInstance> items, ItemSO template)
    {
        if (items == null || template == null) return 0;

        int total = 0;
        for (int i = 0; i < items.Count; i++)
        {
            ItemInstance item = items[i];
            if (item == null || item.template != template) continue;
            total += item.stackSize;
        }
        return total;
    }

    // ============================================================
    // 配方需求（槽 ↔ 材料）
    // ============================================================

    /// <summary>
    /// 该暂存格的配方需求数量：槽里有料 → 按「该材料在配方里对应的那一条」找（找不到按 0）；
    /// 空槽 → 按数组顺序认领（槽 0 = materials[0]、槽 1 = materials[1]，空条目 = 无需求）；
    /// 未载入配方一律 0。
    /// </summary>
    private int GetRequiredCount(int stagingIndex)
    {
        ItemSO template = GetRequiredTemplate(stagingIndex);
        return template != null ? GetRecipeCountForTemplate(template) : 0;
    }

    /// <summary>
    /// 该槽「当前该显示哪种需求材料」：槽里有料就是槽里的材料（需求数另按配方种类找）；
    /// 空槽则是按数组顺序认领的那一条（count &gt; 0 才算有需求）。
    /// </summary>
    private ItemSO GetRequiredTemplate(int stagingIndex)
    {
        if (selectedRecipe == null) return null;

        CraftStagingArea.Entry entry = Staging != null ? Staging.GetEntry(stagingIndex) : null;
        if (entry != null && entry.template != null) return entry.template;

        CraftMaterial[] materials = selectedRecipe.materials;
        if (materials == null) return null;
        if (stagingIndex < 0 || stagingIndex >= materials.Length) return null;
        if (materials[stagingIndex].item == null || materials[stagingIndex].count <= 0) return null;

        return materials[stagingIndex].item;
    }

    /// <summary>
    /// 该材料在配方里的需求数量：同种材料多条时累加（数量口径：同种材料要多个写进数量里，不占两格）；
    /// 配方里没有这种材料 = 0。
    /// </summary>
    private int GetRecipeCountForTemplate(ItemSO template)
    {
        if (selectedRecipe == null || template == null) return 0;

        CraftMaterial[] materials = selectedRecipe.materials;
        if (materials == null) return 0;

        int total = 0;
        for (int i = 0; i < materials.Length; i++)
        {
            if (materials[i].item != template) continue;
            if (materials[i].count <= 0) continue;
            total += materials[i].count;
        }
        return total;
    }

    // ============================================================
    // 界面刷新
    // ============================================================

    /// <summary>全量刷新：两个材料槽 + 产物预览 + 合成按钮可点性，并同步两槽的拖入上限</summary>
    public void RefreshAll()
    {
        for (int i = 0; i < slotViews.Length; i++)
            RefreshSlot(i);

        RefreshPreview();
        RefreshCrateButton();
        SyncPreferredCounts();
    }

    /// <summary>
    /// 刷新一个槽（panelSlot：0 = 左槽、1 = 右槽）：
    ///   图标 = 槽内材料图标；空槽但该槽有配方需求 → 显示需求材料图标并调暗（占位提示）；
    ///   名称 = 材料名（同上口径）；
    ///   数量 = 「已填数量/需求数量」，未载入配方时只显示已填数量；
    ///   拥有总数 = 该材料的背包 + 仓库 + 暂存区合计。
    /// </summary>
    private void RefreshSlot(int panelSlot)
    {
        SlotView view = slotViews[panelSlot];
        int stageIndex = slotIndices[panelSlot];

        CraftStagingArea.Entry entry = Staging != null ? Staging.GetEntry(stageIndex) : null;
        ItemSO staged = entry != null ? entry.template : null;

        int required = GetRequiredCount(stageIndex);
        ItemSO requiredTemplate = GetRequiredTemplate(stageIndex);

        ItemSO display = staged != null ? staged : requiredTemplate;
        bool placeholder = staged == null && requiredTemplate != null;

        if (view.icon != null)
        {
            view.icon.sprite = display != null ? display.icon : null;
            view.icon.color = placeholder ? PlaceholderIconColor : NormalIconColor;
        }

        if (view.name != null)
            view.name.text = display != null ? display.itemName : string.Empty;

        // 2026-09-25 saika 定:数量只用一个文本(界面里的 Quantity),内容 =「总拥有/配方需求」。
        // 界面里的 QuantityALL(owned 字段)不再使用,置空即可,那个子物体可以删。
        if (view.quantity != null)
            view.quantity.text = BuildQuantityText(display, required);

        if (view.owned != null)
            view.owned.text = string.Empty;
    }

    /// <summary>
    /// 数量文本（2026-09-25 saika 定）：一个文本里显示「总拥有/配方需求」，
    /// 总拥有 = 背包 + 仓库 + 暂存区该材料合计；未载入配方（无需求）时只显示总拥有。
    /// 槽里已经填了多少由图标与占位提示体现，不再占用这个文本。
    /// </summary>
    private string BuildQuantityText(ItemSO display, int required)
    {
        if (display == null) return string.Empty;

        int owned = CountTotalOwned(display);
        return required > 0 ? owned + QuantitySeparator + required : owned.ToString();
    }

    /// <summary>刷新产物预览（图标 / 名称 / 描述 / 状态行 / 占位提示）</summary>
    private void RefreshPreview()
    {
        ItemSO result = selectedRecipe != null ? selectedRecipe.resultItem : null;

        if (previewIcon != null) previewIcon.sprite = result != null ? result.icon : null;
        if (previewName != null) previewName.text = result != null ? result.itemName : string.Empty;
        if (previewDesc != null) previewDesc.text = result != null ? result.description : string.Empty;
        if (previewPlaceholder != null) previewPlaceholder.SetActive(selectedRecipe == null);
        if (previewStats != null) previewStats.text = BuildStatsText();
    }

    /// <summary>
    /// 状态行文本：有状态提示（合成成功 / 失败）就先显示它；
    /// 否则显示「材料:材料名 x数量 + …」，材料不够时再补一行「缺少:材料名 x数量」。
    /// 未载入配方 = 空串（占位提示由 previewPlaceholder 负责）。
    /// </summary>
    private string BuildStatsText()
    {
        if (!string.IsNullOrEmpty(statusMessage)) return statusMessage;
        if (selectedRecipe == null) return string.Empty;

        string text = MaterialPrefix + BuildMaterialListText();

        string shortage = BuildShortageText();
        if (!string.IsNullOrEmpty(shortage))
            text += "\n" + ShortagePrefix + shortage;

        return text;
    }

    /// <summary>材料清单文本：「材料名 x数量」按配方数组顺序用「 + 」连接（空条目跳过）</summary>
    private string BuildMaterialListText()
    {
        List<CraftMaterial> materials = selectedRecipe != null ? selectedRecipe.GetValidMaterials() : null;
        if (materials == null || materials.Count == 0) return string.Empty;

        var parts = new List<string>(materials.Count);
        for (int i = 0; i < materials.Count; i++)
        {
            ItemSO item = materials[i].item;
            parts.Add((item != null ? item.itemName : string.Empty) + CraftRecipeListEntry.CountSuffix + materials[i].count);
        }
        return string.Join(MaterialJoinSeparator, parts);
    }

    /// <summary>
    /// 「缺少:材料名 x缺少数」——按材料种类聚合需求后再与暂存区总量比（同种材料多条不会被算成刚好够），
    /// 只列真缺的那几种；都不缺 = 空串。
    /// </summary>
    private string BuildShortageText()
    {
        List<CraftMaterial> materials = selectedRecipe != null ? selectedRecipe.GetValidMaterials() : null;
        if (materials == null || materials.Count == 0) return string.Empty;

        var visited = new List<ItemSO>();
        var parts = new List<string>();

        for (int i = 0; i < materials.Count; i++)
        {
            ItemSO item = materials[i].item;
            if (item == null || visited.Contains(item)) continue;
            visited.Add(item);

            int need = GetRecipeCountForTemplate(item);
            int staged = CountStaged(item);
            if (staged < need)
                parts.Add(item.itemName + CraftRecipeListEntry.CountSuffix + (need - staged));
        }

        return string.Join(ShortageJoinSeparator, parts);
    }

    /// <summary>
    /// 合成按钮可点性：配方无效 / 材料不足 → 不可点。
    /// 容量不参与（容量只在点下去那一刻判，避免玩家把材料凑齐了却因为背包一时满而被灰掉）。
    /// </summary>
    private void RefreshCrateButton()
    {
        if (crateBtn != null) crateBtn.interactable = CanCraft();
    }

    /// <summary>
    /// 把两槽的需求数写进 S4 的 PreferredCount（拖入时按需求封顶，与 tianchong 填入同一口径）。
    /// 需求为 0（未载入配方 / 配方里没这条材料 / 空槽无需求）写 -1 → 拖入按源格整格填入，不设上限。
    /// </summary>
    private void SyncPreferredCounts()
    {
        for (int i = 0; i < slotComponents.Length; i++)
        {
            CraftMakeSlot slot = slotComponents[i];
            if (slot == null) continue;

            int required = GetRequiredCount(slotIndices[i]);
            slot.PreferredCount = required > 0 ? required : -1;
        }
    }

    // ============================================================
    // 槽位号 / 回调
    // ============================================================

    /// <summary>
    /// 取槽位号。S4 只暴露只读的 <see cref="CraftMakeSlot.SlotIndex"/>（没有 setter，面板无法回写），
    /// 所以右槽漏填（仍是默认 0，与左槽撞号）或填了非法值时按 fallback 兜底，并打一条警告提醒编辑器侧补填。
    /// 注意兜底只影响本组件的读写口径：拖入走 S4 自己的 slotIndex，所以 Slot_Right 的 slotIndex 必须人工填 1。
    /// </summary>
    private int ResolveSlotIndex(CraftMakeSlot slot, int fallback)
    {
        if (slot == null) return fallback;

        int index = slot.SlotIndex;
        bool inRange = index >= 0 && index < CraftStagingArea.SLOT_COUNT;
        bool collidesWithLeftSlot = fallback != 0 && index == 0;

        if (inRange && !collidesWithLeftSlot) return index;

        if (!warnedSlotIndexIssue)
        {
            warnedSlotIndexIssue = true;
            Debug.LogWarning("[CraftMakePanel] 槽位号有问题：Slot_Right 的 slotIndex 必须填 1（默认 0 会与左槽撞号），" +
                             "本组件暂按 0/1 兜底显示，但拖入仍按槽自身的 slotIndex 落格。", this);
        }

        return fallback;
    }

    /// <summary>S4 的槽内容变化回调（拖入成功 / 右键取出成功）→ 清掉过期提示 + 全量刷新</summary>
    private void HandleSlotChanged(int slotIndex)
    {
        statusMessage = null;
        RefreshAll();
    }
}
