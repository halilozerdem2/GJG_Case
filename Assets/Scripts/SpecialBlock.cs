using UnityEngine;

public abstract class SpecialBlock : Block
{
    [SerializeField] private bool participateInGroups;
    [SerializeField, Min(1)] private int defaultBombRadius = 1;

    private const int SpecialSortingOrder = 20;

    public override bool CanParticipateInGroup => participateInGroups;

    protected virtual int BombRadius => Mathf.Max(1, defaultBombRadius);

    public override bool CanBlastWith(Block other)
    {
        if (!participateInGroups)
        {
            return false;
        }

        return base.CanBlastWith(other);
    }

    public override int GatherSearchResults(BlockSearchData searchData)
    {
        switch (Archetype)
        {
            case BlockArchetype.RowClear:
                return GatherRowResults(searchData);
            case BlockArchetype.ColumnClear:
                return GatherColumnResults(searchData);
            case BlockArchetype.ColorClear:
                return GatherColorResults(searchData);
            case BlockArchetype.Bomb2x2:
                return GatherBombResults(searchData, BombRadius);
            default:
                return 0;
        }
    }

    protected int GatherRowResults(BlockSearchData searchData)
    {
        BoardModel board = searchData.BoardModel;
        Node[,] nodeGrid = searchData.NodeGrid;
        if (board == null || nodeGrid == null)
        {
            return 0;
        }

        int columns = searchData.Columns;
        int rows = searchData.Rows;
        if (columns <= 0 || rows <= 0)
        {
            return 0;
        }

        int row = searchData.StartY;
        if (row < 0 || row >= rows)
        {
            return 0;
        }

        int[] buffer = searchData.ResultBuffer;
        if (buffer == null)
        {
            return 0;
        }

        int count = 0;
        for (int x = 0; x < columns; x++)
        {
            int index = board.Index(x, row);
            if (index < 0)
            {
                continue;
            }

            Node node = nodeGrid[x, row];
            if (node == null || node.OccupiedBlock == null)
            {
                continue;
            }

            buffer[count++] = index;
        }

        return count;
    }

    protected override void ApplyNodeSortingOrder()
    {
        SpriteRenderer renderer = SpriteRenderer;
        if (renderer == null)
        {
            renderer = GetComponent<SpriteRenderer>();
        }

        if (renderer == null)
        {
            return;
        }

        renderer.sortingOrder = SpecialSortingOrder;
    }

    protected int GatherColumnResults(BlockSearchData searchData)
    {
        BoardModel board = searchData.BoardModel;
        Node[,] nodeGrid = searchData.NodeGrid;
        if (board == null || nodeGrid == null)
        {
            return 0;
        }

        int columns = searchData.Columns;
        int rows = searchData.Rows;
        if (columns <= 0 || rows <= 0)
        {
            return 0;
        }

        int column = searchData.StartX;
        if (column < 0 || column >= columns)
        {
            return 0;
        }

        int[] buffer = searchData.ResultBuffer;
        if (buffer == null)
        {
            return 0;
        }

        int count = 0;
        for (int y = 0; y < rows; y++)
        {
            int index = board.Index(column, y);
            if (index < 0)
            {
                continue;
            }

            Node node = nodeGrid[column, y];
            if (node == null || node.OccupiedBlock == null)
            {
                continue;
            }

            buffer[count++] = index;
        }

        return count;
    }

    protected int GatherColorResults(BlockSearchData searchData)
    {
        BoardModel board = searchData.BoardModel;
        if (board == null)
        {
            return 0;
        }

        Cell startCell = board.GetCell(searchData.StartIndex);
        if (!startCell.occupied)
        {
            return 0;
        }

        return GatherColorResults(searchData, startCell.colorId);
    }

    protected int GatherColorResults(BlockSearchData searchData, byte targetColor)
    {
        BoardModel board = searchData.BoardModel;
        if (board == null || targetColor == BoardModel.EmptyColorId)
        {
            return 0;
        }

        int[] buffer = searchData.ResultBuffer;
        if (buffer == null)
        {
            return 0;
        }

        int count = 0;
        int cellCount = board.CellCount;
        for (int i = 0; i < cellCount; i++)
        {
            Cell cell = board.GetCell(i);
            if (!cell.occupied || cell.colorId != targetColor)
            {
                continue;
            }

            buffer[count++] = i;
        }

        return count;
    }

    protected int GatherBombResults(BlockSearchData searchData, int radius)
    {
        BoardModel board = searchData.BoardModel;
        Node[,] nodeGrid = searchData.NodeGrid;
        if (board == null || nodeGrid == null)
        {
            return 0;
        }

        int columns = searchData.Columns;
        int rows = searchData.Rows;
        if (columns <= 0 || rows <= 0)
        {
            return 0;
        }

        int[] buffer = searchData.ResultBuffer;
        if (buffer == null)
        {
            return 0;
        }

        int startX = searchData.StartX;
        int startY = searchData.StartY;
        int count = 0;
        for (int x = startX - radius; x <= startX + radius; x++)
        {
            if (x < 0 || x >= columns)
            {
                continue;
            }

            for (int y = startY - radius; y <= startY + radius; y++)
            {
                if (y < 0 || y >= rows)
                {
                    continue;
                }

                int index = board.Index(x, y);
                if (index < 0)
                {
                    continue;
                }

                Node node = nodeGrid[x, y];
                if (node == null || node.OccupiedBlock == null)
                {
                    continue;
                }

                buffer[count++] = index;
            }
        }

        return count;
    }
}
