using System.Collections.Generic;
using UnityEngine;

public abstract class Block : MonoBehaviour
{
    public enum BlockArchetype
    {
        Regular = 0,
        RowClear = 1,
        ColumnClear = 2,
        Bomb2x2 = 3,
        ColorClear = 4,
        Static = 5
    }

    public Node node;
    public int blockType;

    [SerializeField] private BlockArchetype archetype = BlockArchetype.Regular;

    private static readonly List<Block> floodFillNeighbours = new List<Block>(4);

    [Header("Visuals")]
    [SerializeField] private SpriteRenderer spriteRenderer;
    [SerializeField] private Sprite defaultIcon;
    [SerializeField] private Vector3 baseLocalScale = Vector3.one;

    public Sprite DefaultIcon => defaultIcon;
    public Vector3 BaseLocalScale => baseLocalScale;
    public BlockArchetype Archetype => archetype;
    public bool IsSpecialVariant => archetype != BlockArchetype.Regular;
    public virtual Sprite TierOneIcon => null;
    public virtual Sprite TierTwoIcon => null;
    public virtual Sprite TierThreeIcon => null;
    protected SpriteRenderer SpriteRenderer => spriteRenderer;

    public abstract bool CanParticipateInGroup { get; }

    protected virtual void Awake()
    {
        CacheRendererAndScale();
    }

#if UNITY_EDITOR
    protected virtual void OnValidate()
    {
        CacheRendererAndScale();
        if (spriteRenderer != null && defaultIcon == null)
        {
            defaultIcon = spriteRenderer.sprite;
        }

        if (!Application.isPlaying)
        {
            baseLocalScale = transform.localScale;
        }
    }
#endif

    public virtual bool CanBlastWith(Block other)
    {
        if (other == null || !CanParticipateInGroup || !other.CanParticipateInGroup)
        {
            return false;
        }

        return SharesBlockType(other);
    }

    public virtual void HandleBlastResult(GroupContext context)
    {
    }

    public virtual void ActivateSpecialEffect(GroupContext context)
    {
    }

    public abstract int GatherSearchResults(BlockSearchData searchData);

    protected bool SharesBlockType(Block other)
    {
        return other != null && other.blockType == blockType;
    }

    public void SetBlock(Node aNode, bool preserveWorldPosition = false)
    {
        if (node != null)
        {
            node.OccupiedBlock = null;
        }

        node = aNode;
        if (node != null)
        {
            node.OccupiedBlock = this;
            ApplyNodeSortingOrder();
        }

        transform.localRotation = Quaternion.identity;
        transform.localScale = baseLocalScale;

        if (!preserveWorldPosition && node != null)
        {
            transform.localPosition = node.transform.localPosition;
        }
    }

    public void ResetBlockState()
    {
        if (node != null)
        {
            node.OccupiedBlock = null;
            node = null;
        }

        if (spriteRenderer != null && defaultIcon != null)
        {
            spriteRenderer.sprite = defaultIcon;
        }

        transform.localScale = baseLocalScale;
        transform.localRotation = Quaternion.identity;
        transform.localPosition = Vector3.zero;

        OnStateReset();
    }

    public HashSet<Block> FloodFill(HashSet<Block> visited, Stack<Block> stack)
    {
        if (visited == null)
        {
            visited = new HashSet<Block>();
        }
        else
        {
            visited.Clear();
        }

        if (stack == null)
        {
            stack = new Stack<Block>();
        }
        else
        {
            stack.Clear();
        }

        stack.Push(this);
        visited.Add(this);

        GameManager manager = GameManager.Instance;
        if (manager == null)
        {
            return visited;
        }

        List<Block> neighbours = floodFillNeighbours;

        while (stack.Count > 0)
        {
            Block current = stack.Pop();

            manager.GetMatchingNeighbours(current, neighbours);
            for (int i = 0; i < neighbours.Count; i++)
            {
                Block neighbour = neighbours[i];
                if (visited.Add(neighbour))
                {
                    stack.Push(neighbour);
                }
            }
        }

        return visited;
    }

    public HashSet<Block> FloodFill()
    {
        return FloodFill(null, null);
    }

    public virtual void ApplyGroupIcon(int groupSize, BoardSettings settings)
    {
        if (spriteRenderer == null || settings == null || !CanParticipateInGroup)
        {
            return;
        }
        if (defaultIcon != null)
        {
            spriteRenderer.sprite = defaultIcon;
        }
    }

    protected void SetSprite(Sprite sprite)
    {
        if (spriteRenderer != null && sprite != null)
        {
            spriteRenderer.sprite = sprite;
        }
    }

    protected virtual void OnStateReset()
    {
    }

    private void OnMouseDown()
    {
        if (GameManager.Instance == null || !GameManager.Instance.IsWaitingForInput)
        {
            return;
        }

        HandleSelection();
    }

    protected virtual void HandleSelection()
    {
        GameManager.Instance?.TryBlastBlock(this);
    }

    private void CacheRendererAndScale()
    {
        if (spriteRenderer == null)
        {
            spriteRenderer = GetComponent<SpriteRenderer>();
        }

        if (baseLocalScale == Vector3.zero)
        {
            baseLocalScale = transform.localScale == Vector3.zero ? Vector3.one : transform.localScale;
        }
    }

    protected virtual void ApplyNodeSortingOrder()
    {
        if (node == null)
        {
            return;
        }

        if (spriteRenderer == null)
        {
            spriteRenderer = GetComponent<SpriteRenderer>();
        }

        if (spriteRenderer == null)
        {
            return;
        }

        spriteRenderer.sortingOrder = node.SortingOrder;
    }
}
