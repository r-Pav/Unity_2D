using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

/// <summary>
/// 存档/读档面板 — 挂 SavePanel（mode=Save）/ LoadPanel（mode=Load）。
/// IPanel：FullScreen + Pause + Lock + Cursor；实现 ISlideClose（兼容 MainMenu / PanelManager 旧分支的调用入口）。
/// 动效（S3 起）：本面板不再自带滑入/滑出手写协程，统一交给 UIPanelMotion——
///   打开：调用方（PanelManager.OpenPanel / MainMenu.OpenSubPanel）SetActive(true) 后调 UIPanelMotion.PlayOpen；
///   关闭：PanelManager.CloseTopPanel 优先走 UIPanelMotion.PlayClose；ISlideClose.SlideClose 内部转调 PlayClose，
///         未挂 UIPanelMotion（saika 场景配置前）时直接 onComplete（瞬间关闭，不写代码兜底动画）。
/// 槽位：slotUIs[5] = 手动槽 0-4；autoSlot = 自动存档槽 5（只读，点按无效、无删除按钮）。
/// 交互：
///   Save：点空槽 → 直接保存；点有存档槽 → 确认"覆盖存档？"；Btn_Delete → 确认"确认删除该存档？"
///   Load：点有存档槽 → 确认"读取该存档？"；空槽/无数据 → 无反应
/// 确认区（ConfirmArea，初始 inactive）：confirmText + confirmOk + confirmCancel。
/// </summary>
public class SaveLoadPanel : MonoBehaviour, IPanel, ISlideClose
{
    public enum Mode
    {
        Save,
        Load
    }

    public PanelType PanelType => PanelType.FullScreen;
    public bool PauseGame => true;
    public bool LockInput => true;
    public bool ShowCursor => true;

    [Header("模式")]
    [Tooltip("SavePanel 选 Save；LoadPanel 选 Load")]
    [SerializeField] private Mode mode = Mode.Save;

    [Header("槽位")]
    [Tooltip("5 个手动存档槽（Slot_1 ~ Slot_5，对应存档槽 0-4）")]
    [SerializeField] private SaveSlotUI[] slotUIs;
    [Tooltip("自动存档槽（Slot_Auto，对应存档槽 5，只读）")]
    [SerializeField] private SaveSlotUI autoSlot;

    [Header("确认区")]
    [Tooltip("确认区根物体（文本 + 确定/取消，初始 inactive）")]
    [SerializeField] private GameObject confirmArea;
    [SerializeField] private TMP_Text confirmText;
    [SerializeField] private Button confirmOk;
    [SerializeField] private Button confirmCancel;

    [Header("返回")]
    [Tooltip("返回按钮 → 关闭当前页（关闭动效由 UIPanelMotion 承担，PauseMenu 回一级）")]
    [SerializeField] private Button quitButton;
    [Tooltip("PauseMenu 引用（拖 PauseMenu 物体）：本面板关闭后菜单回一级（ReturnToLevel1）")]
    [SerializeField] private PauseMenu pauseMenu;

    [Header("存档系统")]
    [Tooltip("Player 上的 SaveSystem（常驻 Player GameObject）")]
    [SerializeField] private SaveSystem saveSystem;

    [Header("外部读档回调")]
    [Tooltip("非空时 Load 确认走此回调（主菜单 TitleScene 用，由 MainMenu 设标记+切场景）；留空 = 原行为（游戏内直接 LoadGame）")]
    public UnityEngine.Events.UnityEvent<int> onLoadRequested;

    /// <summary>确认区当前等待的操作类型</summary>
    private enum PendingAction
    {
        None,
        Save,
        Load,
        Delete
    }

    private PendingAction _pendingAction = PendingAction.None;
    private int _pendingSlot = -1;

    // OnEnable 绑定的按钮/回调缓存（OnDisable 成对解绑；槽位用闭包捕获索引，必须缓存同一委托实例）
    private readonly List<Button> _boundButtons = new List<Button>();
    private readonly List<UnityAction> _boundHandlers = new List<UnityAction>();

    private void Awake()
    {
        // pauseMenu 兜底：Inspector 未拖时自动查找（PauseMenu 打开二级面板前必已激活，同 Canvas 内唯一）
        if (pauseMenu == null)
            pauseMenu = FindObjectOfType<PauseMenu>();
    }

    private void OnEnable()
    {
        // 槽主按钮（槽位根上的 Button）+ 槽内删除按钮
        if (slotUIs != null)
        {
            for (int i = 0; i < slotUIs.Length; i++)
            {
                SaveSlotUI ui = slotUIs[i];
                if (ui == null) continue;

                Button main = ui.GetComponent<Button>();
                if (main != null)
                {
                    int index = i;
                    UnityAction mainHandler = () => OnSlotClicked(index);
                    main.onClick.AddListener(mainHandler);
                    _boundButtons.Add(main);
                    _boundHandlers.Add(mainHandler);
                }

                if (ui.deleteButton != null)
                {
                    int index = i;
                    UnityAction deleteHandler = () => OnDeleteClicked(index);
                    ui.deleteButton.onClick.AddListener(deleteHandler);
                    _boundButtons.Add(ui.deleteButton);
                    _boundHandlers.Add(deleteHandler);
                }
            }
        }
        // autoSlot 只读：不绑主按钮，点按无反应（Btn_Delete 由 SetData(false) 隐藏）

        // 确认区按钮 + 返回按钮
        UnityAction okHandler = OnConfirmOkClicked;
        if (confirmOk != null)
        {
            confirmOk.onClick.AddListener(okHandler);
            _boundButtons.Add(confirmOk);
            _boundHandlers.Add(okHandler);
        }
        UnityAction cancelHandler = OnConfirmCancelClicked;
        if (confirmCancel != null)
        {
            confirmCancel.onClick.AddListener(cancelHandler);
            _boundButtons.Add(confirmCancel);
            _boundHandlers.Add(cancelHandler);
        }
        UnityAction quitHandler = OnQuitClicked;
        if (quitButton != null)
        {
            quitButton.onClick.AddListener(quitHandler);
            _boundButtons.Add(quitButton);
            _boundHandlers.Add(quitHandler);
        }

        // 打开动效由 PanelManager.OpenPanel / MainMenu.OpenSubPanel 调 UIPanelMotion.PlayOpen 承担（S3 起），此处不再自播滑入
        RefreshSlots();
    }

    private void OnDisable()
    {
        for (int i = 0; i < _boundButtons.Count; i++)
        {
            if (_boundButtons[i] != null && _boundHandlers[i] != null)
                _boundButtons[i].onClick.RemoveListener(_boundHandlers[i]);
        }
        _boundButtons.Clear();
        _boundHandlers.Clear();
    }

    // ============================================================
    // 槽位刷新
    // ============================================================

    private void RefreshSlots()
    {
        if (saveSystem != null)
        {
            if (slotUIs != null)
            {
                for (int i = 0; i < slotUIs.Length; i++)
                {
                    SaveSlotUI ui = slotUIs[i];
                    if (ui == null) continue;
                    SaveSystem.SlotMeta meta = saveSystem.GetSlotMeta(i);
                    if (meta.hasData)
                        ui.SetData(meta, mode == Mode.Save);
                    else
                        ui.SetEmpty();
                }
            }
            if (autoSlot != null)
            {
                SaveSystem.SlotMeta meta = saveSystem.GetSlotMeta(SaveSystem.AutoSlotIndex);
                if (meta.hasData)
                    autoSlot.SetData(meta, false); // 自动槽只读：永不显示删除按钮
                else
                    autoSlot.SetEmpty();
            }
        }
        else
        {
            // saveSystem 未拖入：全部显示空槽
            if (slotUIs != null)
            {
                for (int i = 0; i < slotUIs.Length; i++)
                {
                    if (slotUIs[i] != null) slotUIs[i].SetEmpty();
                }
            }
            if (autoSlot != null) autoSlot.SetEmpty();
        }

        HideConfirm();
    }

    // ============================================================
    // 交互
    // ============================================================

    private void OnSlotClicked(int slot)
    {
        if (saveSystem == null) return;

        SaveSystem.SlotMeta meta = saveSystem.GetSlotMeta(slot);
        if (mode == Mode.Save)
        {
            if (meta.hasData)
            {
                // 有存档 → 确认覆盖
                _pendingAction = PendingAction.Save;
                _pendingSlot = slot;
                ShowConfirm("覆盖存档？");
            }
            else
            {
                // 空槽 → 直接保存
                saveSystem.SaveGame(slot);
                RefreshSlots();
            }
        }
        else // Load
        {
            // 有存档 → 确认读取；空槽/无数据 → 无反应
            if (meta.hasData)
            {
                _pendingAction = PendingAction.Load;
                _pendingSlot = slot;
                ShowConfirm("读取该存档？");
            }
        }
    }

    private void OnDeleteClicked(int slot)
    {
        if (saveSystem == null) return;
        if (!saveSystem.HasSave(slot)) return;

        _pendingAction = PendingAction.Delete;
        _pendingSlot = slot;
        ShowConfirm("确认删除该存档？");
    }

    private void OnConfirmOkClicked()
    {
        PendingAction action = _pendingAction;
        int slot = _pendingSlot;
        HideConfirm();

        if (saveSystem == null || action == PendingAction.None || slot < 0) return;

        switch (action)
        {
            case PendingAction.Save:
                saveSystem.SaveGame(slot);
                RefreshSlots();
                break;
            case PendingAction.Load:
                if (onLoadRequested != null && HasAliveLoadListener())
                {
                    // 外部读档回调（主菜单 TitleScene 用）：由回调方（MainMenu）设标记 + 切场景，
                    // 面板随场景销毁，无需手动关闭（TitleScene 里直接 LoadGame 会因场景未加载空引用）
                    onLoadRequested.Invoke(slot);
                }
                else
                {
                    // 游戏内读档（或 onLoadRequested 引用的是已销毁对象——从 TitleScene 复制面板带过来的误配置）：
                    // 直接 LoadGame + 关面板恢复游戏
                    bool loaded = saveSystem.LoadGame(slot);
                    if (loaded)
                        PanelManager.Instance?.CloseAllPanels(); // 读档成功 → 关闭全部面板恢复游戏
                    else
                        RefreshSlots();
                }
                break;
            case PendingAction.Delete:
                saveSystem.DeleteSave(slot);
                RefreshSlots();
                break;
        }
    }

    private void OnConfirmCancelClicked()
    {
        HideConfirm();
    }

    /// <summary>
    /// onLoadRequested 是否存在存活监听目标（2026-08-19 读档修复）：
    /// 游戏内 LoadPanel 若从 TitleScene 复制而来,UnityEvent 引用标题 MainMenu(已销毁)—
    /// 引用非 null 但目标销毁,Invoke 空调用导致"读档没反应"。存活监听判断避免误走外部回调。
    /// </summary>
    private bool HasAliveLoadListener()
    {
        if (onLoadRequested == null) return false;
        int count = onLoadRequested.GetPersistentEventCount();
        for (int i = 0; i < count; i++)
        {
            var target = onLoadRequested.GetPersistentTarget(i);
            if (target != null) return true; // UnityEngine.Object 的 == 能识别销毁对象
        }
        return false;
    }

    private void OnQuitClicked()
    {
        // 游戏内（SampleScene）：PanelManager 在 → 走栈管理 CloseTopPanel
        // （面板挂 UIPanelMotion → PlayClose；未挂 → ISlideClose 兼容分支 → SlideClose → 本面板直接回调隐藏）
        // 主菜单（TitleScene）：无 PanelManager → 自己走 SlideClose（内部转调 UIPanelMotion.PlayClose，
        // 播完 SetActive(false)；未挂 UIPanelMotion 则直接回调隐藏）
        if (PanelManager.Instance != null)
        {
            PanelManager.Instance.CloseTopPanel();
        }
        else
        {
            SlideClose(() => gameObject.SetActive(false));
        }
    }

    private void ShowConfirm(string text)
    {
        if (confirmText != null) confirmText.text = text;
        if (confirmArea != null) confirmArea.SetActive(true);
    }

    private void HideConfirm()
    {
        if (confirmArea != null) confirmArea.SetActive(false);
        _pendingAction = PendingAction.None;
        _pendingSlot = -1;
    }

    // ============================================================
    // ISlideClose — 兼容关闭入口（MainMenu/TitleScene 直接调用；PanelManager 旧分支）
    // ============================================================

    /// <summary>
    /// 关闭动效统一转调 UIPanelMotion.PlayClose（关闭方向/距离由组件 slideDistance 等配置）；
    /// 未挂 UIPanelMotion（saika 场景配置前）直接回调 onComplete，不写代码兜底动画。
    /// pauseMenu.ReturnToLevel1() 保留：SampleScene 暂停菜单二级 bg 回一级动画仍由菜单自身承担（本面板不写动画）。
    /// </summary>
    public void SlideClose(Action onComplete)
    {
        // 并行动效：暂停菜单回一级（二级 bg 反方向滑出消失）与本面板关闭同时进行（SampleScene 兼容路径；TitleScene 无 PauseMenu 自动跳过）
        if (pauseMenu != null) pauseMenu.ReturnToLevel1();

        UIPanelMotion motion = GetComponent<UIPanelMotion>();
        if (motion != null)
            motion.PlayClose(onComplete);
        else
            onComplete?.Invoke();
    }
}
