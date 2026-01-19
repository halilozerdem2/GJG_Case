public readonly struct BlockSearchData
{
    public BlockSearchData(BoardModel boardModel, Node[,] nodeGrid, int startIndex, int startX, int startY,
        int[] resultBuffer, int[] bfsQueue, int[] visitedStamps, int visitStamp)
    {
        BoardModel = boardModel;
        NodeGrid = nodeGrid;
        StartIndex = startIndex;
        StartX = startX;
        StartY = startY;
        ResultBuffer = resultBuffer;
        BfsQueue = bfsQueue;
        VisitedStamps = visitedStamps;
        VisitStamp = visitStamp;
    }

    public BoardModel BoardModel { get; }
    public Node[,] NodeGrid { get; }
    public int StartIndex { get; }
    public int StartX { get; }
    public int StartY { get; }
    public int[] ResultBuffer { get; }
    public int[] BfsQueue { get; }
    public int[] VisitedStamps { get; }
    public int VisitStamp { get; }

    public int Columns => BoardModel != null ? BoardModel.Columns : 0;
    public int Rows => BoardModel != null ? BoardModel.Rows : 0;

    public Cell StartCell => BoardModel != null ? BoardModel.GetCell(StartIndex) : default;

    public bool TryGetIndex(int x, int y, out int index)
    {
        if (BoardModel == null)
        {
            index = -1;
            return false;
        }

        index = BoardModel.Index(x, y);
        return index >= 0;
    }
}
