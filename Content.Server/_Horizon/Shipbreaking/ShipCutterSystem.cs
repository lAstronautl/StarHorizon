using Content.Server.Atmos.EntitySystems;
using Content.Shared._Horizon.Shipbreaking;
using Content.Shared.DoAfter;
using Content.Shared.Interaction;
using Content.Shared.Popups;
using Content.Shared.Tools.Systems;
using Content.Shared.Whitelist;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Map.Components;
using Robust.Shared.Physics;
using Robust.Shared.Physics.Components;
using Robust.Shared.Physics.Systems;

namespace Content.Server._Horizon.Shipbreaking;

/// <summary>
/// Handles <see cref="ShipCutterComponent"/>: cutting walls loose and cutting slits between floor tiles,
/// which leak air and eventually split the grid. The cutter is a welder: it has to be lit and every cut burns fuel.
/// </summary>
public sealed partial class ShipCutterSystem : SharedShipCutterSystem
{
    private const string CutterQuality = "Welding";

    [Dependency] private readonly AtmosphereSystem _atmos = default!;
    [Dependency] private readonly EntityLookupSystem _lookup = default!;
    [Dependency] private readonly EntityWhitelistSystem _whitelist = default!;
    [Dependency] private readonly SharedAudioSystem _audio = default!;
    [Dependency] private readonly SharedPhysicsSystem _physics = default!;
    [Dependency] private readonly SharedPopupSystem _popup = default!;
    [Dependency] private readonly SharedToolSystem _tool = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<ShipCutterComponent, AfterInteractEvent>(OnAfterInteract);

        // The tool system directs the finished do-after at its target: the wall, or the grid for a cut between tiles.
        SubscribeLocalEvent<TransformComponent, ShipCutterWallDoAfterEvent>(OnWallDone);
        SubscribeLocalEvent<MapGridComponent, ShipCutterTileDoAfterEvent>(OnTileDone);
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
            Cut(ent, args.User, wall, ent.Comp.WallDelay, ent.Comp.WallFuel, new ShipCutterWallDoAfterEvent());
            return;
        }

        // The seam between two floor tiles: cut a slit along it.
        if (!TryGetSeam(TransformSystem.ToMapCoordinates(args.ClickLocation), ent.Comp.EdgeTolerance,
                out var gridUid, out var grid, out var tile, out var edge))
            return;

        args.Handled = true;

        if (!CanCut(gridUid, grid, tile, edge))
        {
            _popup.PopupEntity(Loc.GetString("ship-cutter-cannot-cut"), ent, args.User);
            return;
        }

        Cut(ent, args.User, gridUid, ent.Comp.TileDelay, ent.Comp.TileFuel,
            new ShipCutterTileDoAfterEvent(GetNetEntity(gridUid), tile, edge));
    }

    /// <summary>
    /// Starts a tool do-after that checks the cutter is lit and takes <paramref name="fuel"/> from it.
    /// </summary>
    private void Cut(Entity<ShipCutterComponent> ent, EntityUid user, EntityUid target, float delay, float fuel, DoAfterEvent ev)
    {
        if (_tool.UseTool(ent, user, target, delay, CutterQuality, ev, fuel))
            _audio.PlayPvs(ent.Comp.Sound, ent);
    }

    private void OnWallDone(Entity<TransformComponent> wall, ref ShipCutterWallDoAfterEvent args)
    {
        if (args.Cancelled || args.Handled)
            return;

        args.Handled = true;
        CutLoose(wall);
    }

    /// <summary>
    /// Detaches an anchored entity from the grid, turning it into a loose body that can be moved around.
    /// </summary>
    private void CutLoose(EntityUid wall)
    {
        TransformSystem.Unanchor(wall);

        if (!TryComp<PhysicsComponent>(wall, out var body))
            return;

        _physics.SetBodyType(wall, BodyType.Dynamic, body: body);
        _physics.SetCanCollide(wall, true, body: body);
        _physics.WakeBody(wall, body: body);
    }

    private void OnTileDone(Entity<MapGridComponent> grid, ref ShipCutterTileDoAfterEvent args)
    {
        if (args.Cancelled || args.Handled)
            return;

        args.Handled = true;

        if (!CanCut(grid, grid.Comp, args.Tile, args.Edge))
            return;

        AddCut(grid, grid.Comp, args.Tile, args.Edge);
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
