using System.Numerics;
using Content.Server.StationEvents.Events;
using Content.Shared._StarHorizon.Expedition;
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

    public static readonly ResPath ShuttlePath = new("/Maps/_NF/Shuttles/Bus/alleycat.yml");

    private TimeSpan _nextUpdate;

    public override void Initialize()
    {
        SubscribeLocalEvent<ExpeditionExtractionComponent, ComponentShutdown>(OnExpeditionShutdown);
        SubscribeLocalEvent<FTLCompletedEvent>(OnFTLCompleted);
        SubscribeLocalEvent<ExtractionShuttleComponent, ComponentShutdown>(OnShuttleShutdown);
    }

    public override void Update(float frameTime)
    {
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

        if (comp.Stage == ExtractionStage.None && remaining < comp.ArriveBefore)
            SendShuttle(ent, expedition);

        if (comp.Stage == ExtractionStage.ShuttleSent
            && remaining < TimeSpan.FromSeconds(_shuttle.DefaultStartupTime) + comp.DepartMargin
            && comp.Shuttle is { } shuttle
            && TryComp<ExtractionShuttleComponent>(shuttle, out var shuttleComp)
            && shuttleComp.Arrived
            && !HasComp<FTLComponent>(shuttle)) // после прилёта у корабля ещё идёт перезарядка прыжка
        {
            Depart(ent, expedition, (shuttle, shuttleComp));
        }
    }

    #region Shuttle

    private void SendShuttle(Entity<ExpeditionExtractionComponent> ent, SalvageExpeditionComponent expedition)
    {
        // Даже если не получилось, второй раз не пытаемся: экспедиция просто закроется как обычно.
        ent.Comp.Stage = ExtractionStage.ShuttleSent;

        var options = new DeserializationOptions { InitializeMaps = true };
        if (!_loader.TryLoadGrid(ShuttlePath, out _, out var grid, options))
        {
            Log.Error($"Could not load extraction shuttle {ShuttlePath}");
            return;
        }

        var shuttle = grid.Value.Owner;
        ent.Comp.Shuttle = shuttle;
        EnsureComp<ExtractionShuttleMarkerComponent>(shuttle);
        var state = EnsureComp<ExpeditionExtractionStateComponent>(ent.Owner);
        state.Active = true;
        Dirty(ent.Owner, state);
        var shuttleComp = EnsureComp<ExtractionShuttleComponent>(shuttle);
        shuttleComp.Expedition = ent.Owner;
        shuttleComp.DeleteAfter = ent.Comp.DeleteAfter;

        var landing = expedition.LandingCoordinates + new Vector2(ent.Comp.LandingOffset, 0);
        TryComp<ShuttleComponent>(shuttle, out var shuttleData);
        _shuttle.FTLToCoordinates(shuttle, shuttleData, new EntityCoordinates(ent.Owner, landing), Angle.Zero,
            _shuttle.DefaultStartupTime, _shuttle.DefaultTravelTime);

        _salvage.Announce(ent.Owner, Loc.GetString("expedition-extraction-arriving",
            ("x", (int) landing.X), ("y", (int) landing.Y), ("minutes", (int) ent.Comp.ArriveBefore.TotalMinutes)));
    }

    private void OnFTLCompleted(ref FTLCompletedEvent args)
    {
        if (!TryComp<ExtractionShuttleComponent>(args.Entity, out var shuttle))
            return;

        if (!shuttle.Departed)
        {
            shuttle.Arrived = true;
            if (Exists(shuttle.Expedition))
            {
                _salvage.Announce(shuttle.Expedition, Loc.GetString("expedition-extraction-arrived"));
            }
        }
        else
        {
            shuttle.DeleteAt = _timing.CurTime + shuttle.DeleteAfter;
        }
    }

    private void Depart(Entity<ExpeditionExtractionComponent> ent, SalvageExpeditionComponent expedition,
        Entity<ExtractionShuttleComponent> shuttle)
    {
        ent.Comp.Stage = ExtractionStage.Departed;
        shuttle.Comp.Departed = true;

        // Клиентам: эвакуация закончилась, шаттл ушёл в БСС.
        if (TryComp<ExpeditionExtractionStateComponent>(ent.Owner, out var extractionState))
        {
            extractionState.Active = false;
            Dirty(ent.Owner, extractionState);
        }

        var marker = EnsureComp<ExtractionShuttleMarkerComponent>(shuttle);
        marker.Departed = true;
        Dirty(shuttle, marker);

        if (!TryComp<TransformComponent>(expedition.Planet, out var planetXform) || planetXform.MapUid is not { } spaceMap)
        {
            Log.Error("Extraction: planet has no map, shuttle stays.");
            return;
        }

        var planetPos = _xform.GetWorldPosition(planetXform);
        var target = new EntityCoordinates(spaceMap, planetPos + _random.NextAngle().ToVec() * ent.Comp.PlanetDistance);
        TryComp<ShuttleComponent>(shuttle, out var shuttleData);

        // Как у экспедиционного шаттла (SalvageSystem.Runner): старт FTL рассчитан так, чтобы уйти в БСС в конце экспедиции.
        var remaining = expedition.EndTime - _timing.CurTime;
        var startup = (float) remaining.TotalSeconds;
        if (remaining < TimeSpan.FromSeconds(_shuttle.DefaultStartupTime))
            startup = MathF.Max(0, (float) remaining.TotalSeconds - 0.5f);
        startup = MathF.Min(startup, _shuttle.DefaultStartupTime);

        _shuttle.FTLToCoordinates(shuttle, shuttleData, target, Angle.Zero, startup, _shuttle.DefaultTravelTime);

        _salvage.Announce(ent.Owner, Loc.GetString("expedition-extraction-departing"));
    }

    private void DeleteShuttle(Entity<ExtractionShuttleComponent> shuttle)
    {
        shuttle.Comp.DeleteAt = null;

        // Людей оставляем на карте в космосе, корабль удаляется.
        _linkedLifecycle.UnparentPlayersFromGrid(shuttle, deleteGrid: true);

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
