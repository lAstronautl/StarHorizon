using Content.Server.PowerCell;
using Content.Shared._Horizon.Shipbreaking;
using Content.Shared.DoAfter;
using Content.Shared.Interaction;
using Content.Shared.Interaction.Events;
using Content.Shared.Item;
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
/// which leak air and eventually split the grid. Each cut takes charge from the power cell of the cutter.
/// </summary>
public sealed partial class ShipCutterSystem : SharedShipCutterSystem
{
    [Dependency] private readonly EntityLookupSystem _lookup = default!;
    [Dependency] private readonly EntityWhitelistSystem _whitelist = default!;
    [Dependency] private readonly GridCutsSystem _cuts = default!;
    [Dependency] private readonly PowerCellSystem _cell = default!;
    [Dependency] private readonly MetaDataSystem _meta = default!;
    [Dependency] private readonly SharedItemSystem _item = default!;
    [Dependency] private readonly SharedAudioSystem _audio = default!;
    [Dependency] private readonly SharedDoAfterSystem _doAfter = default!;
    [Dependency] private readonly SharedPhysicsSystem _physics = default!;
    [Dependency] private readonly SharedPopupSystem _popup = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<ShipCutterComponent, MapInitEvent>(OnMapInit);
        SubscribeLocalEvent<ShipCutterComponent, UseInHandEvent>(OnUseInHand);
        SubscribeLocalEvent<ShipCutterComponent, AfterInteractEvent>(OnAfterInteract);
        SubscribeLocalEvent<ShipCutterComponent, ShipCutterDoAfterEvent>(OnDoAfter);
    }

    private void OnMapInit(Entity<ShipCutterComponent> ent, ref MapInitEvent args)
    {
        UpdateName(ent);
    }

    /// <summary>
    /// Using the tool in hand switches between cutting and welding.
    /// </summary>
    private void OnUseInHand(Entity<ShipCutterComponent> ent, ref UseInHandEvent args)
    {
        if (args.Handled)
            return;

        args.Handled = true;

        ent.Comp.Mode = ent.Comp.Mode == ShipToolMode.Cut ? ShipToolMode.Weld : ShipToolMode.Cut;
        Dirty(ent);
        UpdateName(ent);

        var changed = new ShipToolModeChangedEvent(ent.Comp.Mode);
        RaiseLocalEvent(ent, ref changed);

        _popup.PopupEntity(Loc.GetString(ent.Comp.Mode == ShipToolMode.Cut ? "ship-tool-mode-cut" : "ship-tool-mode-weld"), ent, args.User);
        _audio.PlayPvs(ent.Comp.ChangeSound, ent);
    }

    private void UpdateName(Entity<ShipCutterComponent> ent)
    {
        var cut = ent.Comp.Mode == ShipToolMode.Cut;
        _meta.SetEntityName(ent, Loc.GetString(cut ? ent.Comp.CutName : ent.Comp.WeldName));
        _meta.SetEntityDescription(ent, Loc.GetString(cut ? ent.Comp.CutDescription : ent.Comp.WeldDescription));

        // What is held in hand looks different too, if the prototype has a look for the mode.
        var prefix = cut ? ent.Comp.CutHeldPrefix : ent.Comp.WeldHeldPrefix;
        if (prefix != null)
            _item.SetHeldPrefix(ent, prefix);
    }

    private void OnAfterInteract(Entity<ShipCutterComponent> ent, ref AfterInteractEvent args)
    {
        if (args.Handled || !args.CanReach || ent.Comp.Mode != ShipToolMode.Cut)
            return;

        if (!TryPickTarget(ent.Comp, args.Target, args.ClickLocation, out var target))
            return;

        args.Handled = true;

        if (!TryResolve(ent.Comp, target, out var entity, out var delay, out var charge))
        {
            _popup.PopupEntity(Loc.GetString("ship-cutter-cannot-cut"), ent, args.User);
            return;
        }

        if (!_cell.HasCharge(ent, charge, user: args.User))
        {
            _popup.PopupEntity(Loc.GetString("ship-cutter-no-charge"), ent, args.User);
            return;
        }

        var doAfter = new DoAfterArgs(EntityManager, args.User, delay, new ShipCutterDoAfterEvent(target), ent, target: entity, used: ent)
        {
            BreakOnDamage = true,
            BreakOnMove = true,
            NeedHand = true,
        };

        if (_doAfter.TryStartDoAfter(doAfter))
            _audio.PlayPvs(ent.Comp.Sound, ent);
    }

    /// <summary>
    /// Works out what a click is aimed at: a wall that can be cut loose, or the seam next to the click.
    /// </summary>
    private bool TryPickTarget(ShipCutterComponent cutter, EntityUid? clicked, EntityCoordinates click, out ShipCutterTarget target)
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

    private void OnDoAfter(Entity<ShipCutterComponent> ent, ref ShipCutterDoAfterEvent args)
    {
        if (args.Cancelled || args.Handled)
            return;

        args.Handled = true;

        if (!TryResolve(ent.Comp, args.Cut, out var entity, out _, out var charge))
            return;

        if (!_cell.TryUseCharge(ent, charge, user: args.User))
        {
            _popup.PopupEntity(Loc.GetString("ship-cutter-no-charge"), ent, args.User);
            return;
        }

        if (args.Cut.IsWall)
            CutLoose(entity);
        else
            CutSeam(entity, args.Cut.Tile, args.Cut.Edge);

        _audio.PlayPvs(ent.Comp.DoneSound, ent);
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

    /// <summary>
    /// Cuts the seam and, if that cuts a part of the grid off, splits it into its own grid.
    /// </summary>
    public void CutSeam(EntityUid gridUid, Vector2i tile, CutEdges edge)
    {
        if (!TryComp<MapGridComponent>(gridUid, out var grid))
            return;

        _cuts.AddCut(gridUid, tile, edge);

        // GridCutsComponent exists now.
        var other = tile + edge.Offset();
        TrySplit(gridUid, grid, Comp<GridCutsComponent>(gridUid), tile, other);
    }
}
