using UnityEngine;

public class ColorClearBlock : SpecialBlock
{
    private byte targetColorId = BoardModel.EmptyColorId;

    public void ConfigureTargetColor(int sourceBlockType)
    {
        targetColorId = (byte)Mathf.Clamp(sourceBlockType, 0, byte.MaxValue);
    }

    public override int GatherSearchResults(BlockSearchData searchData)
    {
        BoardModel board = searchData.BoardModel;
        int[] buffer = searchData.ResultBuffer;
        if (board == null || buffer == null)
        {
            return 0;
        }

        Cell startCell = board.GetCell(searchData.StartIndex);
        byte fallbackColor = startCell.occupied ? startCell.colorId : BoardModel.EmptyColorId;
        byte targetColor = targetColorId != BoardModel.EmptyColorId ? targetColorId : fallbackColor;

        int count = 0;
        if (targetColor != BoardModel.EmptyColorId)
        {
            count = GatherColorResults(searchData, targetColor);
        }

        bool startMatchesTarget = startCell.occupied && startCell.colorId == targetColor;
        if (!startMatchesTarget && count < buffer.Length)
        {
            buffer[count++] = searchData.StartIndex;
        }

        return count;
    }

    protected override void OnStateReset()
    {
        base.OnStateReset();
        targetColorId = BoardModel.EmptyColorId;
    }
}
