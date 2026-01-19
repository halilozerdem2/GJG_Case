using System;

public readonly struct GroupContext
{
    public static readonly GroupContext Empty = new GroupContext(null, null, 0, -1);

    public GroupContext(Block origin, int[] indices, int groupSize, int blockType)
    {
        Origin = origin;
        Indices = indices;
        GroupSize = Math.Max(0, groupSize);
        BlockType = blockType;
    }

    public Block Origin { get; }
    public int[] Indices { get; }
    public int GroupSize { get; }
    public int BlockType { get; }
    public bool IsValid => Origin != null && Indices != null && GroupSize > 0;
}
