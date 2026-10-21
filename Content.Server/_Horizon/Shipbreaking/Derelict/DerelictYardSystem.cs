using Content.Server._NF.Bank;
using Content.Server._NF.Shipyard.Systems;
using Content.Server.Chat.Systems;
using Content.Server.Station.Systems;
using Content.Shared._Horizon.Shipbreaking.Derelict;
using Content.Shared._NF.Bank.Components;
using Content.Shared._NF.Bank.BUI;
using Content.Shared.Doors.Components;
using Content.Shared.Mobs.Components;
using Content.Shared.Popups;
using Content.Shared.Tag;
using Robust.Server.GameObjects;
using Robust.Shared.Containers;
using Robust.Shared.EntitySerialization.Systems;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.Server._Horizon.Shipbreaking.Derelict;

/// <summary>
/// The derelict yard: sells wrecks for the price of the station account and deletes them, with every piece cut off
/// them, when the lease is over. What the wreck is scrapped for pays into the account through the scrap machines.
/// </summary>
public sealed class DerelictYardSystem : EntitySystem
{
    private static readonly ProtoId<TagPrototype> WallTag = "Wall";
    private static readonly ProtoId<TagPrototype> WindowTag = "Window";

    private static readonly TimeSpan FirstWarning = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan LastWarning = TimeSpan.FromMinutes(1);

    [Dependency] private readonly BankSystem _bank = default!;
    [Dependency] private readonly ChatSystem _chat = default!;
    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly IPrototypeManager _proto = default!;
    [Dependency] private readonly MapLoaderSystem _mapLoader = default!;
    [Dependency] private readonly MapSystem _map = default!;
    [Dependency] private readonly SharedContainerSystem _container = default!;
    [Dependency] private readonly SharedPopupSystem _popup = default!;
    [Dependency] private readonly SharedTransformSystem _transform = default!;
    [Dependency] private readonly ShipyardSystem _shipyard = default!;
    [Dependency] private readonly StationSystem _station = default!;
    [Dependency] private readonly TagSystem _tag = default!;
    [Dependency] private readonly UserInterfaceSystem _ui = default!;

    private readonly Dictionary<string, (Vector2i[] Tiles, DerelictPreviewKind[] Kinds)> _previews = new();

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<DerelictYardConsoleComponent, BoundUIOpenedEvent>(OnOpened);
        SubscribeLocalEvent<DerelictYardConsoleComponent, DerelictPurchaseMessage>(OnPurchase);
        SubscribeLocalEvent<DerelictYardConsoleComponent, DerelictPreviewRequestMessage>(OnPreviewRequest);
        SubscribeLocalEvent<DerelictLeaseComponent, PostGridSplitEvent>(OnSplit);
    }

    private void OnOpened(Entity<DerelictYardConsoleComponent> ent, ref BoundUIOpenedEvent args)
    {
        UpdateUi(ent);
    }

    private void UpdateUi(Entity<DerelictYardConsoleComponent> ent)
    {
        var state = new DerelictYardState();

        foreach (var id in ent.Comp.Vessels)
        {
            if (!_proto.TryIndex(id, out var vessel))
                continue;

            state.Vessels.Add(new DerelictVesselEntry
            {
                Id = vessel.ID,
                Name = Loc.GetString(vessel.Name),
                Description = vessel.Description is { } description ? Loc.GetString(description) : string.Empty,
                Price = vessel.Price,
                Lifetime = vessel.Lifetime,
            });
        }

        if (_station.GetOwningStation(ent) != null && _bank.TryGetBalance(SectorBankAccount.Frontier, out var balance))
            state.Balance = balance;

        _ui.SetUiState(ent.Owner, DerelictYardUiKey.Key, state);
    }

    private void OnPurchase(Entity<DerelictYardConsoleComponent> ent, ref DerelictPurchaseMessage args)
    {
        if (!ent.Comp.Vessels.Contains(args.Id) || !_proto.TryIndex<DerelictVesselPrototype>(args.Id, out var vessel))
            return;

        if (_station.GetOwningStation(ent) is not { } station)
        {
            _popup.PopupEntity(Loc.GetString("derelict-yard-no-station"), ent, args.Actor);
            return;
        }

        if (!_bank.TryGetBalance(SectorBankAccount.Frontier, out var balance) || balance < vessel.Price ||
            !_bank.TrySectorWithdraw(SectorBankAccount.Frontier, vessel.Price, LedgerEntryType.StationWithdrawalSupplies))
        {
            _popup.PopupEntity(Loc.GetString("derelict-yard-no-funds"), ent, args.Actor);
            return;
        }

        if (!_shipyard.TryPurchaseShuttle(station, vessel.ShuttlePath, out var shuttle))
        {
            _bank.TrySectorDeposit(SectorBankAccount.Frontier, vessel.Price, LedgerEntryType.StationDepositOther);
            _popup.PopupEntity(Loc.GetString("derelict-yard-failed"), ent, args.Actor);
            return;
        }

        var lease = EnsureComp<DerelictLeaseComponent>(shuttle.Value);
        lease.Expires = _timing.CurTime + vessel.Lifetime;
        lease.Group = shuttle.Value;
        lease.Station = station;

        _chat.DispatchStationAnnouncement(station,
            Loc.GetString("derelict-yard-bought", ("name", Loc.GetString(vessel.Name)), ("minutes", (int) vessel.Lifetime.TotalMinutes)),
            Loc.GetString("derelict-yard-sender"),
            playDefaultSound: false);

        UpdateUi(ent);
    }

    /// <summary>
    /// A piece cut off the wreck is part of the lease too.
    /// </summary>
    private void OnSplit(Entity<DerelictLeaseComponent> ent, ref PostGridSplitEvent args)
    {
        var lease = EnsureComp<DerelictLeaseComponent>(args.Grid);
        lease.Expires = ent.Comp.Expires;
        lease.Group = ent.Comp.Group;
        lease.Station = ent.Comp.Station;
        lease.Warned5 = ent.Comp.Warned5;
        lease.Warned1 = ent.Comp.Warned1;
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var expired = new List<EntityUid>();
        var warn5 = new HashSet<EntityUid>();
        var warn1 = new HashSet<EntityUid>();

        var query = EntityQueryEnumerator<DerelictLeaseComponent>();
        while (query.MoveNext(out var uid, out var lease))
        {
            var left = lease.Expires - _timing.CurTime;
            if (left <= TimeSpan.Zero)
                expired.Add(uid);
            else if (left <= LastWarning && !lease.Warned1)
                warn1.Add(lease.Group);
            else if (left <= FirstWarning && !lease.Warned5)
                warn5.Add(lease.Group);
        }

        foreach (var group in warn5)
        {
            Warn(group, 5, true);
        }

        foreach (var group in warn1)
        {
            Warn(group, 1, false);
        }

        if (expired.Count > 0)
            Expire(expired);
    }

    /// <summary>
    /// Announces once for the whole group of grids.
    /// </summary>
    private void Warn(EntityUid group, int minutes, bool first)
    {
        EntityUid? station = null;

        var query = EntityQueryEnumerator<DerelictLeaseComponent>();
        while (query.MoveNext(out _, out var lease))
        {
            if (lease.Group != group)
                continue;

            station ??= lease.Station;
            if (first)
                lease.Warned5 = true;
            else
                lease.Warned1 = true;
        }

        if (station != null)
        {
            _chat.DispatchStationAnnouncement(station.Value,
                Loc.GetString("derelict-yard-warning", ("minutes", minutes)),
                Loc.GetString("derelict-yard-sender"),
                playDefaultSound: false);
        }
    }

    /// <summary>
    /// Deletes the grids. Whoever is still aboard is put out into space first, only the wreck goes.
    /// </summary>
    private void Expire(List<EntityUid> grids)
    {
        var mobs = EntityQueryEnumerator<MobStateComponent, TransformComponent>();
        var rescued = new List<EntityUid>();
        while (mobs.MoveNext(out var mob, out _, out var xform))
        {
            if (xform.GridUid is { } grid && grids.Contains(grid))
                rescued.Add(mob);
        }

        foreach (var mob in rescued)
        {
            if (_container.TryGetContainingContainer((mob, null, null), out var container))
                _container.Remove(mob, container, force: true);

            // Straight onto the map, keeping the place: the grid under it is the one that goes.
            var xform = Transform(mob);
            if (xform.MapUid is { } map)
                _transform.SetParent(mob, xform, map);
        }

        foreach (var grid in grids)
        {
            QueueDel(grid);
        }
    }

    private void OnPreviewRequest(Entity<DerelictYardConsoleComponent> ent, ref DerelictPreviewRequestMessage args)
    {
        if (!ent.Comp.Vessels.Contains(args.Id) || !_proto.TryIndex<DerelictVesselPrototype>(args.Id, out var vessel))
            return;

        if (!_previews.TryGetValue(vessel.ID, out var preview))
        {
            if (!TryBuildPreview(vessel, out preview))
                return;

            _previews[vessel.ID] = preview;
        }

        _ui.ServerSendUiMessage(ent.Owner, DerelictYardUiKey.Key,
            new DerelictPreviewMessage(vessel.ID, preview.Tiles, preview.Kinds), args.Actor);
    }

    /// <summary>
    /// Loads the wreck on a map of its own, reads which tile holds what, and throws the map away.
    /// </summary>
    private bool TryBuildPreview(DerelictVesselPrototype vessel, out (Vector2i[] Tiles, DerelictPreviewKind[] Kinds) preview)
    {
        preview = default;

        _map.CreateMap(out var mapId, runMapInit: false);
        try
        {
            if (!_mapLoader.TryLoadGrid(mapId, vessel.ShuttlePath, out var grid))
                return false;

            var tiles = new List<Vector2i>();
            var kinds = new List<DerelictPreviewKind>();

            foreach (var tile in _map.GetAllTiles(grid.Value.Owner, grid.Value.Comp))
            {
                var kind = DerelictPreviewKind.Floor;
                foreach (var anchored in _map.GetAnchoredEntities(grid.Value.Owner, grid.Value.Comp, tile.GridIndices))
                {
                    var found = Classify(anchored);
                    if (found > kind)
                        kind = found;
                }

                tiles.Add(tile.GridIndices);
                kinds.Add(kind);
            }

            preview = (tiles.ToArray(), kinds.ToArray());
            return true;
        }
        finally
        {
            _map.DeleteMap(mapId);
        }
    }

    private DerelictPreviewKind Classify(EntityUid uid)
    {
        if (_tag.HasTag(uid, WallTag))
            return DerelictPreviewKind.Wall;

        if (HasComp<DoorComponent>(uid))
            return DerelictPreviewKind.Door;

        if (_tag.HasTag(uid, WindowTag))
            return DerelictPreviewKind.Window;

        return DerelictPreviewKind.Machine;
    }
}
