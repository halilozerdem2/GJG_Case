using System;
using System.Collections;
using System.Collections.Generic;
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

    private readonly List<Node> nodesToFillBuffer = new List<Node>(64);
    private BoardModel boardModel = new BoardModel();
    private int[] bfsQueue;
    private int[] groupIndicesBuffer;
    private int[] visitedStamps;
    private int[] cachedGroupSizes;
    private int[] cachedGroupStamps;
    private bool[] dirtyFlags;
    private int[] dirtyIndices;
    private int visitStamp;
    private int groupEvaluationStamp;
    private int dirtyCount;
    private bool requireFullRefresh;
    private bool isValidMoveExist;
    private Transform blastEffectRoot;
    private int shuffleTweensPending;
    private bool shuffleResolutionPending;
    private Action<bool> shuffleCompletionCallback;

    private BoardSettings Settings => boardSettings != null ? boardSettings : gridManager?.BoardSettings;
    private AudioManager Audio => audioManager != null ? audioManager : AudioManager.Instance;

    public bool HasValidMove => isValidMoveExist;
    public BoardModel BoardModel => boardModel;

    private void OnEnable()
    {
        RefreshAudioManagerReference();
    }

    private void Start()
    {
        RefreshAudioManagerReference();
    }

    private void RefreshAudioManagerReference()
    {
        var instance = AudioManager.Instance;
        if (instance != null)
        {
            audioManager = instance;
        }
    }

    public void Initialize(GridManager grid)
    {
        gridManager = grid != null ? grid : gridManager;
        ConfigureBoardModel();
        EnsureGroupBuffers();
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
        if (block == null || gridManager == null || boardModel == null)
        {
            return false;
        }

        Node[,] nodeGrid = gridManager.NodeGrid;
        if (nodeGrid == null || block.node == null)
        {
            return false;
        }

        Vector2Int position = block.node.gridPosition;
        int startIndex = boardModel.Index(position.x, position.y);
        if (startIndex < 0 || !boardModel.IsOccupied(startIndex))
        {
            return false;
        }

        EnsureGroupBuffers();
        int groupCount = GatherGroupIndices(startIndex);
        if (groupCount < 2)
        {
            PlayInvalidGroupFeedback(groupCount);
            Audio?.PlayInvalidSelection();
            return false;
        }

        Audio?.PlayBlockSfx(block.blockType);

        int columns = boardModel.Columns;
        int rows = boardModel.Rows;
        for (int i = 0; i < groupCount; i++)
        {
            int memberIndex = groupIndicesBuffer[i];
            int x = columns > 0 ? memberIndex % columns : boardModel.X(memberIndex);
            int y = columns > 0 ? memberIndex / columns : boardModel.Y(memberIndex);
            if (x < 0 || x >= columns || y < 0 || y >= rows)
            {
                ClearModelCell(memberIndex);
                continue;
            }

            Node targetNode = nodeGrid[x, y];
            if (targetNode == null)
            {
                ClearModelCell(memberIndex);
                continue;
            }

            Block member = targetNode.OccupiedBlock;
            if (member != null)
            {
                targetNode.OccupiedBlock = null;
                gridManager.FreeNodes.Add(targetNode);
                PlayBlockBlastEffect(member);
                ReleaseBlock(member);
            }

            ClearModelCell(memberIndex);
        }

        return true;
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
            if (nodeGrid == null || settings == null || boardModel == null)
            {
                return;
            }

            float dropDuration = Mathf.Max(0f, blockDropDuration);
            for (int x = 0; x < settings.Columns; x++)
            {
                int writeIndex = 0;
                for (int y = 0; y < settings.Rows; y++)
                {
                    int fromIndex = boardModel.Index(x, y);
                    if (fromIndex < 0)
                    {
                        continue;
                    }

                    Node currentNode = nodeGrid[x, y];
                    if (currentNode == null)
                    {
                        ClearModelCell(fromIndex);
                        continue;
                    }

                    Block block = currentNode.OccupiedBlock;
                    if (block == null)
                    {
                        ClearModelCell(fromIndex);
                        continue;
                    }

                    if (!boardModel.IsOccupied(fromIndex))
                    {
                        SetModelCell(fromIndex, block.blockType);
                    }

                    if (y != writeIndex)
                    {
                        int targetIndex = boardModel.Index(x, writeIndex);
                        boardModel.CopyCell(fromIndex, targetIndex);
                        MarkDirtyCell(targetIndex, includeNeighbours: true);
                        ClearModelCell(fromIndex);

                        Node targetNode = nodeGrid[x, writeIndex];
                        if (targetNode != null)
                        {
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
                    }

                    writeIndex++;
                }

                for (int y = writeIndex; y < settings.Rows; y++)
                {
                    int emptyIndex = boardModel.Index(x, y);
                    if (emptyIndex >= 0)
                    {
                        ClearModelCell(emptyIndex);
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
                ClearModelCell(node.gridPosition);
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
                    ClearModelCell(node.gridPosition);
                    continue;
                }

                block.SetBlock(node, true);
                SetModelCell(node.gridPosition, block.blockType);
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
            if (node != null)
            {
                ClearModelCell(node.gridPosition);
            }
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
            if (node != null)
            {
                ClearModelCell(node.gridPosition);
            }
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
                if (node == null)
                {
                    continue;
                }

                Block prefab = settings.BlockPrefabs[Random.Range(0, settings.BlockPrefabs.Length)];
                if (prefab == null)
                {
                    continue;
                }

                Block randomBlock = SpawnBlockFromPool(prefab.blockType, node.transform);
                if (randomBlock == null)
                {
                    continue;
                }

                SetModelCell(node.gridPosition, prefab.blockType);
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
        Node[,] nodeGrid = gridManager?.NodeGrid;
        BoardSettings settings = Settings;
        if (gridManager == null || boardModel == null || nodeGrid == null || settings == null)
        {
            isValidMoveExist = false;
            return;
        }

        EnsureGroupBuffers();
        int columns = Mathf.Max(0, boardModel.Columns);
        int rows = Mathf.Max(0, boardModel.Rows);
        if (columns == 0 || rows == 0)
        {
            dirtyCount = 0;
            return;
        }

        if (requireFullRefresh)
        {
            MarkEntireBoardDirty();
            requireFullRefresh = false;
        }
        else if (dirtyCount == 0)
        {
            return;
        }

        IncrementGroupEvaluationStamp();

        int maxIndex = boardModel.CellCount;

        for (int i = 0; i < dirtyCount; i++)
        {
            int index = dirtyIndices[i];
            if (index < 0 || index >= maxIndex)
            {
                continue;
            }

            if (dirtyFlags != null)
            {
                dirtyFlags[index] = false;
            }

            if (cachedGroupStamps != null && cachedGroupStamps[index] == groupEvaluationStamp)
            {
                continue;
            }

            if (!boardModel.IsOccupied(index))
            {
                if (cachedGroupStamps != null)
                {
                    cachedGroupStamps[index] = groupEvaluationStamp;
                    cachedGroupSizes[index] = 0;
                }
                continue;
            }

            int groupCount = GatherGroupIndices(index);
            if (groupCount <= 0)
            {
                continue;
            }

            for (int j = 0; j < groupCount; j++)
            {
                int memberIndex = groupIndicesBuffer[j];
                if (cachedGroupSizes != null)
                {
                    cachedGroupSizes[memberIndex] = groupCount;
                    cachedGroupStamps[memberIndex] = groupEvaluationStamp;
                }

                int memberX = columns > 0 ? memberIndex % columns : boardModel.X(memberIndex);
                int memberY = columns > 0 ? memberIndex / columns : boardModel.Y(memberIndex);
                if (memberX < 0 || memberX >= columns || memberY < 0 || memberY >= rows)
                {
                    continue;
                }

                Node memberNode = nodeGrid[memberX, memberY];
                Block memberBlock = memberNode?.OccupiedBlock;
                if (memberBlock != null)
                {
                    memberBlock.ApplyGroupIcon(groupCount, settings);
                }
            }
        }

        isValidMoveExist = false;
        if (cachedGroupSizes != null)
        {
            int limit = Mathf.Min(cachedGroupSizes.Length, boardModel.CellCount);
            for (int i = 0; i < limit; i++)
            {
                if (cachedGroupSizes[i] >= 2)
                {
                    isValidMoveExist = true;
                    break;
                }
            }
        }

        dirtyCount = 0;
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

    private void PlayInvalidGroupFeedback(int groupCount)
    {
        if (groupCount <= 0 || gridManager == null || boardModel == null)
        {
            return;
        }

        Node[,] nodeGrid = gridManager.NodeGrid;
        if (nodeGrid == null)
        {
            return;
        }

        float duration = Mathf.Max(0.01f, invalidGroupShakeDuration);
        Vector3 strength = invalidGroupShakeStrength;
        int columns = Mathf.Max(0, boardModel.Columns);
        int rows = Mathf.Max(0, boardModel.Rows);

        for (int i = 0; i < groupCount; i++)
        {
            if (groupIndicesBuffer == null || i >= groupIndicesBuffer.Length)
            {
                break;
            }

            int index = groupIndicesBuffer[i];
            int x = columns > 0 ? index % columns : boardModel.X(index);
            int y = columns > 0 ? index / columns : boardModel.Y(index);
            if (x < 0 || x >= columns || y < 0 || y >= rows)
            {
                continue;
            }

            Node node = nodeGrid[x, y];
            Block block = node?.OccupiedBlock;
            if (block == null)
            {
                continue;
            }

            Transform target = block.transform;
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
                bool regenerated = RegenerateBoardWithGuaranteedPairs(nodeGrid, settings);
                if (regenerated)
                {
                    RequireFullBoardRefresh();
                }
                return regenerated;
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

        for (int y = 0; y < settings.Rows; y++)
        {
            for (int x = 0; x < settings.Columns; x++)
            {
                Node node = nodeGrid[x, y];
                if (node == null)
                {
                    continue;
                }

                Block block = node.OccupiedBlock;
                if (block == null)
                {
                    continue;
                }

                block.SetBlock(node, true);
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
        }

            RequireFullBoardRefresh();
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

        SwapModelCells(source.gridPosition, destination.gridPosition);
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

        boardModel?.Clear();
        RequireFullBoardRefresh();
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
        SetModelCell(targetNode.gridPosition, blockType);
        spawned.transform.localPosition = Vector3.zero;
        gridManager?.FreeNodes.Remove(targetNode);
        return spawned;
    }

    private void RequireFullBoardRefresh()
    {
        requireFullRefresh = true;
    }

    private void EnsureGroupBuffers()
    {
        int cellCount = boardModel?.CellCount ?? 0;
        if (cellCount <= 0)
        {
            bfsQueue = null;
            groupIndicesBuffer = null;
            visitedStamps = null;
            cachedGroupSizes = null;
            cachedGroupStamps = null;
            dirtyFlags = null;
            dirtyIndices = null;
            dirtyCount = 0;
            visitStamp = 0;
            groupEvaluationStamp = 0;
            requireFullRefresh = true;
            return;
        }

        if (bfsQueue == null || bfsQueue.Length < cellCount)
        {
            bfsQueue = new int[cellCount];
        }

        if (groupIndicesBuffer == null || groupIndicesBuffer.Length < cellCount)
        {
            groupIndicesBuffer = new int[cellCount];
        }

        if (visitedStamps == null || visitedStamps.Length < cellCount)
        {
            visitedStamps = new int[cellCount];
            visitStamp = 0;
        }

        if (cachedGroupSizes == null || cachedGroupSizes.Length < cellCount)
        {
            cachedGroupSizes = new int[cellCount];
        }

        if (cachedGroupStamps == null || cachedGroupStamps.Length < cellCount)
        {
            cachedGroupStamps = new int[cellCount];
            groupEvaluationStamp = 0;
        }

        if (dirtyFlags == null || dirtyFlags.Length < cellCount)
        {
            dirtyFlags = new bool[cellCount];
            dirtyIndices = new int[cellCount];
            dirtyCount = 0;
            requireFullRefresh = true;
        }
        else if (dirtyIndices == null || dirtyIndices.Length < cellCount)
        {
            dirtyIndices = new int[cellCount];
        }
    }

    private int GatherGroupIndices(int startIndex)
    {
        if (boardModel == null || bfsQueue == null || groupIndicesBuffer == null || visitedStamps == null)
        {
            return 0;
        }

        if (!boardModel.IsValidIndex(startIndex))
        {
            return 0;
        }

        Cell startCell = boardModel.GetCell(startIndex);
        if (!startCell.occupied)
        {
            return 0;
        }

        int columns = boardModel.Columns;
        int rows = boardModel.Rows;
        if (columns <= 0 || rows <= 0)
        {
            return 0;
        }

        int stamp = AcquireVisitStamp();
        int head = 0;
        int tail = 0;
        bfsQueue[tail++] = startIndex;
        visitedStamps[startIndex] = stamp;
        int groupCount = 0;
        byte colorId = startCell.colorId;

        while (head < tail)
        {
            int current = bfsQueue[head++];
            groupIndicesBuffer[groupCount++] = current;

            int cx = columns > 0 ? current % columns : boardModel.X(current);
            int cy = columns > 0 ? current / columns : boardModel.Y(current);

            TryVisit(cx - 1, cy);
            TryVisit(cx + 1, cy);
            TryVisit(cx, cy - 1);
            TryVisit(cx, cy + 1);
        }

        return groupCount;

        void TryVisit(int x, int y)
        {
            if (x < 0 || x >= columns || y < 0 || y >= rows)
            {
                return;
            }

            int index = y * columns + x;
            if (visitedStamps[index] == stamp)
            {
                return;
            }

            Cell cell = boardModel.GetCell(index);
            if (!cell.occupied || cell.colorId != colorId)
            {
                return;
            }

            visitedStamps[index] = stamp;
            bfsQueue[tail++] = index;
        }
    }

    private int AcquireVisitStamp()
    {
        visitStamp++;
        if (visitStamp == int.MaxValue)
        {
            if (visitedStamps != null)
            {
                Array.Clear(visitedStamps, 0, visitedStamps.Length);
            }
            visitStamp = 1;
        }

        return visitStamp;
    }

    private void IncrementGroupEvaluationStamp()
    {
        groupEvaluationStamp++;
        if (groupEvaluationStamp == int.MaxValue)
        {
            if (cachedGroupStamps != null)
            {
                Array.Clear(cachedGroupStamps, 0, cachedGroupStamps.Length);
            }
            groupEvaluationStamp = 1;
        }
    }

    private void MarkDirtyCell(int index, bool includeNeighbours = false)
    {
        if (boardModel == null || index < 0)
        {
            return;
        }

        EnsureGroupBuffers();
        if (dirtyFlags == null || dirtyIndices == null)
        {
            return;
        }

        if (!dirtyFlags[index])
        {
            dirtyFlags[index] = true;
            if (dirtyCount >= dirtyIndices.Length)
            {
                Array.Resize(ref dirtyIndices, dirtyIndices.Length * 2);
            }
            dirtyIndices[dirtyCount++] = index;
        }

        if (!includeNeighbours)
        {
            return;
        }

        int x = boardModel.X(index);
        int y = boardModel.Y(index);
        MarkDirtyNeighbour(x - 1, y);
        MarkDirtyNeighbour(x + 1, y);
        MarkDirtyNeighbour(x, y - 1);
        MarkDirtyNeighbour(x, y + 1);
    }

    private void MarkDirtyNeighbour(int x, int y)
    {
        int neighbourIndex = boardModel.Index(x, y);
        if (neighbourIndex >= 0)
        {
            MarkDirtyCell(neighbourIndex);
        }
    }

    private void MarkEntireBoardDirty()
    {
        if (boardModel == null)
        {
            dirtyCount = 0;
            return;
        }

        EnsureGroupBuffers();
        if (dirtyFlags == null || dirtyIndices == null)
        {
            return;
        }

        int total = boardModel.CellCount;
        dirtyCount = 0;
        for (int i = 0; i < total; i++)
        {
            dirtyFlags[i] = true;
            dirtyIndices[dirtyCount++] = i;
        }
    }

    private void ConfigureBoardModel()
    {
        boardModel ??= new BoardModel();
        BoardSettings settings = Settings;
        if (settings == null)
        {
            boardModel.Configure(0, 0);
            RequireFullBoardRefresh();
            return;
        }

        boardModel.Configure(settings.Columns, settings.Rows);
        EnsureGroupBuffers();
        RequireFullBoardRefresh();
    }

    private void SetModelCell(Vector2Int gridPosition, int blockType, byte iconTier = 0)
    {
        if (boardModel == null)
        {
            return;
        }

        int index = boardModel.Index(gridPosition.x, gridPosition.y);
        SetModelCell(index, blockType, iconTier);
    }

    private void SetModelCell(int index, int blockType, byte iconTier = 0)
    {
        if (boardModel == null || index < 0)
        {
            return;
        }

        boardModel.SetCell(index, ToColorId(blockType), iconTier, true);
        MarkDirtyCell(index, includeNeighbours: true);
    }

    private void ClearModelCell(Vector2Int gridPosition)
    {
        if (boardModel == null)
        {
            return;
        }

        int index = boardModel.Index(gridPosition.x, gridPosition.y);
        ClearModelCell(index);
    }

    private void ClearModelCell(int index)
    {
        if (boardModel == null || index < 0)
        {
            return;
        }

        boardModel.ClearCell(index);
        MarkDirtyCell(index, includeNeighbours: true);
    }

    private void SwapModelCells(Vector2Int positionA, Vector2Int positionB)
    {
        if (boardModel == null)
        {
            return;
        }

        int indexA = boardModel.Index(positionA.x, positionA.y);
        int indexB = boardModel.Index(positionB.x, positionB.y);
        if (indexA < 0 || indexB < 0)
        {
            return;
        }

        boardModel.SwapCells(indexA, indexB);
        MarkDirtyCell(indexA, includeNeighbours: true);
        MarkDirtyCell(indexB, includeNeighbours: true);
    }

    private static byte ToColorId(int blockType)
    {
        int clamped = Mathf.Clamp(blockType, 0, byte.MaxValue);
        return (byte)clamped;
    }
}
