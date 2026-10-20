using Content.Shared.DoAfter;
using Robust.Shared.Serialization;

namespace Content.Shared._Horizon.GridWelding.Events;

/// <summary>
/// Raised once the welder's initial grid-merging weld (the one that joins two separate grids
/// into one) finishes its DoAfter.
/// </summary>
[Serializable, NetSerializable]
public sealed partial class GridWeldMergeDoAfterEvent : DoAfterEvent
{
    public NetEntity GridA;
    public Vector2i TileA;
    public NetEntity GridB;
    public Vector2i TileB;

    public GridWeldMergeDoAfterEvent(NetEntity gridA, Vector2i tileA, NetEntity gridB, Vector2i tileB)
    {
        GridA = gridA;
        TileA = tileA;
        GridB = gridB;
        TileB = tileB;
    }

    public override DoAfterEvent Clone()
    {
        return this;
    }

    public override bool IsDuplicate(DoAfterEvent other)
    {
        return other is GridWeldMergeDoAfterEvent otherMerge
               && GridA == otherMerge.GridA
               && TileA == otherMerge.TileA
               && GridB == otherMerge.GridB
               && TileB == otherMerge.TileB;
    }
}
