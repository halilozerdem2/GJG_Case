public interface IState
{
    LilStateId Id { get; }
    bool IsOneShot { get; }
    void Enter(LilStateMachine machine);
    void Exit(LilStateMachine machine);
    void Tick(LilStateMachine machine, float deltaTime);
}

