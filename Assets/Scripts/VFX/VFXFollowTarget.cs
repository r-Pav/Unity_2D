using UnityEngine;

/// <summary>
/// 特效跟随组件:把特效位置(可选旋转)同步到目标物体(武器 clone)。
/// 目标销毁后自动停止同步,粒子按自身 Lifetime 播完,由 VFXAutoDestruct 负责销毁。
/// 用法:独立生成特效后 Init(target) —— 特效不挂目标子级,播放生命周期独立。
/// </summary>
public class VFXFollowTarget : MonoBehaviour
{
    private Transform _target;
    private Vector3 _offset;
    private bool _followRotation;

    /// <summary>绑定跟随目标。target 销毁后同步自动停止(Unity 假 null 判断)。</summary>
    public void Init(Transform target, Vector3 offset, bool followRotation = false)
    {
        _target = target;
        _offset = offset;
        _followRotation = followRotation;
    }

    private void LateUpdate()
    {
        // 目标销毁后停止同步,粒子按自身 Lifetime 自然播完(残留由 VFXAutoDestruct 收尾)
        if (_target == null) return;

        transform.position = _target.position + _offset;
        if (_followRotation)
        {
            transform.rotation = _target.rotation;
        }
    }
}
