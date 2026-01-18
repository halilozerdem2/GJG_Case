using System.Collections.Generic;
using UnityEngine;

public class GameManager : MonoBehaviour
{
    public static GameManager Instance;

    [SerializeField] private GridManager gridManager;
    [SerializeField] private BlockManager blockManager;
    [SerializeField] private int targetFrameRate = 60;
    [SerializeField] private int vSyncCount = 0;

    private GameState _state;

    private void Awake()
    {
        Instance = this;
        ApplyPerformanceSettings();
    }

    private void Start()
    {
        if (gridManager == null || blockManager == null)
        {
            Debug.LogError("GameManager requires references to GridManager and BlockManager.");
            enabled = false;
            return;
        }

        ChangeState(GameState.GenerateLevel);
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
}
