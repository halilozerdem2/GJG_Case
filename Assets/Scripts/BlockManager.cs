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
    private static readonly ProfilerMarker GroupDetectionMarker = new ProfilerMarker("BlockManager.GroupDetection");
    private static readonly ProfilerMarker IconTierUpdateMarker = new ProfilerMarker("BlockManager.IconTierUpdate");
    private static readonly ProfilerMarker GravityCompactionMarker = new ProfilerMarker("BlockManager.GravityCompaction");
    private static readonly ProfilerMarker RefillMarker = new ProfilerMarker("BlockManager.Refill");
    private static readonly ProfilerMarker DeadlockCheckMarker = new ProfilerMarker("BlockManager.DeadlockCheck");

    private const int MaxColorIds = 256;
    private readonly List<BlockMove> blockMoves = new List<BlockMove>(64);
    private readonly List<int> pendingSpawnIndices = new List<int>(64);
    private readonly List<BlockAnimation> activeAnimations = new List<BlockAnimation>(128);
    private readonly List<Node> shuffleNodesBuffer = new List<Node>(64);
    private readonly List<Node>[] shuffleColorBuckets = new List<Node>[MaxColorIds];
    private readonly bool[] shuffleColorUsage = new bool[MaxColorIds];
    private readonly int[] usedColorIds = new int[MaxColorIds];
    private readonly int[] colorFirstIndex = new int[MaxColorIds];
    private readonly int[] colorSecondIndex = new int[MaxColorIds];
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
    private int usedColorCount;
    private bool requireFullRefresh;
    private bool isValidMoveExist;
    private Transform blastEffectRoot;
    private Transform blocksRoot;
    private int shuffleTweensPending;
    private bool shuffleResolutionPending;
    private Action<bool> shuffleCompletionCallback;
    private bool[] shuffleLockedFlags;

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
        EnsureBlocksRoot();
        gridManager?.UpdateFreeNodes();
        QueueAllNodesForSpawn();
    }

    private void Update()
    {
        UpdateBlockAnimations(Time.deltaTime);
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
        using (GravityCompactionMarker.Auto())
        {
            Node[,] nodeGrid = gridManager.NodeGrid;
            BoardSettings settings = Settings;
            if (nodeGrid == null || settings == null || boardModel == null)
            {
                return;
            }

            blockMoves.Clear();
            pendingSpawnIndices.Clear();

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

                    if (y != writeIndex)
                    {
                        int targetIndex = boardModel.Index(x, writeIndex);
                        blockMoves.Add(new BlockMove
                        {
                            fromIndex = fromIndex,
                            toIndex = targetIndex,
                            block = block
                        });

                        SetModelCell(targetIndex, block.blockType);
                        ClearModelCell(fromIndex);
                    }
                    else
                    {
                        SetModelCell(fromIndex, block.blockType);
                    }

                    writeIndex++;
                }

                for (int y = writeIndex; y < settings.Rows; y++)
                {
                    int emptyIndex = boardModel.Index(x, y);
                    if (emptyIndex >= 0)
                    {
                        ClearModelCell(emptyIndex);
                        pendingSpawnIndices.Add(emptyIndex);
                    }
                }
            }

            float dropDuration = Mathf.Max(0f, blockDropDuration);
            foreach (var move in blockMoves)
            {
                Node sourceNode = GetNodeFromIndex(move.fromIndex);
                Node targetNode = GetNodeFromIndex(move.toIndex);
                Block block = move.block;
                if (block == null || targetNode == null)
                {
                    continue;
                }

                if (sourceNode != null)
                {
                    sourceNode.OccupiedBlock = null;
                }

                block.SetBlock(targetNode, true);
                AnimateBlockToNode(block, targetNode, dropDuration, AnimationEase.OutBounce);
                targetNode.OccupiedBlock = block;
            }

            blockMoves.Clear();

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
                    powerShuffleTweens++;
                    AnimateBlockToNode(block, node, duration, AnimationEase.OutCubic, () =>
                    {
                        powerShuffleTweens = Mathf.Max(0, powerShuffleTweens - 1);
                        TryCompletePowerShuffle();
                    });
                    AnimateScaleDip(block, duration);
                }
                else
                {
                    AnimateBlockToNode(block, node, 0f, AnimationEase.Linear);
                    AnimateScaleDip(block, 0f);
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
        QueueAllNodesForSpawn();
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
        using (RefillMarker.Auto())
        {
            gridManager.UpdateFreeNodes();
            float dropDuration = Mathf.Max(0f, blockDropDuration);
            BoardSettings settings = Settings;
            if (settings == null)
            {
                onCompleted?.Invoke(false);
                yield break;
            }

            if (pendingSpawnIndices.Count == 0)
            {
                RefreshGroupVisuals();
                onCompleted?.Invoke(isValidMoveExist);
                yield break;
            }

            foreach (var index in pendingSpawnIndices)
            {
                Node node = GetNodeFromIndex(index);
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
                randomBlock.SetBlock(node, true);
                float dropOffset = GetSpawnOffsetForRow(node.gridPosition.y);
                Vector3 targetPosition = node.transform.localPosition;
                randomBlock.transform.localPosition = targetPosition + Vector3.up * dropOffset;
                gridManager.FreeNodes.Remove(node);

                AnimateBlockToNode(randomBlock, node, dropDuration, AnimationEase.OutBounce);
            }

            pendingSpawnIndices.Clear();
            gridManager.UpdateFreeNodes();
            RefreshGroupVisuals();
            onCompleted?.Invoke(isValidMoveExist);
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

        EnsureBlocksRoot();
        return blockPool.Spawn(blockType, blocksRoot != null ? blocksRoot : parent);
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

        using (IconTierUpdateMarker.Auto())
        {
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
        }

        isValidMoveExist = ModelHasValidMove();

        dirtyCount = 0;
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

    private void SnapBlockToNode(Block block, Node node)
    {
        if (block == null || node == null)
        {
            return;
        }

        StopAnimation(block.transform, AnimationType.Position);
        block.transform.localPosition = node.transform.localPosition;
    }

    private void AnimateBlockToNode(Block block, Node node, float duration, AnimationEase ease,
        Action onComplete = null)
    {
        if (block == null || node == null)
        {
            onComplete?.Invoke();
            return;
        }

        Vector3 targetPosition = node.transform.localPosition;
        if (duration <= 0f)
        {
            StopAnimation(block.transform, AnimationType.Position);
            block.transform.localPosition = targetPosition;
            onComplete?.Invoke();
            return;
        }

        StartBlockAnimation(block.transform, targetPosition, duration, ease, AnimationType.Position, onComplete);
    }

    private void AnimateScaleDip(Block block, float duration)
    {
        if (block == null)
        {
            return;
        }

        Vector3 baseScale = block.BaseLocalScale;
        StopAnimation(block.transform, AnimationType.Scale);
        ApplyAnimationValue(block.transform, AnimationType.Scale, baseScale);

        float clampedDuration = Mathf.Max(0f, duration);
        if (clampedDuration <= 0f)
        {
            return;
        }

        float dipRatio = Mathf.Clamp01(shuffleScaleDipRatio);
        float dipAmount = Mathf.Clamp(shuffleScaleDipAmount, 0.1f, 2f);
        if (Mathf.Approximately(dipAmount, 1f) || dipRatio <= 0f || dipRatio >= 1f)
        {
            StartBlockAnimation(block.transform, baseScale, clampedDuration, AnimationEase.OutBack, AnimationType.Scale, null);
            return;
        }

        float dipDuration = Mathf.Max(0.01f, clampedDuration * dipRatio);
        float recoverDuration = Mathf.Max(0.01f, clampedDuration - dipDuration);
        Vector3 dipScale = baseScale * dipAmount;

        StartBlockAnimation(block.transform, dipScale, dipDuration, AnimationEase.OutSine, AnimationType.Scale, () =>
        {
            StartBlockAnimation(block.transform, baseScale, recoverDuration, AnimationEase.OutBack, AnimationType.Scale, null);
        });
    }

    private void AnimateBlockScale(Block block, Vector3 targetScale, float duration, AnimationEase ease)
    {
        if (block == null)
        {
            return;
        }

        StartBlockAnimation(block.transform, targetScale, duration, ease, AnimationType.Scale, null);
    }

    private void AnimateShuffleBlock(Block block, Node node, float duration)
    {
        if (block == null || node == null)
        {
            return;
        }

        shuffleTweensPending++;
        AnimateBlockToNode(block, node, duration, AnimationEase.OutCubic, () =>
        {
            shuffleTweensPending = Mathf.Max(0, shuffleTweensPending - 1);
            TryResolveShuffleTweens();
        });
        AnimateScaleDip(block, duration);
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
            Node[,] nodeGrid = gridManager?.NodeGrid;
            BoardSettings settings = Settings;
            if (nodeGrid == null || settings == null || boardModel == null)
            {
                return false;
            }

            int totalNodes = settings.Columns * settings.Rows;
            if (totalNodes <= 0)
            {
                return false;
            }

            EnsureShuffleBuffers(totalNodes);
            ResetShuffleLocks(totalNodes);
            ResetColorBuckets();
            shuffleNodesBuffer.Clear();

            foreach (Node node in nodeGrid)
            {
                if (node?.OccupiedBlock == null)
                {
                    continue;
                }

                int colorId = ToColorId(node.OccupiedBlock.blockType);
                GetColorBucket(colorId).Add(node);
            }

            int colorWithPair = GetColorWithPair();
            if (colorWithPair == -1)
            {
                bool regenerated = RegenerateBoardWithGuaranteedPairs(nodeGrid, settings);
                if (regenerated)
                {
                    RequireFullBoardRefresh();
                }
                return regenerated;
            }

            Vector2Int firstPairA = new Vector2Int(0, 0);
            Vector2Int firstPairB = settings.Columns > 1 ? new Vector2Int(1, 0) : new Vector2Int(0, 1);
            CommitPair(colorWithPair, firstPairA, firstPairB);

            int secondColor = GetDifferentColorWithPair(colorWithPair);
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
                    CommitPair(secondColor, secondPairA, secondPairB);
                }
            }

            foreach (Node node in nodeGrid)
            {
                if (node?.OccupiedBlock == null || IsNodeLocked(node))
                {
                    continue;
                }

                shuffleNodesBuffer.Add(node);
            }

            for (int i = shuffleNodesBuffer.Count - 1; i > 0; i--)
            {
                int j = Random.Range(0, i + 1);
                SwapNodeBlock(shuffleNodesBuffer[i], shuffleNodesBuffer[j]);
            }

            if (!ModelHasValidMove())
            {
                bool guaranteed = TryGuaranteeMove(nodeGrid, settings);
                if (!guaranteed || !ModelHasValidMove())
                {
                    bool regenerated = RegenerateBoardWithGuaranteedPairs(nodeGrid, settings);
                    if (regenerated)
                    {
                        RequireFullBoardRefresh();
                    }
                    return regenerated;
                }
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
                        AnimateShuffleBlock(block, node, blockDropDuration);
                    }
                    else
                    {
                        AnimateBlockToNode(block, node, 0f, AnimationEase.Linear);
                        AnimateScaleDip(block, 0f);
                    }
                }
            }

            RequireFullBoardRefresh();
            return true;
        }
    }

    private int GetColorWithPair()
    {
        for (int i = 0; i < usedColorCount; i++)
        {
            int colorId = usedColorIds[i];
            List<Node> nodes = shuffleColorBuckets[colorId];
            if (nodes != null && nodes.Count >= 2)
            {
                return colorId;
            }
        }

        return -1;
    }

    private int GetDifferentColorWithPair(int excludedColor)
    {
        for (int i = 0; i < usedColorCount; i++)
        {
            int colorId = usedColorIds[i];
            if (colorId == excludedColor)
            {
                continue;
            }

            List<Node> nodes = shuffleColorBuckets[colorId];
            if (nodes != null && nodes.Count >= 2)
            {
                return colorId;
            }
        }

        return -1;
    }

    private void CommitPair(int color, Vector2Int posA, Vector2Int posB)
    {
        if (!gridManager.TryGetNode(posA, out Node nodeA) || !gridManager.TryGetNode(posB, out Node nodeB))
        {
            return;
        }

        List<Node> nodesOfColor = color >= 0 && color < shuffleColorBuckets.Length
            ? shuffleColorBuckets[color]
            : null;
        if (nodesOfColor == null || nodesOfColor.Count < 2)
        {
            return;
        }

        EnsureColorAtPositions(nodesOfColor, nodeA, nodeB);
        LockNode(nodeA);
        LockNode(nodeB);
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

    private void EnsureShuffleBuffers(int totalNodes)
    {
        if (shuffleLockedFlags == null || shuffleLockedFlags.Length < totalNodes)
        {
            shuffleLockedFlags = new bool[totalNodes];
        }

        if (shuffleNodesBuffer.Capacity < totalNodes)
        {
            shuffleNodesBuffer.Capacity = totalNodes;
        }
    }

    private void ResetShuffleLocks(int totalNodes)
    {
        if (shuffleLockedFlags == null)
        {
            return;
        }

        int length = Mathf.Min(totalNodes, shuffleLockedFlags.Length);
        Array.Clear(shuffleLockedFlags, 0, length);
    }

    private void ResetColorBuckets()
    {
        for (int i = 0; i < usedColorCount; i++)
        {
            int colorId = usedColorIds[i];
            shuffleColorBuckets[colorId]?.Clear();
            shuffleColorUsage[colorId] = false;
        }

        usedColorCount = 0;
    }

    private List<Node> GetColorBucket(int colorId)
    {
        colorId = Mathf.Clamp(colorId, 0, shuffleColorBuckets.Length - 1);
        List<Node> bucket = shuffleColorBuckets[colorId];
        if (bucket == null)
        {
            bucket = new List<Node>(8);
            shuffleColorBuckets[colorId] = bucket;
        }

        if (!shuffleColorUsage[colorId])
        {
            bucket.Clear();
            shuffleColorUsage[colorId] = true;
            if (usedColorCount < usedColorIds.Length)
            {
                usedColorIds[usedColorCount++] = colorId;
            }
        }

        return bucket;
    }

    private void LockNode(Node node)
    {
        if (node == null || boardModel == null || shuffleLockedFlags == null)
        {
            return;
        }

        int index = boardModel.Index(node.gridPosition.x, node.gridPosition.y);
        if (index >= 0 && index < shuffleLockedFlags.Length)
        {
            shuffleLockedFlags[index] = true;
        }
    }

    private bool IsNodeLocked(Node node)
    {
        if (node == null || boardModel == null || shuffleLockedFlags == null)
        {
            return false;
        }

        int index = boardModel.Index(node.gridPosition.x, node.gridPosition.y);
        return index >= 0 && index < shuffleLockedFlags.Length && shuffleLockedFlags[index];
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

        pendingSpawnIndices.Clear();

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
        SnapBlockToNode(spawned, targetNode);
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
        using (GroupDetectionMarker.Auto())
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
            QueueAllNodesForSpawn();
            return;
        }

        boardModel.Configure(settings.Columns, settings.Rows);
        EnsureGroupBuffers();
        RequireFullBoardRefresh();
        QueueAllNodesForSpawn();
        EnsureBlocksRoot();
    }

    private void EnsureBlocksRoot()
    {
        Transform desiredParent = gridManager != null ? gridManager.GridRoot : transform;
        if (blocksRoot == null)
        {
            GameObject container = new GameObject("BlocksRoot");
            blocksRoot = container.transform;
        }

        if (blocksRoot.parent != desiredParent)
        {
            blocksRoot.SetParent(desiredParent, false);
        }
    }

    private void QueueAllNodesForSpawn()
    {
        pendingSpawnIndices.Clear();
        if (boardModel == null || gridManager == null)
        {
            return;
        }

        int columns = boardModel.Columns;
        int rows = boardModel.Rows;
        if (columns <= 0 || rows <= 0)
        {
            return;
        }

        for (int x = 0; x < columns; x++)
        {
            for (int y = 0; y < rows; y++)
            {
                int index = boardModel.Index(x, y);
                if (index >= 0)
                {
                    pendingSpawnIndices.Add(index);
                }
            }
        }
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

    private void StartBlockAnimation(Transform target, Vector3 endValue, float duration, AnimationEase ease,
        AnimationType type, Action onComplete)
    {
        if (target == null)
        {
            onComplete?.Invoke();
            return;
        }

        float clampedDuration = Mathf.Max(0f, duration);
        if (clampedDuration <= 0f)
        {
            ApplyAnimationValue(target, type, endValue);
            onComplete?.Invoke();
            return;
        }

        for (int i = 0; i < activeAnimations.Count; i++)
        {
            if (activeAnimations[i].target == target && activeAnimations[i].type == type)
            {
                activeAnimations[i] = new BlockAnimation
                {
                    target = target,
                    start = GetCurrentValue(target, type),
                    end = endValue,
                    duration = clampedDuration,
                    elapsed = 0f,
                    ease = ease,
                    type = type,
                    onComplete = onComplete
                };
                return;
            }
        }

        activeAnimations.Add(new BlockAnimation
        {
            target = target,
            start = GetCurrentValue(target, type),
            end = endValue,
            duration = clampedDuration,
            elapsed = 0f,
            ease = ease,
            type = type,
            onComplete = onComplete
        });
    }

    private Vector3 GetCurrentValue(Transform target, AnimationType type)
    {
        return type == AnimationType.Scale ? target.localScale : target.localPosition;
    }

    private void ApplyAnimationValue(Transform target, AnimationType type, Vector3 value)
    {
        if (type == AnimationType.Scale)
        {
            target.localScale = value;
        }
        else
        {
            target.localPosition = value;
        }
    }

    private void StopAnimation(Transform target, AnimationType type)
    {
        if (target == null)
        {
            return;
        }

        for (int i = activeAnimations.Count - 1; i >= 0; i--)
        {
            if (activeAnimations[i].target == target && activeAnimations[i].type == type)
            {
                activeAnimations.RemoveAt(i);
            }
        }
    }

    private void UpdateBlockAnimations(float deltaTime)
    {
        if (activeAnimations.Count == 0)
        {
            return;
        }

        for (int i = activeAnimations.Count - 1; i >= 0; i--)
        {
            BlockAnimation anim = activeAnimations[i];
            Transform target = anim.target;
            if (target == null)
            {
                activeAnimations.RemoveAt(i);
                continue;
            }

            anim.elapsed += deltaTime;
            float t = anim.duration <= 0f ? 1f : Mathf.Clamp01(anim.elapsed / anim.duration);
            float easedT = EvaluateEase(anim.ease, t);
            Vector3 value = Vector3.LerpUnclamped(anim.start, anim.end, easedT);
            ApplyAnimationValue(target, anim.type, value);

            if (anim.elapsed >= anim.duration)
            {
                // Remove completed animation before invoking callbacks so follow-up animations can register cleanly.
                activeAnimations.RemoveAt(i);
                anim.onComplete?.Invoke();
            }
            else
            {
                activeAnimations[i] = anim;
            }
        }
    }

    private static float EvaluateEase(AnimationEase ease, float t)
    {
        switch (ease)
        {
            case AnimationEase.OutCubic:
                return 1f - Mathf.Pow(1f - t, 3f);
            case AnimationEase.OutBounce:
                const float n1 = 7.5625f;
                const float d1 = 2.75f;
                if (t < 1f / d1)
                {
                    return n1 * t * t;
                }
                if (t < 2f / d1)
                {
                    t -= 1.5f / d1;
                    return n1 * t * t + 0.75f;
                }
                if (t < 2.5f / d1)
                {
                    t -= 2.25f / d1;
                    return n1 * t * t + 0.9375f;
                }
                t -= 2.625f / d1;
                return n1 * t * t + 0.984375f;
            case AnimationEase.OutBack:
                const float c1 = 1.70158f;
                const float c3 = c1 + 1f;
                return 1f + c3 * Mathf.Pow(t - 1f, 3f) + c1 * Mathf.Pow(t - 1f, 2f);
            case AnimationEase.OutSine:
                return Mathf.Sin((t * Mathf.PI) / 2f);
            default:
                return t;
        }
    }

    private Node GetNodeFromIndex(int index)
    {
        if (boardModel == null || gridManager == null)
        {
            return null;
        }

        int columns = boardModel.Columns;
        if (columns <= 0 || index < 0)
        {
            return null;
        }

        int x = boardModel.X(index);
        int y = boardModel.Y(index);
        if (x < 0 || y < 0)
        {
            return null;
        }

        Node[,] grid = gridManager.NodeGrid;
        if (grid == null || x >= grid.GetLength(0) || y >= grid.GetLength(1))
        {
            return null;
        }

        return grid[x, y];
    }

    private bool ModelHasValidMove()
    {
        using (DeadlockCheckMarker.Auto())
        {
            if (boardModel == null)
            {
                return false;
            }

            int columns = boardModel.Columns;
            int rows = boardModel.Rows;
            if (columns <= 0 || rows <= 0)
            {
                return false;
            }

            for (int y = 0; y < rows; y++)
            {
                for (int x = 0; x < columns; x++)
                {
                    int index = boardModel.Index(x, y);
                    if (index < 0)
                    {
                        continue;
                    }

                    Cell cell = boardModel.GetCell(index);
                    if (!cell.occupied)
                    {
                        continue;
                    }

                    byte color = cell.colorId;
                    int rightIndex = boardModel.Index(x + 1, y);
                    if (rightIndex >= 0)
                    {
                        Cell rightCell = boardModel.GetCell(rightIndex);
                        if (rightCell.occupied && rightCell.colorId == color)
                        {
                            return true;
                        }
                    }

                    int upIndex = boardModel.Index(x, y + 1);
                    if (upIndex >= 0)
                    {
                        Cell upCell = boardModel.GetCell(upIndex);
                        if (upCell.occupied && upCell.colorId == color)
                        {
                            return true;
                        }
                    }
                }
            }

            return false;
        }
    }

    private int FindNeighbourIndex(int index)
    {
        if (boardModel == null)
        {
            return -1;
        }

        int x = boardModel.X(index);
        int y = boardModel.Y(index);
        if (x < 0 || y < 0)
        {
            return -1;
        }

        int idx = boardModel.Index(x + 1, y);
        if (idx >= 0)
        {
            return idx;
        }

        idx = boardModel.Index(x - 1, y);
        if (idx >= 0)
        {
            return idx;
        }

        idx = boardModel.Index(x, y + 1);
        if (idx >= 0)
        {
            return idx;
        }

        idx = boardModel.Index(x, y - 1);
        if (idx >= 0)
        {
            return idx;
        }

        return -1;
    }

    private enum AnimationType
    {
        Position,
        Scale
    }

    private enum AnimationEase
    {
        Linear,
        OutCubic,
        OutBounce,
        OutBack,
        OutSine
    }

    private struct BlockMove
    {
        public int fromIndex;
        public int toIndex;
        public Block block;
    }

    private struct BlockAnimation
    {
        public Transform target;
        public Vector3 start;
        public Vector3 end;
        public float duration;
        public float elapsed;
        public AnimationEase ease;
        public AnimationType type;
        public Action onComplete;
    }

    private bool TryGuaranteeMove(Node[,] nodeGrid, BoardSettings settings)
    {
        if (boardModel == null || nodeGrid == null || settings == null)
        {
            return false;
        }

        Array.Fill(colorFirstIndex, -1);
        Array.Fill(colorSecondIndex, -1);

        int cellCount = boardModel.CellCount;
        for (int i = 0; i < cellCount; i++)
        {
            Cell cell = boardModel.GetCell(i);
            if (!cell.occupied)
            {
                continue;
            }

            int color = cell.colorId;
            if (color < 0 || color >= MaxColorIds)
            {
                continue;
            }

            if (colorFirstIndex[color] == -1)
            {
                colorFirstIndex[color] = i;
            }
            else if (colorSecondIndex[color] == -1)
            {
                colorSecondIndex[color] = i;
            }
        }

        int chosenColor = -1;
        for (int c = 0; c < MaxColorIds; c++)
        {
            if (colorFirstIndex[c] != -1 && colorSecondIndex[c] != -1)
            {
                chosenColor = c;
                break;
            }
        }

        if (chosenColor == -1)
        {
            return false;
        }

        int anchorIndex = colorFirstIndex[chosenColor];
        int movingIndex = colorSecondIndex[chosenColor];
        int targetNeighbour = FindNeighbourIndex(anchorIndex);

        if (targetNeighbour < 0)
        {
            anchorIndex = movingIndex;
            movingIndex = colorFirstIndex[chosenColor];
            targetNeighbour = FindNeighbourIndex(anchorIndex);
        }

        if (targetNeighbour < 0 || movingIndex == targetNeighbour)
        {
            return false;
        }

        Node movingNode = GetNodeFromIndex(movingIndex);
        Node targetNode = GetNodeFromIndex(targetNeighbour);
        if (movingNode == null || targetNode == null)
        {
            return false;
        }

        SwapNodeBlock(movingNode, targetNode);
        return true;
    }
}
