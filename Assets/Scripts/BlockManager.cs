using System;
using System.Collections;
using System.Collections.Generic;
using DG.Tweening;
using Unity.Profiling;
using UnityEngine;
using Random = UnityEngine.Random;

public class BlockManager : MonoBehaviour
{
    public event Action<StaticBlock> StaticBlockSpawned;
    public event Action<int, Vector3> StaticBlockCollected;
    public event Action<int, int, int> StaticTargetProgressChanged;

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
    [SerializeField] private List<SpecialActivationEffectEntry> specialActivationEffects = new List<SpecialActivationEffectEntry>();

    private static readonly ProfilerMarker FallingMarker = new ProfilerMarker("BlockManager.ResolveFalling");
    private static readonly ProfilerMarker SpawnBlocksMarker = new ProfilerMarker("BlockManager.SpawnBlocks");
    private static readonly ProfilerMarker ShuffleMarker = new ProfilerMarker("BlockManager.TryShuffleBoard");
    private static readonly ProfilerMarker GroupDetectionMarker = new ProfilerMarker("BlockManager.GroupDetection");
    private static readonly ProfilerMarker IconTierUpdateMarker = new ProfilerMarker("BlockManager.IconTierUpdate");
    private static readonly ProfilerMarker GravityCompactionMarker = new ProfilerMarker("BlockManager.GravityCompaction");
    private static readonly ProfilerMarker RefillMarker = new ProfilerMarker("BlockManager.Refill");
    private static readonly ProfilerMarker DeadlockCheckMarker = new ProfilerMarker("BlockManager.DeadlockCheck");

    private const int MaxColorIds = 256;
    private const float SpecialSpawnScaleDuration = 0.25f;
    private const float SpecialPulseDuration = 0.65f;
    private const float SpecialPulseMultiplier = 1.2f;
    private readonly List<BlockMove> blockMoves = new List<BlockMove>(64);
    private readonly List<int> pendingSpawnIndices = new List<int>(64);
    private readonly List<BlockAnimation> activeAnimations = new List<BlockAnimation>(128);
    private readonly List<Block> poolPrefabsBuffer = new List<Block>(32);
    private readonly List<Node> shuffleNodesBuffer = new List<Node>(64);
    private readonly List<Node> staticPlacementSlots = new List<Node>(64);
    private readonly List<Node>[] shuffleColorBuckets = new List<Node>[MaxColorIds];
    private readonly bool[] shuffleColorUsage = new bool[MaxColorIds];
    private readonly int[] usedColorIds = new int[MaxColorIds];
    private readonly int[] colorFirstIndex = new int[MaxColorIds];
    private readonly int[] colorSecondIndex = new int[MaxColorIds];
    private readonly Queue<ChainClearGroup> pendingChainGroups = new Queue<ChainClearGroup>(8);
    private readonly HashSet<int> processedClearIndices = new HashSet<int>();
    private readonly Stack<int[]> chainBufferPool = new Stack<int[]>();
    private readonly List<SpecialClearEntry> pendingSpecialClears = new List<SpecialClearEntry>(8);
    private readonly HashSet<int> staticTargetIndices = new HashSet<int>();
    private readonly HashSet<int> pendingStaticRemovalIndices = new HashSet<int>();
    private readonly HashSet<int> staticPlacementIndexLookup = new HashSet<int>();
    private readonly Dictionary<int, StaticTargetInfo> staticTargetInfos = new Dictionary<int, StaticTargetInfo>();
    private readonly List<int> staticTargetKeyBuffer = new List<int>(8);
    private readonly Dictionary<Block.BlockArchetype, Queue<ParticleSystem>> specialEffectPools = new Dictionary<Block.BlockArchetype, Queue<ParticleSystem>>();
    private readonly Dictionary<Block.BlockArchetype, ParticleSystem> specialEffectPrefabs = new Dictionary<Block.BlockArchetype, ParticleSystem>();
    private readonly Dictionary<int, ParticleSystem> staticBlastPrefabs = new Dictionary<int, ParticleSystem>();
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
    private bool specialEffectLookupInitialized;
    private bool staticTargetsSpawned;
    private int totalStaticTargetCount;

    private BoardSettings Settings => boardSettings != null ? boardSettings : gridManager?.BoardSettings;
    private AudioManager Audio => audioManager != null ? audioManager : AudioManager.Instance;

    public bool HasValidMove => isValidMoveExist;
    public BoardModel BoardModel => boardModel;
    public int TotalStaticTargetCount => totalStaticTargetCount;
    public int RemainingStaticTargetCount => staticTargetIndices.Count;
    public bool TryGetStaticTargetProgress(int blockType, out int collected, out int total)
    {
        if (staticTargetInfos.TryGetValue(blockType, out StaticTargetInfo info))
        {
            total = info.Total;
            collected = info.Total - info.Remaining;
            return true;
        }

        collected = 0;
        total = 0;
        return false;
    }

    public void SetBoardSettings(BoardSettings settings)
    {
        boardSettings = settings;
    }

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
        specialEffectLookupInitialized = false;
        staticTargetIndices.Clear();
        pendingStaticRemovalIndices.Clear();
        staticTargetsSpawned = false;
        totalStaticTargetCount = 0;
        ResetStaticTargetData();
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

        if (block.IsSpecialVariant)
        {
            bool activated = TryExecuteSpecialBlock(block);
            if (activated)
            {
                return true;
            }

            PlayInvalidGroupFeedback(1);
            Audio?.PlayInvalidSelection();
            return false;
        }

        if (!block.CanParticipateInGroup)
        {
            PlayInvalidGroupFeedback(1);
            Audio?.PlayInvalidSelection();
            return false;
        }

        Node[,] nodeGrid = gridManager.NodeGrid;
        Node originNode = block.node;
        if (nodeGrid == null || originNode == null)
        {
            return false;
        }

        Vector2Int position = originNode.gridPosition;
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

        GroupContext groupContext = new GroupContext(block, groupIndicesBuffer, groupCount, block.blockType);
        Audio?.PlayBlockSfx(block.blockType);

        return ExecuteBlast(groupContext, originNode, true, false, true);
    }

    private bool TryExecuteSpecialBlock(Block block)
    {
        if (!TryBuildSearchData(block, out BlockSearchData searchData))
        {
            return false;
        }

        int count = block.GatherSearchResults(searchData);
        if (count <= 0)
        {
            return false;
        }

        GroupContext context = new GroupContext(block, groupIndicesBuffer, count, block.blockType);
        return ExecuteBlast(context, null, false, true, false);
    }

    private bool ExecuteBlast(in GroupContext context, Node originNode, bool allowSpecialSpawn, bool activateSpecial, bool allowStaticAdjacency)
    {
        if (!context.IsValid || gridManager == null)
        {
            return false;
        }

        Node[,] nodeGrid = gridManager.NodeGrid;
        if (nodeGrid == null)
        {
            return false;
        }

        Block origin = context.Origin;
        if (activateSpecial)
        {
            origin?.ActivateSpecialEffect(context);
        }

        origin?.HandleBlastResult(context);
        bool cleared = ClearBlocksForContext(context, nodeGrid, allowStaticAdjacency);

        if (cleared && allowSpecialSpawn)
        {
            TrySpawnSpecialBlock(context, originNode);
        }

        return cleared;
    }

    private bool ClearBlocksForContext(GroupContext context, Node[,] nodeGrid, bool allowStaticAdjacency)
    {
        if (boardModel == null || nodeGrid == null || context.Indices == null)
        {
            return false;
        }

        int columns = boardModel.Columns;
        int rows = boardModel.Rows;
        if (columns <= 0 || rows <= 0)
        {
            return false;
        }

        bool clearedAny = false;
        processedClearIndices.Clear();
        pendingChainGroups.Clear();

        int limit = Mathf.Min(context.GroupSize, context.Indices.Length);
        EnqueueChainGroup(context.Indices, limit, allowStaticAdjacency);

        while (pendingChainGroups.Count > 0)
        {
            ChainClearGroup group = pendingChainGroups.Dequeue();
            int[] indices = group.Indices;
            int groupCount = group.Count;
            pendingSpecialClears.Clear();
            if (indices == null || groupCount <= 0)
            {
                ReleaseChainBuffer(indices);
                continue;
            }

            // Phase 1: determine subsequent group areas before clearing current one.
            for (int i = 0; i < groupCount; i++)
            {
                int memberIndex = indices[i];
                int x = columns > 0 ? memberIndex % columns : boardModel.X(memberIndex);
                int y = columns > 0 ? memberIndex / columns : boardModel.Y(memberIndex);
                if (x < 0 || x >= columns || y < 0 || y >= rows)
                {
                    continue;
                }

                Node targetNode = nodeGrid[x, y];
                Block member = targetNode != null ? targetNode.OccupiedBlock : null;
                if (member == null || !member.IsSpecialVariant || member.Archetype == Block.BlockArchetype.Static)
                {
                    continue;
                }

                if (!TryBuildSearchData(member, out BlockSearchData specialSearch))
                {
                    continue;
                }

                int specialCount = member.GatherSearchResults(specialSearch);
                if (specialCount <= 0)
                {
                    continue;
                }

                TriggerSpecialActivationFeedback(member);
                EnqueueChainGroup(specialSearch.ResultBuffer, specialCount, false);
            }

            // Phase 2: clear current group's regular blocks first.
            for (int i = 0; i < groupCount; i++)
            {
                int memberIndex = indices[i];
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
                    if (member.IsSpecialVariant)
                    {
                        pendingSpecialClears.Add(new SpecialClearEntry
                        {
                            Block = member,
                            Node = targetNode,
                            ModelIndex = memberIndex
                        });
                        continue;
                    }

                    targetNode.OccupiedBlock = null;
                    if (gridManager != null)
                    {
                        gridManager.FreeNodes.Add(targetNode);
                    }
                    PlayBlockBlastEffect(member);
                    ReleaseBlock(member);
                    clearedAny = true;
                }
                else if (gridManager != null)
                {
                    gridManager.FreeNodes.Add(targetNode);
                }

                ClearModelCell(memberIndex);
            }

            // Phase 3: clear pending special blocks after regulars are gone.
            for (int i = 0; i < pendingSpecialClears.Count; i++)
            {
                SpecialClearEntry entry = pendingSpecialClears[i];
                Node targetNode = entry.Node;
                Block special = entry.Block;

                if (special is StaticBlock)
                {
                    RemoveStaticBlockAt(entry.ModelIndex);
                    continue;
                }

                if (targetNode != null && targetNode.OccupiedBlock == special)
                {
                    targetNode.OccupiedBlock = null;
                    if (gridManager != null)
                    {
                        gridManager.FreeNodes.Add(targetNode);
                    }
                }

                if (special != null)
                {
                    PlayBlockBlastEffect(special);
                    ReleaseBlock(special);
                    clearedAny = true;
                }

                ClearModelCell(entry.ModelIndex);
            }

            if (group.AllowStaticAdjacent)
            {
                CollectAdjacentStaticBlocks(indices, groupCount);
            }
            ReleaseChainBuffer(indices);
        }

        processedClearIndices.Clear();
        pendingChainGroups.Clear();
        return clearedAny;
    }

    private void TrySpawnSpecialBlock(GroupContext context, Node originNode)
    {
        if (originNode == null || originNode.OccupiedBlock != null)
        {
            return;
        }

        if (!TryResolveSpecialBlockSpawn(context, out Block.BlockArchetype archetype, out int blockType))
        {
            return;
        }

        Block spawned = SpawnBlockOfType(blockType, originNode, archetype);
        if (spawned == null)
        {
            Debug.LogWarning($"Failed to spawn special block {archetype} for block type {blockType}. Ensure prefab is registered in GameModeConfig.");
            return;
        }

        if (spawned is ColorClearBlock colorClear)
        {
            colorClear.ConfigureTargetColor(context.BlockType);
        }
    }

    private bool TryBuildSearchData(Block block, out BlockSearchData searchData)
    {
        searchData = default;
        if (block == null || boardModel == null || gridManager == null)
        {
            return false;
        }

        Node[,] nodeGrid = gridManager.NodeGrid;
        Node blockNode = block.node;
        if (nodeGrid == null || blockNode == null)
        {
            return false;
        }

        Vector2Int gridPos = blockNode.gridPosition;
        int startIndex = boardModel.Index(gridPos.x, gridPos.y);
        if (startIndex < 0)
        {
            return false;
        }

        EnsureGroupBuffers();
        int stamp = AcquireVisitStamp();
        searchData = new BlockSearchData(boardModel, nodeGrid, startIndex, gridPos.x, gridPos.y,
            groupIndicesBuffer, bfsQueue, visitedStamps, stamp);
        return true;
    }

    private bool TryResolveSpecialBlockSpawn(GroupContext context, out Block.BlockArchetype archetype, out int blockType)
    {
        archetype = Block.BlockArchetype.Regular;
        blockType = context.BlockType;

        if (!context.IsValid)
        {
            return false;
        }

        Block originBlock = context.Origin;
        if (originBlock == null || originBlock.IsSpecialVariant)
        {
            return false;
        }

        GameManager manager = GameManager.Instance;
        if (manager == null || !manager.IsGameMode)
        {
            return false;
        }

        var thresholds = manager.ActiveSpecialBlockThresholds;
        if (thresholds == null || thresholds.Count == 0)
        {
            return false;
        }

        BoardSettings settings = Settings;
        if (settings == null)
        {
            return false;
        }

        GameModeConfig.SpecialBlockThreshold selected = default;
        int selectedSize = 0;
        bool found = false;

        for (int i = 0; i < thresholds.Count; i++)
        {
            GameModeConfig.SpecialBlockThreshold entry = thresholds[i];
            int required = entry.ResolveMinimumGroupSize(settings);
            if (required <= 0)
            {
                continue;
            }

            if (context.GroupSize >= required && required >= selectedSize)
            {
                selected = entry;
                selectedSize = required;
                found = true;
            }
        }

        if (!found)
        {
            return false;
        }

        archetype = selected.SpawnArchetype;
        blockType = selected.ResolveSpawnBlockType(context.BlockType);
        return true;
    }

    private void EnqueueChainGroup(int[] source, int count, bool allowStaticAdjacent)
    {
        if (source == null || count <= 0 || boardModel == null)
        {
            return;
        }

        int[] buffer = AcquireChainBuffer(count);
        int accepted = 0;
        for (int i = 0; i < count; i++)
        {
            int index = source[i];
            if (!boardModel.IsValidIndex(index))
            {
                continue;
            }

            if (!processedClearIndices.Add(index))
            {
                continue;
            }

            buffer[accepted++] = index;
        }

        if (accepted > 0)
        {
            pendingChainGroups.Enqueue(new ChainClearGroup
            {
                Indices = buffer,
                Count = accepted,
                AllowStaticAdjacent = allowStaticAdjacent
            });
        }
        else
        {
            ReleaseChainBuffer(buffer);
        }
    }

    private int[] AcquireChainBuffer(int minSize)
    {
        int required = Mathf.Max(1, minSize);
        if (chainBufferPool.Count > 0)
        {
            int[] buffer = chainBufferPool.Pop();
            if (buffer.Length < required)
            {
                Array.Resize(ref buffer, required);
            }
            return buffer;
        }

        return new int[required];
    }

    private void ReleaseChainBuffer(int[] buffer)
    {
        if (buffer == null)
        {
            return;
        }

        chainBufferPool.Push(buffer);
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
                int segmentStart = 0;
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

                    if (block is StaticBlock)
                    {
                        FinalizeColumnSegment(x, segmentStart, writeIndex, y);
                        segmentStart = y + 1;
                        writeIndex = segmentStart;
                        SetModelCell(fromIndex, block.blockType);
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

                FinalizeColumnSegment(x, segmentStart, writeIndex, settings.Rows);
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
                if (occupiedBlock is StaticBlock)
                {
                    SetModelCell(node.gridPosition, occupiedBlock.blockType);
                    continue;
                }

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

                if (node.OccupiedBlock != null)
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

            if (block is StaticBlock)
            {
                if (TryRemoveStaticBlock(node))
                {
                    anyDestroyed = true;
                }
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

            if (block is StaticBlock)
            {
                if (TryRemoveStaticBlock(node))
                {
                    destroyedAny = true;
                }
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

            SpawnStaticTargetsIfNeeded();

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
        Block[] pooledPrefabs = BuildPoolPrefabArray(settings);
        blockPool.Initialize(pooledPrefabs, totalCells, 1.2f);
        blockPool.InitializeEffects(settings.BlockPrefabs, settings.BlastEffectPrefabs);
        BuildStaticBlastLookup();
    }

    private void BuildStaticBlastLookup()
    {
        staticBlastPrefabs.Clear();
        GameManager manager = GameManager.Instance;
        var config = manager != null ? manager.ActiveGameModeConfig : null;
        if (config == null)
        {
            return;
        }

        var staticSpawns = config.StaticTargetSpawns;
        for (int i = 0; i < staticSpawns.Count; i++)
        {
            StaticBlock prefab = staticSpawns[i].TargetPrefab as StaticBlock;
            if (prefab == null)
            {
                continue;
            }

            ParticleSystem fx = prefab.StaticBlastEffect;
            if (fx == null || staticBlastPrefabs.ContainsKey(prefab.blockType))
            {
                continue;
            }

            staticBlastPrefabs[prefab.blockType] = fx;
        }
    }

    private Block[] BuildPoolPrefabArray(BoardSettings settings)
    {
        poolPrefabsBuffer.Clear();

        if (settings != null && settings.BlockPrefabs != null)
        {
            for (int i = 0; i < settings.BlockPrefabs.Length; i++)
            {
                Block prefab = settings.BlockPrefabs[i];
                if (prefab != null)
                {
                    poolPrefabsBuffer.Add(prefab);
                }
            }
        }

        GameModeConfig config = GameManager.Instance != null ? GameManager.Instance.ActiveGameModeConfig : null;
        if (config != null)
        {
            var specialPrefabs = config.SpecialBlockPrefabs;
            for (int i = 0; i < specialPrefabs.Count; i++)
            {
                Block extraPrefab = specialPrefabs[i].Prefab;
                if (extraPrefab != null)
                {
                    poolPrefabsBuffer.Add(extraPrefab);
                }
            }

            var staticPrefabs = config.StaticTargetSpawns;
            for (int i = 0; i < staticPrefabs.Count; i++)
            {
                Block targetPrefab = staticPrefabs[i].TargetPrefab;
                if (targetPrefab != null)
                {
                    poolPrefabsBuffer.Add(targetPrefab);
                }
            }
        }

        if (poolPrefabsBuffer.Count == 0)
        {
            return Array.Empty<Block>();
        }

        return poolPrefabsBuffer.ToArray();
    }

    private Block SpawnBlockFromPool(int blockType, Transform parent, Block.BlockArchetype archetype = Block.BlockArchetype.Regular)
    {
        if (blockPool == null)
        {
            Debug.LogError("Block pool not assigned; cannot spawn blocks.");
            return null;
        }

        EnsureBlocksRoot();
        return blockPool.Spawn(blockType, archetype, blocksRoot != null ? blocksRoot : parent);
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
                    if (cachedGroupSizes != null)
                    {
                        cachedGroupSizes[index] = 0;
                        cachedGroupStamps[index] = groupEvaluationStamp;
                    }
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

    private void PlayStaticBlockEffect(StaticBlock block)
    {
        if (block == null)
        {
            return;
        }

        if (staticBlastPrefabs.TryGetValue(block.blockType, out ParticleSystem prefab) && prefab != null)
        {
            ParticleSystem instance = Instantiate(prefab, GetBlastEffectRoot());
            Transform t = instance.transform;
            t.position = block.transform.position;
            t.rotation = Quaternion.identity;
            instance.Play(true);
            StartCoroutine(ReturnStaticEffect(instance));
        }
        else
        {
            PlayBlockBlastEffect(block);
        }
    }

    private IEnumerator ReturnStaticEffect(ParticleSystem effect)
    {
        if (effect == null)
        {
            yield break;
        }

        while (effect.IsAlive(true))
        {
            yield return null;
        }

        Destroy(effect.gameObject);
    }

    private void RegisterStaticTarget(int blockType)
    {
        if (!staticTargetInfos.TryGetValue(blockType, out StaticTargetInfo info))
        {
            info = new StaticTargetInfo();
        }

        info.Total++;
        info.Remaining++;
        staticTargetInfos[blockType] = info;
        NotifyStaticTargetProgress(blockType, info);
    }

    private void UnregisterStaticTarget(int blockType)
    {
        if (!staticTargetInfos.TryGetValue(blockType, out StaticTargetInfo info))
        {
            return;
        }

        info.Remaining = Mathf.Max(0, info.Remaining - 1);
        staticTargetInfos[blockType] = info;
        NotifyStaticTargetProgress(blockType, info);
    }

    private void NotifyStaticTargetProgress(int blockType, StaticTargetInfo info)
    {
        StaticTargetProgressChanged?.Invoke(blockType, info.Total - info.Remaining, info.Total);
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

    private void TriggerSpecialActivationFeedback(Block block)
    {
        if (block == null || !block.IsSpecialVariant || block.Archetype == Block.BlockArchetype.Static)
        {
            return;
        }

        Audio?.PlayBlockSfx(block.blockType);
        PlaySpecialActivationEffect(block);
    }

    private void PlaySpecialActivationEffect(Block block)
    {
        if (block == null)
        {
            return;
        }

        EnsureSpecialEffectLookup();
        if (!specialEffectPrefabs.TryGetValue(block.Archetype, out ParticleSystem prefab) || prefab == null)
        {
            return;
        }

        ParticleSystem effect = AcquireSpecialEffect(block.Archetype, prefab);
        if (effect == null)
        {
            return;
        }

        Transform effectRoot = GetBlastEffectRoot();
        Transform effectTransform = effect.transform;
        effectTransform.SetParent(effectRoot, false);
        effectTransform.position = block.transform.position;
        effectTransform.rotation = Quaternion.identity;
        effect.gameObject.SetActive(true);
        effect.Play(true);
        StartCoroutine(ReturnSpecialEffect(block.Archetype, effect));
    }

    private void EnsureSpecialEffectLookup()
    {
        if (specialEffectLookupInitialized)
        {
            return;
        }

        specialEffectPrefabs.Clear();
        if (specialActivationEffects != null)
        {
            for (int i = 0; i < specialActivationEffects.Count; i++)
            {
                SpecialActivationEffectEntry entry = specialActivationEffects[i];
                if (entry.effectPrefab == null)
                {
                    continue;
                }

                if (specialEffectPrefabs.ContainsKey(entry.archetype))
                {
                    continue;
                }

                specialEffectPrefabs[entry.archetype] = entry.effectPrefab;
            }
        }

        specialEffectLookupInitialized = true;
    }

    private ParticleSystem AcquireSpecialEffect(Block.BlockArchetype archetype, ParticleSystem prefab)
    {
        if (prefab == null)
        {
            return null;
        }

        if (!specialEffectPools.TryGetValue(archetype, out Queue<ParticleSystem> pool))
        {
            pool = new Queue<ParticleSystem>();
            specialEffectPools[archetype] = pool;
        }

        ParticleSystem effect = pool.Count > 0 ? pool.Dequeue() : Instantiate(prefab, GetBlastEffectRoot());
        return effect;
    }

    private IEnumerator ReturnSpecialEffect(Block.BlockArchetype archetype, ParticleSystem effect)
    {
        if (effect == null)
        {
            yield break;
        }

        while (effect.IsAlive(true))
        {
            yield return null;
        }

        effect.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        effect.gameObject.SetActive(false);

        if (!specialEffectPools.TryGetValue(archetype, out Queue<ParticleSystem> pool))
        {
            pool = new Queue<ParticleSystem>();
            specialEffectPools[archetype] = pool;
        }

        pool.Enqueue(effect);
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
            LockStaticNodes();
            ResetColorBuckets();
            shuffleNodesBuffer.Clear();

            foreach (Node node in nodeGrid)
            {
                Block occupant = node?.OccupiedBlock;
                if (occupant == null || occupant is StaticBlock)
                {
                    continue;
                }

                int colorId = ToColorId(occupant.blockType);
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
                if (node?.OccupiedBlock == null || node.OccupiedBlock is StaticBlock || IsNodeLocked(node))
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

        if (IsStaticNode(nodeA) || IsStaticNode(nodeB))
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

    private void LockStaticNodes()
    {
        if (staticTargetIndices.Count == 0)
        {
            return;
        }

        foreach (int index in staticTargetIndices)
        {
            Node node = GetNodeFromIndex(index);
            LockNode(node);
        }
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

    private static bool IsStaticNode(Node node)
    {
        return node != null && node.OccupiedBlock is StaticBlock;
    }

    private void SwapNodeBlock(Node source, Node destination)
    {
        if (source == null || destination == null || source == destination)
        {
            return;
        }

        if (IsStaticNode(source) || IsStaticNode(destination))
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
            if (block == null || block is StaticBlock)
            {
                continue;
            }

            ReleaseBlock(block);
            node.OccupiedBlock = null;
            ClearModelCell(node.gridPosition);
        }

        RequireFullBoardRefresh();
        gridManager.UpdateFreeNodes();

        Block forcedPrefab = prefabs[Random.Range(0, prefabs.Length)];
        if (forcedPrefab == null)
        {
            return false;
        }

        Node forcedNodeA = null;
        Node forcedNodeB = null;
        for (int y = 0; y < rows && (forcedNodeA == null || forcedNodeB == null); y++)
        {
            for (int x = 0; x < columns && (forcedNodeA == null || forcedNodeB == null); x++)
            {
                Node node = nodeGrid[x, y];
                if (node == null || IsStaticNode(node))
                {
                    continue;
                }

                if (forcedNodeA == null)
                {
                    forcedNodeA = node;
                }
                else if (forcedNodeB == null)
                {
                    forcedNodeB = node;
                }
            }
        }

        if (forcedNodeA == null || forcedNodeB == null)
        {
            return false;
        }

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

    private Block SpawnBlockOfType(int blockType, Node targetNode, Block.BlockArchetype archetype = Block.BlockArchetype.Regular)
    {
        if (targetNode == null)
        {
            return null;
        }

        Block spawned = SpawnBlockFromPool(blockType, targetNode.transform, archetype);
        if (spawned == null)
        {
            return null;
        }

        spawned.SetBlock(targetNode);
        SetModelCell(targetNode.gridPosition, blockType);
        SnapBlockToNode(spawned, targetNode);
        gridManager?.FreeNodes.Remove(targetNode);
        if (spawned.IsSpecialVariant)
        {
            AnimateSpecialSpawn(spawned);
        }
        return spawned;
    }

    private void SpawnStaticTargetsIfNeeded()
    {
        if (staticTargetsSpawned)
        {
            return;
        }

        staticTargetsSpawned = true;

        GameManager manager = GameManager.Instance;
        if (manager == null)
        {
            return;
        }

        var staticEntries = manager.ActiveStaticTargetSpawns;
        if (staticEntries == null || staticEntries.Count == 0)
        {
            return;
        }

        staticPlacementIndexLookup.Clear();

        Node[,] nodeGrid = gridManager?.NodeGrid;
        if (nodeGrid == null)
        {
            return;
        }

        for (int i = 0; i < staticEntries.Count; i++)
        {
            GameModeConfig.StaticTargetSpawn entry = staticEntries[i];
            if (!(entry.TargetPrefab is StaticBlock staticPrefab))
            {
                if (entry.TargetPrefab != null)
                {
                    Debug.LogWarning($"Static target spawn {entry.TargetPrefab.name} is not configured as StaticBlock.");
                }
                continue;
            }

            BuildStaticPlacementSlots(entry.PlacementMask, staticPlacementSlots);
            if (staticPlacementSlots.Count == 0)
            {
                continue;
            }

            for (int slot = 0; slot < staticPlacementSlots.Count; slot++)
            {
                ReplaceNodeWithStatic(staticPlacementSlots[slot], staticPrefab);
            }

            staticPlacementSlots.Clear();
        }
    }

    private void ReplaceNodeWithStatic(Node node, StaticBlock prefab)
    {
        if (node == null || prefab == null)
        {
            return;
        }

        Block current = node.OccupiedBlock;
        if (current != null)
        {
            node.OccupiedBlock = null;
            ReleaseBlock(current);
        }

        StaticBlock spawned = SpawnStaticBlockInstance(prefab, node);
        if (spawned == null)
        {
            return;
        }

        int index = boardModel.Index(node.gridPosition.x, node.gridPosition.y);
        if (index >= 0)
        {
            staticTargetIndices.Add(index);
        }

        totalStaticTargetCount++;
        RegisterStaticTarget(prefab.blockType);

        StaticBlockSpawned?.Invoke(spawned);
    }

    private StaticBlock SpawnStaticBlockInstance(StaticBlock prefab, Node node)
    {
        if (prefab == null || node == null)
        {
            return null;
        }

        Block.BlockArchetype archetype = prefab.Archetype;
        Block spawned = SpawnBlockFromPool(prefab.blockType, node.transform, archetype);
        if (!(spawned is StaticBlock staticBlock))
        {
            Debug.LogWarning($"Prefab {prefab.name} is not configured as a StaticBlock.");
            if (spawned != null)
            {
                ReleaseBlock(spawned);
            }
            return null;
        }

        staticBlock.SetBlock(node);
        SnapBlockToNode(staticBlock, node);
        SetModelCell(node.gridPosition, prefab.blockType);
        gridManager?.FreeNodes.Remove(node);
        return staticBlock;
    }

    private void BuildStaticPlacementSlots(GameModeConfig.StaticPlacementMask mask, List<Node> results)
    {
        results.Clear();

        Node[,] nodeGrid = gridManager?.NodeGrid;
        if (nodeGrid == null || boardModel == null)
        {
            return;
        }

        int columns = boardModel.Columns;
        int rows = boardModel.Rows;
        if (columns <= 0 || rows <= 0)
        {
            return;
        }

        void TryAdd(int x, int y)
        {
            if (x < 0 || y < 0 || x >= columns || y >= rows)
            {
                return;
            }

            int index = boardModel.Index(x, y);
            if (index < 0 || staticTargetIndices.Contains(index) || !staticPlacementIndexLookup.Add(index))
            {
                return;
            }

            Node node = nodeGrid[x, y];
            if (node == null)
            {
                return;
            }

            Block occupant = node.OccupiedBlock;
            if (occupant == null || occupant is StaticBlock)
            {
                return;
            }

            results.Add(node);
        }

        var custom = mask.CustomCells;
        if (custom == null || custom.Length == 0)
        {
            return;
        }

        int max = Mathf.Min(custom.Length, columns * rows);
        for (int idx = 0; idx < max; idx++)
        {
            if (!custom[idx])
            {
                continue;
            }

            int y = idx / columns;
            int x = idx % columns;
            TryAdd(x, y);
        }
    }

    private void CollectAdjacentStaticBlocks(int[] indices, int count)
    {
        if (boardModel == null || gridManager == null || indices == null || count <= 0)
        {
            return;
        }

        if (staticTargetIndices.Count == 0)
        {
            return;
        }

        pendingStaticRemovalIndices.Clear();
        int columns = boardModel.Columns;
        int rows = boardModel.Rows;

        for (int i = 0; i < count; i++)
        {
            if (i >= indices.Length)
            {
                break;
            }

            int index = indices[i];
            if (!boardModel.IsValidIndex(index))
            {
                continue;
            }

            int x = columns > 0 ? index % columns : boardModel.X(index);
            int y = columns > 0 ? index / columns : boardModel.Y(index);
            if (x < 0 || y < 0 || x >= columns || y >= rows)
            {
                continue;
            }

            QueueStaticNeighbour(x - 1, y);
            QueueStaticNeighbour(x + 1, y);
            QueueStaticNeighbour(x, y - 1);
            QueueStaticNeighbour(x, y + 1);
        }

        if (pendingStaticRemovalIndices.Count == 0)
        {
            return;
        }

        foreach (int staticIndex in pendingStaticRemovalIndices)
        {
            RemoveStaticBlockAt(staticIndex);
        }

        pendingStaticRemovalIndices.Clear();
    }

    private void QueueStaticNeighbour(int x, int y)
    {
        if (boardModel == null)
        {
            return;
        }

        int index = boardModel.Index(x, y);
        if (index < 0 || !staticTargetIndices.Contains(index))
        {
            return;
        }

        pendingStaticRemovalIndices.Add(index);
    }

    private bool TryRemoveStaticBlock(Node node)
    {
        if (node == null)
        {
            return false;
        }

        int index = boardModel.Index(node.gridPosition.x, node.gridPosition.y);
        if (index < 0 || !staticTargetIndices.Contains(index))
        {
            return false;
        }

        RemoveStaticBlockAt(index);
        return true;
    }

    private void RemoveStaticBlockAt(int index)
    {
        if (!staticTargetIndices.Contains(index))
        {
            return;
        }

        Node node = GetNodeFromIndex(index);
        StaticBlock staticBlock = node?.OccupiedBlock as StaticBlock;

        if (node != null && staticBlock != null)
        {
            if (node.OccupiedBlock == staticBlock)
            {
                node.OccupiedBlock = null;
                gridManager?.FreeNodes.Add(node);
            }

            Vector3 worldPosition = staticBlock.transform.position;
            PlayStaticBlockEffect(staticBlock);
            ReleaseBlock(staticBlock);
            UnregisterStaticTarget(staticBlock.blockType);
            StaticBlockCollected?.Invoke(staticBlock.blockType, worldPosition);
        }

        ClearModelCell(index);
        staticTargetIndices.Remove(index);
    }

    private void AnimateSpecialSpawn(Block block)
    {
        if (block == null)
        {
            return;
        }

        Transform target = block.transform;
        DOTween.Kill(target);
        target.localScale = Vector3.zero;

        target.DOScale(block.BaseLocalScale, SpecialSpawnScaleDuration)
            .SetEase(Ease.OutBack)
            .SetTarget(target)
            .OnComplete(() => StartSpecialIdleTween(block));
    }

    private void StartSpecialIdleTween(Block block)
    {
        if (block == null)
        {
            return;
        }

        Transform target = block.transform;
        DOTween.Kill(target);
        Vector3 baseScale = block.BaseLocalScale;
        target.localScale = baseScale;

        Tween tween = null;
        switch (block.Archetype)
        {
            case Block.BlockArchetype.Bomb2x2:
                tween = target.DOScale(baseScale * SpecialPulseMultiplier, SpecialPulseDuration);
                break;
            case Block.BlockArchetype.ColumnClear:
                tween = target.DOScaleY(baseScale.y * SpecialPulseMultiplier, SpecialPulseDuration);
                break;
            case Block.BlockArchetype.RowClear:
                tween = target.DOScaleX(baseScale.x * SpecialPulseMultiplier, SpecialPulseDuration);
                break;
        }

        if (tween != null)
        {
            tween.SetEase(Ease.InOutSine)
                .SetLoops(-1, LoopType.Yoyo)
                .SetTarget(target);
        }
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

            if (!boardModel.IsValidIndex(startIndex) || !boardModel.IsOccupied(startIndex))
            {
                return 0;
            }

            Node[,] nodeGrid = gridManager != null ? gridManager.NodeGrid : null;
            if (nodeGrid == null)
            {
                return 0;
            }

            int columns = boardModel.Columns;
            int rows = boardModel.Rows;
            if (columns <= 0 || rows <= 0)
            {
                return 0;
            }

            int startX = columns > 0 ? startIndex % columns : boardModel.X(startIndex);
            int startY = columns > 0 ? startIndex / columns : boardModel.Y(startIndex);
            if (startX < 0 || startY < 0 || startX >= columns || startY >= rows)
            {
                return 0;
            }

            Block startBlock = nodeGrid[startX, startY]?.OccupiedBlock;
            if (startBlock == null || !startBlock.CanParticipateInGroup)
            {
                return 0;
            }

            int stamp = AcquireVisitStamp();
            var searchData = new BlockSearchData(boardModel, nodeGrid, startIndex, startX, startY,
                groupIndicesBuffer, bfsQueue, visitedStamps, stamp);
            return startBlock.GatherSearchResults(searchData);
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

    private void ResetStaticTargetData()
    {
        if (staticTargetInfos.Count == 0)
        {
            return;
        }

        staticTargetKeyBuffer.Clear();
        foreach (var key in staticTargetInfos.Keys)
        {
            staticTargetKeyBuffer.Add(key);
        }

        staticTargetInfos.Clear();

        for (int i = 0; i < staticTargetKeyBuffer.Count; i++)
        {
            StaticTargetProgressChanged?.Invoke(staticTargetKeyBuffer[i], 0, 0);
        }

        staticTargetKeyBuffer.Clear();
    }

    private void FinalizeColumnSegment(int column, int segmentStart, int writeIndex, int segmentEnd)
    {
        if (boardModel == null)
        {
            return;
        }

        int clampedWrite = Mathf.Clamp(writeIndex, segmentStart, segmentEnd);
        for (int y = clampedWrite; y < segmentEnd; y++)
        {
            int index = boardModel.Index(column, y);
            if (index < 0 || staticTargetIndices.Contains(index))
            {
                continue;
            }

            ClearModelCell(index);
            pendingSpawnIndices.Add(index);
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
                if (index >= 0 && !staticTargetIndices.Contains(index))
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

                    Node node = GetNodeFromIndex(index);
                    Block block = node?.OccupiedBlock;
                    if (block == null || !block.CanParticipateInGroup)
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
                            Node rightNode = GetNodeFromIndex(rightIndex);
                            Block rightBlock = rightNode?.OccupiedBlock;
                            if (rightBlock != null && rightBlock.CanParticipateInGroup)
                            {
                                return true;
                            }
                        }
                    }

                    int upIndex = boardModel.Index(x, y + 1);
                    if (upIndex >= 0)
                    {
                        Cell upCell = boardModel.GetCell(upIndex);
                        if (upCell.occupied && upCell.colorId == color)
                        {
                            Node upNode = GetNodeFromIndex(upIndex);
                            Block upBlock = upNode?.OccupiedBlock;
                            if (upBlock != null && upBlock.CanParticipateInGroup)
                            {
                                return true;
                            }
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

            Node node = GetNodeFromIndex(i);
            Block block = node?.OccupiedBlock;
            if (block == null || !block.CanParticipateInGroup)
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

    [Serializable]
    private struct SpecialActivationEffectEntry
    {
        public Block.BlockArchetype archetype;
        public ParticleSystem effectPrefab;
    }

    private struct StaticTargetInfo
    {
        public int Total;
        public int Remaining;
    }

    private struct ChainClearGroup
    {
        public int[] Indices;
        public int Count;
        public bool AllowStaticAdjacent;
    }

    private struct SpecialClearEntry
    {
        public Block Block;
        public Node Node;
        public int ModelIndex;
    }
}
