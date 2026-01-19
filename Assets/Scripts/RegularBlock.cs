using UnityEngine;

public class RegularBlock : Block
{
    [SerializeField] private Sprite tierOneIcon;
    [SerializeField] private Sprite tierTwoIcon;
    [SerializeField] private Sprite tierThreeIcon;

    public override bool CanParticipateInGroup => true;
    public override Sprite TierOneIcon => tierOneIcon;
    public override Sprite TierTwoIcon => tierTwoIcon;
    public override Sprite TierThreeIcon => tierThreeIcon;

    public override int GatherSearchResults(BlockSearchData searchData)
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

        int[] resultBuffer = searchData.ResultBuffer;
        int[] bfsQueue = searchData.BfsQueue;
        int[] visited = searchData.VisitedStamps;
        if (resultBuffer == null || bfsQueue == null || visited == null)
        {
            return 0;
        }

        Cell startCell = board.GetCell(searchData.StartIndex);
        if (!startCell.occupied)
        {
            return 0;
        }

        Node startNode = nodeGrid[searchData.StartX, searchData.StartY];
        Block occupant = startNode != null ? startNode.OccupiedBlock : null;
        if (occupant == null || occupant.blockType != blockType || !occupant.CanParticipateInGroup)
        {
            return 0;
        }

        int stamp = searchData.VisitStamp;
        int head = 0;
        int tail = 0;
        bfsQueue[tail++] = searchData.StartIndex;
        visited[searchData.StartIndex] = stamp;
        int groupCount = 0;
        byte colorId = startCell.colorId;

        while (head < tail)
        {
            int current = bfsQueue[head++];
            resultBuffer[groupCount++] = current;

            int cx = columns > 0 ? current % columns : board.X(current);
            int cy = columns > 0 ? current / columns : board.Y(current);

            TryVisit(cx - 1, cy);
            TryVisit(cx + 1, cy);
            TryVisit(cx, cy - 1);
            TryVisit(cx, cy + 1);
        }

        return groupCount;

        void TryVisit(int x, int y)
        {
            if (x < 0 || x >= columns || y < 0 || y >= rows)
            {
                return;
            }

            int index = y * columns + x;
            if (visited[index] == stamp)
            {
                return;
            }

            Cell cell = board.GetCell(index);
            if (!cell.occupied || cell.colorId != colorId)
            {
                return;
            }

            Node node = nodeGrid[x, y];
            Block other = node != null ? node.OccupiedBlock : null;
            if (other == null || !other.CanParticipateInGroup || other.blockType != blockType)
            {
                return;
            }

            visited[index] = stamp;
            bfsQueue[tail++] = index;
        }
    }

    public override void HandleBlastResult(GroupContext context)
    {
        // Regular blocks do not introduce special behaviour after blasts.
    }

    public override void ActivateSpecialEffect(GroupContext context)
    {
        // Regular blocks have no manual activation effect.
    }

    public override void ApplyGroupIcon(int groupSize, BoardSettings settings)
    {
        if (settings == null)
        {
            base.ApplyGroupIcon(groupSize, settings);
            return;
        }

        Sprite spriteToUse = DefaultIcon;
        if (groupSize >= settings.ThresholdC && tierThreeIcon != null)
        {
            spriteToUse = tierThreeIcon;
        }
        else if (groupSize >= settings.ThresholdB && tierTwoIcon != null)
        {
            spriteToUse = tierTwoIcon;
        }
        else if (groupSize >= settings.ThresholdA && tierOneIcon != null)
        {
            spriteToUse = tierOneIcon;
        }

        SetSprite(spriteToUse);
    }
}
