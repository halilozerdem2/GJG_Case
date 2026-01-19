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
    [SerializeField] private List<GameModeConfigBinding> gameModeConfigs = new List<GameModeConfigBinding>();

    private GameState _state;
    private GameMode _currentGameMode = GameMode.Game;
    private readonly Dictionary<GameMode, GameModeConfig> _configLookup = new Dictionary<GameMode, GameModeConfig>();
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
    private bool objectivesComplete = true;

    public GameMode CurrentGameMode => _currentGameMode;
    public bool IsCaseMode => _currentGameMode == GameMode.Case;
    public bool IsGameMode => _currentGameMode == GameMode.Game;
    public GameModeConfig ActiveGameModeConfig => _activeGameModeConfig;
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
        BuildGameModeConfigLookup();
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
        _state = newState;
        StateChanged?.Invoke(_state);
        switch (newState)
        {
            case GameState.GenerateLevel:
                gridManager.InitializeGrid();
                blockManager.Initialize(gridManager);
                ChangeState(GameState.SpawningBlocks);
                break;
            case GameState.SpawningBlocks:
                blockManager.SpawnBlocks(HandleBlocksSpawned);
                break;
            case GameState.WaitingInput:
                break;
            case GameState.Falling:
                blockManager.ResolveFalling();
                ChangeState(GameState.SpawningBlocks);
                break;
            case GameState.Deadlock:
                blockManager.ResolveDeadlock(HandleDeadlockResolved);
                break;
            case GameState.Win:
                StopLimitTimer();
                break;
            case GameState.Lose:
                StopLimitTimer();
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
            ConsumeMoveIfNeeded();
            ChangeState(GameState.Falling);
        }
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
        Falling,
        Deadlock,
        Win,
        Lose,
        Pause
    }

    public enum GameMode
    {
        Game,
        Case
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
    }

    public void TriggerLoseState()
    {
        if (_state == GameState.Lose)
        {
            return;
        }

        ChangeState(GameState.Lose);
    }

    private void BuildGameModeConfigLookup()
    {
        _configLookup.Clear();
        if (gameModeConfigs == null)
        {
            return;
        }

        for (int i = 0; i < gameModeConfigs.Count; i++)
        {
            GameModeConfigBinding entry = gameModeConfigs[i];
            if (entry.config == null)
            {
                continue;
            }

            if (_configLookup.ContainsKey(entry.mode))
            {
                Debug.LogWarning($"Duplicate GameModeConfig assignment detected for mode {entry.mode}. Using the first entry only.");
                continue;
            }

            _configLookup[entry.mode] = entry.config;
        }
    }

    private void ResolveActiveGameModeConfig(GameMode mode)
    {
        if (_configLookup.Count == 0)
        {
            BuildGameModeConfigLookup();
        }

        if (_configLookup.TryGetValue(mode, out GameModeConfig config))
        {
            _activeGameModeConfig = config;
        }
        else
        {
            _activeGameModeConfig = null;
            Debug.LogWarning($"GameManager does not have a GameModeConfig assigned for mode {mode}.");
        }
    }

    [Serializable]
    private struct GameModeConfigBinding
    {
        public GameMode mode;
        public GameModeConfig config;
    }
}
