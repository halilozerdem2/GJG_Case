using UnityEngine;

public class StaticBlock : Block
{
    public override bool CanParticipateInGroup => false;

    [SerializeField] private ParticleSystem staticBlastEffect;
    [SerializeField] private IceBlock iceBlock;

    public ParticleSystem StaticBlastEffect => staticBlastEffect;
    public bool HasIceBarrier => ResolveIceBlock()?.HasStrength == true;

    public override int GatherSearchResults(BlockSearchData searchData)
    {
        return 0;
    }

    public void ResetIce()
    {
        ResolveIceBlock()?.ResetStrength();
    }

    public bool TryDamageIce()
    {
        IceBlock overlay = ResolveIceBlock();
        if (overlay == null || !overlay.HasStrength)
        {
            return false;
        }

        overlay.ApplyHit();
        return true;
    }

    protected override void HandleSelection()
    {
        // Static blocks cannot be selected to trigger blasts directly.
    }

    public bool IsAdjacentToBlast(GroupContext context, BoardModel board)
    {
        if (context.Indices == null || node == null || board == null)
        {
            return false;
        }

        Vector2Int gridPos = node.gridPosition;
        int[] indices = context.Indices;
        for (int i = 0; i < context.GroupSize && i < indices.Length; i++)
        {
            int boardIndex = indices[i];
            int x = board.X(boardIndex);
            int y = board.Y(boardIndex);
            if (x < 0 || y < 0)
            {
                continue;
            }

            int distance = Mathf.Abs(x - gridPos.x) + Mathf.Abs(y - gridPos.y);
            if (distance == 1)
            {
                return true;
            }
        }

        return false;
    }

    protected override void OnStateReset()
    {
        base.OnStateReset();
        ResetIce();
        ConfigureIceOverlaySorting();
    }

    protected override void ApplyNodeSortingOrder()
    {
        base.ApplyNodeSortingOrder();
        ConfigureIceOverlaySorting();
    }

    private IceBlock ResolveIceBlock()
    {
        if (iceBlock == null)
        {
            iceBlock = GetComponentInChildren<IceBlock>(true);
        }

        return iceBlock;
    }

    private void ConfigureIceOverlaySorting()
    {
        IceBlock overlay = ResolveIceBlock();
        if (overlay != null)
        {
            overlay.AlignWith(SpriteRenderer);
        }
    }
}
