using System.Numerics;
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
