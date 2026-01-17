using System.Collections.Generic;
using UnityEngine;

public class Block : MonoBehaviour
{
    public Node node;
    public int blockType;

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

    private void OnMouseDown()
    {
        GameManager.Instance.TryBlastBlock(this);
    }
}
