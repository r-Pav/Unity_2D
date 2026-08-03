using UnityEngine;

/// <summary>
/// 挂到检测点子对象上 — 移动子节点时自动显示检测射线
/// Inspector 中可调射线方向和长度、颜色
/// </summary>
public class DetectionGizmo : MonoBehaviour
{
    public enum Direction { Down, Right, Left }

    [SerializeField] private Direction rayDirection = Direction.Down;
    [SerializeField] private float rayLength = 0.15f;
    [SerializeField] private Color activeColor = Color.green;
    [SerializeField] private Color inactiveColor = Color.red;

    private CharacterBase owner;

    void Awake()
    {
        owner = GetComponentInParent<CharacterBase>();
    }

    void OnDrawGizmos()
    {
        Vector2 dir = rayDirection switch
        {
            Direction.Right => Vector2.right,
            Direction.Left  => Vector2.left,
            _               => Vector2.down,
        };

        bool isTouchingWall = (owner is PlayerCharacterBase pc) && pc.IsTouchingWall;
        bool active = owner != null && (rayDirection == Direction.Down ? owner.IsGrounded : isTouchingWall);
        Gizmos.color = active ? activeColor : inactiveColor;
        Gizmos.DrawRay(transform.position, dir * rayLength);
    }
}
