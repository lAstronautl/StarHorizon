using System.Numerics;
using Content.Server.StationEvents.Events;
using Content.Server.Salvage;
using Content.Server.Salvage.Expeditions;
using Content.Server.Shuttles.Components;
using Content.Server.Shuttles.Events;
using Content.Server.Shuttles.Systems;
using Content.Shared.Salvage.Expeditions;
using Content.Shared.Shuttles.Components;
using Content.Shared.Weapons.Ranged.Components;
using Robust.Server.GameObjects;
using Robust.Shared.Containers;
using Robust.Shared.EntitySerialization;
using Robust.Shared.EntitySerialization.Systems;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Physics.Components;
using Robust.Shared.Physics.Systems;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;
using Robust.Shared.Timing;
using Robust.Shared.Utility;

namespace Content.Server._StarHorizon.Expedition;

/// <summary>
/// Эвакуация с планетной экспедиции: маяк с крюками, alleycat, игроки висят снаружи корабля и остаются в космосе
/// у планеты, когда корабль удаляется (как при событии с шаттлом синдиката).
/// </summary>
public sealed class ExpeditionExtractionSystem : EntitySystem
{
    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly IRobustRandom _random = default!;
    [Dependency] private readonly SalvageSystem _salvage = default!;
    [Dependency] private readonly ShuttleSystem _shuttle = default!;
    [Dependency] private readonly MapLoaderSystem _loader = default!;
    [Dependency] private readonly MapSystem _map = default!;
    [Dependency] private readonly TransformSystem _xform = default!;
    [Dependency] private readonly SharedPhysicsSystem _physics = default!;
    [Dependency] private readonly SharedContainerSystem _container = default!;
    [Dependency] private readonly EntityLookupSystem _lookup = default!;
    [Dependency] private readonly LinkedLifecycleGridSystem _linkedLifecycle = default!;

    public static readonly EntProtoId BeaconProto = "StarHorizonExtractionBeacon";
    public static readonly EntProtoId GunProto = "WeaponGrapplingGun";
    public static readonly ResPath ShuttlePath = new("/Maps/_NF/Shuttles/Bus/alleycat.yml");

    private TimeSpan _nextUpdate;

    public override void Initialize()
    {
        SubscribeLocalEvent<ExpeditionExtractionComponent, ComponentShutdown>(OnExpeditionShutdown);
        SubscribeLocalEvent<FTLCompletedEvent>(OnFTLCompleted);
        SubscribeLocalEvent<ExtractionShuttleComponent, ComponentShutdown>(OnShuttleShutdown);
        SubscribeLocalEvent<ExtractionPassengerComponent, ComponentStartup>(OnPassengerStartup);
        SubscribeLocalEvent<ExtractionPassengerComponent, ComponentShutdown>(OnPassengerShutdown);
    }

    public override void Update(float frameTime)
    {
        // Каждый тик: люди и маяк снаружи корпуса не должны возвращаться на карту.
        var passengers = EntityQueryEnumerator<ExtractionPassengerComponent, TransformComponent>();
        while (passengers.MoveNext(out var passenger, out var pass, out var xform))
        {
            if (!Exists(pass.Grid) || TerminatingOrDeleted(pass.Grid))
            {
                RemCompDeferred<ExtractionPassengerComponent>(passenger);
                continue;
            }

            // Движок может вернуть сущность вне тайлов на карту: держим её на корабле.
            if (xform.ParentUid != pass.Grid)
                _xform.SetCoordinates(passenger, xform, new EntityCoordinates(pass.Grid, pass.Local));
        }

        if (_timing.CurTime < _nextUpdate)
            return;
        _nextUpdate = _timing.CurTime + TimeSpan.FromSeconds(1);

        var query = EntityQueryEnumerator<SalvageExpeditionComponent>();
        while (query.MoveNext(out var uid, out var expedition))
        {
            if (!expedition.Planet.IsValid())
                continue;

            var extraction = EnsureComp<ExpeditionExtractionComponent>(uid);
            UpdateExpedition((uid, extraction), expedition);
        }

        var shuttles = EntityQueryEnumerator<ExtractionShuttleComponent>();
        while (shuttles.MoveNext(out var shuttle, out var comp))
        {
            if (comp.DeleteAt is { } at && _timing.CurTime >= at)
                DeleteShuttle((shuttle, comp));
        }

    }

    private void UpdateExpedition(Entity<ExpeditionExtractionComponent> ent, SalvageExpeditionComponent expedition)
    {
        var remaining = expedition.EndTime - _timing.CurTime;
        var comp = ent.Comp;

        if (comp.Stage == ExtractionStage.None && remaining < comp.BeaconBefore)
            PlaceBeacon(ent, expedition);

        if (comp.Stage == ExtractionStage.BeaconPlaced && remaining < comp.ArriveBefore)
            SendShuttle(ent, expedition);

        if (comp.Stage == ExtractionStage.ShuttleSent
            && remaining < comp.DepartBefore
            && comp.Shuttle is { } shuttle
            && TryComp<ExtractionShuttleComponent>(shuttle, out var shuttleComp)
            && shuttleComp.Arrived
            && !HasComp<FTLComponent>(shuttle)) // после прилёта у корабля ещё идёт перезарядка прыжка
        {
            Depart(ent, expedition, (shuttle, shuttleComp));
        }
    }

    #region Beacon

    private void PlaceBeacon(Entity<ExpeditionExtractionComponent> ent, SalvageExpeditionComponent expedition)
    {
        // Даже если не получилось, второй раз не пытаемся: экспедиция просто закроется как обычно.
        ent.Comp.Stage = ExtractionStage.BeaconPlaced;

        var pos = FindFreeSpot(ent.Owner, expedition.LandingCoordinates);
        var coords = new EntityCoordinates(ent.Owner, pos);

        ent.Comp.BeaconPosition = pos;
        ent.Comp.Beacon = Spawn(BeaconProto, coords);
        for (var i = 0; i < 2; i++)
            Spawn(GunProto, new EntityCoordinates(ent.Owner, pos + new Vector2(i == 0 ? -1.2f : 1.2f, 0.8f)));

        _salvage.Announce(ent.Owner, Loc.GetString("expedition-extraction-beacon",
            ("x", (int) pos.X), ("y", (int) pos.Y), ("minutes", (int) ent.Comp.BeaconBefore.TotalMinutes)));
    }

    private Vector2 FindFreeSpot(EntityUid map, Vector2 around)
    {
        if (!TryComp<MapGridComponent>(map, out var grid))
            return around + new Vector2(3, 0);

        for (var radius = 3f; radius <= 12f; radius += 1.5f)
        {
            for (var step = 0; step < 8; step++)
            {
                var pos = around + Angle.FromDegrees(step * 45).ToVec() * radius;
                var coords = new EntityCoordinates(map, pos);
                if (!_map.TryGetTileRef(map, grid, coords, out var tile) || tile.Tile.IsEmpty)
                    continue;
                if (_lookup.AnyEntitiesIntersecting(Comp<MapComponent>(map).MapId, Box2.CenteredAround(pos, new Vector2(1.5f, 1.5f))))
                    continue;
                return pos;
            }
        }

        return around + new Vector2(3, 0);
    }

    #endregion

    #region Shuttle

    private void SendShuttle(Entity<ExpeditionExtractionComponent> ent, SalvageExpeditionComponent expedition)
    {
        ent.Comp.Stage = ExtractionStage.ShuttleSent;

        if (ent.Comp.Beacon is not { } beacon || !Exists(beacon))
            return;

        var options = new DeserializationOptions { InitializeMaps = true };
        if (!_loader.TryLoadGrid(ShuttlePath, out _, out var grid, options))
        {
            Log.Error($"Could not load extraction shuttle {ShuttlePath}");
            return;
        }

        var shuttle = grid.Value.Owner;
        ent.Comp.Shuttle = shuttle;
        var shuttleComp = EnsureComp<ExtractionShuttleComponent>(shuttle);
        shuttleComp.Expedition = ent.Owner;
        shuttleComp.Beacon = beacon;
        shuttleComp.DeleteAfter = ent.Comp.DeleteAfter;

        TryComp<ShuttleComponent>(shuttle, out var shuttleData);
        var target = new EntityCoordinates(ent.Owner, ent.Comp.BeaconPosition + new Vector2(ent.Comp.LandingOffset, 0));
        _shuttle.FTLToCoordinates(shuttle, shuttleData, target, Angle.Zero,
            _shuttle.DefaultStartupTime, _shuttle.DefaultTravelTime);

        _salvage.Announce(ent.Owner, Loc.GetString("expedition-extraction-arriving",
            ("meters", (int) ent.Comp.LandingOffset)));
    }

    private void OnFTLCompleted(ref FTLCompletedEvent args)
    {
        if (!TryComp<ExtractionShuttleComponent>(args.Entity, out var shuttle))
            return;

        if (!shuttle.Departed)
        {
            shuttle.Arrived = true;
            AttachBeacon((args.Entity, shuttle));
            if (Exists(shuttle.Expedition))
                _salvage.Announce(shuttle.Expedition, Loc.GetString("expedition-extraction-arrived"));
        }
        else
        {
            shuttle.DeleteAt = _timing.CurTime + shuttle.DeleteAfter;
        }
    }

    /// <summary>Корабль «берёт» маяк: тот становится частью корпуса и едет вместе с ним.</summary>
    private void AttachBeacon(Entity<ExtractionShuttleComponent> shuttle)
    {
        if (!Exists(shuttle.Comp.Beacon) || !TryComp<MapGridComponent>(shuttle, out var grid))
            return;

        var box = grid.LocalAABB;
        var local = new Vector2(box.Right + 1f, box.Center.Y);
        _xform.SetCoordinates(shuttle.Comp.Beacon, new EntityCoordinates(shuttle, local));

        // Маяк стоит снаружи корпуса, вне тайлов: держим его на корабле так же, как пассажиров.
        var passenger = EnsureComp<ExtractionPassengerComponent>(shuttle.Comp.Beacon);
        passenger.Grid = shuttle;
        passenger.Local = local;
    }

    private void Depart(Entity<ExpeditionExtractionComponent> ent, SalvageExpeditionComponent expedition,
        Entity<ExtractionShuttleComponent> shuttle)
    {
        ent.Comp.Stage = ExtractionStage.Departed;
        shuttle.Comp.Departed = true;

        var count = CarryHookedPlayers(shuttle);

        if (!TryComp<TransformComponent>(expedition.Planet, out var planetXform) || planetXform.MapUid is not { } spaceMap)
        {
            Log.Error("Extraction: planet has no map, shuttle stays.");
            return;
        }

        var planetPos = _xform.GetWorldPosition(planetXform);
        var target = new EntityCoordinates(spaceMap, planetPos + _random.NextAngle().ToVec() * ent.Comp.PlanetDistance);
        TryComp<ShuttleComponent>(shuttle, out var shuttleData);
        _shuttle.FTLToCoordinates(shuttle, shuttleData, target, Angle.Zero,
            _shuttle.DefaultStartupTime, _shuttle.DefaultTravelTime);

        _salvage.Announce(ent.Owner, Loc.GetString("expedition-extraction-departing", ("count", count)));
    }

    /// <summary>
    /// Забирает всех, чей крюк зацеплен за маяк: крюк убирается (верёвка через БСС не живёт), игрок встаёт «снаружи»
    /// на корабль и остаётся его дочерним объектом.
    /// </summary>
    private int CarryHookedPlayers(Entity<ExtractionShuttleComponent> shuttle)
    {
        var beacon = shuttle.Comp.Beacon;
        var count = 0;
        var guns = new List<(EntityUid Gun, EntityUid Hook)>();

        var query = EntityQueryEnumerator<GrapplingGunComponent>();
        while (query.MoveNext(out var gun, out var gunComp))
        {
            if (gunComp.Projectile is { } hook
                && Exists(hook)
                && Transform(hook).ParentUid == beacon)
            {
                guns.Add((gun, hook));
            }
        }

        var inv = _xform.GetInvWorldMatrix(shuttle);
        foreach (var (gun, hook) in guns)
        {
            var holder = _container.TryGetContainingContainer((gun, null, null), out var container)
                ? container.Owner
                : Transform(gun).ParentUid;

            QueueDel(hook);
            if (TryComp<GrapplingGunComponent>(gun, out var gunComp))
            {
                gunComp.Projectile = null;
                gunComp.Reeling = false;
                Dirty(gun, gunComp);
            }

            if (!HasComp<PhysicsComponent>(holder) || HasComp<ExtractionPassengerComponent>(holder))
                continue;

            var local = Vector2.Transform(_xform.GetWorldPosition(holder), inv);
            _physics.SetLinearVelocity(holder, Vector2.Zero);
            _xform.SetCoordinates(holder, new EntityCoordinates(shuttle, local));

            var passenger = EnsureComp<ExtractionPassengerComponent>(holder);
            passenger.Grid = shuttle;
            passenger.Local = local;
            count++;
        }

        return count;
    }

    private void DeleteShuttle(Entity<ExtractionShuttleComponent> shuttle)
    {
        shuttle.Comp.DeleteAt = null;

        var passengers = EntityQueryEnumerator<ExtractionPassengerComponent>();
        var list = new List<EntityUid>();
        while (passengers.MoveNext(out var uid, out var pass))
        {
            if (pass.Grid == shuttle.Owner)
                list.Add(uid);
        }

        // Людей оставляем на карте в космосе, корабль удаляется.
        _linkedLifecycle.UnparentPlayersFromGrid(shuttle, deleteGrid: true);

        foreach (var uid in list)
            RemCompDeferred<ExtractionPassengerComponent>(uid);
    }

    // Снаружи корпуса нет тайлов: без этого движок сразу вернёт сущность с корабля на карту.
    private void OnPassengerStartup(Entity<ExtractionPassengerComponent> ent, ref ComponentStartup args)
    {
        Transform(ent).GridTraversal = false;
    }

    private void OnPassengerShutdown(Entity<ExtractionPassengerComponent> ent, ref ComponentShutdown args)
    {
        if (!TerminatingOrDeleted(ent))
            Transform(ent).GridTraversal = true;
    }

    private void OnShuttleShutdown(Entity<ExtractionShuttleComponent> ent, ref ComponentShutdown args)
    {
        if (Exists(ent.Comp.Expedition) && TryComp<ExpeditionExtractionComponent>(ent.Comp.Expedition, out var extraction))
            extraction.Shuttle = null;
    }

    /// <summary>Карта экспедиции закрылась, а корабль ещё не улетел: он ни с чем не нужен.</summary>
    private void OnExpeditionShutdown(Entity<ExpeditionExtractionComponent> ent, ref ComponentShutdown args)
    {
        if (ent.Comp.Shuttle is { } shuttle
            && Exists(shuttle)
            && TryComp<ExtractionShuttleComponent>(shuttle, out var comp)
            && !comp.Departed)
        {
            QueueDel(shuttle);
        }
    }

    #endregion
}
