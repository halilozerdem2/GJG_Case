using System.Collections.Generic;
using UnityEngine;

public class BoardSafetyService : MonoBehaviour
{
    public static BoardSafetyService Instance { get; private set; }

    private readonly List<Block> neighbours = new List<Block>(4);

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
        DontDestroyOnLoad(gameObject);
    }

    public bool HasAtLeastOneLegalMove()
    {
        var grid = FindObjectOfType<GridManager>();
        if (grid == null)
        {
            return true; // be permissive if grid not ready
        }

        Node[,] nodeGrid = grid.NodeGrid;
        if (nodeGrid == null)
        {
            return true;
        }

        int cols = nodeGrid.GetLength(0);
        int rows = nodeGrid.GetLength(1);
        for (int x = 0; x < cols; x++)
        {
            for (int y = 0; y < rows; y++)
            {
                Block b = nodeGrid[x, y]?.OccupiedBlock;
                if (b == null || !b.CanParticipateInGroup)
                {
                    continue;
                }

                if (grid.GetMatchingNeighbours(b, neighbours) > 0)
                {
                    return true;
                }
            }
        }

        return false;
    }
}

