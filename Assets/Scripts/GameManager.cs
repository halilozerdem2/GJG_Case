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
    [SerializeField] private int _width = 5;
    [SerializeField] private int _height = 7;

    [SerializeField] private Node _nodePrefab;
    [SerializeField] private Block[] blockTypes;
    [SerializeField] private SpriteRenderer _boardPrefab;

    private static readonly Vector2Int[] CardinalDirections =
    {
        Vector2Int.up,
        Vector2Int.down,
        Vector2Int.left,
        Vector2Int.right
    };

    private Dictionary<Vector2Int, Node> _nodes;
    private List<Block> _blocks;
    private HashSet<Node> freeNodes; // Boş olan düğümleri takip eden liste

    private GameState _state;

    private void Awake()
    {
        Instance = this;
        _nodes = new Dictionary<Vector2Int, Node>();
        _blocks = new List<Block>();
        freeNodes = new HashSet<Node>(); // Boş düğümler burada saklanacak
    }

    private void Start()
    {
        ChangeState(GameState.GenerateLevel);
    }

    void GenerateGrid()
    {
        for (int x = 0; x < _width; x++)
        {
            for (int y = 0; y < _height; y++)
            {
                var node = Instantiate(_nodePrefab, new Vector3(x, y), Quaternion.identity);
                node.gridPosition = new Vector2Int(x, y);
                _nodes[node.gridPosition] = node;

                freeNodes.Add(node); // Başlangıçta tüm düğümler boş olacak
            }
        }

        var center = new Vector2((float)_width / 2 - 0.5f, (float)_height / 2 - 0.5f);
        var board = Instantiate(_boardPrefab, center, Quaternion.identity);
        board.size = new Vector2(_width, _height);

        Camera.main.transform.position = new Vector3(center.x, center.y, -10);
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
            case GameState.Blasting:
                UpdateGrid();
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

    private void SpawnBlocks()
    {
        StartCoroutine(SpawnBlocksCoroutine());
    }

    private IEnumerator SpawnBlocksCoroutine()
    {
        yield return new WaitForSeconds(0.2f); // Bloklar düştükten sonra 0.2 saniye bekle

        UpdateFreeNodes();
        List<Node> nodesToFill = freeNodes.ToList(); // Şu anda boş olan düğümleri listeye al

        foreach (var node in nodesToFill)
        {
            // Blokları üstten (örneğin _height + 1 seviyesinden) spawn et
            Vector3 spawnPos = new Vector3(node.Pos.x, _height + 1, 0);
            Block randomBlock = Instantiate(blockTypes[Random.Range(0, blockTypes.Length)], spawnPos, Quaternion.identity);

            randomBlock.SetBlock(node);
            _blocks.Add(randomBlock);
            freeNodes.Remove(node); // Artık dolu, freeNodes listesinden çıkar

            // Animasyonlu düşme efekti
            randomBlock.transform.DOMove(node.Pos, 0.3f).SetEase(Ease.OutBounce);
        }

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
            ChangeState(GameState.Blasting);
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

    public void UpdateGrid() // O(n^3) karmaşıklığındaki yapı dicitonary kullanılarak               
    {                        // O(n^2 Log N) seviyesine düşürülecek
        for (int x = 0; x < _width; x++)
        {
            for (int y = 0; y < _height; y++)
            {
                Node currentNode = _nodes.FirstOrDefault(n => n.Key.x == x && n.Key.y == y).Value;
                if (currentNode == null || currentNode.OccupiedBlock != null) continue;

                for (int k = y + 1; k < _height; k++)
                {
                    Node upperNode = _nodes.FirstOrDefault(n => n.Key.x == x && n.Key.y == k).Value;
                    if (upperNode == null) continue;
                    if (upperNode.OccupiedBlock != null)
                    {
                        // Swap 
                        Block blockToMove = upperNode.OccupiedBlock;
                        blockToMove.SetBlock(currentNode);
                        blockToMove.transform.DOMove(currentNode.Pos, 0.3f).SetEase(Ease.OutBounce);

                        upperNode.OccupiedBlock = null;
                        freeNodes.Add(upperNode);
                        freeNodes.Remove(currentNode);
                        break;
                    }
                }
            }
        }
        SpawnBlocks();
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
        Blasting,
        NoMoreMove,
        Win,
        Lose,
        Pause
    }
}
