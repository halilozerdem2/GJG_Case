using System.Collections.Generic;
using UnityEngine;

public class Block : MonoBehaviour
{
    public Node node;
    public int blockType;

    [Header("Visuals")]
    [SerializeField] private SpriteRenderer spriteRenderer;
    [SerializeField] private Sprite defaultIcon;
    [SerializeField] private Sprite tierOneIcon;
    [SerializeField] private Sprite tierTwoIcon;
    [SerializeField] private Sprite tierThreeIcon;
    [SerializeField] private Vector3 baseLocalScale = Vector3.one;

    public Sprite DefaultIcon => defaultIcon;
    public Sprite TierOneIcon => tierOneIcon;
    public Sprite TierTwoIcon => tierTwoIcon;
    public Sprite TierThreeIcon => tierThreeIcon;
    public Vector3 BaseLocalScale => baseLocalScale;

    private void Awake()
    {
        CacheRendererAndScale();
    }

#if UNITY_EDITOR
    private void OnValidate()
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

        while (stack.Count > 0)
        {
            Block current = stack.Pop();

            foreach (Block neighbour in GameManager.Instance.GetMatchingNeighbours(current))
            {
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

    public void ApplyGroupIcon(int groupSize, BoardSettings settings)
    {
        if (spriteRenderer == null || settings == null)
        {
            return;
        }

        Sprite spriteToUse = defaultIcon != null ? defaultIcon : spriteRenderer.sprite;

        if (groupSize > settings.ThresholdC && tierThreeIcon != null)
        {
            spriteToUse = tierThreeIcon;
        }
        else if (groupSize > settings.ThresholdB && tierTwoIcon != null)
        {
            spriteToUse = tierTwoIcon;
        }
        else if (groupSize > settings.ThresholdA && tierOneIcon != null)
        {
            spriteToUse = tierOneIcon;
        }

        spriteRenderer.sprite = spriteToUse;
    }

    private void OnMouseDown()
    {
        if (GameManager.Instance == null || !GameManager.Instance.IsWaitingForInput)
        {
            return;
        }

        GameManager.Instance.TryBlastBlock(this);
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

    private void ApplyNodeSortingOrder()
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
