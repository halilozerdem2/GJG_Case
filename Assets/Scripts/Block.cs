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

    private void Awake()
    {
        if (spriteRenderer == null)
        {
            spriteRenderer = GetComponent<SpriteRenderer>();
        }
    }

#if UNITY_EDITOR
    private void OnValidate()
    {
        if (spriteRenderer == null)
        {
            spriteRenderer = GetComponent<SpriteRenderer>();
        }

        if (spriteRenderer != null && defaultIcon == null)
        {
            defaultIcon = spriteRenderer.sprite;
        }
    }
#endif

    public void SetBlock(Node aNode)
    {
        if (node != null) node.OccupiedBlock = null;
        node = aNode;
        node.OccupiedBlock = this;
        transform.SetParent(node.transform);
    }

    public HashSet<Block> FloodFill()
    {
        HashSet<Block> visited = new HashSet<Block>();
        Stack<Block> stack = new Stack<Block>();

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
}
