using System;
using UnityEngine;

/// <summary>
/// 父面板内子级浮层的自动开合入口(2026-09-05 saika 定)。
///
/// 适用场景:父级页面内部、随父级一起进出但又有独立弹出/收起时机的区块
/// (确认框/二级弹层/提示条等)。这类区块不是 IPanel、不进 PanelManager 栈,
/// 原来由父代码直接 SetActive 硬切,挂 UIPanelMotion 也无人触发。
///
/// 用法:
/// - 浮层根物体同时挂 UIPanelMotion + 本组件;动画类型/时长/UIPanelMotion 上配,
///   本组件只负责"入口触发",不重复实现动画。
/// - 打开:父代码 SetActive(true) 即可(OnEnable 自动调 UIPanelMotion.PlayOpen),
///   或调 Open() 语义等价。浮层根若同挂 UIContentReveal,UIPanelMotion 的
///   prehide + 内容错峰联动照常生效。
/// - 关闭:必须调 Close()(内部 PlayClose 播完才 SetActive(false),带防连点)。
///   不要对浮层直接 SetActive(false):物体已隐藏后 OnDisable 才触发,播不了关闭动画。
/// - 兜底:若外部仍直接 SetActive(false) 硬切,本次无关闭动画但不坏;
///   下次激活 OnEnable 照常 PlayOpen(UIPanelMotion 内部先归位到摆位)。
///
/// 约定:浮层初始须为 inactive(autoPlayOnEnable=true 时)。若某浮层初始 active,
/// 场景加载期的首次 OnEnable 会白播一次,关掉 autoPlayOnEnable 或由代码控制。
/// </summary>
public class UIActivateMotion : MonoBehaviour
{
    [Tooltip("true=物体被 SetActive(true) 激活时自动播 UIPanelMotion.PlayOpen;false=完全由代码显式调 Open/PlayOpen")]
    [SerializeField] private bool autoPlayOnEnable = true;

    [Tooltip("true=仅当同物体挂有 UIPanelMotion 才播;false=无 UIPanelMotion 时也允许(此时开=直接显示、关=直接隐藏)")]
    [SerializeField] private bool requireMotion = true;

    private UIPanelMotion _motion;
    private bool _closing;   // 关闭动画播放中防连点/防重复 Close
    private bool _warnedNoMotion;

    private UIPanelMotion Motion
    {
        get
        {
            if (_motion == null)
                _motion = GetComponent<UIPanelMotion>();
            return _motion;
        }
    }

    private void OnEnable()
    {
        // 场景加载期初始 active 的物体也会触发本方法;约定浮层初始 inactive,
        // 首次 SetActive(true) 即真实打开时机,直接播。若需过滤首次可关 autoPlayOnEnable。
        if (!autoPlayOnEnable)
            return;
        if (_closing)
            return; // 理论上不会:Close 播完才 SetActive(false),OnEnable 必然是新开一轮

        UIPanelMotion motion = Motion;
        if (motion != null)
        {
            motion.PlayOpen();
        }
        else if (requireMotion)
        {
            WarnNoMotionOnce();
        }
        // requireMotion=false 且无 UIPanelMotion:物体已被 SetActive(true),直接显示即可,无事可做
    }

    /// <summary>
    /// 打开浮层(SetActive(true) 语义;autoPlayOnEnable=true 时激活即自动播开启动画)。
    /// 已在激活状态时重复调用无副作用。
    /// </summary>
    public void Open()
    {
        if (gameObject.activeSelf)
            return;
        gameObject.SetActive(true);
    }

    /// <summary>
    /// 关闭浮层:播 UIPanelMotion.PlayClose,播完回调里 SetActive(false)。
    /// 关闭动画期间重复调用被忽略。onClosed 在真正 SetActive(false) 后回调。
    /// 未挂 UIPanelMotion(requireMotion=false)时直接 SetActive(false) 并回调。
    /// </summary>
    public void Close(Action onClosed = null)
    {
        if (_closing)
            return;
        if (!gameObject.activeSelf)
        {
            onClosed?.Invoke();
            return;
        }

        UIPanelMotion motion = Motion;
        if (motion != null)
        {
            _closing = true;
            motion.PlayClose(() =>
            {
                _closing = false;
                if (gameObject != null && gameObject.activeSelf)
                    gameObject.SetActive(false);
                onClosed?.Invoke();
            });
        }
        else if (requireMotion)
        {
            WarnNoMotionOnce();
            gameObject.SetActive(false);
            onClosed?.Invoke();
        }
        else
        {
            gameObject.SetActive(false);
            onClosed?.Invoke();
        }
    }

    private void OnDisable()
    {
        _closing = false; // 外部直接 SetActive(false) 打断关闭/开启动画时复位,保证下次 Close 可用
    }

    private void WarnNoMotionOnce()
    {
        if (_warnedNoMotion)
            return;
        _warnedNoMotion = true;
        Debug.LogWarning("[UIActivateMotion] " + name + ":同物体未挂 UIPanelMotion,浮层开合无动画(本组件只做入口,动画由 UIPanelMotion 承担)", this);
    }
}
