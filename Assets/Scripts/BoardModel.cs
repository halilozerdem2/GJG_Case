using System;

[Serializable]
public struct Cell
{
    public byte colorId;
    public byte iconTier;
    public bool occupied;

    public bool IsEmpty => !occupied;
}

public sealed class BoardModel
{
    public const byte EmptyColorId = byte.MaxValue;

    private Cell[] cells = Array.Empty<Cell>();

    public int Columns { get; private set; }
    public int Rows { get; private set; }
    public int CellCount => cells?.Length ?? 0;

    public BoardModel()
    {
    }

    public BoardModel(int columns, int rows)
    {
        Configure(columns, rows);
    }

    public void Configure(int columns, int rows)
    {
        Columns = Math.Max(0, columns);
        Rows = Math.Max(0, rows);
        int total = Columns * Rows;
        if (total <= 0)
        {
            cells = Array.Empty<Cell>();
            return;
        }

        if (cells == null || cells.Length != total)
        {
            cells = new Cell[total];
        }

        Clear();
    }

    public void Clear()
    {
        if (cells == null)
        {
            cells = Array.Empty<Cell>();
            return;
        }

        for (int i = 0; i < cells.Length; i++)
        {
            cells[i].colorId = EmptyColorId;
            cells[i].iconTier = 0;
            cells[i].occupied = false;
        }
    }

    public bool IsValidIndex(int index)
    {
        return cells != null && index >= 0 && index < cells.Length;
    }

    public int Index(int x, int y)
    {
        if (x < 0 || x >= Columns || y < 0 || y >= Rows || cells == null || cells.Length == 0)
        {
            return -1;
        }

        return y * Columns + x;
    }

    public int X(int index)
    {
        if (!IsValidIndex(index) || Columns == 0)
        {
            return -1;
        }

        return index % Columns;
    }

    public int Y(int index)
    {
        if (!IsValidIndex(index) || Columns == 0)
        {
            return -1;
        }

        return index / Columns;
    }

    public Cell GetCell(int index)
    {
        if (!IsValidIndex(index))
        {
            return default;
        }

        return cells[index];
    }

    public bool IsOccupied(int index)
    {
        return IsValidIndex(index) && cells[index].occupied;
    }

    public void SetCell(int index, byte colorId, byte iconTier = 0, bool occupied = true)
    {
        if (!IsValidIndex(index))
        {
            return;
        }

        cells[index].colorId = colorId;
        cells[index].iconTier = iconTier;
        cells[index].occupied = occupied;
    }

    public void SetCell(int x, int y, byte colorId, byte iconTier = 0, bool occupied = true)
    {
        int index = Index(x, y);
        if (index >= 0)
        {
            SetCell(index, colorId, iconTier, occupied);
        }
    }

    public void ClearCell(int index)
    {
        if (!IsValidIndex(index))
        {
            return;
        }

        cells[index].colorId = EmptyColorId;
        cells[index].iconTier = 0;
        cells[index].occupied = false;
    }

    public void SwapCells(int indexA, int indexB)
    {
        if (!IsValidIndex(indexA) || !IsValidIndex(indexB) || indexA == indexB)
        {
            return;
        }

        (cells[indexA], cells[indexB]) = (cells[indexB], cells[indexA]);
    }

    public void CopyCell(int fromIndex, int toIndex)
    {
        if (!IsValidIndex(fromIndex) || !IsValidIndex(toIndex) || fromIndex == toIndex)
        {
            return;
        }

        cells[toIndex] = cells[fromIndex];
    }
}
