using Content.Server.Shuttles.Components;
using Content.Shared._Horizon.Shipbreaking;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Physics.Components;

namespace Content.Server._Horizon.Shipbreaking;

// Splitting a grid along cuts.
public sealed partial class ShipCutterSystem
{
    /// <summary>
    /// After a cut between <paramref name="a"/> and <paramref name="b"/>, checks whether the two tiles are still
    /// connected through tiles and uncut seams. If not, the smaller side is split off into a new grid.
    /// </summary>
    private void TrySplit(EntityUid gridUid, MapGridComponent grid, GridCutsComponent cuts, Vector2i a, Vector2i b)
    {
        var sideA = Flood(grid, cuts, a);
        if (sideA.Contains(b))
            return;

        var sideB = Flood(grid, cuts, b);
        SplitOff(gridUid, grid, cuts, sideA.Count <= sideB.Count ? sideA : sideB);
    }

    /// <summary>
    /// All tiles reachable from <paramref name="start"/> without crossing a cut or an empty tile.
    /// </summary>
    private HashSet<Vector2i> Flood(MapGridComponent grid, GridCutsComponent cuts, Vector2i start)
    {
        var result = new HashSet<Vector2i> { start };
        var queue = new Queue<Vector2i>();
        queue.Enqueue(start);

        while (queue.TryDequeue(out var current))
        {
            var blocked = cuts.Cuts.GetValueOrDefault(current);
            foreach (var edge in CutEdgesExtensions.All)
            {
                if (blocked.HasFlag(edge))
                    continue;

                var next = current + edge.Offset();
                if (result.Contains(next) || !_map.TryGetTile(grid, next, out var tile) || tile.IsEmpty)
                    continue;

                result.Add(next);
                queue.Enqueue(next);
            }
        }

        return result;
    }

    private void SplitOff(EntityUid oldUid, MapGridComponent oldGrid, GridCutsComponent oldCuts, HashSet<Vector2i> moved)
    {
        var newGrid = _mapManager.CreateGridEntity(Transform(oldUid).MapID);
        var (position, rotation) = _transform.GetWorldPositionRotation(oldUid);
        _transform.SetWorldPositionRotation(newGrid.Owner, position, rotation);

        var newTiles = new List<(Vector2i, Tile)>(moved.Count);
        var emptyTiles = new List<(Vector2i, Tile)>(moved.Count);
        foreach (var indices in moved)
        {
            if (_map.TryGetTile(oldGrid, indices, out var tile))
                newTiles.Add((indices, tile));

            emptyTiles.Add((indices, Tile.Empty));
        }

        _map.SetTiles(newGrid.Owner, newGrid.Comp, newTiles);

        // Everything standing on the moved tiles goes with them. Both grids share the same transform,
        // so local positions stay the same.
        var entities = new HashSet<EntityUid>();
        foreach (var indices in moved)
        {
            _lookup.GetLocalEntitiesIntersecting(oldUid, indices, entities);
        }

        foreach (var uid in entities)
        {
            var xform = Transform(uid);
            if (xform.ParentUid != oldUid || HasComp<MapGridComponent>(uid))
                continue;

            var indices = _map.LocalToTile(oldUid, oldGrid, xform.Coordinates);
            if (!moved.Contains(indices))
                continue;

            var anchored = xform.Anchored;
            if (anchored)
                _transform.Unanchor(uid, xform);

            _transform.SetCoordinates(uid, xform, new EntityCoordinates(newGrid.Owner, xform.LocalPosition));

            if (anchored)
                _transform.AnchorEntity((uid, xform), newGrid, indices);
        }

        _map.SetTiles(oldUid, oldGrid, emptyTiles);

        MoveCuts(oldUid, oldCuts, newGrid.Owner, moved);

        // The piece is loose debris: movable, keeping the momentum of what it was cut from.
        EnsureComp<ShuttleComponent>(newGrid.Owner);
        if (TryComp<PhysicsComponent>(oldUid, out var oldBody) && TryComp<PhysicsComponent>(newGrid.Owner, out var newBody))
        {
            _physics.SetLinearVelocity(newGrid.Owner, oldBody.LinearVelocity, body: newBody);
            _physics.SetAngularVelocity(newGrid.Owner, oldBody.AngularVelocity, body: newBody);
        }

        var ev = new GridSplitEvent(new[] { newGrid.Owner }, oldUid);
        RaiseLocalEvent(oldUid, ref ev, true);
    }

    /// <summary>
    /// Moves the cuts inside the moved part to the new grid and drops the ones that ran along the split.
    /// </summary>
    private void MoveCuts(EntityUid oldUid, GridCutsComponent oldCuts, EntityUid newUid, HashSet<Vector2i> moved)
    {
        var newCuts = EnsureComp<GridCutsComponent>(newUid);

        foreach (var indices in moved)
        {
            if (!oldCuts.Cuts.Remove(indices, out var flags))
                continue;

            var kept = CutEdges.None;
            foreach (var edge in CutEdgesExtensions.All)
            {
                if (flags.HasFlag(edge) && moved.Contains(indices + edge.Offset()))
                    kept |= edge;
            }

            if (kept != CutEdges.None)
                newCuts.Cuts[indices] = kept;
        }

        // The tiles left behind no longer have a neighbour across the split: their cut is now just an open edge.
        foreach (var indices in moved)
        {
            foreach (var edge in CutEdgesExtensions.All)
            {
                var neighbor = indices + edge.Offset();
                if (moved.Contains(neighbor) ||
                    !oldCuts.Cuts.TryGetValue(neighbor, out var flags) ||
                    !flags.HasFlag(edge.Opposite()))
                    continue;

                flags &= ~edge.Opposite();
                if (flags == CutEdges.None)
                    oldCuts.Cuts.Remove(neighbor);
                else
                    oldCuts.Cuts[neighbor] = flags;

                _atmos.InvalidateTile(oldUid, neighbor);
            }
        }

        Dirty(oldUid, oldCuts);
        Dirty(newUid, newCuts);
    }
}
