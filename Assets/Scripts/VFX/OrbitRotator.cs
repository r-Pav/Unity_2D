using UnityEngine;

/// <summary>
/// 环绕容器旋转器(特效 prefab 内部驱动)— 挂在轨道容器上,绕 Z 轴匀速旋转,
/// 子物体(环绕小球)即绕中心公转。2D 平面环绕用,不改位置只转角度。
/// 仅做特效展示用;与能量球追踪/掉落逻辑无关。
/// </summary>
public class OrbitRotator : MonoBehaviour
{
    [Tooltip("绕 Z 轴转速(度/秒),正=逆时针")]
    [SerializeField] private float speed = 140f;

    private void Update()
    {
        transform.Rotate(0f, 0f, speed * Time.deltaTime);
    }
}
