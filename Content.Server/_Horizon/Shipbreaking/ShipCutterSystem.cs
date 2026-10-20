using Content.Server.PowerCell;
using Content.Shared._Horizon.Shipbreaking;
using Content.Shared.DoAfter;
using Content.Shared.Interaction;
using Content.Shared.Interaction.Events;
using Content.Shared.Popups;
using Content.Shared.Whitelist;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Map.Components;
using Robust.Shared.Physics;
using Robust.Shared.Physics.Components;
using Robust.Shared.Physics.Systems;

namespace Content.Server._Horizon.Shipbreaking;

/// <summary>
/// Handles <see cref="ShipCutterComponent"/>: cutting walls loose and cutting slits between floor tiles,
/// which leak air and eventually split the grid. Every click queues a target and the user works through the queue.
/// Each cut takes charge from the power cell of the cutter.
/// </summary>
public sealed partial class ShipCutterSystem : SharedShipCutterSystem
{
    [Dependency] private readonly EntityLookupSystem _lookup = default!;
    [Dependency] private readonly EntityWhitelistSystem _whitelist = default!;
    [Dependency] private readonly GridCutsSystem _cuts = default!;
    [Dependency] private readonly PowerCellSystem _cell = default!;
    [Dependency] private readonly SharedAudioSystem _audio = default!;
    [Dependency] private readonly SharedDoAfterSystem _doAfter = default!;
    [Dependency] private readonly SharedInteractionSystem _interaction = default!;
    [Dependency] private readonly SharedPhysicsSystem _physics = default!;
    [Dependency] private readonly SharedPopupSystem _popup = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<ShipCutterComponent, AfterInteractEvent>(OnAfterInteract);
        SubscribeLocalEvent<ShipCutterComponent, UseInHandEvent>(OnUseInHand);
        SubscribeLocalEvent<ShipCutterComponent, ShipCutterDoAfterEvent>(OnDoAfter);
    }

    private void OnAfterInteract(Entity<ShipCutterComponent> ent, ref AfterInteractEvent args)
    {
        if (args.Handled || !args.CanReach)
            return;

        if (!TryPickTarget(ent.Comp, args.Target, args.ClickLocation, out var target))
            return;

        args.Handled = true;

        if (!TryResolve(ent.Comp, target, out _, out _, out _))
        {
            _popup.PopupEntity(Loc.GetString("ship-cutter-cannot-cut"), ent, args.User);
            return;
        }

        if (ent.Comp.Queue.Contains(target))
            return;

        if (ent.Comp.Queue.Count >= ent.Comp.MaxQueue)
        {
            _popup.PopupEntity(Loc.GetString("ship-cutter-queue-full"), ent, args.User);
            return;
        }

        ent.Comp.Queue.Add(target);
        Dirty(ent);

        if (!ent.Comp.Working)
            StartNext(ent, args.User);
    }

    /// <summary>
    /// Using the cutter in hand drops the queue and stops the cut in progress.
    /// </summary>
    private void OnUseInHand(Entity<ShipCutterComponent> ent, ref UseInHandEvent args)
    {
        if (args.Handled || ent.Comp.Queue.Count == 0)
            return;

        args.Handled = true;
        ClearQueue(ent);
        _popup.PopupEntity(Loc.GetString("ship-cutter-queue-cleared"), ent, args.User);
    }

    private void ClearQueue(Entity<ShipCutterComponent> ent)
    {
        ent.Comp.Queue.Clear();
        Dirty(ent);

        if (ent.Comp.ActiveDoAfter is { } id)
            _doAfter.Cancel(id);

        ent.Comp.Working = false;
        ent.Comp.ActiveDoAfter = null;
    }

    /// <summary>
    /// Works out what a click is aimed at: a wall that can be cut loose, or the seam next to the click.
    /// </summary>
    private bool TryPickTarget(ShipCutterComponent cutter, EntityUid? clicked, Robust.Shared.Map.EntityCoordinates click, out ShipCutterTarget target)
    {
        target = default;

        if (clicked is { } wall && Transform(wall).Anchored && _whitelist.IsWhitelistPass(cutter.WallWhitelist, wall))
        {
            target = ShipCutterTarget.Wall(GetNetEntity(wall));
            return true;
        }

        if (!TryGetSeam(TransformSystem.ToMapCoordinates(click), cutter.EdgeTolerance, out var gridUid, out _, out var tile, out var edge))
            return false;

        target = ShipCutterTarget.Seam(GetNetEntity(gridUid), tile, edge);
        return true;
    }

    /// <summary>
    /// Checks the target can still be cut, and gives the entity to do-after on with the delay and charge it takes.
    /// </summary>
    private bool TryResolve(ShipCutterComponent cutter, ShipCutterTarget target, out EntityUid entity, out float delay, out float charge)
    {
        entity = default;
        delay = 0f;
        charge = 0f;

        if (!TryGetEntity(target.Entity, out var uid) || TerminatingOrDeleted(uid.Value))
            return false;

        entity = uid.Value;

        if (target.IsWall)
        {
            if (!Transform(entity).Anchored || !_whitelist.IsWhitelistPass(cutter.WallWhitelist, entity))
                return false;

            delay = cutter.WallDelay;
            charge = cutter.WallCharge;
            return true;
        }

        if (!TryComp<MapGridComponent>(entity, out var grid) || !CanCut(entity, grid, target.Tile, target.Edge))
            return false;

        delay = cutter.TileDelay;
        charge = cutter.TileCharge;
        return true;
    }

    /// <summary>
    /// Starts the first target in the queue that can still be cut. Targets that can not are dropped.
    /// </summary>
    private void StartNext(Entity<ShipCutterComponent> ent, EntityUid user)
    {
        var cutter = ent.Comp;

        while (cutter.Queue.Count > 0)
        {
            var target = cutter.Queue[0];

            if (!TryResolve(cutter, target, out var entity, out var delay, out var charge) ||
                !IsInReach(user, entity, target))
            {
                cutter.Queue.RemoveAt(0);
                _popup.PopupEntity(Loc.GetString("ship-cutter-skipped"), ent, user);
                continue;
            }

            if (!_cell.HasCharge(ent, charge, user: user))
            {
                _popup.PopupEntity(Loc.GetString("ship-cutter-no-charge"), ent, user);
                cutter.Queue.Clear();
                break;
            }

            var args = new DoAfterArgs(EntityManager, user, delay, new ShipCutterDoAfterEvent(target), ent, target: entity, used: ent)
            {
                BreakOnDamage = true,
                BreakOnMove = true,
                NeedHand = true,
            };

            if (!_doAfter.TryStartDoAfter(args, out var id))
            {
                cutter.Queue.Clear();
                break;
            }

            cutter.Working = true;
            cutter.ActiveDoAfter = id;
            Dirty(ent);
            _audio.PlayPvs(cutter.Sound, ent);
            return;
        }

        cutter.Working = false;
        cutter.ActiveDoAfter = null;
        Dirty(ent);
    }

    private bool IsInReach(EntityUid user, EntityUid entity, ShipCutterTarget target)
    {
        if (target.IsWall)
            return _interaction.InRangeUnobstructed(user, entity, popup: false);

        if (!TryComp<MapGridComponent>(entity, out var grid))
            return false;

        return _interaction.InRangeUnobstructed(user, MapSystem.GridTileToLocal(entity, grid, target.Tile), popup: false);
    }

    private void OnDoAfter(Entity<ShipCutterComponent> ent, ref ShipCutterDoAfterEvent args)
    {
        if (args.Handled)
            return;

        args.Handled = true;

        var cutter = ent.Comp;
        cutter.Working = false;
        cutter.ActiveDoAfter = null;

        // The user moved or was hurt: the rest of the queue goes too.
        if (args.Cancelled)
        {
            cutter.Queue.Clear();
            Dirty(ent);
            return;
        }

        cutter.Queue.Remove(args.Cut);

        if (!TryResolve(cutter, args.Cut, out var entity, out _, out var charge))
        {
            StartNext(ent, args.User);
            return;
        }

        if (!_cell.TryUseCharge(ent, charge, user: args.User))
        {
            _popup.PopupEntity(Loc.GetString("ship-cutter-no-charge"), ent, args.User);
            cutter.Queue.Clear();
            Dirty(ent);
            return;
        }

        if (args.Cut.IsWall)
            CutLoose(entity);
        else if (TryComp<MapGridComponent>(entity, out var grid))
            AddCut(entity, grid, args.Cut.Tile, args.Cut.Edge);

        _audio.PlayPvs(cutter.DoneSound, ent);
        StartNext(ent, args.User);
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

    private void AddCut(EntityUid gridUid, MapGridComponent grid, Vector2i tile, CutEdges edge)
    {
        _cuts.AddCut(gridUid, tile, edge);

        // GridCutsComponent exists now.
        var other = tile + edge.Offset();
        TrySplit(gridUid, grid, Comp<GridCutsComponent>(gridUid), tile, other);
    }
}
