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

    private GameState _state;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
        DontDestroyOnLoad(gameObject);
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

    public IEnumerable<Block> GetMatchingNeighbours(Block block)
    {
        if (gridManager == null)
        {
            yield break;
        }

        foreach (var neighbour in gridManager.GetMatchingNeighbours(block))
        {
            yield return neighbour;
        }
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

    public void RegenerateBoardFromSettings()
    {
        if (gridManager == null || blockManager == null)
        {
            return;
        }

        StopAllCoroutines();
        blockManager.StopAllCoroutines();
        blockManager.DestroyAllBlocks();
        ChangeState(GameState.GenerateLevel);
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
}
