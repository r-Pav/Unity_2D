using System;
using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

/// <summary>
/// 存档/读档面板 — 挂 SavePanel（mode=Save）/ LoadPanel（mode=Load）。
/// IPanel：FullScreen + Pause + Lock + Cursor；实现 ISlideClose（关闭时向左渐隐，不拖入 UIFadeManager.fadePanels）。
/// 动效：打开 = 从右侧滑入出现（SlideIn）；关闭 = 向左渐隐（SlideClose）。
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
    [Tooltip("返回按钮 → 关闭当前页（向左渐隐，PauseMenu 向右渐显回居中）")]
    [SerializeField] private Button quitButton;
    [Tooltip("PauseMenu 引用（拖 PauseMenu 物体）：本面板关闭后菜单右移回默认位置")]
    [SerializeField] private PauseMenu pauseMenu;

    [Header("存档系统")]
    [Tooltip("Player 上的 SaveSystem（常驻 Player GameObject）")]
    [SerializeField] private SaveSystem saveSystem;

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

    private RectTransform _rect;
    private CanvasGroup _canvasGroup;
    private Coroutine _closeRoutine;

    // OnEnable 绑定的按钮/回调缓存（OnDisable 成对解绑；槽位用闭包捕获索引，必须缓存同一委托实例）
    private readonly List<Button> _boundButtons = new List<Button>();
    private readonly List<UnityAction> _boundHandlers = new List<UnityAction>();

    private const float SlideCloseDuration = 0.2f;
    private const float SlideCloseDistance = -300f;
    private const float SlideInDistance = 300f;

    private void Awake()
    {
        _rect = GetComponent<RectTransform>();
        _canvasGroup = GetComponent<CanvasGroup>();
        if (_canvasGroup == null)
            _canvasGroup = gameObject.AddComponent<CanvasGroup>();
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

        // 每次打开：从右侧滑入出现（右滑出现 + 渐显；本面板不拖入 UIFadeManager.fadePanels，alpha/位置自管）
        if (_canvasGroup != null)
            _canvasGroup.alpha = 0f;
        StartSlideIn();

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
                if (saveSystem.LoadGame(slot))
                    PanelManager.Instance?.CloseAllPanels(); // 读档成功 → 关闭全部面板恢复游戏
                else
                    RefreshSlots();
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

    private void OnQuitClicked()
    {
        PanelManager.Instance?.CloseTopPanel();
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
    // ISlideClose — 向左渐隐（不拖入 UIFadeManager.fadePanels）
    // ============================================================

    /// <summary>打开时从右侧滑入出现（右滑出现 + 渐显 0→1）</summary>
    private void StartSlideIn()
    {
        if (_rect == null) return;
        if (_closeRoutine != null) StopCoroutine(_closeRoutine);
        Vector2 target = _rect.anchoredPosition;
        _closeRoutine = StartCoroutine(SlideInRoutine(target));
    }

    private IEnumerator SlideInRoutine(Vector2 target)
    {
        Vector2 from = target + new Vector2(SlideInDistance, 0f);
        float elapsed = 0f;
        while (elapsed < SlideCloseDuration)
        {
            elapsed += Time.unscaledDeltaTime; // 暂停（timeScale=0）时仍正常播放
            float t = Mathf.Clamp01(elapsed / SlideCloseDuration);
            if (_rect != null) _rect.anchoredPosition = Vector2.Lerp(from, target, t);
            if (_canvasGroup != null) _canvasGroup.alpha = Mathf.Lerp(0f, 1f, t);
            yield return null;
        }
        if (_rect != null) _rect.anchoredPosition = target;
        if (_canvasGroup != null) _canvasGroup.alpha = 1f;
    }

    public void SlideClose(Action onComplete)
    {
        if (_closeRoutine != null) StopCoroutine(_closeRoutine);
        // 并行动效：菜单右移回默认位置 与 本面板左滑消失 同时进行（一起走）
        if (pauseMenu != null) pauseMenu.ReturnToCenter();
        _closeRoutine = StartCoroutine(SlideCloseRoutine(onComplete));
    }

    private IEnumerator SlideCloseRoutine(Action onComplete)
    {
        Vector2 startPos = _rect != null ? _rect.anchoredPosition : Vector2.zero;
        Vector2 targetPos = startPos + new Vector2(SlideCloseDistance, 0f);
        float startAlpha = _canvasGroup != null ? _canvasGroup.alpha : 1f;

        float elapsed = 0f;
        while (elapsed < SlideCloseDuration)
        {
            elapsed += Time.unscaledDeltaTime; // 暂停（timeScale=0）时仍正常播放
            float t = Mathf.Clamp01(elapsed / SlideCloseDuration);
            if (_rect != null) _rect.anchoredPosition = Vector2.Lerp(startPos, targetPos, t);
            if (_canvasGroup != null) _canvasGroup.alpha = Mathf.Lerp(startAlpha, 0f, t);
            yield return null;
        }
        if (_rect != null) _rect.anchoredPosition = targetPos;
        if (_canvasGroup != null) _canvasGroup.alpha = 0f;

        // 菜单右移已由 SlideClose() 提前并行启动（见 SlideClose 注释）
        // 左移只是视觉动画：播完恢复初始位置，否则下次打开 SlideIn 的 target 取到偏移位置，反复开关会累积偏移
        if (_rect != null) _rect.anchoredPosition = startPos;

        onComplete?.Invoke();
    }
}
