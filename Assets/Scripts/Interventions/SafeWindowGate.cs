using System;
using UnityEngine;

/// <summary>
/// Defines safe windows where interventions may run.
/// Opens only after a move is committed and its cascades fully resolve.
/// Closes immediately when player starts input or on level start/end.
/// </summary>
public class SafeWindowGate : MonoBehaviour
{
    public static SafeWindowGate Instance { get; private set; }

    public bool IsOpen { get; private set; }
    public event Action WindowOpened;
    public event Action WindowClosed;

    private bool moveCommittedPending;
    private bool cascadeInProgress;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
        DontDestroyOnLoad(gameObject);
    }

    private void OnEnable()
    {
        GameEventBus.OnLevelStart += HandleLevelStart;
        GameEventBus.OnLevelEnd += HandleLevelEnd;
        GameEventBus.OnMoveInputStarted += HandleMoveInputStarted;
        GameEventBus.OnMoveCommitted += HandleMoveCommitted;
        GameEventBus.OnCascadeStarted += HandleCascadeStarted;
        GameEventBus.OnCascadeEnded += HandleCascadeEnded;
        GameEventBus.OnBoardRefillComplete += HandleBoardRefillComplete;
        GameEventBus.OnDeadlock += HandleDeadlock;
    }

    private void OnDisable()
    {
        GameEventBus.OnLevelStart -= HandleLevelStart;
        GameEventBus.OnLevelEnd -= HandleLevelEnd;
        GameEventBus.OnMoveInputStarted -= HandleMoveInputStarted;
        GameEventBus.OnMoveCommitted -= HandleMoveCommitted;
        GameEventBus.OnCascadeStarted -= HandleCascadeStarted;
        GameEventBus.OnCascadeEnded -= HandleCascadeEnded;
        GameEventBus.OnBoardRefillComplete -= HandleBoardRefillComplete;
        GameEventBus.OnDeadlock -= HandleDeadlock;
    }

    private void HandleLevelStart()
    {
        ResetFlags();
        Close();
    }

    private void HandleLevelEnd(LevelEndResult _)
    {
        ResetFlags();
        Close();
    }

    private void HandleMoveInputStarted()
    {
        // Any new input immediately closes the safe window
        Close();
    }

    private void HandleMoveCommitted()
    {
        moveCommittedPending = true;
        // We expect a cascade to start soon, keep closed until it fully ends
        Close();
    }

    private void HandleCascadeStarted()
    {
        cascadeInProgress = true;
        Close();
    }

    private void HandleCascadeEnded()
    {
        // Open only if there was a committed move and we actually ran a cascade cycle
        if (moveCommittedPending && cascadeInProgress)
        {
            moveCommittedPending = false;
            cascadeInProgress = false;
            Open();
        }
        else
        {
            Close();
        }
    }

    private void HandleBoardRefillComplete()
    {
        // Treat refill complete as the end of the cascade pipeline
        if (moveCommittedPending)
        {
            moveCommittedPending = false;
            cascadeInProgress = false;
            Open();
        }
        else
        {
            Close();
        }
    }

    private void HandleDeadlock()
    {
        // During deadlock resolution, do not allow interventions
        Close();
    }

    private void Open()
    {
        if (IsOpen) return;
        IsOpen = true;
        WindowOpened?.Invoke();
    }

    private void Close()
    {
        if (!IsOpen) return;
        IsOpen = false;
        WindowClosed?.Invoke();
    }

    private void ResetFlags()
    {
        moveCommittedPending = false;
        cascadeInProgress = false;
    }
}
