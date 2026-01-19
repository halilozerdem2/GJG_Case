using System;
using System.Collections.Generic;
using UnityEngine;

public class ObjectPool : MonoBehaviour
{
    public static ObjectPool Instance;

    [SerializeField] private Transform inactiveParent;

    private readonly Dictionary<BlockPrefabKey, Queue<Block>> pools = new Dictionary<BlockPrefabKey, Queue<Block>>();
    private readonly Dictionary<BlockPrefabKey, Block> prefabLookup = new Dictionary<BlockPrefabKey, Block>();
    private readonly Dictionary<int, Queue<ParticleSystem>> effectPools = new Dictionary<int, Queue<ParticleSystem>>();
    private readonly Dictionary<int, ParticleSystem> effectPrefabLookup = new Dictionary<int, ParticleSystem>();
    private readonly Dictionary<ParticleSystem, int> effectInstanceLookup = new Dictionary<ParticleSystem, int>();

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
        if (inactiveParent == null)
        {
            inactiveParent = transform;
        }

        DontDestroyOnLoad(gameObject);
    }

    public void Initialize(Block[] prefabs, int totalCells, float bufferMultiplier = 1.2f)
    {
        pools.Clear();
        prefabLookup.Clear();

        if (prefabs == null || prefabs.Length == 0)
        {
            return;
        }

        List<Block> uniquePrefabs = new List<Block>();
        foreach (var prefab in prefabs)
        {
            if (prefab == null)
            {
                continue;
            }

            BlockPrefabKey key = new BlockPrefabKey(prefab.blockType, prefab.Archetype);
            if (prefabLookup.ContainsKey(key))
            {
                continue;
            }

            prefabLookup[key] = prefab;
            uniquePrefabs.Add(prefab);
        }

        if (uniquePrefabs.Count == 0)
        {
            return;
        }

        float multiplier = Mathf.Max(1f, bufferMultiplier);
        int totalNeeded = Mathf.Max(1, Mathf.CeilToInt(totalCells * multiplier));
        int perType = Mathf.Max(1, Mathf.CeilToInt((float)totalNeeded / uniquePrefabs.Count));

        foreach (var prefab in uniquePrefabs)
        {
            BlockPrefabKey key = new BlockPrefabKey(prefab.blockType, prefab.Archetype);
            Queue<Block> queue = new Queue<Block>(perType);
            for (int i = 0; i < perType; i++)
            {
                Block instance = Instantiate(prefab, inactiveParent);
                instance.gameObject.SetActive(false);
                queue.Enqueue(instance);
            }
            pools[key] = queue;
        }
    }

    public void InitializeEffects(Block[] blockPrefabs, ParticleSystem[] effectPrefabs, int prewarmPerType = 2)
    {
        effectPools.Clear();
        effectPrefabLookup.Clear();

        if (blockPrefabs == null || effectPrefabs == null)
        {
            return;
        }

        int count = Mathf.Min(blockPrefabs.Length, effectPrefabs.Length);
        for (int i = 0; i < count; i++)
        {
            Block blockPrefab = blockPrefabs[i];
            ParticleSystem effectPrefab = effectPrefabs[i];
            if (blockPrefab == null || effectPrefab == null)
            {
                continue;
            }

            int blockType = blockPrefab.blockType;
            if (effectPrefabLookup.ContainsKey(blockType))
            {
                continue;
            }

            effectPrefabLookup[blockType] = effectPrefab;
            Queue<ParticleSystem> queue = new Queue<ParticleSystem>(Mathf.Max(0, prewarmPerType));
            for (int j = 0; j < prewarmPerType; j++)
            {
                ParticleSystem instance = CreateEffectInstance(blockType);
                if (instance != null)
                {
                    queue.Enqueue(instance);
                }
            }
            effectPools[blockType] = queue;
        }
    }

    public Block Spawn(int blockType, Block.BlockArchetype archetype, Transform parent)
    {
        BlockPrefabKey key = new BlockPrefabKey(blockType, archetype);
        if (!prefabLookup.ContainsKey(key))
        {
            Debug.LogWarning($"No prefab registered for block type {blockType} and archetype {archetype}.");
            return null;
        }

        if (!pools.TryGetValue(key, out Queue<Block> queue))
        {
            queue = new Queue<Block>();
            pools[key] = queue;
        }

        Block block;
        if (queue.Count > 0)
        {
            block = queue.Dequeue();
        }
        else
        {
            block = Instantiate(prefabLookup[key], inactiveParent);
        }

        block.gameObject.SetActive(true);
        block.transform.SetParent(parent, false);
        block.ResetBlockState();
        return block;
    }

    public void Release(Block block)
    {
        if (block == null)
        {
            return;
        }

        BlockPrefabKey key = new BlockPrefabKey(block.blockType, block.Archetype);
        block.ResetBlockState();
        block.gameObject.SetActive(false);
        block.transform.SetParent(inactiveParent, false);

        if (!pools.TryGetValue(key, out Queue<Block> queue))
        {
            queue = new Queue<Block>();
            pools[key] = queue;
        }

        queue.Enqueue(block);
    }

    public ParticleSystem SpawnBlastEffect(int blockType, Vector3 position, Transform parent)
    {
        if (!effectPrefabLookup.ContainsKey(blockType))
        {
            Debug.LogWarning($"No blast effect prefab registered for block type {blockType}");
            return null;
        }

        if (!effectPools.TryGetValue(blockType, out Queue<ParticleSystem> queue))
        {
            queue = new Queue<ParticleSystem>();
            effectPools[blockType] = queue;
        }

        ParticleSystem effect = queue.Count > 0 ? queue.Dequeue() : CreateEffectInstance(blockType);
        if (effect == null)
        {
            return null;
        }
        Transform targetParent = parent != null ? parent : inactiveParent;
        Transform effectTransform = effect.transform;
        effectTransform.SetParent(targetParent, false);
        effectTransform.position = position;
        effectTransform.rotation = Quaternion.identity;

        effect.gameObject.SetActive(true);
        effect.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        effect.Clear(true);
        effect.Play();
        return effect;
    }

    public void ReleaseBlastEffect(ParticleSystem effect)
    {
        if (effect == null || !effectInstanceLookup.TryGetValue(effect, out int blockType))
        {
            return;
        }

        effect.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        effect.gameObject.SetActive(false);
        effect.transform.SetParent(inactiveParent, false);

        if (!effectPools.TryGetValue(blockType, out Queue<ParticleSystem> queue))
        {
            queue = new Queue<ParticleSystem>();
            effectPools[blockType] = queue;
        }

        queue.Enqueue(effect);
    }

    private ParticleSystem CreateEffectInstance(int blockType)
    {
        if (!effectPrefabLookup.TryGetValue(blockType, out ParticleSystem prefab))
        {
            return null;
        }

        ParticleSystem instance = Instantiate(prefab, inactiveParent);
        instance.gameObject.SetActive(false);
        effectInstanceLookup[instance] = blockType;
        return instance;
    }

    private readonly struct BlockPrefabKey : IEquatable<BlockPrefabKey>
    {
        public readonly int BlockType;
        public readonly Block.BlockArchetype Archetype;

        public BlockPrefabKey(int blockType, Block.BlockArchetype archetype)
        {
            BlockType = blockType;
            Archetype = archetype;
        }

        public bool Equals(BlockPrefabKey other)
        {
            return BlockType == other.BlockType && Archetype == other.Archetype;
        }

        public override bool Equals(object obj)
        {
            return obj is BlockPrefabKey other && Equals(other);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                return (BlockType * 397) ^ (int)Archetype;
            }
        }
    }
}
