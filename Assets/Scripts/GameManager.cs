using System;
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

    public GameMode CurrentGameMode => _currentGameMode;
    public bool IsCaseMode => _currentGameMode == GameMode.Case;
    public bool IsGameMode => _currentGameMode == GameMode.Game;
    public GameModeConfig ActiveGameModeConfig => _activeGameModeConfig;
    public GameModeConfig.MoveTimeLimitSettings ActiveLimitSettings => _activeGameModeConfig != null ? _activeGameModeConfig.Limits : GameModeConfig.MoveTimeLimitSettings.Default;
    public IReadOnlyList<GameModeConfig.PowerupCooldownEntry> ActivePowerupCooldowns => _activeGameModeConfig != null ? _activeGameModeConfig.PowerupCooldowns : EmptyPowerupCooldowns;
    public IReadOnlyList<GameModeConfig.SpecialBlockThreshold> ActiveSpecialBlockThresholds => _activeGameModeConfig != null ? _activeGameModeConfig.SpecialBlockThresholds : EmptySpecialThresholds;
    public IReadOnlyList<GameModeConfig.StaticTargetSpawn> ActiveStaticTargetSpawns => _activeGameModeConfig != null ? _activeGameModeConfig.StaticTargetSpawns : EmptyStaticTargets;

    public event Action<GameMode> GameModeChanged;

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
                break;
            case GameState.Lose:
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
            return;
        }

        gridManager = FindObjectOfType<GridManager>();
        blockManager = FindObjectOfType<BlockManager>();

        if (gridManager == null || blockManager == null)
        {
            Debug.LogWarning("GameManager could not find GridManager or BlockManager in the scene.");
            return;
        }

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
