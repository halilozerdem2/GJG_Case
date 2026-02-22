using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

public class GameManager : MonoBehaviour
{
    public static GameManager Instance;

    [SerializeField] private GridManager gridManager;
    [SerializeField] private BlockManager blockManager;
    [SerializeField] private int targetFrameRate = 60;
    [SerializeField] private int vSyncCount = 0;
    [SerializeField] private string mainMenuSceneName = "MainMenu";
    [SerializeField] private GameMode startupMode = GameMode.Game;

    private GameState _state;
    private GameMode _currentGameMode = GameMode.Game;
    private GameModeConfig _activeGameModeConfig;
    private static readonly GameModeConfig.PowerupCooldownEntry[] EmptyPowerupCooldowns = Array.Empty<GameModeConfig.PowerupCooldownEntry>();
    private static readonly GameModeConfig.SpecialBlockThreshold[] EmptySpecialThresholds = Array.Empty<GameModeConfig.SpecialBlockThreshold>();
    private static readonly GameModeConfig.StaticTargetSpawn[] EmptyStaticTargets = Array.Empty<GameModeConfig.StaticTargetSpawn>();
    private bool useMoveLimit;
    private bool useTimeLimit;
    private int remainingMoves;
    private int maxMoves;
    private float remainingTime;
    private float maxTime;
    private Coroutine limitTimerRoutine;
    private bool nearFailTimeRaised;
    private bool nearFailMovesRaised;
    private bool objectivesComplete = true;
    private int currentLevelNumber = 1;

    public GameMode CurrentGameMode => _currentGameMode;
    public bool IsCaseMode => _currentGameMode == GameMode.Case;
    public bool IsGameMode => _currentGameMode != GameMode.Case;
    public GameModeConfig ActiveGameModeConfig => _activeGameModeConfig;
    public BlockManager BlockManager => blockManager;
    public GameModeConfig.MoveTimeLimitSettings ActiveLimitSettings => _activeGameModeConfig != null ? _activeGameModeConfig.Limits : GameModeConfig.MoveTimeLimitSettings.Default;
    public IReadOnlyList<GameModeConfig.PowerupCooldownEntry> ActivePowerupCooldowns => _activeGameModeConfig != null ? _activeGameModeConfig.PowerupCooldowns : EmptyPowerupCooldowns;
    public IReadOnlyList<GameModeConfig.SpecialBlockThreshold> ActiveSpecialBlockThresholds => _activeGameModeConfig != null ? _activeGameModeConfig.SpecialBlockThresholds : EmptySpecialThresholds;
    public IReadOnlyList<GameModeConfig.StaticTargetSpawn> ActiveStaticTargetSpawns => _activeGameModeConfig != null ? _activeGameModeConfig.StaticTargetSpawns : EmptyStaticTargets;
    public bool HasMoveLimit => useMoveLimit;
    public int RemainingMoves => remainingMoves;
    public int MaxMoves => maxMoves;
    public bool HasTimeLimit => useTimeLimit;
    public float RemainingTime => remainingTime;
    public float TimeLimitSeconds => maxTime;
    public bool AreObjectivesComplete => objectivesComplete;
    public int CurrentLevelNumber => Mathf.Max(1, currentLevelNumber);

    public event Action<GameMode> GameModeChanged;
    public event Action<int, int> MovesChanged;
    public event Action<float, float> TimeChanged;
    public event Action<GameState> StateChanged;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
        DontDestroyOnLoad(gameObject);
        _currentGameMode = startupMode;
        ResolveActiveGameModeConfig(_currentGameMode);
        ApplyPerformanceSettings();
        SceneManager.sceneLoaded += HandleSceneLoaded;
        EnsureMainMenuLoaded();
    }

    private void OnDestroy()
    {
        if (Instance == this)
        {
            SceneManager.sceneLoaded -= HandleSceneLoaded;
        }
    }

    private void Start()
    {
        SetupScene(SceneManager.GetActiveScene());
    }

    private void ChangeState(GameState newState)
    {
        var previous = _state;
        if (IsTerminalGameState(_state))
        {
            return;
        }

        _state = newState;
        StateChanged?.Invoke(_state);
        switch (newState)
        {
            case GameState.GenerateLevel:
                GameEventBus.RaiseLevelStart();
                gridManager.InitializeGrid();
                blockManager.Initialize(gridManager);
                ChangeState(GameState.SpawningBlocks);
                break;
            case GameState.SpawningBlocks:
                blockManager.SpawnBlocks(HandleBlocksSpawned);
                break;
            case GameState.WaitingInput:
                if (previous == GameState.Falling)
                {
                    GameEventBus.RaiseCascadeEnded();
                }
                else if (previous == GameState.SpawningBlocks)
                {
                    GameEventBus.RaiseBoardRefillComplete();
                }
                break;
            case GameState.BlastAnimation:
                break;
            case GameState.Falling:
                GameEventBus.RaiseCascadeStarted();
                blockManager.ResolveFalling();
                ChangeState(GameState.SpawningBlocks);
                break;
            case GameState.Deadlock:
                GameEventBus.RaiseDeadlock();
                blockManager.ResolveDeadlock(HandleDeadlockResolved);
                break;
            case GameState.Win:
                GameEventBus.RaiseLevelEnd(LevelEndResult.Win);
                StopLimitTimer();
                WinLosePanelController.ActiveInstance?.ShowWinPanel();
                break;
            case GameState.Lose:
                GameEventBus.RaiseLevelEnd(LevelEndResult.Lose);
                StopLimitTimer();
                WinLosePanelController.ActiveInstance?.ShowLosePanel();
                break;
            case GameState.Pause:
                break;
            default:
                Debug.LogWarning($"Unhandled state transition: {newState}");
                break;
        }
    }

    public void SetGameMode(GameMode mode)
    {
        if (_currentGameMode == mode)
        {
            return;
        }

        _currentGameMode = mode;
        ResolveActiveGameModeConfig(_currentGameMode);
        GameModeChanged?.Invoke(_currentGameMode);
        if (gridManager != null && blockManager != null)
        {
            ApplyLimitSettings();
        }
    }

    private void HandleBlocksSpawned(bool hasValidMove)
    {
        ChangeState(hasValidMove ? GameState.WaitingInput : GameState.Deadlock);
    }

    private void HandleDeadlockResolved(bool success)
    {
        if (success)
        {
            ChangeState(GameState.WaitingInput);
        }
        else
        {
            Debug.LogWarning("Deadlock persists: unable to create a new move.");
            ChangeState(GameState.Deadlock);
        }
    }

    public bool IsWaitingForInput => _state == GameState.WaitingInput;

    public void TryBlastBlock(Block block)
    {
        if (!IsWaitingForInput)
        {
            return;
        }

        if (blockManager.TryHandleBlockSelection(block))
        {
            GameEventBus.RaiseMoveCommitted();
            ConsumeMoveIfNeeded();
            ChangeState(GameState.BlastAnimation);
            blockManager.WaitForBlastAnimations(() =>
            {
                ChangeState(GameState.Falling);
            });
        }
    }

    public bool TrySwapBlocks(Block first, Block second)
    {
        if (!IsWaitingForInput || blockManager == null)
        {
            return false;
        }

        return blockManager.TrySwapBlocks(first, second);
    }

    public int GetMatchingNeighbours(Block block, List<Block> buffer)
    {
        if (buffer == null)
        {
            return 0;
        }

        buffer.Clear();
        if (gridManager == null)
        {
            return 0;
        }

        return gridManager.GetMatchingNeighbours(block, buffer);
    }

    public void UpdateGrid()
    {
        blockManager.ResolveFalling();
    }

    public void ForceResolveAfterPowerup()
    {
        if (blockManager == null)
        {
            return;
        }

        ChangeState(GameState.Falling);
    }

    public void ForceSpawnAfterBoardClear()
    {
        if (blockManager == null)
        {
            return;
        }

        ChangeState(GameState.SpawningBlocks);
    }

    public void ForceShuffleInProgress()
    {
        _state = GameState.Pause;
    }

    public void ForceWaitingAfterShuffle()
    {
        ChangeState(GameState.WaitingInput);
    }

    public enum GameState
    {
        GenerateLevel,
        SpawningBlocks,
        WaitingInput,
        BlastAnimation,
        Falling,
        Deadlock,
        Win,
        Lose,
        Pause
    }

    public enum GameMode
    {
        Game,
        Case,
        Easy,
        Medium,
        Hard
    }

    public void RetryCurrentLevel()
    {
        StopLimitTimer();
        _state = GameState.Pause;
        var activeScene = SceneManager.GetActiveScene();
        if (!activeScene.IsValid())
        {
            return;
        }

        gridManager = null;
        blockManager = null;
        Time.timeScale = 1f;
        SceneManager.LoadScene(activeScene.buildIndex);
    }

    public void ReturnToMainMenu()
    {
        StopLimitTimer();
        _state = GameState.Pause;
        Time.timeScale = 1f;
        SceneManager.LoadScene(0);
    }

    private void ApplyPerformanceSettings()
    {
        QualitySettings.vSyncCount = Mathf.Max(0, vSyncCount);

        if (targetFrameRate > 0)
        {
            Application.targetFrameRate = targetFrameRate;
        }
    }

    private void HandleSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        if (_activeGameModeConfig == null)
        {
            ResolveActiveGameModeConfig(_currentGameMode);
        }

        SetupScene(scene);
    }

    private void SetupScene(Scene scene)
    {
        if (IsMainMenuScene(scene))
        {
            _state = GameState.Pause;
            gridManager = null;
            blockManager = null;
            StopLimitTimer();
            return;
        }

        gridManager = FindObjectOfType<GridManager>();
        blockManager = FindObjectOfType<BlockManager>();

        if (gridManager == null || blockManager == null)
        {
            Debug.LogWarning("GameManager could not find GridManager or BlockManager in the scene.");
            return;
        }

        ApplyBoardSettingsToManagers();
        ApplyLimitSettings();
        ChangeState(GameState.GenerateLevel);
    }

    private bool IsMainMenuScene(Scene scene)
    {
        if (!string.IsNullOrEmpty(mainMenuSceneName) && scene.name == mainMenuSceneName)
        {
            return true;
        }

        return scene.buildIndex == 0;
    }

    private void EnsureMainMenuLoaded()
    {
        Scene current = SceneManager.GetActiveScene();
        if (!IsMainMenuScene(current))
        {
            SceneManager.LoadScene(0);
        }
    }

    private void ApplyLimitSettings()
    {
        StopLimitTimer();

        var limits = ActiveLimitSettings;
        useMoveLimit = IsGameMode && limits.UseMoveLimit;
        maxMoves = useMoveLimit ? Mathf.Max(0, limits.MoveLimit) : 0;
        remainingMoves = maxMoves;
        MovesChanged?.Invoke(remainingMoves, maxMoves);

        useTimeLimit = IsGameMode && limits.UseTimeLimit;
        maxTime = useTimeLimit ? Mathf.Max(0f, limits.TimeLimitSeconds) : 0f;
        remainingTime = maxTime;
        TimeChanged?.Invoke(remainingTime, maxTime);

        objectivesComplete = true;

        nearFailTimeRaised = false;
        nearFailMovesRaised = false;

        if (useTimeLimit && maxTime > 0f)
        {
            limitTimerRoutine = StartCoroutine(LimitTimer());
        }
    }

    private IEnumerator LimitTimer()
    {
        while (useTimeLimit && remainingTime > 0f)
        {
            if (_state == GameState.Pause)
            {
                yield return null;
                continue;
            }

            remainingTime = Mathf.Max(0f, remainingTime - Time.deltaTime);
            TimeChanged?.Invoke(remainingTime, maxTime);

            // Near-fail on time used >= 80%
            if (!nearFailTimeRaised && maxTime > 0f)
            {
                float usedRatio = 1f - (remainingTime / maxTime);
                if (usedRatio >= 0.8f)
                {
                    nearFailTimeRaised = true;
                    GameEventBus.RaiseNearFail();
                }
            }

            if (remainingTime <= 0f)
            {
                break;
            }

            yield return null;
        }

        limitTimerRoutine = null;

        if (useTimeLimit && remainingTime <= 0f)
        {
            if (!objectivesComplete)
            {
                TriggerLoseState();
            }
            else
            {
                TriggerWinState();
            }
        }
    }

    private void StopLimitTimer()
    {
        if (limitTimerRoutine != null)
        {
            StopCoroutine(limitTimerRoutine);
            limitTimerRoutine = null;
        }
    }

    private void ConsumeMoveIfNeeded()
    {
        if (!useMoveLimit || maxMoves <= 0)
        {
            return;
        }

        remainingMoves = Mathf.Max(0, remainingMoves - 1);
        MovesChanged?.Invoke(remainingMoves, maxMoves);

        // Near-fail on moves used >= 90%
        if (!nearFailMovesRaised && maxMoves > 0)
        {
            int used = Mathf.Max(0, maxMoves - remainingMoves);
            float usedRatio = (float)used / maxMoves;
            if (usedRatio >= 0.9f)
            {
                nearFailMovesRaised = true;
                GameEventBus.RaiseNearFail();
            }
        }

        if (remainingMoves > 0)
        {
            return;
        }

        if (!objectivesComplete)
        {
            TriggerLoseState();
        }
        else if (!useTimeLimit)
        {
            TriggerWinState();
        }
    }

    public void SetObjectivesPending(bool pending)
    {
        objectivesComplete = !pending;
    }

    public void ReportObjectivesCompletion()
    {
        objectivesComplete = true;
        TriggerWinState();
    }

    public void TriggerWinState()
    {
        if (_state == GameState.Win)
        {
            return;
        }

        ChangeState(GameState.Win);

        // Compute stars based on resource usage and persist progress
        int stars = CalculateStarsForWin();
        ReportLevelCompleted(stars);
    }

    public void TriggerLoseState()
    {
        if (_state == GameState.Lose)
        {
            return;
        }

        ChangeState(GameState.Lose);
    }

    public void SetActiveLevelConfig(GameModeConfig config)
    {
        _activeGameModeConfig = config;
        if (gridManager != null && blockManager != null)
        {
            ApplyBoardSettingsToManagers();
            ApplyLimitSettings();
        }
    }

    private void ResolveActiveGameModeConfig(GameMode mode)
    {
        GameModeConfig config = null;

        // Prefer per-level config under Resources/Levels/Level_XX/GameModeConfig
        if (mode != GameMode.Case && CurrentLevelNumber > 0)
        {
            string levelPath = $"Levels/Level_{CurrentLevelNumber:D2}/GameModeConfig";
            config = Resources.Load<GameModeConfig>(levelPath);
            if (config == null)
            {
                Debug.LogWarning($"Unable to load per-level GameModeConfig at Resources/{levelPath}.");
            }
        }

        _activeGameModeConfig = config;
        if (_activeGameModeConfig == null)
        {
            Debug.LogWarning($"GameManager does not have a GameModeConfig assigned for mode {mode}.");
            return;
        }

        ApplyBoardSettingsToManagers();
    }

    private void ApplyBoardSettingsToManagers()
    {
        if (_activeGameModeConfig == null)
        {
            return;
        }

        BoardSettings settings = _activeGameModeConfig.BoardSettings;
        if (settings == null)
        {
            Debug.LogWarning($"GameModeConfig for mode {_currentGameMode} is missing BoardSettings.");
            return;
        }

        gridManager?.SetBoardSettings(settings);
        blockManager?.SetBoardSettings(settings);
    }

    private static bool IsTerminalGameState(GameState state)
    {
        return state == GameState.Win || state == GameState.Lose;
    }

    public void SetCurrentLevelNumber(int levelNumber)
    {
        currentLevelNumber = Mathf.Max(1, levelNumber);
    }

    public void ReportLevelCompleted(int stars)
    {
        LevelProgressService.Instance.ReportLevelResult(CurrentLevelNumber, Mathf.Clamp(stars, 0, 3));
    }

    private int CalculateStarsForWin()
    {
        // Prefer move-based scoring when a move limit is active
        if (useMoveLimit && maxMoves > 0)
        {
            int movesUsed = Mathf.Max(0, maxMoves - Mathf.Max(0, remainingMoves));
            float usedRatio = Mathf.Clamp01(maxMoves > 0 ? (float)movesUsed / maxMoves : 1f);
            return StarsFromUsageRatio(usedRatio);
        }

        // Fallback to time-based scoring when a time limit is active
        if (useTimeLimit && maxTime > 0f)
        {
            float timeUsed = Mathf.Max(0f, maxTime - Mathf.Max(0f, remainingTime));
            float usedRatio = Mathf.Clamp01(maxTime > 0f ? timeUsed / maxTime : 1f);
            return StarsFromUsageRatio(usedRatio);
        }

        // No limits → award maximum by default
        return 3;
    }

    private static int StarsFromUsageRatio(float usedRatio)
    {
        // 3★: <= 50% kaynak kullanımı, 2★: <= 80%, 1★: > 80%
        if (usedRatio <= 0.5f) return 3;
        if (usedRatio <= 0.8f) return 2;
        return 1;
    }
}
