using System.Collections.Generic;
using UnityEngine;
using System.Linq;

public class Block : MonoBehaviour
{
    public Node node;
    public bool isBlastable = false;
    public int neighboursCount = 0;
    public int blockType;
    public List<Block> neighbours = new List<Block>();

    public void SetBlock(Node aNode)
    {
        if (node != null) node.OccupiedBlock = null;
        node = aNode;
        node.OccupiedBlock = this;
        transform.SetParent(node.transform);
    }

    public void FindNeighbours(List<Node> nodes)
    {
        neighbours.Clear();
        Vector2Int[] directions = { Vector2Int.up, Vector2Int.down, Vector2Int.left, Vector2Int.right };

        foreach (var dir in directions)
        {
            Node neighbourNode = nodes.FirstOrDefault(n => n.gridPosition == node.gridPosition + dir);
            if (neighbourNode != null && neighbourNode.OccupiedBlock != null)
            {
                Block neighbourBlock = neighbourNode.OccupiedBlock;
                if (neighbourBlock.blockType == this.blockType) // Aynı tipte mi kontrol et
                {
                    neighbours.Add(neighbourBlock);
                }
            }
        }

        neighboursCount = neighbours.Count;
        isBlastable = neighboursCount > 0;
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

            foreach (Block neighbour in current.neighbours)
            {
                if (!visited.Contains(neighbour))
                {
                    visited.Add(neighbour);
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