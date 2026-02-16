public abstract class BaseLilState : IState
{
    public abstract LilStateId Id { get; }
    public virtual bool IsOneShot => false;

    public virtual void Enter(LilStateMachine machine) { }
    public virtual void Exit(LilStateMachine machine) { }
    public virtual void Tick(LilStateMachine machine, float deltaTime) { }
}

