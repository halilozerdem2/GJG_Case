using UnityEngine;

public class EffectRunner : MonoBehaviour
{
    public static EffectRunner Instance { get; private set; }

    private LilStateMachine stateMachine;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
        DontDestroyOnLoad(gameObject);

        var controller = LilController.Instance;
        stateMachine = controller != null ? controller.StateMachine : null;
        if (stateMachine == null && controller != null)
        {
            stateMachine = controller.gameObject.AddComponent<LilStateMachine>();
        }
    }

    public void PlayStrategy(StrategyId strategy)
    {
        if (stateMachine == null) return;

        switch (strategy)
        {
            case StrategyId.Manipulation1:
                stateMachine.SetState(LilStateId.Manipulation1);
                break;
            case StrategyId.Manipulation2:
                stateMachine.SetState(LilStateId.Manipulation2);
                break;
            case StrategyId.Manipulation3:
                stateMachine.SetState(LilStateId.Manipulation3);
                break;
        }
    }

    public void PlayOutcome(LilStateId outcome)
    {
        if (stateMachine == null) return;
        stateMachine.SetState(outcome);
    }
}
