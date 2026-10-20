using Robust.Shared.GameStates;

namespace Content.Shared._Horizon.Shipbreaking;

/// <summary>
/// Stores the cuts made along the seams between tiles of a grid with a <see cref="ShipCutterComponent"/>.
/// A cut lets air through (the tiles next to it leak to space), is drawn as a thin gap, and stops the
/// two tiles from counting as connected, so the grid falls apart once a part has no connections left.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class GridCutsComponent : Component
{
    /// <summary>
    /// Cut edges per tile. Every cut is stored on both tiles it separates.
    /// </summary>
    [DataField, AutoNetworkedField]
    public Dictionary<Vector2i, CutEdges> Cuts = new();

    /// <summary>
    /// Width of a drawn cut, in pixels (one tile is 32 pixels).
    /// </summary>
    public const float CutWidthPixels = 3f;
}

[Flags]
public enum CutEdges : byte
{
    None = 0,
    North = 1 << 0,
    East = 1 << 1,
    South = 1 << 2,
    West = 1 << 3,
}

public static class CutEdgesExtensions
{
    public static readonly CutEdges[] All = { CutEdges.North, CutEdges.East, CutEdges.South, CutEdges.West };

    public static CutEdges Opposite(this CutEdges edge)
    {
        return edge switch
        {
            CutEdges.North => CutEdges.South,
            CutEdges.South => CutEdges.North,
            CutEdges.East => CutEdges.West,
            CutEdges.West => CutEdges.East,
            _ => CutEdges.None,
        };
    }

    /// <summary>
    /// The rectangle of a cut along the given edge of a tile, in grid local coordinates.
    /// </summary>
    public static Box2 GetRect(this CutEdges edge, Vector2i tile, float tileSize, float halfWidth)
    {
        var x = tile.X * tileSize;
        var y = tile.Y * tileSize;

        return edge switch
        {
            CutEdges.North => new Box2(x, y + tileSize - halfWidth, x + tileSize, y + tileSize + halfWidth),
            CutEdges.South => new Box2(x, y - halfWidth, x + tileSize, y + halfWidth),
            CutEdges.East => new Box2(x + tileSize - halfWidth, y, x + tileSize + halfWidth, y + tileSize),
            _ => new Box2(x - halfWidth, y, x + halfWidth, y + tileSize),
        };
    }

    /// <summary>
    /// The indices offset to the tile on the other side of the edge.
    /// </summary>
    public static Vector2i Offset(this CutEdges edge)
    {
        return edge switch
        {
            CutEdges.North => new Vector2i(0, 1),
            CutEdges.South => new Vector2i(0, -1),
            CutEdges.East => new Vector2i(1, 0),
            CutEdges.West => new Vector2i(-1, 0),
            _ => Vector2i.Zero,
        };
    }
}
