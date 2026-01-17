using System.Collections.Generic;
using UnityEngine;
using System.Linq;
using DG.Tweening;
using System;
using Random = UnityEngine.Random;
using System.Collections;

public class GameManager : MonoBehaviour
{
    public static GameManager Instance;
    [SerializeField] private BoardSettings boardSettings;
    [SerializeField] private Node _nodePrefab;
    [SerializeField] private GameObject _boardPrefab;
    [SerializeField] private Vector2 boardSurroundPadding = new Vector2(0.2f, 0.2f);
    [SerializeField] private Vector3 gridWorldCenter = Vector3.zero;
    [SerializeField] private float boardScreenPadding = 0.5f;
    [SerializeField] private float spawnHeightPadding = 1f;
    [SerializeField] private float blockDropDuration = 0.3f;

    private static readonly Vector2Int[] CardinalDirections =
    {
        Vector2Int.up,
        Vector2Int.down,
        Vector2Int.left,
        Vector2Int.right
    };

    private Dictionary<Vector2Int, Node> _nodes;
    private HashSet<Node> freeNodes; // Boş olan düğümleri takip eden liste
    private GameObject boardInstance;
    private Vector3 boardBaseScale = Vector3.one;
    private Vector2 boardEnvelopeSize;
    private Transform gridRoot;
    private bool settingsReady;

    private GameState _state;

    private void Awake()
    {
        Instance = this;
        _nodes = new Dictionary<Vector2Int, Node>();
        freeNodes = new HashSet<Node>(); // Boş düğümler burada saklanacak

        if (boardSettings == null)
        {
            Debug.LogError("BoardSettings reference is missing on GameManager.");
        }
        else if (!boardSettings.IsValid(out string validationMessage))
        {
            Debug.LogError($"Invalid BoardSettings: {validationMessage}");
        }
        else
        {
            settingsReady = true;
        }
    }

    private void Start()
    {
        if (!settingsReady)
        {
            enabled = false;
            return;
        }

        ChangeState(GameState.GenerateLevel);
    }

    void GenerateGrid()
    {
        SetupBoard();

        for (int x = 0; x < boardSettings.Columns; x++)
        {
            for (int y = 0; y < boardSettings.Rows; y++)
            {
                Node node;
                if (gridRoot != null)
                {
                    node = Instantiate(_nodePrefab, gridRoot);
                    node.transform.localPosition = new Vector3(x - (boardSettings.Columns / 2f - 0.5f),
                        y - (boardSettings.Rows / 2f - 0.5f), 0f);
                }
                else
                {
                    node = Instantiate(_nodePrefab, new Vector3(x, y, 0f), Quaternion.identity);
                }

                node.gridPosition = new Vector2Int(x, y);
                _nodes[node.gridPosition] = node;
                freeNodes.Add(node); // Başlangıçta tüm düğümler boş olacak
            }
        }

        FitBoardToScreen();
        ChangeState(GameState.SpawningBlocks);
    }

    private void ChangeState(GameState newState)
    {
        _state = newState;
        switch (newState)
        {
            case GameState.GenerateLevel:
                GenerateGrid();
                break;
            case GameState.SpawningBlocks:
                SpawnBlocks();
                break;
            case GameState.WaitingInput:
                break;
            case GameState.Falling:
                HandleFallingState();
                break;
            case GameState.NoMoreMove:
                break;
            case GameState.Win:
                break;
            case GameState.Lose:
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(newState), newState, null);
        }
    }

    public bool IsWaitingForInput => _state == GameState.WaitingInput;

    private void SpawnBlocks()
    {
        StartCoroutine(SpawnBlocksCoroutine());
    }

    private IEnumerator SpawnBlocksCoroutine()
    {
        yield return new WaitForSeconds(0.2f); // Bloklar düştükten sonra 0.2 saniye bekle

        UpdateFreeNodes();
        List<Node> nodesToFill = freeNodes.ToList(); // Şu anda boş olan düğümleri listeye al
        float dropDuration = Mathf.Max(0f, blockDropDuration);

        foreach (var node in nodesToFill)
        {
            Block randomBlock = Instantiate(boardSettings.BlockPrefabs[Random.Range(0, boardSettings.BlockPrefabs.Length)]);

            randomBlock.SetBlock(node);
            float dropOffset = GetSpawnOffsetForRow(node.gridPosition.y);
            randomBlock.transform.localPosition = Vector3.up * dropOffset;
            freeNodes.Remove(node); // Artık dolu, freeNodes listesinden çıkar

            if (dropDuration > 0f)
            {
                randomBlock.transform.DOLocalMove(Vector3.zero, dropDuration).SetEase(Ease.OutBounce);
            }
            else
            {
                randomBlock.transform.localPosition = Vector3.zero;
            }
        }

        RefreshGroupVisuals();
        ChangeState(GameState.WaitingInput);
    }



    public void TryBlastBlock(Block block)
    {
        HashSet<Block> group = block.FloodFill();
        if (group.Count >= 2)
        {
            foreach (var b in group)
            {
                if (b.node != null)
                {
                    b.node.OccupiedBlock = null;
                    freeNodes.Add(b.node); // Boşalan düğümü freeNodes'a ekle
                }

                Destroy(b.gameObject);
            }
            ChangeState(GameState.Falling);
        }
    }

    public IEnumerable<Block> GetMatchingNeighbours(Block block)
    {
        if (block == null || block.node == null)
        {
            yield break;
        }

        foreach (var dir in CardinalDirections)
        {
            Vector2Int neighbourPosition = block.node.gridPosition + dir;
            if (_nodes.TryGetValue(neighbourPosition, out Node neighbourNode))
            {
                Block neighbourBlock = neighbourNode.OccupiedBlock;
                if (neighbourBlock != null && neighbourBlock.blockType == block.blockType)
                {
                    yield return neighbourBlock;
                }
            }
        }
    }

    private void RefreshGroupVisuals()
    {
        if (!settingsReady || _nodes == null || _nodes.Count == 0)
        {
            return;
        }

        HashSet<Block> processed = new HashSet<Block>();
        foreach (var node in _nodes.Values)
        {
            Block block = node.OccupiedBlock;
            if (block == null || !processed.Add(block)) continue;

            HashSet<Block> group = block.FloodFill();
            int groupSize = group.Count;
            foreach (var member in group)
            {
                member.ApplyGroupIcon(groupSize, boardSettings);
                processed.Add(member);
            }
        }
    }

    private void SetupBoard()
    {
        if (boardInstance != null)
        {
            Destroy(boardInstance);
        }

        if (gridRoot != null)
        {
            Destroy(gridRoot.gameObject);
        }

        Vector3 boardCenter = GetGridCenter();
        boardEnvelopeSize = new Vector2(
            Mathf.Max(0.01f, boardSettings.Columns + Mathf.Max(0f, boardSurroundPadding.x)),
            Mathf.Max(0.01f, boardSettings.Rows + Mathf.Max(0f, boardSurroundPadding.y)));

        boardInstance = Instantiate(_boardPrefab, boardCenter, Quaternion.identity);
        boardBaseScale = CalculateBoardScaleForGrid(boardInstance);
        boardInstance.transform.localScale = boardBaseScale;

        gridRoot = new GameObject("GridRoot").transform;
        gridRoot.position = boardCenter;
    }

    private Vector3 CalculateBoardScaleForGrid(GameObject instance)
    {
        if (instance == null)
        {
            return Vector3.one;
        }

        Vector3 originalScale = instance.transform.localScale;
        Vector2 visualSize = GetBoardVisualSize(instance);

        if (visualSize.x <= 0f || visualSize.y <= 0f)
        {
            return new Vector3(
                boardEnvelopeSize.x,
                boardEnvelopeSize.y,
                originalScale.z);
        }

        float widthMultiplier = boardEnvelopeSize.x / visualSize.x;
        float heightMultiplier = boardEnvelopeSize.y / visualSize.y;

        return new Vector3(
            originalScale.x * widthMultiplier,
            originalScale.y * heightMultiplier,
            originalScale.z);
    }

    private Vector2 GetBoardVisualSize(GameObject instance)
    {
        if (instance == null)
        {
            return Vector2.one;
        }

        Renderer[] renderers = instance.GetComponentsInChildren<Renderer>();
        if (renderers == null || renderers.Length == 0)
        {
            return new Vector2(
                Mathf.Max(0.01f, boardSettings.Columns),
                Mathf.Max(0.01f, boardSettings.Rows));
        }

        Bounds combinedBounds = renderers[0].bounds;
        for (int i = 1; i < renderers.Length; i++)
        {
            combinedBounds.Encapsulate(renderers[i].bounds);
        }

        return new Vector2(
            Mathf.Max(0.01f, combinedBounds.size.x),
            Mathf.Max(0.01f, combinedBounds.size.y));
    }

    private Vector3 GetGridCenter()
    {
        return gridWorldCenter;
    }

    private void FitBoardToScreen()
    {
        Camera cam = Camera.main;
        if (cam == null || !cam.orthographic)
        {
            return;
        }

        float boardWidth = boardEnvelopeSize.x > 0.01f ? boardEnvelopeSize.x : boardSettings.Columns;
        float boardHeight = boardEnvelopeSize.y > 0.01f ? boardEnvelopeSize.y : boardSettings.Rows;
        float verticalView = Mathf.Max(0.01f, cam.orthographicSize * 2f - boardScreenPadding * 2f);
        float horizontalView = verticalView * cam.aspect;

        float scaleX = horizontalView / boardWidth;
        float scaleY = verticalView / boardHeight;
        float scale = Mathf.Clamp(Mathf.Min(scaleX, scaleY), 0.01f, 100f);

        Vector3 scaled = new Vector3(scale, scale, 1f);
        if (boardInstance != null)
        {
            boardInstance.transform.localScale = Vector3.Scale(boardBaseScale, scaled);
        }
        if (gridRoot != null)
        {
            gridRoot.localScale = scaled;
            Vector3 boardCenter = GetGridCenter();
            gridRoot.position = boardCenter;
        }
    }

    private float GetSpawnOffsetForRow(int rowIndex)
    {
        float rowsAbove = boardSettings.Rows - rowIndex;
        return rowsAbove + spawnHeightPadding;
    }

    public void UpdateGrid()
    {
        HandleFallingState();
    }

    private void HandleFallingState() // O(n^3) karmaşıklığındaki yapı dicitonary kullanılarak               
    {                                  // O(n^2 Log N) seviyesine düşürülecek
        float dropDuration = Mathf.Max(0f, blockDropDuration);
        for (int x = 0; x < boardSettings.Columns; x++)
        {
            for (int y = 0; y < boardSettings.Rows; y++)
            {
                Vector2Int currentKey = new Vector2Int(x, y);
                if (!_nodes.TryGetValue(currentKey, out Node currentNode) || currentNode.OccupiedBlock != null)
                {
                    continue;
                }

                for (int k = y + 1; k < boardSettings.Rows; k++)
                {
                    Vector2Int upperKey = new Vector2Int(x, k);
                    if (!_nodes.TryGetValue(upperKey, out Node upperNode) || upperNode.OccupiedBlock == null)
                    {
                        continue;
                    }

                    Block blockToMove = upperNode.OccupiedBlock;
                    blockToMove.SetBlock(currentNode, true);
                    if (dropDuration > 0f)
                    {
                        blockToMove.transform.DOLocalMove(Vector3.zero, dropDuration).SetEase(Ease.OutBounce);
                    }
                    else
                    {
                        blockToMove.transform.localPosition = Vector3.zero;
                    }

                    freeNodes.Add(upperNode);
                    freeNodes.Remove(currentNode);
                    break;
                }
            }
        }
        UpdateFreeNodes();
        RefreshGroupVisuals();
        ChangeState(GameState.SpawningBlocks);
    }

    private void UpdateFreeNodes()
    {
        freeNodes.Clear();
        foreach (var node in _nodes.Values)
        {
            if (node.OccupiedBlock == null)
            {
                freeNodes.Add(node);
            }
        }
    }

    public enum GameState
    {
        GenerateLevel,
        SpawningBlocks,
        WaitingInput,
        Falling,
        NoMoreMove,
        Win,
        Lose,
        Pause
    }
}
