using Content.Shared._Horizon.SpaceHazards; // Horizon
using System.Linq;
using System.Threading;
using Content.Server.Salvage.Expeditions;
using Content.Shared.CCVar;
using Content.Shared.Examine;
using Content.Shared.Salvage.Expeditions;
using Content.Shared.Shuttles.Components;
using Robust.Shared.CPUJob.JobQueues;
using Robust.Shared.CPUJob.JobQueues.Queues;
using Robust.Shared.GameStates;
using Content.Server._NF.Salvage.Expeditions; // Frontier
using Content.Shared.Ghost; // Frontier
using Content.Shared.Procedural; // Frontier
using Content.Shared.Salvage; // Frontier
using Content.Shared.Station.Components; // Frontier
using Content.Shared._NF.CCVar; // Frontier
using Robust.Shared.Configuration; // Frontier
using Robust.Shared.Map; // Frontier
using Robust.Shared.Prototypes; // Frontier
using System.Numerics; // Frontier

namespace Content.Server.Salvage;

public sealed partial class SalvageSystem
{
    /*
     * Handles setup / teardown of salvage expeditions.
     */

    private readonly JobQueue _salvageQueue = new();
    private readonly List<(SpawnSalvageMissionJob Job, CancellationTokenSource CancelToken)> _salvageJobs = new();
    private const double SalvageJobTime = 0.002;
    private readonly List<(ProtoId<SalvageDifficultyPrototype> id, int value)> _missionDifficulties = [("NFModerate", 0), ("NFHazardous", 1), ("NFExtreme", 2), ("Inferno", 3)]; // Frontier: mission difficulties with order, Anco: Inferno

    [Dependency] private IConfigurationManager _configurationManager = default!; // Frontier

    /// <summary>Indices of planet tasks (unique per task, used as the mission id).</summary>
    private ushort _planetMissionIndex = 1;

    public float TravelTime { get; private set; } // Frontier
    public bool ProximityCheck { get; private set; } // Frontier

    private void InitializeExpeditions()
    {
        SubscribeLocalEvent<SalvageExpeditionConsoleComponent, ComponentInit>(OnSalvageConsoleInit);
        SubscribeLocalEvent<SalvageExpeditionConsoleComponent, EntParentChangedMessage>(OnSalvageConsoleParent);
        SubscribeLocalEvent<SalvageExpeditionConsoleComponent, BoundUIOpenedEvent>(OnSalvageConsoleOpened);
        SubscribeLocalEvent<ExpeditionPlanetComponent, PlanetExpeditionSpawnCompleteEvent>(OnPlanetExpeditionSpawnComplete);

        SubscribeLocalEvent<SalvageExpeditionComponent, MapInitEvent>(OnExpeditionMapInit);
        SubscribeLocalEvent<SalvageExpeditionComponent, ComponentShutdown>(OnExpeditionShutdown);
        SubscribeLocalEvent<SalvageExpeditionComponent, ComponentGetState>(OnExpeditionGetState);
        SubscribeLocalEvent<SalvageExpeditionComponent, EntityTerminatingEvent>(OnMapTerminating); // Frontier

        SubscribeLocalEvent<SalvageStructureComponent, ExaminedEvent>(OnStructureExamine); // Frontier

        TravelTime = _configurationManager.GetCVar(NFCCVars.SalvageExpeditionTravelTime); // Frontier
        Subs.CVar(_configurationManager, NFCCVars.SalvageExpeditionTravelTime, SetTravelTime); // Frontier
        ProximityCheck = _configurationManager.GetCVar(NFCCVars.SalvageExpeditionProximityCheck); // Frontier
        Subs.CVar(_configurationManager, NFCCVars.SalvageExpeditionProximityCheck, SetProximityCheck); // Frontier
    }

    private void OnExpeditionGetState(EntityUid uid, SalvageExpeditionComponent component, ref ComponentGetState args)
    {
        args.State = new SalvageExpeditionComponentState()
        {
            Stage = component.Stage,
            SelectedSong = component.SelectedSong // Frontier: note, not dirtied on map init (not needed)
        };
    }

    private void SetTravelTime(float obj)
    {
        TravelTime = obj;
    }

    private void SetProximityCheck(bool obj)
    {
        ProximityCheck = obj;
    }

    private void OnExpeditionMapInit(EntityUid uid, SalvageExpeditionComponent component, MapInitEvent args)
    {
        component.SelectedSong = _audio.ResolveSound(component.Sound);
    }

    #region Planet expeditions

    /// <summary>
    /// Rolls the planet's single expedition task: a random mission type and a random difficulty that fits the planet's
    /// biome. Called when the planet needs its first task and again as soon as its expedition closes.
    /// </summary>
    /// <summary>StarHorizon: типы заданий планет (Combined выдаётся отдельно, только на Inferno).</summary>
    private static readonly SalvageMissionType[] PlanetMissionTypes =
    {
        SalvageMissionType.Destruction,
        SalvageMissionType.Elimination,
        SalvageMissionType.Retrieval,
    };

    public void RollPlanetMission(Entity<ExpeditionPlanetComponent> planet)
    {
        var pickable = _missionDifficulties.Where(d => BiomeFitsDifficulty(planet.Comp.BiomeId, d.id)).ToList();
        if (pickable.Count <= 0)
        {
            Log.Error($"No expedition mission difficulties fit the biome {planet.Comp.BiomeId}!");
            return;
        }

        var difficulty = pickable[_random.Next(pickable.Count)].id;

        // For Inferno difficulty, always use Combined mission type
        var missionType = difficulty == "Inferno"
            ? SalvageMissionType.Combined
            : PlanetMissionTypes[_random.Next(PlanetMissionTypes.Length)];

        planet.Comp.Mission = new SalvageMissionParams
        {
            Index = _planetMissionIndex++,
            MissionType = missionType,
            Seed = _random.Next(),
            Difficulty = difficulty,
            Biome = planet.Comp.BiomeId,
        };

        // Время планеты идёт всегда: у нового задания таймер уже запущен, по его концу задание сменится.
        // Длительность берётся из задания: у биомов вроде Анко она случайная, плюс редкие модификаторы времени.
        var duration = TimeSpan.FromSeconds(_configurationManager.GetCVar(CCVars.SalvageExpeditionDuration));
        try
        {
            duration = GetMission(missionType, _prototypeManager.Index<SalvageDifficultyPrototype>(difficulty),
                planet.Comp.Mission.Seed, planet.Comp.BiomeId).Duration;
        }
        catch (InvalidOperationException e)
        {
            Log.Error($"Could not roll the duration of the planet mission: {e.Message}");
        }

        planet.Comp.EndTime = _timing.CurTime + duration;
    }

    /// <summary>The planet's task, rolled on first use.</summary>
    public SalvageMissionParams? EnsurePlanetMission(Entity<ExpeditionPlanetComponent> planet)
    {
        if (planet.Comp.Mission == null)
            RollPlanetMission(planet);

        return planet.Comp.Mission;
    }

    /// <summary>
    /// Starts the planet's expedition (a drop pod was dropped on it). The map is created at once so pods have a valid
    /// destination, the dungeon and loot are generated by a background job. Does nothing if the expedition already runs.
    /// </summary>
    /// <returns>True if the expedition exists afterwards (started now or already running).</returns>
    public bool StartPlanetExpedition(Entity<ExpeditionPlanetComponent> planet)
    {
        if (planet.Comp.Status != PlanetExpeditionStatus.Idle)
            return planet.Comp.ExpeditionMap != null;

        if (EnsurePlanetMission(planet) is not { } mission)
            return false;

        var mapUid = _mapSystem.CreateMap(out _, runMapInit: false);
        planet.Comp.ExpeditionMap = mapUid;
        planet.Comp.Status = PlanetExpeditionStatus.Generating;

        var cancelToken = new CancellationTokenSource();
        var job = new SpawnSalvageMissionJob(
            SalvageJobTime,
            EntityManager,
            _timing,
            _logManager,
            _prototypeManager,
            _anchorable,
            _biome,
            _dungeon,
            _metaData,
            _mapSystem,
            _station,
            _shuttle,
            this,
            EntityUid.Invalid, // no station
            EntityUid.Invalid, // no shuttle: drop pods arrive instead
            null,
            mission,
            cancelToken.Token,
            planet.Owner,
            mapUid);

        _salvageJobs.Add((job, cancelToken));
        _salvageQueue.EnqueueJob(job);

        RefreshConsoles();
        return true;
    }

    /// <summary>The expedition map is built: the expedition is open for everybody and its time is running.</summary>
    private void OnPlanetExpeditionSpawnComplete(EntityUid uid, ExpeditionPlanetComponent planet, PlanetExpeditionSpawnCompleteEvent args)
    {
        if (planet.ExpeditionMap != args.Map)
            return;

        if (!args.Success || !TryComp<SalvageExpeditionComponent>(args.Map, out var expedition))
        {
            // Generation failed: the map is deleted by the job, the task stays and its timer keeps running.
            planet.ExpeditionMap = null;
            planet.Status = PlanetExpeditionStatus.Idle;
            RefreshConsoles();
            return;
        }

        planet.Status = PlanetExpeditionStatus.Active;
        RefreshConsoles();
    }

    /// <summary>The expedition of the planet ended: no map, a new task is rolled at once.</summary>
    private void ResetPlanet(Entity<ExpeditionPlanetComponent> planet)
    {
        planet.Comp.ExpeditionMap = null;
        planet.Comp.Status = PlanetExpeditionStatus.Idle;
        RollPlanetMission(planet);
        RefreshConsoles();
    }

    /// <summary>The map of a planet expedition at the planet. Null while the planet has none.</summary>
    public EntityUid? GetPlanetExpeditionMap(EntityUid planet)
    {
        return TryComp<ExpeditionPlanetComponent>(planet, out var comp) ? comp.ExpeditionMap : null;
    }

    #endregion

    private void OnExpeditionShutdown(EntityUid uid, SalvageExpeditionComponent component, ComponentShutdown args)
    {
        // component.Stream = _audio.Stop(component.Stream); // Frontier: moved to client

        // First wipe any disks referencing us
        var disks = AllEntityQuery<ShuttleDestinationCoordinatesComponent>();
        while (disks.MoveNext(out var disk, out var diskComp)
               && diskComp.Destination == uid)
        {
            diskComp.Destination = null;
            Dirty(disk, diskComp);
        }

        // Planet expedition: close it and give the planet a new task.
        if (component.Planet.IsValid())
        {
            foreach (var (job, cancelToken) in _salvageJobs.ToArray())
            {
                if (job.Planet == component.Planet)
                {
                    cancelToken.Cancel();
                    _salvageJobs.Remove((job, cancelToken));
                }
            }

            if (Exists(component.Planet)
                && TryComp<ExpeditionPlanetComponent>(component.Planet, out var planet)
                && planet.ExpeditionMap == uid)
            {
                Announce(uid, Loc.GetString(component.Completed ? "salvage-expedition-completed" : "salvage-expedition-failed"));
                ResetPlanet((component.Planet, planet));
            }

            return;
        }

        foreach (var (job, cancelToken) in _salvageJobs.ToArray())
        {
            if (job.Station == component.Station)
            {
                cancelToken.Cancel();
                _salvageJobs.Remove((job, cancelToken));
            }
        }
    }

    /// <summary>Таймер каждой планеты идёт всегда: когда он кончился, а карты нет, задание меняется на новое.</summary>
    private void UpdatePlanetCycles()
    {
        var query = EntityQueryEnumerator<ExpeditionPlanetComponent>();
        while (query.MoveNext(out var uid, out var planet))
        {
            if (planet.Mission == null || (planet.Status == PlanetExpeditionStatus.Idle && _timing.CurTime >= planet.EndTime))
            {
                RollPlanetMission((uid, planet));
                RefreshConsoles();
            }
        }
    }

    private void UpdateExpeditions()
    {
        _salvageQueue.Process();
        UpdatePlanetCycles();

        foreach (var (job, cancelToken) in _salvageJobs.ToArray())
        {
            switch (job.Status)
            {
                case JobStatus.Finished:
                    _salvageJobs.Remove((job, cancelToken));
                    break;
            }
        }
    }

    // Frontier: Restore salvage structure examine, ghost reparenting
    private void OnStructureExamine(EntityUid uid, SalvageStructureComponent component, ExaminedEvent args)
    {
        args.PushMarkup(Loc.GetString("salvage-expedition-structure-examine"));
    }

    // Send all ghosts (relevant for admins) back to the default map so they don't lose their stuff.
    private void OnMapTerminating(EntityUid uid, SalvageExpeditionComponent component, EntityTerminatingEvent ev)
    {
        var ghosts = EntityQueryEnumerator<GhostComponent, TransformComponent>();
        var newCoords = new MapCoordinates(Vector2.Zero, _gameTicker.DefaultMap);
        while (ghosts.MoveNext(out var ghostUid, out _, out var xform))
        {
            if (xform.MapUid == uid)
                _transform.SetMapCoordinates(ghostUid, newCoords);
        }
    }
    // End Frontier
}
