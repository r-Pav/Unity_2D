/// <summary>
/// 状态接口 — 所有状态实现这三个方法
/// </summary>
public interface IState
{
    void OnEnter();
    void OnUpdate();
    void OnExit();
}

/// <summary>
/// 状态机 — 管理状态切换，驱动当前状态 Update
/// </summary>
public class StateMachine
{
    private IState currentState;

    public IState CurrentState => currentState;
    public IState PreviousState { get; private set; }

    /// <summary>切换到新状态（自动 Exit 旧状态 → Enter 新状态）。传 null 退出到 idle。</summary>
    public void ChangeState(IState newState)
    {
        if (newState == currentState) return;

        PreviousState = currentState;
        currentState?.OnExit();
        currentState = newState;
        currentState?.OnEnter();
    }

    /// <summary>每帧调用驱动当前状态</summary>
    public void Update()
    {
        currentState?.OnUpdate();
    }
}
