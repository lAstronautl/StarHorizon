using System.Numerics;
using Content.Server.Atmos.EntitySystems;
using Content.Shared._Horizon.Shipbreaking;
using Content.Shared.DoAfter;
using Content.Shared.Interaction;
using Content.Shared.Popups;
using Content.Shared.Whitelist;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Physics;
using Robust.Shared.Physics.Components;
using Robust.Shared.Physics.Systems;

namespace Content.Server._Horizon.Shipbreaking;

/// <summary>
/// Handles <see cref="ShipCutterComponent"/>: cutting walls loose and cutting slits between floor tiles,
/// which leak air and eventually split the grid.
/// </summary>
public sealed partial class ShipCutterSystem : EntitySystem
{
    [Dependency] private readonly AtmosphereSystem _atmos = default!;
    [Dependency] private readonly EntityLookupSystem _lookup = default!;
    [Dependency] private readonly EntityWhitelistSystem _whitelist = default!;
    [Dependency] private readonly IMapManager _mapManager = default!;
    [Dependency] private readonly SharedAudioSystem _audio = default!;
    [Dependency] private readonly SharedDoAfterSystem _doAfter = default!;
    [Dependency] private readonly SharedMapSystem _map = default!;
    [Dependency] private readonly SharedPhysicsSystem _physics = default!;
    [Dependency] private readonly SharedPopupSystem _popup = default!;
    [Dependency] private readonly SharedTransformSystem _transform = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<ShipCutterComponent, AfterInteractEvent>(OnAfterInteract);
        SubscribeLocalEvent<ShipCutterComponent, ShipCutterWallDoAfterEvent>(OnWallDone);
        SubscribeLocalEvent<ShipCutterComponent, ShipCutterTileDoAfterEvent>(OnTileDone);
    }

    private void OnAfterInteract(Entity<ShipCutterComponent> ent, ref AfterInteractEvent args)
    {
        if (args.Handled || !args.CanReach)
            return;

        // A wall: cut it loose from the grid.
        if (args.Target is { } wall &&
            Transform(wall).Anchored &&
            _whitelist.IsWhitelistPass(ent.Comp.WallWhitelist, wall))
        {
            args.Handled = true;
            StartDoAfter(ent, args.User, ent.Comp.WallDelay, new ShipCutterWallDoAfterEvent(), wall);
            return;
        }

        // The seam between two floor tiles: cut a slit along it.
        if (!TryGetSeam(args.ClickLocation, ent.Comp.EdgeTolerance, out var gridUid, out var grid, out var tile, out var edge))
            return;

        args.Handled = true;

        if (!CanCut(gridUid, grid, tile, edge))
        {
            _popup.PopupEntity(Loc.GetString("ship-cutter-cannot-cut"), ent, args.User);
            return;
        }

        StartDoAfter(ent, args.User, ent.Comp.TileDelay, new ShipCutterTileDoAfterEvent(GetNetEntity(gridUid), tile, edge), null);
    }

    private void StartDoAfter(Entity<ShipCutterComponent> ent, EntityUid user, TimeSpan delay, DoAfterEvent ev, EntityUid? target)
    {
        var args = new DoAfterArgs(EntityManager, user, delay, ev, ent, target: target, used: ent)
        {
            BreakOnDamage = true,
            BreakOnMove = true,
            NeedHand = true,
        };

        if (_doAfter.TryStartDoAfter(args))
            _audio.PlayPvs(ent.Comp.Sound, ent);
    }

    private void OnWallDone(Entity<ShipCutterComponent> ent, ref ShipCutterWallDoAfterEvent args)
    {
        if (args.Cancelled || args.Handled || args.Target is not { } wall)
            return;

        args.Handled = true;
        CutLoose(wall);
    }

    /// <summary>
    /// Detaches an anchored entity from the grid, turning it into a loose body that can be moved around.
    /// </summary>
    private void CutLoose(EntityUid wall)
    {
        _transform.Unanchor(wall);

        if (!TryComp<PhysicsComponent>(wall, out var body))
            return;

        _physics.SetBodyType(wall, BodyType.Dynamic, body: body);
        _physics.SetCanCollide(wall, true, body: body);
        _physics.WakeBody(wall, body: body);
    }

    private void OnTileDone(Entity<ShipCutterComponent> ent, ref ShipCutterTileDoAfterEvent args)
    {
        if (args.Cancelled || args.Handled)
            return;

        args.Handled = true;

        var gridUid = GetEntity(args.Grid);
        if (!TryComp<MapGridComponent>(gridUid, out var grid) || !CanCut(gridUid, grid, args.Tile, args.Edge))
            return;

        AddCut(gridUid, grid, args.Tile, args.Edge);
    }

    /// <summary>
    /// Finds the grid tile edge the click was close to.
    /// </summary>
    private bool TryGetSeam(EntityCoordinates click, float tolerance, out EntityUid gridUid, out MapGridComponent grid, out Vector2i tile, out CutEdges edge)
    {
        edge = CutEdges.None;
        tile = default;
        grid = default!;

        var mapPos = _transform.ToMapCoordinates(click);
        if (!_mapManager.TryFindGridAt(mapPos, out gridUid, out grid!))
            return false;

        var local = Vector2.Transform(mapPos.Position, _transform.GetInvWorldMatrix(gridUid)) / grid.TileSize;
        tile = new Vector2i((int) MathF.Floor(local.X), (int) MathF.Floor(local.Y));
        var fraction = local - new Vector2(tile.X, tile.Y);

        var best = tolerance;
        void Consider(float distance, CutEdges candidate)
        {
            if (distance > best)
                return;

            best = distance;
            edge = candidate;
        }

        Consider(fraction.X, CutEdges.West);
        Consider(1f - fraction.X, CutEdges.East);
        Consider(fraction.Y, CutEdges.South);
        Consider(1f - fraction.Y, CutEdges.North);

        return edge != CutEdges.None;
    }

    private bool CanCut(EntityUid gridUid, MapGridComponent grid, Vector2i tile, CutEdges edge)
    {
        // Both sides have to be floor, and the seam must not be cut already.
        if (!_map.TryGetTile(grid, tile, out var a) || a.IsEmpty ||
            !_map.TryGetTile(grid, tile + edge.Offset(), out var b) || b.IsEmpty)
            return false;

        return !(TryComp<GridCutsComponent>(gridUid, out var cuts) &&
                 cuts.Cuts.GetValueOrDefault(tile).HasFlag(edge));
    }

    private void AddCut(EntityUid gridUid, MapGridComponent grid, Vector2i tile, CutEdges edge)
    {
        var cuts = EnsureComp<GridCutsComponent>(gridUid);
        var other = tile + edge.Offset();

        cuts.Cuts[tile] = cuts.Cuts.GetValueOrDefault(tile) | edge;
        cuts.Cuts[other] = cuts.Cuts.GetValueOrDefault(other) | edge.Opposite();
        Dirty(gridUid, cuts);

        // The tiles on both sides now leak to space, see AtmosphereSystem.UpdateTileData.
        _atmos.InvalidateTile(gridUid, tile);
        _atmos.InvalidateTile(gridUid, other);

        TrySplit(gridUid, grid, cuts, tile, other);
    }
}
