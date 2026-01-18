using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using DG.Tweening;
using Unity.Profiling;
using UnityEngine;
using Random = UnityEngine.Random;

public class BlockManager : MonoBehaviour
{
    [SerializeField] private BoardSettings boardSettings;
    [SerializeField] private GridManager gridManager;
    [SerializeField] private ObjectPool blockPool;
    [SerializeField] private AudioManager audioManager;
    [SerializeField] private float spawnHeightPadding = 1f;
    [SerializeField] private float blockDropDuration = 0.3f;
    [SerializeField] private float invalidGroupShakeDuration = 0.15f;
    [SerializeField] private Vector3 invalidGroupShakeStrength = new Vector3(0.1f, 0.1f, 0f);
    [SerializeField, Range(0f, 1f)] private float shuffleScaleDipRatio = 0.4f;
    [SerializeField] private float shuffleScaleDipAmount = 0.9f;

    private static readonly ProfilerMarker FallingMarker = new ProfilerMarker("BlockManager.ResolveFalling");
    private static readonly ProfilerMarker SpawnBlocksMarker = new ProfilerMarker("BlockManager.SpawnBlocks");
    private static readonly ProfilerMarker ShuffleMarker = new ProfilerMarker("BlockManager.TryShuffleBoard");

    private readonly Dictionary<Block, HashSet<Block>> blockGroups = new Dictionary<Block, HashSet<Block>>();
    private readonly Stack<Block> floodStack = new Stack<Block>(64);
    private readonly Stack<HashSet<Block>> groupSetPool = new Stack<HashSet<Block>>();
    private readonly HashSet<HashSet<Block>> uniqueGroupCollector = new HashSet<HashSet<Block>>();
    private readonly HashSet<Block> processedBlocks = new HashSet<Block>();
    private readonly List<Node> nodesToFillBuffer = new List<Node>(64);
    private bool isValidMoveExist;
    private Transform blastEffectRoot;
    private int shuffleTweensPending;
    private bool shuffleResolutionPending;
    private Action<bool> shuffleCompletionCallback;

    private BoardSettings Settings => boardSettings != null ? boardSettings : gridManager?.BoardSettings;

    public bool HasValidMove => isValidMoveExist;

    public void Initialize(GridManager grid)
    {
        gridManager = grid != null ? grid : gridManager;
        ClearCachedGroups();
        isValidMoveExist = false;
        shuffleTweensPending = 0;
        shuffleResolutionPending = false;
        blastEffectRoot = null;

        PrepareBlockPool();
        gridManager?.UpdateFreeNodes();
    }

    public void SpawnBlocks(Action<bool> onCompleted)
    {
        StartCoroutine(SpawnBlocksCoroutine(onCompleted));
    }

    public bool TryHandleBlockSelection(Block block)
    {
        if (block == null || gridManager == null)
        {
            return false;
        }

        bool fromCache = blockGroups.TryGetValue(block, out HashSet<Block> group);
        if (!fromCache)
        {
            group = AcquireGroupSet();
            block.FloodFill(group, floodStack);
        }

        if (group.Count >= 2)
        {
            audioManager?.PlayBlockSfx(block.blockType);
            foreach (var b in group)
            {
                if (b.node != null)
                {
                    b.node.OccupiedBlock = null;
                    gridManager.FreeNodes.Add(b.node);
                }

                PlayBlockBlastEffect(b);
                ReleaseBlock(b);
                blockGroups.Remove(b);
            }

            ReleaseGroupSet(group);
            return true;
        }

        PlayInvalidGroupFeedback(group);
        audioManager?.PlayInvalidSelection();
        if (!fromCache)
        {
            ReleaseGroupSet(group);
        }
        return false;
    }

    public void ResolveFalling()
    {
        if (gridManager == null)
        {
            return;
        }

        using (FallingMarker.Auto())
        {
            Node[,] nodeGrid = gridManager.NodeGrid;
            BoardSettings settings = Settings;
            if (nodeGrid == null || settings == null)
            {
                return;
            }

            float dropDuration = Mathf.Max(0f, blockDropDuration);
            for (int x = 0; x < settings.Columns; x++)
            {
                int writeIndex = 0;
                for (int y = 0; y < settings.Rows; y++)
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
                            gridManager.FreeNodes.Add(currentNode);
                            gridManager.FreeNodes.Remove(targetNode);
                        }

                        writeIndex++;
                    }
                }
            }

            gridManager.UpdateFreeNodes();
            RefreshGroupVisuals();
        }
    }

    public void ResolveDeadlock(Action<bool> onResolved)
    {
        if (gridManager == null)
        {
            onResolved?.Invoke(false);
            return;
        }

        shuffleCompletionCallback = onResolved;
        shuffleTweensPending = 0;
        shuffleResolutionPending = false;

        if (!TryShuffleBoard())
        {
            onResolved?.Invoke(false);
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

    public void PowerShuffle(Action onCompleted = null)
    {
        if (gridManager == null)
        {
            onCompleted?.Invoke();
            return;
        }

        Node[,] nodeGrid = gridManager.NodeGrid;
        BoardSettings settings = Settings;
        if (nodeGrid == null || settings == null)
        {
            onCompleted?.Invoke();
            return;
        }

        ClearCachedGroups();
        List<Block> blocks = new List<Block>(settings.Rows * settings.Columns);
        for (int x = 0; x < settings.Columns; x++)
        {
            for (int y = 0; y < settings.Rows; y++)
            {
                Node node = nodeGrid[x, y];
                if (node == null)
                {
                    continue;
                }

                Block occupiedBlock = node.OccupiedBlock;
                if (occupiedBlock != null)
                {
                    occupiedBlock.node = null; // prevent SetBlock from clearing a reused node later
                    blocks.Add(occupiedBlock);
                }

                node.OccupiedBlock = null;
            }
        }

        if (blocks.Count == 0)
        {
            gridManager.UpdateFreeNodes();
            RefreshGroupVisuals();
            return;
        }

        blocks.Sort((a, b) => a.blockType.CompareTo(b.blockType));

        float duration = Mathf.Max(0f, blockDropDuration * 0.5f);
        int blockIndex = 0;
        int powerShuffleTweens = 0;
        bool completionInvoked = false;

        void TryCompletePowerShuffle()
        {
            if (!completionInvoked && powerShuffleTweens <= 0)
            {
                completionInvoked = true;
                onCompleted?.Invoke();
            }
        }

        for (int x = 0; x < settings.Columns && blockIndex < blocks.Count; x++)
        {
            for (int y = 0; y < settings.Rows && blockIndex < blocks.Count; y++)
            {
                Node node = nodeGrid[x, y];
                if (node == null)
                {
                    continue;
                }

                Block block = blocks[blockIndex++];
                if (block == null)
                {
                    continue;
                }

                block.SetBlock(node, true);
                if (duration > 0f)
                {
                    Tween tween = PlayShuffleTween(block.transform, duration);
                    if (tween != null)
                    {
                        powerShuffleTweens++;
                        tween.OnComplete(() =>
                        {
                            powerShuffleTweens = Mathf.Max(0, powerShuffleTweens - 1);
                            TryCompletePowerShuffle();
                        });
                    }
                    else
                    {
                        block.transform.localPosition = Vector3.zero;
                    }
                }
                else
                {
                    block.transform.localPosition = Vector3.zero;
                }
            }
        }

        gridManager.UpdateFreeNodes();
        RefreshGroupVisuals();
        TryCompletePowerShuffle();
    }

    public bool DestroyAllBlocks()
    {
        if (gridManager == null)
        {
            return false;
        }

        Node[,] nodeGrid = gridManager.NodeGrid;
        if (nodeGrid == null)
        {
            return false;
        }

        ClearCachedGroups();
        isValidMoveExist = false;
        bool anyDestroyed = false;

        foreach (Node node in nodeGrid)
        {
            Block block = node?.OccupiedBlock;
            if (block == null)
            {
                continue;
            }

            PlayBlockBlastEffect(block);
            ReleaseBlock(block);
            node.OccupiedBlock = null;
            anyDestroyed = true;
        }

        gridManager.UpdateFreeNodes();
        RefreshGroupVisuals();
        return anyDestroyed;
    }

    public bool DestroyBlocksOfType(int targetBlockType)
    {
        if (gridManager == null)
        {
            return false;
        }

        Node[,] nodeGrid = gridManager.NodeGrid;
        if (nodeGrid == null)
        {
            return false;
        }

        bool destroyedAny = false;
        foreach (Node node in nodeGrid)
        {
            Block block = node?.OccupiedBlock;
            if (block == null || block.blockType != targetBlockType)
            {
                continue;
            }

            PlayBlockBlastEffect(block);
            ReleaseBlock(block);
            node.OccupiedBlock = null;
            blockGroups.Remove(block);
            destroyedAny = true;
        }

        if (!destroyedAny)
        {
            return false;
        }

        gridManager.UpdateFreeNodes();
        RefreshGroupVisuals();
        return true;
    }

    private IEnumerator SpawnBlocksCoroutine(Action<bool> onCompleted)
    {
        yield return new WaitForSeconds(0.2f);

        if (gridManager == null)
        {
            onCompleted?.Invoke(false);
            yield break;
        }

        using (SpawnBlocksMarker.Auto())
        {
            gridManager.UpdateFreeNodes();
            nodesToFillBuffer.Clear();
            foreach (var node in gridManager.FreeNodes)
            {
                if (node != null)
                {
                    nodesToFillBuffer.Add(node);
                }
            }
            float dropDuration = Mathf.Max(0f, blockDropDuration);
            BoardSettings settings = Settings;
            if (settings == null)
            {
                onCompleted?.Invoke(false);
                yield break;
            }

            foreach (var node in nodesToFillBuffer)
            {
                Block prefab = settings.BlockPrefabs[Random.Range(0, settings.BlockPrefabs.Length)];
                Block randomBlock = SpawnBlockFromPool(prefab.blockType, node.transform);
                if (randomBlock == null)
                {
                    continue;
                }

                randomBlock.SetBlock(node);
                float dropOffset = GetSpawnOffsetForRow(node.gridPosition.y);
                randomBlock.transform.localPosition = Vector3.up * dropOffset;
                gridManager.FreeNodes.Remove(node);

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
            onCompleted?.Invoke(isValidMoveExist);
            nodesToFillBuffer.Clear();
        }
    }

    private void PrepareBlockPool()
    {
        BoardSettings settings = Settings;
        if (settings == null)
        {
            Debug.LogError("BlockManager cannot initialize pool without BoardSettings.");
            return;
        }

        if (blockPool == null)
        {
            GameObject poolObject = new GameObject("BlockPool");
            blockPool = poolObject.AddComponent<ObjectPool>();
        }

        blockPool.transform.SetParent(transform);
        int totalCells = Mathf.Max(1, settings.Rows * settings.Columns);
        blockPool.Initialize(settings.BlockPrefabs, totalCells, 1.2f);
        blockPool.InitializeEffects(settings.BlockPrefabs, settings.BlastEffectPrefabs);
    }

    private Block SpawnBlockFromPool(int blockType, Transform parent)
    {
        if (blockPool == null)
        {
            Debug.LogError("Block pool not assigned; cannot spawn blocks.");
            return null;
        }

        return blockPool.Spawn(blockType, parent);
    }

    private void ReleaseBlock(Block block)
    {
        if (block == null)
        {
            return;
        }

        if (blockPool != null)
        {
            blockPool.Release(block);
        }
        else
        {
            Debug.LogError("Block pool missing; cannot release block instance.");
        }
    }

    private float GetSpawnOffsetForRow(int rowIndex)
    {
        BoardSettings settings = Settings;
        if (settings == null)
        {
            return spawnHeightPadding;
        }

        float rowsAbove = settings.Rows - rowIndex;
        return rowsAbove + spawnHeightPadding;
    }

    private void RefreshGroupVisuals()
    {
        if (gridManager == null || gridManager.Nodes == null || gridManager.Nodes.Count == 0)
        {
            ClearCachedGroups();
            isValidMoveExist = false;
            return;
        }

        ClearCachedGroups();
        isValidMoveExist = false;
        processedBlocks.Clear();
        foreach (var node in gridManager.Nodes.Values)
        {
            Block block = node.OccupiedBlock;
            if (block == null || !processedBlocks.Add(block))
            {
                continue;
            }

            HashSet<Block> group = AcquireGroupSet();
            block.FloodFill(group, floodStack);
            int groupSize = group.Count;

            if (groupSize >= 2)
            {
                isValidMoveExist = true;
            }

            foreach (var member in group)
            {
                blockGroups[member] = group;
                processedBlocks.Add(member);
                member.ApplyGroupIcon(groupSize, Settings);
            }
        }
    }

    private Tween PlayShuffleTween(Transform target, float duration)
    {
        if (target == null)
        {
            return null;
        }

        float moveDuration = Mathf.Max(0f, duration);
        if (moveDuration <= 0f)
        {
            target.localPosition = Vector3.zero;
            return null;
        }

        target.DOKill();

        Vector3 originalScale = target.localScale;
        Sequence sequence = DOTween.Sequence();
        sequence.Join(target.DOLocalMove(Vector3.zero, moveDuration).SetEase(Ease.OutCubic));

        float dipRatio = Mathf.Clamp01(shuffleScaleDipRatio);
        float dipAmount = Mathf.Clamp(shuffleScaleDipAmount, 0.1f, 2f);
        if (!Mathf.Approximately(dipAmount, 1f) && dipRatio > 0f && dipRatio < 1f)
        {
            float dipDuration = moveDuration * dipRatio;
            float recoverDuration = Mathf.Max(0.01f, moveDuration - dipDuration);
            Sequence scaleSequence = DOTween.Sequence();
            scaleSequence.Append(target.DOScale(originalScale * dipAmount, dipDuration).SetEase(Ease.OutSine));
            scaleSequence.Append(target.DOScale(originalScale, recoverDuration).SetEase(Ease.OutBack));
            sequence.Join(scaleSequence);
        }
        else
        {
            sequence.Join(target.DOScale(originalScale, moveDuration).SetEase(Ease.OutBack));
        }

        return sequence;
    }

    private void PlayBlockBlastEffect(Block block)
    {
        if (block == null || blockPool == null)
        {
            return;
        }

        ParticleSystem effect = blockPool.SpawnBlastEffect(block.blockType, block.transform.position, GetBlastEffectRoot());
        if (effect == null)
        {
            return;
        }

        StartCoroutine(ReturnEffectToPool(effect));
    }

    private IEnumerator ReturnEffectToPool(ParticleSystem effect)
    {
        if (effect == null)
        {
            yield break;
        }

        while (effect.IsAlive(true))
        {
            yield return null;
        }

        blockPool?.ReleaseBlastEffect(effect);
    }

    private void PlayInvalidGroupFeedback(IEnumerable<Block> group)
    {
        if (group == null)
        {
            return;
        }

        float duration = Mathf.Max(0.01f, invalidGroupShakeDuration);
        Vector3 strength = invalidGroupShakeStrength;

        foreach (Block b in group)
        {
            if (b == null)
            {
                continue;
            }

            Transform target = b.transform;
            target.DOKill();
            target.DOPunchScale(strength, duration, vibrato: 8, elasticity: 0.5f)
                .SetEase(Ease.OutQuad);
        }
    }

    private Transform GetBlastEffectRoot()
    {
        if (blastEffectRoot == null)
        {
            GameObject effectsContainer = new GameObject("BlastEffects");
            blastEffectRoot = effectsContainer.transform;
            blastEffectRoot.SetParent(transform);
        }

        return blastEffectRoot;
    }

    private bool TryShuffleBoard()
    {
        using (ShuffleMarker.Auto())
        {
            Dictionary<Vector2Int, Node> nodes = gridManager?.Nodes;
            Node[,] nodeGrid = gridManager?.NodeGrid;
            BoardSettings settings = Settings;
            if (nodes == null || nodes.Count == 0 || nodeGrid == null || settings == null)
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
                return RegenerateBoardWithGuaranteedPairs(nodeGrid, settings);
            }

            HashSet<Node> lockedNodes = new HashSet<Node>();
            Vector2Int firstPairA = new Vector2Int(0, 0);
            Vector2Int firstPairB = settings.Columns > 1 ? new Vector2Int(1, 0) : new Vector2Int(0, 1);
            CommitPair(colorNodes, colorWithPair, firstPairA, firstPairB, lockedNodes);

            int secondColor = GetDifferentColorWithPair(colorNodes, colorWithPair);
            if (secondColor != -1)
            {
                Vector2Int secondPairA;
                Vector2Int secondPairB;
                if (settings.Columns > 2)
                {
                    secondPairA = new Vector2Int(settings.Columns - 2, 0);
                    secondPairB = new Vector2Int(settings.Columns - 1, 0);
                }
                else if (settings.Rows > 2)
                {
                    secondPairA = new Vector2Int(0, settings.Rows - 2);
                    secondPairB = new Vector2Int(0, settings.Rows - 1);
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
                    Tween tween = PlayShuffleTween(block.transform, blockDropDuration);
                    if (tween != null)
                    {
                        RegisterShuffleTween(tween);
                    }
                    else
                    {
                        block.transform.localPosition = Vector3.zero;
                    }
                }
                else
                {
                    block.transform.localPosition = Vector3.zero;
                }
            }

            return true;
        }
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

    private void CommitPair(Dictionary<int, List<Node>> colorNodes, int color, Vector2Int posA, Vector2Int posB,
        HashSet<Node> lockedNodes)
    {
        if (!colorNodes.TryGetValue(color, out List<Node> nodesOfColor) || nodesOfColor.Count < 2)
        {
            return;
        }

        if (!gridManager.Nodes.TryGetValue(posA, out Node nodeA) || !gridManager.Nodes.TryGetValue(posB, out Node nodeB))
        {
            return;
        }

        EnsureColorAtPositions(nodesOfColor, nodeA, nodeB);
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

    private void CompleteShuffle()
    {
        shuffleResolutionPending = false;
        gridManager.UpdateFreeNodes();
        RefreshGroupVisuals();
        shuffleCompletionCallback?.Invoke(isValidMoveExist);
    }

    private void RegisterShuffleTween(Tween tween)
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

    private HashSet<Block> AcquireGroupSet()
    {
        return groupSetPool.Count > 0 ? groupSetPool.Pop() : new HashSet<Block>();
    }

    private void ReleaseGroupSet(HashSet<Block> set)
    {
        if (set == null)
        {
            return;
        }

        set.Clear();
        groupSetPool.Push(set);
    }

    private void ClearCachedGroups()
    {
        if (blockGroups.Count == 0)
        {
            return;
        }

        uniqueGroupCollector.Clear();
        foreach (var kvp in blockGroups)
        {
            if (kvp.Value != null)
            {
                uniqueGroupCollector.Add(kvp.Value);
            }
        }

        blockGroups.Clear();

        foreach (var group in uniqueGroupCollector)
        {
            ReleaseGroupSet(group);
        }

        uniqueGroupCollector.Clear();
    }

    private bool RegenerateBoardWithGuaranteedPairs(Node[,] nodeGrid, BoardSettings settings)
    {
        if (nodeGrid == null || settings == null || gridManager == null)
        {
            return false;
        }

        if (blockPool == null)
        {
            PrepareBlockPool();
        }

        Block[] prefabs = settings.BlockPrefabs;
        if (prefabs == null || prefabs.Length == 0)
        {
            return false;
        }

        int columns = Mathf.Max(0, settings.Columns);
        int rows = Mathf.Max(0, settings.Rows);
        if (columns * rows < 2)
        {
            return false;
        }

        foreach (Node node in nodeGrid)
        {
            Block block = node?.OccupiedBlock;
            if (block == null)
            {
                continue;
            }

            ReleaseBlock(block);
            node.OccupiedBlock = null;
        }

        gridManager.UpdateFreeNodes();

        Vector2Int forcedPairA = new Vector2Int(0, 0);
        Vector2Int forcedPairB = columns > 1 ? new Vector2Int(1, 0) : new Vector2Int(0, 1);
        Block forcedPrefab = prefabs[Random.Range(0, prefabs.Length)];
        if (forcedPrefab == null)
        {
            return false;
        }

        Node forcedNodeA = nodeGrid[forcedPairA.x, forcedPairA.y];
        Node forcedNodeB = nodeGrid[forcedPairB.x, forcedPairB.y];
        SpawnBlockOfType(forcedPrefab.blockType, forcedNodeA);
        SpawnBlockOfType(forcedPrefab.blockType, forcedNodeB);

        for (int x = 0; x < columns; x++)
        {
            for (int y = 0; y < rows; y++)
            {
                Node node = nodeGrid[x, y];
                if (node == null || node.OccupiedBlock != null)
                {
                    continue;
                }

                Block randomPrefab = prefabs[Random.Range(0, prefabs.Length)];
                if (randomPrefab == null)
                {
                    continue;
                }

                SpawnBlockOfType(randomPrefab.blockType, node);
            }
        }

        gridManager.UpdateFreeNodes();
        return true;
    }

    private Block SpawnBlockOfType(int blockType, Node targetNode)
    {
        if (targetNode == null)
        {
            return null;
        }

        Block spawned = SpawnBlockFromPool(blockType, targetNode.transform);
        if (spawned == null)
        {
            return null;
        }

        spawned.SetBlock(targetNode);
        spawned.transform.localPosition = Vector3.zero;
        gridManager?.FreeNodes.Remove(targetNode);
        return spawned;
    }
}
