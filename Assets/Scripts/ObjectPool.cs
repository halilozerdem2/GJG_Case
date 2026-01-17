using System.Collections.Generic;
using UnityEngine;

public class ObjectPool : MonoBehaviour
{
    public static ObjectPool Instance;

    [SerializeField] private Transform inactiveParent;

    private readonly Dictionary<int, Queue<Block>> pools = new Dictionary<int, Queue<Block>>();
    private readonly Dictionary<int, Block> prefabLookup = new Dictionary<int, Block>();

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

    public void Initialize(Block[] prefabs, int sizePerPrefab)
    {
        pools.Clear();
        prefabLookup.Clear();

        foreach (var prefab in prefabs)
        {
            if (prefab == null)
            {
                continue;
            }

            int blockType = prefab.blockType;
            if (prefabLookup.ContainsKey(blockType))
            {
                continue;
            }

            prefabLookup[blockType] = prefab;
            Queue<Block> queue = new Queue<Block>(sizePerPrefab);
            for (int i = 0; i < sizePerPrefab; i++)
            {
                Block instance = Instantiate(prefab, inactiveParent);
                instance.gameObject.SetActive(false);
                queue.Enqueue(instance);
            }
            pools[blockType] = queue;
        }
    }

    public Block Spawn(int blockType, Transform parent)
    {
        if (!prefabLookup.ContainsKey(blockType))
        {
            Debug.LogWarning($"No prefab registered for block type {blockType}");
            return null;
        }

        if (!pools.TryGetValue(blockType, out Queue<Block> queue))
        {
            queue = new Queue<Block>();
            pools[blockType] = queue;
        }

        Block block;
        if (queue.Count > 0)
        {
            block = queue.Dequeue();
        }
        else
        {
            block = Instantiate(prefabLookup[blockType], inactiveParent);
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

        int blockType = block.blockType;
        block.ResetBlockState();
        block.gameObject.SetActive(false);
        block.transform.SetParent(inactiveParent, false);

        if (!pools.TryGetValue(blockType, out Queue<Block> queue))
        {
            queue = new Queue<Block>();
            pools[blockType] = queue;
        }

        queue.Enqueue(block);
    }
}
