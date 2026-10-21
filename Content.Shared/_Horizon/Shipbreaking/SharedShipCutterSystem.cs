using System.Numerics;
using Content.Shared.Whitelist;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;

namespace Content.Shared._Horizon.Shipbreaking;

/// <summary>
/// Finding where on a grid a <see cref="ShipCutterComponent"/> would cut. Shared so the client can show a ghost of the cut.
/// </summary>
public abstract class SharedShipCutterSystem : EntitySystem
{
    [Dependency] protected readonly IMapManager MapManager = default!;
    [Dependency] protected readonly SharedMapSystem MapSystem = default!;
    [Dependency] protected readonly SharedTransformSystem TransformSystem = default!;
    [Dependency] protected readonly EntityWhitelistSystem Whitelist = default!;

    /// <summary>
    /// Finds the tile edge a point is close to.
    /// </summary>
    /// <param name="tolerance">How close to the edge the point has to be, in tiles.</param>
    public bool TryGetSeam(MapCoordinates mapPos, float tolerance, out EntityUid gridUid, out MapGridComponent grid, out Vector2i tile, out CutEdges edge)
    {
        edge = CutEdges.None;
        tile = default;
        grid = default!;

        if (!MapManager.TryFindGridAt(mapPos, out gridUid, out grid!))
            return false;

        var local = Vector2.Transform(mapPos.Position, TransformSystem.GetInvWorldMatrix(gridUid)) / grid.TileSize;
        tile = new Vector2i((int) MathF.Floor(local.X), (int) MathF.Floor(local.Y));
        var fraction = local - new Vector2(tile.X, tile.Y);

        // Not a local function: out parameters can't be used inside one.
        var best = tolerance;
        var bestEdge = CutEdges.None;

        if (fraction.X <= best)
        {
            best = fraction.X;
            bestEdge = CutEdges.West;
        }

        if (1f - fraction.X <= best)
        {
            best = 1f - fraction.X;
            bestEdge = CutEdges.East;
        }

        if (fraction.Y <= best)
        {
            best = fraction.Y;
            bestEdge = CutEdges.South;
        }

        if (1f - fraction.Y <= best)
        {
            best = 1f - fraction.Y;
            bestEdge = CutEdges.North;
        }

        edge = bestEdge;
        return edge != CutEdges.None;
    }

    /// <summary>
    /// Works out what a click with the cutter is aimed at. Used by the server to cut and by the client to draw the ghost,
    /// so the ghost always shows what the click does.
    /// A click near the edge of a tile is the seam there, also under a wall: the gap shows over the wall.
    /// Otherwise a wall on the clicked tile is cut loose. Near an edge that can not be cut (space, already cut)
    /// it is still the seam, so the player is told that there is nothing to cut.
    /// </summary>
    public ShipCutterPick Pick(MapCoordinates click, ShipCutterComponent cutter)
    {
        if (!MapManager.TryFindGridAt(click, out var gridUid, out var grid))
            return default;

        var local = Vector2.Transform(click.Position, TransformSystem.GetInvWorldMatrix(gridUid)) / grid.TileSize;
        var tilePos = new Vector2i((int) MathF.Floor(local.X), (int) MathF.Floor(local.Y));

        // The wall and the seam are both looked for on the tile under the click.
        EntityUid? wall = null;
        foreach (var anchored in MapSystem.GetAnchoredEntities((gridUid, grid), tilePos))
        {
            if (Whitelist.IsWhitelistPass(cutter.WallWhitelist, anchored))
            {
                wall = anchored;
                break;
            }
        }

        var tolerance = wall != null ? cutter.WallEdgeTolerance : cutter.EdgeTolerance;
        var hasSeam = TryGetSeam(click, tolerance, out _, out _, out var tile, out var edge);
        var canCut = hasSeam && CanCut(gridUid, grid, tile, edge);

        if (canCut || (hasSeam && wall == null))
            return new ShipCutterPick(ShipCutterPickKind.Seam, gridUid, grid, tile, edge, null, canCut);

        if (wall != null)
            return new ShipCutterPick(ShipCutterPickKind.Wall, gridUid, grid, tilePos, CutEdges.None, wall, true);

        return default;
    }

    /// <summary>
    /// Whether the edge faces open space: the tile is floor and the tile on the other side of the edge is not.
    /// Only such edges can be joined to another grid.
    /// </summary>
    public bool IsOpenEdge(MapGridComponent grid, Vector2i tile, CutEdges edge)
    {
        return MapSystem.TryGetTile(grid, tile, out var a) && !a.IsEmpty &&
               !(MapSystem.TryGetTile(grid, tile + edge.Offset(), out var b) && !b.IsEmpty);
    }

    /// <summary>
    /// Whether the given edge of the tile is cut.
    /// </summary>
    public bool HasCut(EntityUid gridUid, Vector2i tile, CutEdges edge)
    {
        return TryComp<GridCutsComponent>(gridUid, out var cuts) && cuts.Cuts.GetValueOrDefault(tile).HasFlag(edge);
    }

    /// <summary>
    /// Whether the seam can be cut: both sides have to be floor and the seam must not be cut already.
    /// </summary>
    public bool CanCut(EntityUid gridUid, MapGridComponent grid, Vector2i tile, CutEdges edge)
    {
        if (!MapSystem.TryGetTile(grid, tile, out var a) || a.IsEmpty ||
            !MapSystem.TryGetTile(grid, tile + edge.Offset(), out var b) || b.IsEmpty)
            return false;

        return !HasCut(gridUid, tile, edge);
    }
}

public enum ShipCutterPickKind
{
    None,
    Seam,
    Wall,
}

/// <summary>
/// What a click with the cutter is aimed at, see <see cref="SharedShipCutterSystem.Pick"/>.
/// </summary>
public readonly record struct ShipCutterPick(
    ShipCutterPickKind Kind,
    EntityUid Grid,
    MapGridComponent? GridComp,
    Vector2i Tile,
    CutEdges Edge,
    EntityUid? Wall,
    bool CanCut);
