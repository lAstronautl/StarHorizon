using System.Linq;
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
/// Эвакуация с планетной экспедиции: маяк с крюками, эвакуационный шаттл (Bus/expedition), игроки висят снаружи корабля и остаются в космосе
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

    public static readonly ResPath ShuttlePath = new("/Maps/_Horizon/Shuttles/Bus/expedition.yml");

    private TimeSpan _nextUpdate;

    public override void Initialize()
    {
        SubscribeLocalEvent<ExpeditionExtractionComponent, ComponentShutdown>(OnExpeditionShutdown);
        SubscribeLocalEvent<FTLCompletedEvent>(OnFTLCompleted);
        SubscribeLocalEvent<FTLStartedEvent>(OnFTLStarted);
        SubscribeLocalEvent<ExtractionShuttleComponent, ComponentShutdown>(OnShuttleShutdown);
        SubscribeLocalEvent<ExtractionShuttleComponent, ConsoleFTLAttemptEvent>(OnFTLAttempt);
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

        // Задание «Ремонт шаттла»: шаттл разбитым садится сразу, в начале задания.
        if (comp.Stage == ExtractionStage.None && HasComp<SalvageRepairExpeditionComponent>(ent))
            SendShuttle(ent, expedition, crashed: true);

        if (!comp.Called && remaining < comp.ArriveBefore)
        {
            comp.Called = true;
            if (comp.Stage == ExtractionStage.None)
            {
                SendShuttle(ent, expedition);
            }
            else if (comp.Shuttle is { } crashed && HasComp<ExpeditionBrokenShuttleComponent>(crashed))
            {
                // Шаттл так и не починили: присылают резервный с другой стороны, задание провалено.
                _salvage.Announce(ent.Owner, Loc.GetString("expedition-repair-failed"));
                SendShuttle(ent, expedition, side: -1f);
            }
            else
            {
                // Починенный шаттл уже на месте: эвакуация с него.
                var state = EnsureComp<ExpeditionExtractionStateComponent>(ent.Owner);
                state.Active = true;
                Dirty(ent.Owner, state);
                _salvage.Announce(ent.Owner, Loc.GetString("expedition-extraction-ready",
                    ("minutes", (int) comp.ArriveBefore.TotalMinutes)));
            }
        }

        if (comp.Stage == ExtractionStage.ShuttleSent
            && remaining < TimeSpan.FromSeconds(_shuttle.DefaultStartupTime) + comp.DepartMargin
            && comp.Shuttle is { } shuttle
            && TryComp<ExtractionShuttleComponent>(shuttle, out var shuttleComp)
            && shuttleComp.Arrived
            && !HasComp<ExpeditionBrokenShuttleComponent>(shuttle) // разбитый шаттл не улетает
            && !HasComp<FTLComponent>(shuttle)) // после прилёта у корабля ещё идёт перезарядка прыжка
        {
            Depart(ent, expedition, (shuttle, shuttleComp));
        }
    }

    #region Shuttle

    /// <param name="crashed">Задание «Ремонт шаттла»: шаттл садится в начале и разбит, эвакуация ещё не объявлена.</param>
    /// <param name="side">С какой стороны от места посадки капсул сесть (резервный шаттл садится с другой стороны).</param>
    private void SendShuttle(Entity<ExpeditionExtractionComponent> ent, SalvageExpeditionComponent expedition,
        bool crashed = false, float side = 1f)
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
        if (crashed)
        {
            EnsureComp<ExpeditionBrokenShuttleComponent>(shuttle);
        }
        else
        {
            var state = EnsureComp<ExpeditionExtractionStateComponent>(ent.Owner);
            state.Active = true;
            Dirty(ent.Owner, state);
        }
        var shuttleComp = EnsureComp<ExtractionShuttleComponent>(shuttle);
        shuttleComp.Expedition = ent.Owner;
        shuttleComp.DeleteAfter = ent.Comp.DeleteAfter;

        var landing = expedition.LandingCoordinates + new Vector2(ent.Comp.LandingOffset * side, 0);
        ent.Comp.LandingPosition = landing;

        // Разбитый шаттл (задание ремонта) падает серединой на центр комплекса; всё твёрдое на месте падения
        // расчищается заранее (дыра от крушения).
        if (crashed && expedition.DungeonCenter is { } center && TryComp<MapGridComponent>(shuttle, out var shuttleGrid))
        {
            var bounds = shuttleGrid.LocalAABB;
            landing = center - bounds.Center;
            ent.Comp.LandingPosition = center;
            ClearCrashSite(ent.Owner, new Box2(landing + bounds.BottomLeft - Vector2.One, landing + bounds.TopRight + Vector2.One));
        }
        TryComp<ShuttleComponent>(shuttle, out var shuttleData);
        _shuttle.FTLToCoordinates(shuttle, shuttleData, new EntityCoordinates(ent.Owner, landing), Angle.Zero,
            _shuttle.DefaultStartupTime, _shuttle.DefaultTravelTime);

        if (crashed)
        {
            var site = ent.Comp.LandingPosition ?? landing;
            _salvage.Announce(ent.Owner, Loc.GetString("expedition-repair-incoming", ("x", (int) site.X), ("y", (int) site.Y)));
            return;
        }

        _salvage.Announce(ent.Owner, Loc.GetString("expedition-extraction-arriving",
            ("x", (int) landing.X), ("y", (int) landing.Y), ("minutes", (int) ent.Comp.ArriveBefore.TotalMinutes)));
    }

    /// <summary>
    /// Место падения разбитого шаттла: стены, двери, машины, порода и прочее твёрдое на клетках планеты в этой области
    /// удаляются (узлы ремонта не трогаем). Земля биома под шаттлом при посадке сама не обрастает породой (FTL её выравнивает).
    /// </summary>
    private void ClearCrashSite(EntityUid map, Box2 area)
    {
        if (!TryComp<MapGridComponent>(map, out var grid))
            return;

        foreach (var tile in _map.GetTilesIntersecting(map, grid, area, false))
        {
            foreach (var anchored in _map.GetAnchoredEntities(map, grid, tile.GridIndices).ToArray())
            {
                if (HasComp<ExpeditionRepairPointComponent>(anchored))
                    continue;

                if (HasComp<Content.Server.Gatherable.Components.GatherableComponent>(anchored)
                    || TryComp<PhysicsComponent>(anchored, out var body) && body.CanCollide && body.Hard)
                {
                    QueueDel(anchored);
                }
            }
        }
    }

    /// <summary>Шаттл ушёл в БСС (после старта FTL): только теперь клиентам играет победная музыка.</summary>
    private void OnFTLStarted(ref FTLStartedEvent args)
    {
        if (!TryComp<ExtractionShuttleComponent>(args.Entity, out var shuttle) || !shuttle.Departed)
            return;

        if (Exists(shuttle.Expedition) && TryComp<ExpeditionExtractionStateComponent>(shuttle.Expedition, out var state))
        {
            state.Active = false;
            Dirty(shuttle.Expedition, state);
        }

        var marker = EnsureComp<ExtractionShuttleMarkerComponent>(args.Entity);
        marker.Departed = true;
        Dirty(args.Entity, marker);
    }

    private void OnFTLCompleted(ref FTLCompletedEvent args)
    {
        if (!TryComp<ExtractionShuttleComponent>(args.Entity, out var shuttle))
            return;

        if (!shuttle.Departed)
        {
            shuttle.Arrived = true;
            // О разбитом шаттле объявляет задание ремонта (сколько узлов чинить).
            if (Exists(shuttle.Expedition) && !HasComp<ExpeditionBrokenShuttleComponent>(args.Entity))
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

    /// <summary>
    /// Пока экспедиция не закончилась, эвакуационный шаттл (и разбитый в задании ремонта) никуда не прыгает с консоли
    /// или командой: он улетает сам в конце экспедиции (наш вылет идёт мимо этой проверки). После вылета прыгать можно.
    /// </summary>
    private void OnFTLAttempt(Entity<ExtractionShuttleComponent> ent, ref ConsoleFTLAttemptEvent args)
    {
        if (ent.Comp.Departed || args.Cancelled)
            return;

        // Просто не даём прыгнуть, без своего сообщения.
        args.Cancelled = true;
        args.Reason = string.Empty;
    }

    private void OnShuttleShutdown(Entity<ExtractionShuttleComponent> ent, ref ComponentShutdown args)
    {
        if (Exists(ent.Comp.Expedition) && TryComp<ExpeditionExtractionComponent>(ent.Comp.Expedition, out var extraction)
            && extraction.Shuttle == ent.Owner)
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
