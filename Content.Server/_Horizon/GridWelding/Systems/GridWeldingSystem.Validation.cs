using System.Linq;
using Content.Shared._Horizon.GridWelding.Components;
using Content.Shared._Horizon.GridWelding.Systems;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Physics;

namespace Content.Server._Horizon.GridWelding.Systems;

public sealed partial class GridWeldingSystem
{
    /// <summary>
    /// Runs the full validation checklist for welding <paramref name="tileA"/> on grid A to
    /// <paramref name="tileB"/> on grid B, and if everything checks out, returns the offset+rotation
    /// <see cref="Robust.Server.Physics.GridFixtureSystem.Merge"/> needs. Always re-run this right
    /// before actually calling Merge, not just at the initial click, since the two grids can
    /// move/change during the DoAfter.
    /// </summary>
    private bool TryValidateWeld(
        GridWeldingToolComponent comp,
        EntityUid user,
        EntityUid gridAUid,
        Vector2i tileA,
        EntityUid gridBUid,
        Vector2i tileB,
        out Vector2i offset,
        out Angle rotation,
        out string? errorKey)
    {
        offset = default;
        rotation = Angle.Zero;
        errorKey = null;

        if (gridAUid == gridBUid)
        {
            errorKey = "grid-welding-same-grid";
            return false;
        }

        if (!TryComp<MapGridComponent>(gridAUid, out var gridA) ||
            !TryComp<MapGridComponent>(gridBUid, out var gridB) ||
            !TryComp<TransformComponent>(gridAUid, out var xformA) ||
            !TryComp<TransformComponent>(gridBUid, out var xformB))
        {
            errorKey = "grid-welding-invalid-target";
            return false;
        }

        if (HasComp<MapComponent>(gridAUid) || HasComp<MapComponent>(gridBUid))
        {
            errorKey = "grid-welding-invalid-target";
            return false;
        }

        if (!TryComputeWeldGeometry(gridAUid, gridA, xformA, tileA, gridBUid, gridB, xformB, tileB,
                comp.TileProximityTolerance, comp.RotationSnapTolerance,
                out offset, out rotation, out var targetTileA, out errorKey))
        {
            return false;
        }

        var tileCountA = _maps.GetAllTiles(gridAUid, gridA).Count();
        var tileCountB = _maps.GetAllTiles(gridBUid, gridB).Count();
        if (tileCountA + tileCountB > comp.MaxMergedGridTiles)
        {
            errorKey = "grid-welding-too-large";
            return false;
        }

        // Full-footprint overlap check: transform every tile of B into A's local frame. Nothing
        // except the single explicit weld point is allowed to land on an already-occupied A tile
        // or collide with an anchored structure on either side.
        var half = gridA.TileSizeHalfVector;
        foreach (var bTile in _maps.GetAllTiles(gridBUid, gridB))
        {
            var transformed = SharedGridWeldingSystem.TransformTile(bTile.GridIndices, half, offset, rotation);

            if (transformed == targetTileA)
                continue;

            if (_maps.TryGetTileRef(gridAUid, gridA, transformed, out var existing) && !existing.Tile.IsEmpty)
            {
                errorKey = "grid-welding-obstructed";
                return false;
            }

            if (_maps.GetAnchoredEntities(gridAUid, gridA, transformed).Any(HasHardFixture) ||
                _maps.GetAnchoredEntities(gridBUid, gridB, bTile.GridIndices).Any(HasHardFixture))
            {
                errorKey = "grid-welding-obstructed";
                return false;
            }
        }

        // The 8px tolerance above is grid-to-grid alignment, not player reach - the player still
        // needs to actually be able to touch both points, not just whichever one they clicked.
        var coordsA = _maps.GridTileToLocal(gridAUid, gridA, tileA);
        var coordsB = _maps.GridTileToLocal(gridBUid, gridB, tileB);
        if (!_interaction.InRangeUnobstructed(user, coordsA, popup: false) ||
            !_interaction.InRangeUnobstructed(user, coordsB, popup: false))
        {
            errorKey = "grid-welding-out-of-range";
            return false;
        }

        return true;
    }

    private bool HasHardFixture(EntityUid uid)
    {
        return TryComp<FixturesComponent>(uid, out var fixtures) && fixtures.Fixtures.Values.Any(f => f.Hard);
    }
}
