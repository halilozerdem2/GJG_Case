using System;

public static class GameEventBus
{
    // Level lifecycle
    public static event Action OnLevelStart;
    public static event Action<LevelEndResult> OnLevelEnd;

    // Input / move lifecycle
    public static event Action OnMoveInputStarted;
    public static event Action OnMoveCommitted;

    // Board/cascade lifecycle
    public static event Action OnCascadeStarted;
    public static event Action OnCascadeEnded;
    public static event Action OnBoardRefillComplete;
    public static event Action OnDeadlock;

    // Highlights (contextual)
    public static event Action<int> OnBigCombo; // combo size
    public static event Action OnNearFail;      // nearing fail (time/moves)
    public static event Action OnObjectiveAlmostDone;

    public static void RaiseLevelStart() => OnLevelStart?.Invoke();
    public static void RaiseLevelEnd(LevelEndResult result) => OnLevelEnd?.Invoke(result);

    public static void RaiseMoveInputStarted() => OnMoveInputStarted?.Invoke();
    public static void RaiseMoveCommitted() => OnMoveCommitted?.Invoke();

    public static void RaiseCascadeStarted() => OnCascadeStarted?.Invoke();
    public static void RaiseCascadeEnded() => OnCascadeEnded?.Invoke();
    public static void RaiseBoardRefillComplete() => OnBoardRefillComplete?.Invoke();
    public static void RaiseDeadlock() => OnDeadlock?.Invoke();

    public static void RaiseBigCombo(int size) => OnBigCombo?.Invoke(size);
    public static void RaiseNearFail() => OnNearFail?.Invoke();
    public static void RaiseObjectiveAlmostDone() => OnObjectiveAlmostDone?.Invoke();
}

public enum LevelEndResult
{
    Win,
    Lose
}

