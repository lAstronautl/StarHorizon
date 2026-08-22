using System.Linq;
using System.Numerics;
using Content.Server._Horizon.StationDeployment.Components;
using Content.Server.Cargo.Systems;
using Content.Server.Shuttles.Components;
using Content.Server.Shuttles.Events;
using Content.Server.Shuttles.Systems;
using Content.Server.Station.Systems;
using Content.Shared._Horizon.StationDeployment;
using Content.Shared._Horizon.StationDeployment.Components;
using Content.Shared._Horizon.StationDeployment.Prototypes;
using Content.Shared.Popups;
using Content.Shared.Research.Prototypes;
using Robust.Shared.EntitySerialization.Systems;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;

namespace Content.Server._Horizon.StationDeployment;

/// <summary>
/// Manages a deployed station's order pool and its task console's "summon"/"recall" cargo capsule flow.
/// </summary>
public sealed class StationOrderSystem : EntitySystem
{
    [Dependency] private readonly SharedUserInterfaceSystem _uiSystem = default!;
    [Dependency] private readonly StationSystem _station = default!;
    [Dependency] private readonly IPrototypeManager _protoMan = default!;
    [Dependency] private readonly IRobustRandom _random = default!;
    [Dependency] private readonly CargoSystem _cargo = default!;
    [Dependency] private readonly ShuttleSystem _shuttles = default!;
    [Dependency] private readonly DockingSystem _docking = default!;
    [Dependency] private readonly MapLoaderSystem _mapLoader = default!;
    [Dependency] private readonly SharedPopupSystem _popup = default!;
    [Dependency] private readonly SharedTransformSystem _transform = default!;

    /// <summary>
    /// How far from the console's grid the capsule is loaded before it FTLs in to dock,
    /// so it doesn't pop into existence directly on top of the station.
    /// </summary>
    private static readonly Vector2 CapsuleSpawnOffset = new(0f, 50f);

    private const string CargoCapsuleDockTag = "CargoCapsuleDock";

    // Note: the base Industrial/Arsenal/Experimental/CivilianServices disciplines
    // (Resources/Prototypes/Research/disciplines.yml) are made abstract by this fork's
    // /Prototypes/Research entry in Resources/IgnoredPrototypes/ignoredPrototypes.yml ("Moved
    // science techs"), so they can't be Index()'d - use the fork's own active NF discipline set
    // instead (same sprite states, so visually equivalent to the vanilla icons).
    private static readonly ProtoId<TechDisciplinePrototype>[] DevelopmentCategories =
    {
        "NFEngineering", "NFArsenalMercenary", "NFScience", "NFService"
    };

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<StationOrderDatabaseComponent, MapInitEvent>(OnOrderDbMapInit);
        SubscribeLocalEvent<StationTaskConsoleComponent, BoundUIOpenedEvent>(OnUiOpened);
        SubscribeLocalEvent<StationTaskConsoleComponent, StationOrderSummonCapsuleMessage>(OnSummon);
        SubscribeLocalEvent<StationTaskConsoleComponent, StationOrderRecallCapsuleMessage>(OnRecall);
        SubscribeLocalEvent<CargoCapsuleComponent, FTLCompletedEvent>(OnCapsuleDocked);
    }

    private void OnOrderDbMapInit(Entity<StationOrderDatabaseComponent> ent, ref MapInitEvent args)
    {
        FillOrderDatabase(ent);
    }

    private void OnUiOpened(Entity<StationTaskConsoleComponent> ent, ref BoundUIOpenedEvent args)
    {
        UpdateUi(ent);
    }

    private void OnSummon(Entity<StationTaskConsoleComponent> ent, ref StationOrderSummonCapsuleMessage args)
    {
        if (_station.GetOwningStation(ent.Owner) is not { Valid: true } station)
            return;

        if (FindActiveCapsule(station) != null)
        {
            _popup.PopupEntity(Loc.GetString("station-order-console-capsule-already-present"), ent, args.Actor, PopupType.SmallCaution);
            return;
        }

        if (Transform(ent.Owner).GridUid is not { Valid: true } consoleGrid)
            return;

        var mapId = Transform(consoleGrid).MapID;
        var spawnPos = _transform.GetWorldPosition(consoleGrid) + CapsuleSpawnOffset;
        if (!_mapLoader.TryLoadGrid(mapId, ent.Comp.CapsulePath, out var capsuleGrid, offset: spawnPos))
        {
            _popup.PopupEntity(Loc.GetString("station-order-console-capsule-spawn-failed"), ent, args.Actor, PopupType.MediumCaution);
            return;
        }

        var capsuleComp = EnsureComp<CargoCapsuleComponent>(capsuleGrid.Value.Owner);
        capsuleComp.OwningStation = station;

        var shuttle = EnsureComp<ShuttleComponent>(capsuleGrid.Value.Owner);
        _shuttles.FTLToDock(capsuleGrid.Value.Owner, shuttle, consoleGrid, hyperspaceTime: ent.Comp.CapsuleTravelTime, priorityTag: CargoCapsuleDockTag);

        UpdateUi(ent);
    }

    private void OnRecall(Entity<StationTaskConsoleComponent> ent, ref StationOrderRecallCapsuleMessage args)
    {
        if (_station.GetOwningStation(ent.Owner) is not { Valid: true } station)
            return;

        if (FindActiveCapsule(station) is not { } capsule || !capsule.Comp.Docked)
            return;

        EvaluateAndConsume(station, capsule.Owner);
        _docking.UndockDocks(capsule.Owner);
        QueueDel(capsule.Owner);

        UpdateUi(ent);
    }

    private void OnCapsuleDocked(Entity<CargoCapsuleComponent> ent, ref FTLCompletedEvent args)
    {
        ent.Comp.Docked = true;

        if (ent.Comp.OwningStation is not { Valid: true } station)
            return;

        var query = EntityQueryEnumerator<StationTaskConsoleComponent>();
        while (query.MoveNext(out var uid, out var console))
        {
            if (_station.GetOwningStation(uid) == station)
                UpdateUi((uid, console));
        }
    }

    private void EvaluateAndConsume(EntityUid station, EntityUid capsuleGrid)
    {
        if (!TryComp<StationOrderDatabaseComponent>(station, out var orderDb) ||
            !TryComp<StationDevelopmentComponent>(station, out var devel))
            return;

        var entities = new HashSet<EntityUid>();
        var enumerator = Transform(capsuleGrid).ChildEnumerator;
        while (enumerator.MoveNext(out var child))
        {
            entities.UnionWith(_cargo.GetBountyEntities(child));
        }

        foreach (var order in orderDb.Orders.ToArray())
        {
            if (!_protoMan.TryIndex(order.Order, out var prototype))
                continue;

            if (!_cargo.IsBountyComplete(entities, prototype.Entries, out _))
                continue;

            orderDb.Orders.Remove(order);
            devel.Progress.TryGetValue(prototype.Category, out var progress);
            devel.Progress[prototype.Category] = progress + 1;
        }

        FillOrderDatabase((station, orderDb));
    }

    private void FillOrderDatabase(Entity<StationOrderDatabaseComponent> ent)
    {
        while (ent.Comp.Orders.Count < ent.Comp.MaxOrders)
        {
            if (!TryAddOrder(ent))
                break;
        }
    }

    private bool TryAddOrder(Entity<StationOrderDatabaseComponent> ent)
    {
        var allOrders = _protoMan.EnumeratePrototypes<StationOrderPrototype>().ToList();
        if (allOrders.Count == 0)
            return false;

        var filtered = allOrders.Where(o => ent.Comp.Orders.All(active => active.Order != o.ID)).ToList();
        var pool = filtered.Count == 0 ? allOrders : filtered;
        var picked = _random.Pick(pool);

        var newOrder = new StationOrderData(picked, ent.Comp.TotalOrders);
        if (ent.Comp.Orders.Any(o => o.Id == newOrder.Id))
            return false;

        ent.Comp.Orders.Add(newOrder);
        ent.Comp.TotalOrders++;
        return true;
    }

    private Entity<CargoCapsuleComponent>? FindActiveCapsule(EntityUid station)
    {
        var query = EntityQueryEnumerator<CargoCapsuleComponent>();
        while (query.MoveNext(out var uid, out var comp))
        {
            if (comp.OwningStation == station)
                return (uid, comp);
        }

        return null;
    }

    private void UpdateUi(Entity<StationTaskConsoleComponent> ent)
    {
        if (_station.GetOwningStation(ent.Owner) is not { Valid: true } station)
            return;

        if (!TryComp<StationOrderDatabaseComponent>(station, out var orderDb) ||
            !TryComp<StationDevelopmentComponent>(station, out var devel))
            return;

        var orders = orderDb.Orders.Select(o => new StationOrderUiEntry(o.Id, o.Order)).ToList();
        var levels = BuildLevels(devel);
        var capsule = FindActiveCapsule(station);

        _uiSystem.SetUiState(ent.Owner, StationTaskConsoleUiKey.Key,
            new StationTaskConsoleBuiState(orders, levels, capsule != null, capsule?.Comp.Docked ?? false));
    }

    private static Dictionary<ProtoId<TechDisciplinePrototype>, int> BuildLevels(StationDevelopmentComponent devel)
    {
        var levels = new Dictionary<ProtoId<TechDisciplinePrototype>, int>();
        foreach (var category in DevelopmentCategories)
        {
            devel.Progress.TryGetValue(category, out var progress);
            levels[category] = progress / devel.OrdersPerLevel;
        }

        return levels;
    }
}
