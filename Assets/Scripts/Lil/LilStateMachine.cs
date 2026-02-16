using System;
using System.Collections.Generic;
using UnityEngine;

public class LilStateMachine : MonoBehaviour
{
    [SerializeField] private LilStateId initialState = LilStateId.Menu;
    [SerializeField] private StateVFXConfig vfxConfig;
    [SerializeField] private float defaultOneShotDuration = 0.6f;

    public LilStateId CurrentStateId { get; private set; }
    public IState CurrentState { get; private set; }

    public event Action<LilStateId, LilStateId> StateChanged; // (prev, next)

    private readonly Dictionary<LilStateId, IState> states = new Dictionary<LilStateId, IState>();
    private float stateTimer;

    private static readonly HashSet<LilStateId> DefaultOneShot = new HashSet<LilStateId>
    {
        LilStateId.Humiliation,
        LilStateId.Manipulation1,
        LilStateId.Manipulation2,
        LilStateId.Manipulation3,
        LilStateId.Sad
    };

    private void Awake()
    {
        // Register built-in states
        Register(new MenuState());
        Register(new WaitingState());
        Register(new HumiliationState());
        Register(new Manipulation1State());
        Register(new Manipulation2State());
        Register(new Manipulation3State());
        Register(new SadState());
        Register(new LoseState());
        Register(new WinState());
    }

    private void OnEnable()
    {
        SetState(initialState, true);
    }

    private void Update()
    {
        if (CurrentState == null) return;
        stateTimer += Time.deltaTime;
        CurrentState.Tick(this, Time.deltaTime);

        // Auto-exit one-shots back to Menu after configured min duration
        if (IsOneShot(CurrentStateId) && HasOneShotMinDurationElapsed(CurrentStateId))
        {
            SetState(LilStateId.Menu);
        }
    }

    public bool IsOneShotActive
    {
        get
        {
            if (CurrentState == null) return false;
            if (!IsOneShot(CurrentStateId)) return false;
            return !HasOneShotMinDurationElapsed(CurrentStateId);
        }
    }

    public void Register(IState state)
    {
        if (state == null) return;
        states[state.Id] = state;
    }

    public bool SetState(LilStateId next, bool force = false)
    {
        if (!force && next == CurrentStateId) return false;
        if (!states.TryGetValue(next, out IState nextState)) return false;

        var prev = CurrentStateId;

        CurrentState?.Exit(this);
        CurrentState = nextState;
        CurrentStateId = next;
        stateTimer = 0f;

        CurrentState.Enter(this);
        RunStateVisuals(next);
        StateChanged?.Invoke(prev, next);
        try
        {
            Debug.Log($"[Lil] State: {prev} -> {next}");
        }
        catch { }
        return true;
    }

    private void RunStateVisuals(LilStateId state)
    {
        if (vfxConfig == null) return;
        if (!vfxConfig.TryGet(state, out var entry)) return;
        // Defer actual visuals to listeners (LilView) via UnityEvents/Animation hooks as needed.
        // This class only owns timing/flow.
    }

    private bool IsOneShot(LilStateId id)
    {
        if (states.TryGetValue(id, out var st))
        {
            return st.IsOneShot || DefaultOneShot.Contains(id);
        }
        return DefaultOneShot.Contains(id);
    }

    private bool HasOneShotMinDurationElapsed(LilStateId id)
    {
        float minDur = defaultOneShotDuration;
        if (vfxConfig != null && vfxConfig.TryGet(id, out var entry))
        {
            minDur = Mathf.Max(minDur, entry.MinDuration);
        }
        return stateTimer >= minDur;
    }
}
