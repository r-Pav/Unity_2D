using UnityEngine;
using UnityEngine.UI;

public class EnemyHealthBar : MonoBehaviour
{
    [SerializeField] private Slider slider;
    [SerializeField] private Image fillImage;

    [Header("颜色")]
    [SerializeField] private Color highColor = Color.green;
    [SerializeField] private Color midColor = Color.yellow;
    [SerializeField] private Color lowColor = Color.red;

    private EnemyControllerBase enemy;
    private float lastParentScaleX = 1f;

    private void Awake()
    {
        enemy = GetComponentInParent<EnemyControllerBase>();
    }

    private void LateUpdate()
    {
        if (enemy == null || enemy.IsDead)
        {
            gameObject.SetActive(false);
            return;
        }

        float ratio = enemy.CurrentHealth / enemy.MaxHealth;
        slider.value = ratio;

        if (ratio > 0.5f)
            fillImage.color = Color.Lerp(midColor, highColor, (ratio - 0.5f) * 2f);
        else
            fillImage.color = Color.Lerp(lowColor, midColor, ratio * 2f);

        float sx = enemy.transform.localScale.x;
        if (sx != lastParentScaleX)
        {
            lastParentScaleX = sx;
            transform.localScale = sx < 0 ? new Vector3(-1, 1, 1) : Vector3.one;
        }
    }
}
