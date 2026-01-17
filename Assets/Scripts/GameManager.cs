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
    private Node[,] nodeGrid;
    private GameObject boardInstance;
    private Vector3 boardBaseScale = Vector3.one;
    private Vector2 boardEnvelopeSize;
    private Transform gridRoot;
    private bool settingsReady;
    private bool isValidMoveExist;
    private int shuffleTweensPending;
    private bool shuffleResolutionPending;
    private readonly Dictionary<Block, HashSet<Block>> blockGroups = new Dictionary<Block, HashSet<Block>>();

    private GameState _state;

    private void Awake()
    {
        Instance = this;
        _nodes = new Dictionary<Vector2Int, Node>();
        freeNodes = new HashSet<Node>(); // Boş düğümler burada saklanacak
        nodeGrid = new Node[boardSettings.Columns, boardSettings.Rows];

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
                nodeGrid[x, y] = node;
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
            case GameState.Deadlock:
                HandleDeadlockState();
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
        ChangeState(isValidMoveExist ? GameState.WaitingInput : GameState.Deadlock);
    }



    public void TryBlastBlock(Block block)
    {
        if (!IsWaitingForInput)
        {
            return;
        }

        if (!blockGroups.TryGetValue(block, out HashSet<Block> group))
        {
            Debug.Log("FloodFill recalculated for block " + block.name);
            group = block.FloodFill();
        }
        else
        {
            Debug.Log("FloodFill cache hit for block " + block.name);
        }
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
                blockGroups.Remove(b);
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

        blockGroups.Clear();
        isValidMoveExist = false;
        HashSet<Block> processed = new HashSet<Block>();
        foreach (var node in _nodes.Values)
        {
            Block block = node.OccupiedBlock;
            if (block == null || !processed.Add(block)) continue;

            HashSet<Block> group = block.FloodFill();
            int groupSize = group.Count;
            Debug.Log($"Caching group of size {groupSize} for seed {block.name}");
            foreach (var member in group)
            {
                blockGroups[member] = group;
            }
            if (groupSize >= 2)
            {
                isValidMoveExist = true;
            }
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
            int writeIndex = 0;
            for (int y = 0; y < boardSettings.Rows; y++)
            {
                Node currentNode = nodeGrid[x, y];
                if (currentNode == null)
                {
                    continue;
                }

                Block block = currentNode.OccupiedBlock;
                if (block != null)
                {
                    if (y != writeIndex)
                    {
                        Node targetNode = nodeGrid[x, writeIndex];
                        block.SetBlock(targetNode, true);
                        if (dropDuration > 0f)
                        {
                            block.transform.DOLocalMove(Vector3.zero, dropDuration).SetEase(Ease.OutBounce);
                        }
                        else
                        {
                            block.transform.localPosition = Vector3.zero;
                        }

                        targetNode.OccupiedBlock = block;
                        currentNode.OccupiedBlock = null;
                        freeNodes.Add(currentNode);
                        freeNodes.Remove(targetNode);
                    }

                    writeIndex++;
                }
            }
        }
        UpdateFreeNodes();
        RefreshGroupVisuals();
        ChangeState(GameState.SpawningBlocks);
    }

    private void HandleDeadlockState()
    {
        shuffleTweensPending = 0;
        shuffleResolutionPending = false;

        shuffleTweensPending = 0;
        shuffleResolutionPending = false;

        if (!TryShuffleBoard())
        {
            Debug.LogWarning("Deadlock persists: unable to create a new move.");
            ChangeState(GameState.NoMoreMove);
            return;
        }

        if (shuffleTweensPending == 0)
        {
            CompleteShuffle();
        }
        else
        {
            shuffleResolutionPending = true;
        }
    }

    private bool TryShuffleBoard()
    {
        if (_nodes == null || _nodes.Count == 0)
        {
            return false;
        }

        Dictionary<int, List<Node>> colorNodes = new Dictionary<int, List<Node>>();
        foreach (Node node in nodeGrid)
        {
            if (node?.OccupiedBlock == null)
            {
                continue;
            }

            int color = node.OccupiedBlock.blockType;
            if (!colorNodes.TryGetValue(color, out List<Node> list))
            {
                list = new List<Node>();
                colorNodes[color] = list;
            }
            list.Add(node);
        }

        int colorWithPair = GetColorWithPair(colorNodes);
        if (colorWithPair == -1)
        {
            return false;
        }

        HashSet<Node> lockedNodes = new HashSet<Node>();
        Vector2Int firstPairA = new Vector2Int(0, 0);
        Vector2Int firstPairB = boardSettings.Columns > 1 ? new Vector2Int(1, 0) : new Vector2Int(0, 1);
        CommitPair(colorNodes, colorWithPair, firstPairA, firstPairB, lockedNodes);

        int secondColor = GetDifferentColorWithPair(colorNodes, colorWithPair);
        if (secondColor != -1)
        {
            Vector2Int secondPairA;
            Vector2Int secondPairB;
            if (boardSettings.Columns > 2)
            {
                secondPairA = new Vector2Int(boardSettings.Columns - 2, 0);
                secondPairB = new Vector2Int(boardSettings.Columns - 1, 0);
            }
            else if (boardSettings.Rows > 2)
            {
                secondPairA = new Vector2Int(0, boardSettings.Rows - 2);
                secondPairB = new Vector2Int(0, boardSettings.Rows - 1);
            }
            else
            {
                secondColor = -1;
                secondPairA = Vector2Int.zero;
                secondPairB = Vector2Int.zero;
            }

            if (secondColor != -1)
            {
                CommitPair(colorNodes, secondColor, secondPairA, secondPairB, lockedNodes);
            }
        }

        List<Node> swappableNodes = new List<Node>();
        foreach (Node node in nodeGrid)
        {
            if (node?.OccupiedBlock == null || lockedNodes.Contains(node))
            {
                continue;
            }

            swappableNodes.Add(node);
        }

        for (int i = swappableNodes.Count - 1; i > 0; i--)
        {
            int j = Random.Range(0, i + 1);
            SwapNodeBlock(swappableNodes[i], swappableNodes[j]);
        }

        foreach (Node node in nodeGrid)
        {
            Block block = node.OccupiedBlock;
            if (block == null)
            {
                continue;
            }

            block.transform.SetParent(node.transform, true);
            if (blockDropDuration > 0f)
            {
                Tweener tween = block.transform.DOLocalMove(Vector3.zero, blockDropDuration).SetEase(Ease.InOutQuad);
                RegisterShuffleTween(tween);
            }
            else
            {
                block.transform.localPosition = Vector3.zero;
            }
        }

        return true;
    }


    private int GetColorWithPair(Dictionary<int, List<Node>> colorNodes)
    {
        foreach (var kvp in colorNodes)
        {
            if (kvp.Value.Count >= 2)
            {
                return kvp.Key;
            }
        }

        return -1;
    }

    private void CommitPair(Dictionary<int, List<Node>> colorNodes, int color, Vector2Int posA, Vector2Int posB,
        HashSet<Node> lockedNodes)
    {
        if (!colorNodes.TryGetValue(color, out List<Node> nodes) || nodes.Count < 2)
        {
            return;
        }

        if (!_nodes.TryGetValue(posA, out Node nodeA) || !_nodes.TryGetValue(posB, out Node nodeB))
        {
            return;
        }

        EnsureColorAtPositions(nodes, nodeA, nodeB);
        lockedNodes?.Add(nodeA);
        lockedNodes?.Add(nodeB);
    }

    private void EnsureColorAtPositions(List<Node> colorNodes, Node targetA, Node targetB)
    {
        if (colorNodes == null || colorNodes.Count < 2)
        {
            return;
        }

        SwapNodeBlock(colorNodes[0], targetA);
        SwapNodeBlock(colorNodes[1], targetB);

        colorNodes[0] = targetA;
        colorNodes[1] = targetB;
    }

    private void SwapNodeBlock(Node source, Node destination)
    {
        if (source == null || destination == null || source == destination)
        {
            return;
        }

        Block sourceBlock = source.OccupiedBlock;
        Block destBlock = destination.OccupiedBlock;

        source.OccupiedBlock = destBlock;
        if (destBlock != null)
        {
            destBlock.node = source;
        }

        destination.OccupiedBlock = sourceBlock;
        if (sourceBlock != null)
        {
            sourceBlock.node = destination;
        }
    }

    private int GetDifferentColorWithPair(Dictionary<int, List<Node>> colorNodes, int excludedColor)
    {
        foreach (var kvp in colorNodes)
        {
            if (kvp.Key == excludedColor)
            {
                continue;
            }

            if (kvp.Value.Count >= 2)
            {
                return kvp.Key;
            }
        }

        return -1;
    }

    private void RegisterShuffleTween(Tweener tween)
    {
        if (tween == null)
        {
            return;
        }

        shuffleTweensPending++;
        tween.OnComplete(() =>
        {
            shuffleTweensPending = Mathf.Max(0, shuffleTweensPending - 1);
            TryResolveShuffleTweens();
        });
    }

    private void TryResolveShuffleTweens()
    {
        if (!shuffleResolutionPending || shuffleTweensPending > 0)
        {
            return;
        }

        CompleteShuffle();
    }

    private void CompleteShuffle()
    {
        shuffleResolutionPending = false;
        UpdateFreeNodes();
        RefreshGroupVisuals();

        if (isValidMoveExist)
        {
            ChangeState(GameState.WaitingInput);
        }
        else
        {
            Debug.LogError("Deadlock unresolved after shuffle attempt.");
        }
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
        Deadlock,
        NoMoreMove,
        Win,
        Lose,
        Pause
    }
}
