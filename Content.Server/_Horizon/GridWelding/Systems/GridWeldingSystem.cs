using Content.Server._Horizon.Shipbreaking;
using Content.Server.Administration.Logs;
using Content.Shared._Horizon.GridWelding.Components;
using Content.Shared._Horizon.GridWelding.Events;
using Content.Shared._Horizon.GridWelding.Systems;
using Content.Shared._Horizon.Shipbreaking;
using Content.Shared.Database;
using Content.Shared.Gravity;
using Content.Shared.Interaction;
using Content.Shared.Popups;
using Content.Shared.Tools.Systems;
using Robust.Server.Physics;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Timing;

namespace Content.Server._Horizon.GridWelding.Systems;

/// <summary>
/// Handles the grid welding tool: picking two points on two different grids, merging them into one
/// via <see cref="GridFixtureSystem.Merge"/>, and tracking/sealing the resulting unwelded seam.
/// Server-only - merging grids and the validation that guards it must not be client-predicted.
/// </summary>
public sealed partial class GridWeldingSystem : SharedGridWeldingSystem
{
    [Dependency] private readonly SharedToolSystem _tool = default!;
    [Dependency] private readonly SharedPopupSystem _popup = default!;
    [Dependency] private readonly SharedAudioSystem _audio = default!;
    [Dependency] private readonly SharedInteractionSystem _interaction = default!;
    [Dependency] private readonly SharedTransformSystem _xform = default!;
    [Dependency] private readonly SharedMapSystem _maps = default!;
    [Dependency] private readonly IMapManager _mapManager = default!;
    [Dependency] private readonly GridFixtureSystem _gridFixtures = default!;
    [Dependency] private readonly IAdminLogManager _adminLogger = default!;
    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly GridCutsSystem _cuts = default!;
    [Dependency] private readonly SharedShipCutterSystem _cutter = default!;
    [Dependency] private readonly GridGravitySystem _gridGravity = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<SeamWelderComponent, AfterInteractEvent>(OnAfterInteract);
        SubscribeLocalEvent<SeamWelderComponent, GridWeldMergeDoAfterEvent>(OnMergeDoAfter);
        SubscribeLocalEvent<SeamWelderComponent, GridWeldSealDoAfterEvent>(OnSealDoAfter);
    }

    private void OnAfterInteract(Entity<SeamWelderComponent> ent, ref AfterInteractEvent args)
    {
        if (args.Handled || !args.CanReach)
            return;

        var comp = ent.Comp;
        var clickCoords = _xform.ToMapCoordinates(args.ClickLocation);

        if (!_mapManager.TryFindGridAt(clickCoords, out var gridUid, out var grid))
            return;

        if (HasComp<MapComponent>(gridUid))
        {
            _popup.PopupEntity(Loc.GetString("grid-welding-invalid-target"), ent, args.User, PopupType.SmallCaution);
            return;
        }

        var tile = _maps.TileIndicesFor((gridUid, grid), clickCoords);
        if (!_maps.TryGetTileRef(gridUid, grid, tile, out var tileRef) || tileRef.Tile.IsEmpty)
        {
            _popup.PopupEntity(Loc.GetString("grid-welding-no-tile"), ent, args.User, PopupType.SmallCaution);
            return;
        }

        args.Handled = true;

        // Sealing a cut is a single click on the seam - no two-step pick needed.
        if (_cutter.TryGetSeam(clickCoords, comp.SeamTolerance, out var seamGrid, out _, out var seamTile, out var seamEdge) &&
            _cutter.HasCut(seamGrid, seamTile, seamEdge))
        {
            StartSeal(ent, args.User, seamGrid, seamTile, seamEdge, comp);
            return;
        }

        // Welding two grids joins an edge of one with an edge of the other: the click picks the nearest edge of the tile.
        if (!_cutter.TryGetSeam(clickCoords, comp.EdgeSelectTolerance, out _, out _, out var edgeTile, out var edge))
            return;

        // Only an edge that faces open space can be joined to something.
        if (!_cutter.IsOpenEdge(grid, edgeTile, edge))
        {
            _popup.PopupEntity(Loc.GetString("grid-welding-edge-not-open"), ent, args.User, PopupType.SmallCaution);
            return;
        }

        if (!comp.HasPendingSelection || comp.PendingSelectionExpiry is not { } expiry || _timing.CurTime > expiry)
        {
            comp.HasPendingSelection = true;
            comp.PendingGrid = GetNetEntity(gridUid);
            comp.PendingTile = edgeTile;
            comp.PendingEdge = edge;
            comp.PendingSelectionExpiry = _timing.CurTime + comp.SelectionTimeout;
            Dirty(ent);
            _popup.PopupEntity(Loc.GetString("grid-welding-first-point-selected"), ent, args.User, PopupType.Medium);
            return;
        }

        var gridA = GetEntity(comp.PendingGrid!.Value);
        var tileA = comp.PendingTile;
        var edgeA = comp.PendingEdge;

        // Clear the pending selection up front - any failure past this point resets it, rather than
        // leaving the tool armed against a target we already know is bad.
        ClearPendingSelection(comp);
        Dirty(ent);

        if (!TryValidateWeld(comp, args.User, gridA, tileA, edgeA, gridUid, edgeTile, edge, out _, out _, out var errorKey))
        {
            if (errorKey != null)
                _popup.PopupEntity(Loc.GetString(errorKey), ent, args.User, PopupType.SmallCaution);
            _audio.PlayPvs(comp.ErrorSound, ent);
            return;
        }

        var ev = new GridWeldMergeDoAfterEvent(GetNetEntity(gridA), tileA, edgeA, GetNetEntity(gridUid), edgeTile, edge);
        // The tool is the target, so the finished do-after is raised on it (a null target would broadcast it).
        _tool.UseTool(ent, args.User, ent, (float) comp.WeldMergeDelay.TotalSeconds, "Welding", ev, comp.MergeFuelCost);
    }

    private void StartSeal(Entity<SeamWelderComponent> ent, EntityUid user, EntityUid gridUid, Vector2i tile, CutEdges edge, SeamWelderComponent comp)
    {
        _popup.PopupEntity(Loc.GetString("grid-welding-sealing-seam"), ent, user, PopupType.Medium);
        var ev = new GridWeldSealDoAfterEvent(GetNetEntity(gridUid), tile, edge);
        _tool.UseTool(ent, user, ent, (float) comp.WeldSealDelay.TotalSeconds, "Welding", ev, comp.SealFuelCost);
    }

    private void OnMergeDoAfter(Entity<SeamWelderComponent> ent, ref GridWeldMergeDoAfterEvent args)
    {
        if (args.Cancelled || args.Handled)
            return;

        args.Handled = true;

        var gridAUid = GetEntity(args.GridA);
        var gridBUid = GetEntity(args.GridB);

        if (!TryValidateWeld(ent.Comp, args.User, gridAUid, args.TileA, args.EdgeA, gridBUid, args.TileB, args.EdgeB, out var offset, out var rotation, out var errorKey))
        {
            if (errorKey != null)
                _popup.PopupEntity(Loc.GetString(errorKey), ent, args.User, PopupType.SmallCaution);
            _audio.PlayPvs(ent.Comp.ErrorSound, ent);
            return;
        }

        MergeGrids(gridAUid, gridBUid, offset, rotation, args.TileA, args.TileB);

        _popup.PopupEntity(Loc.GetString("grid-welding-merge-success"), ent, args.User, PopupType.Medium);
        _audio.PlayPvs(ent.Comp.WeldSound, ent);

        _adminLogger.Add(LogType.Construction, LogImpact.High,
            $"{ToPrettyString(args.User):player} welded {ToPrettyString(gridBUid):grid} into {ToPrettyString(gridAUid):grid}");
    }

    /// <summary>
    /// Merges grid B into grid A at the given offset and rotation (from <see cref="TryValidateWeld"/>). Every border
    /// between the two grids, besides the welded pair of tiles, is left as a cut that has to be welded shut.
    /// </summary>
    public void MergeGrids(EntityUid gridAUid, EntityUid gridBUid, Vector2i offset, Angle rotation, Vector2i tileA, Vector2i tileB)
    {
        var gridA = Comp<MapGridComponent>(gridAUid);
        var gridB = Comp<MapGridComponent>(gridBUid);
        var xformA = Transform(gridAUid);
        var xformB = Transform(gridBUid);

        // Snapshot both footprints before Merge mutates grid A and deletes grid B outright.
        var gridBTiles = new List<TileRef>(_maps.GetAllTiles(gridBUid, gridB));
        var gridAFootprint = new HashSet<Vector2i>();
        foreach (var tile in _maps.GetAllTiles(gridAUid, gridA))
            gridAFootprint.Add(tile.GridIndices);

        // Grid B is deleted by the merge, take its cuts with us first.
        var gridBCuts = TryComp<GridCutsComponent>(gridBUid, out var bCuts)
            ? new Dictionary<Vector2i, CutEdges>(bCuts.Cuts)
            : null;

        // The gravity of grid B goes with it too.
        var bInherentGravity = false;
        var bGravityEnabled = false;
        if (TryComp<GravityComponent>(gridBUid, out var bGravity))
        {
            bInherentGravity = bGravity.Inherent;
            bGravityEnabled = bGravity.Enabled;
        }

        gridA.CanSplit = false;
        _gridFixtures.Merge(gridAUid, gridBUid, offset, rotation, gridA, gridB, xformA, xformB);
        gridA.CanSplit = true;
        _gridFixtures.CheckSplits(gridAUid);

        // The generators of grid B now stand on grid A, which has to count them.
        _gridGravity.AfterMerge(gridAUid, bInherentGravity, bGravityEnabled);

        ApplyMergeSeams(gridAUid, gridA, gridAFootprint, gridBTiles, gridBCuts, offset, rotation, tileA, tileB);
    }

    private void OnSealDoAfter(Entity<SeamWelderComponent> ent, ref GridWeldSealDoAfterEvent args)
    {
        if (args.Cancelled || args.Handled)
            return;

        args.Handled = true;

        var gridUid = GetEntity(args.Grid);
        if (!_cuts.RemoveCut(gridUid, args.Tile, args.Edge))
        {
            _popup.PopupEntity(Loc.GetString("grid-welding-no-seam"), ent, args.User, PopupType.SmallCaution);
            return;
        }

        _audio.PlayPvs(ent.Comp.SealSound, ent);

        if (_cuts.HasCuts(gridUid))
            _popup.PopupEntity(Loc.GetString("grid-welding-seam-sealed"), ent, args.User, PopupType.Medium);
        else
            _popup.PopupEntity(Loc.GetString("grid-welding-hull-fully-sealed"), ent, args.User, PopupType.Medium);

        _adminLogger.Add(LogType.Construction, LogImpact.Low,
            $"{ToPrettyString(args.User):player} welded a seam shut on {ToPrettyString(gridUid):grid}");
    }

    private static void ClearPendingSelection(SeamWelderComponent comp)
    {
        comp.HasPendingSelection = false;
        comp.PendingGrid = null;
        comp.PendingEdge = CutEdges.None;
        comp.PendingSelectionExpiry = null;
    }
}
