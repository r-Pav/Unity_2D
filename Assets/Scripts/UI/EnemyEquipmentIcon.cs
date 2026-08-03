using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 敌人装备图标 UI — 挂 EnemyEquipmentIcon GameObject
/// 显示 Enemy 当前拾取的装备图标（半透明 alpha=0.5）
/// 
/// 职责：
///   1. 无装备时完全隐藏（SetActive(false)）
///   2. 有装备时显示装备图标，alpha=0.5 以区分玩家装备
///   3. 不参与拖拽交互（纯视觉显示）
/// 
/// 被调用方：EnemyEquipment.Pickup() / DropOnDeath() / 初始化
/// </summary>
public class EnemyEquipmentIcon : MonoBehaviour
{
    // ============================================================
    // 配置
    // ============================================================

    [Header("UI 组件")]
    [Tooltip("装备图标 Image 组件（自动查找子节点 Image）")]
    [SerializeField] private Image iconImage;

    [Tooltip("CanvasGroup 组件（用于控制整体透明度，可选）")]
    [SerializeField] private CanvasGroup canvasGroup;

    [Header("外观")]
    [Tooltip("有装备时的透明度")]
    [SerializeField] [Range(0f, 1f)] private float equippedAlpha = 0.5f;

    // ============================================================
    // 生命周期
    // ============================================================

    private void Awake()
    {
        if (iconImage == null)
            iconImage = GetComponentInChildren<Image>();

        if (canvasGroup == null)
            canvasGroup = GetComponent<CanvasGroup>();

        // 初始隐藏
        HideEquipment();
    }

    // ============================================================
    // 公开方法
    // ============================================================

    /// <summary>
    /// 显示装备图标
    /// </summary>
    /// <param name="icon">要显示的装备 Sprite</param>
    public void ShowEquipment(Sprite icon)
    {
        if (iconImage != null)
        {
            iconImage.sprite = icon;
            iconImage.enabled = true;
        }

        // 设置 alpha = 0.5
        if (canvasGroup != null)
        {
            canvasGroup.alpha = equippedAlpha;
        }
        else if (iconImage != null)
        {
            Color c = iconImage.color;
            c.a = equippedAlpha;
            iconImage.color = c;
        }

        gameObject.SetActive(true);
    }

    /// <summary>
    /// 隐藏装备图标（无装备时调用）
    /// </summary>
    public void HideEquipment()
    {
        gameObject.SetActive(false);

        // 清理状态
        if (iconImage != null)
        {
            iconImage.sprite = null;
            iconImage.enabled = false;
        }

        if (canvasGroup != null)
        {
            canvasGroup.alpha = 1f; // 重置
        }
    }

    /// <summary>
    /// 由 EnemyEquipment 在 Pickup/DropOnDeath 时调用
    /// 配合 EnemyEquipment.cs 中的 ShowIcon/HideIcon 方法
    /// </summary>
    public void UpdateFromEnemy(EnemyEquipment enemyEquip)
    {
        if (enemyEquip == null)
        {
            HideEquipment();
            return;
        }

        if (enemyEquip.HasEquipment)
        {
            ItemInstance item = enemyEquip.GetEquippedItem();
            if (item?.template.icon != null)
                ShowEquipment(item.template.icon);
            else
                HideEquipment();
        }
        else
        {
            HideEquipment();
        }
    }
}
