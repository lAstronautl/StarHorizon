using Content.Shared.Shuttles.Components;
using Content.Shared.Procedural;
using Content.Shared.Salvage.Expeditions;
using Content.Shared.Dataset;
using Robust.Shared.Prototypes;
using Content.Server.Salvage.Expeditions; // Frontier
using Content.Server._NF.Salvage; // Frontier
using Content.Server._Horizon.SpaceHazards; // Horizon
using Content.Shared.Mind.Components; // Frontier
using Content.Shared.Mobs.Components; // Frontier
using Content.Shared.NPC.Components; // Frontier
using Content.Shared.NPC; // Frontier
using Content.Shared.Popups; // Frontier
using Content.Shared.Station.Components; // Frontier
using Content.Shared._NF.CCVar; // Frontier
using Robust.Shared.Map.Components; // Frontier
using Robust.Shared.Physics.Components; // Frontier
using Robust.Shared.Physics; // Frontier

namespace Content.Server.Salvage;

public sealed partial class SalvageSystem
{
    public static readonly EntProtoId CoordinatesDisk = "CoordinatesDisk";
    public static readonly ProtoId<LocalizedDatasetPrototype> PlanetNames = "NamesBorer";

    [Dependency] private readonly SharedPopupSystem _popupSystem = default!; // Frontier
    [Dependency] private readonly SalvageSystem _salvage = default!; // Frontier
    [Dependency] private readonly ExpeditionPlanetSystem _expeditionPlanets = default!; // Horizon

    private const float ShuttleFTLMassThreshold = 50f; // Frontier
    private const float ShuttleFTLRange = 150f; // Frontier

    private void OnSalvageClaimMessage(EntityUid uid, SalvageExpeditionConsoleComponent component, ClaimSalvageMessage args)
    {
        var station = _station.GetOwningStation(uid);

        if (!TryComp<SalvageExpeditionDataComponent>(station, out var data) || data.Claimed)
            return;

        if (!data.Missions.TryGetValue(args.Index, out var missionparams))
            return;

        // Horizon: expeditions can only be started next to a planet, and the planet decides the biome.
        if (!component.Debug)
        {
            if (!_expeditionPlanets.TryGetPlanetNear(uid, out var planet))
            {
                PlayDenySound((uid, component));
                _popupSystem.PopupEntity(Loc.GetString("salvage-expedition-no-planet"), uid, PopupType.MediumCaution);
                UpdateConsoles((station.Value, data));
                return;
            }

            missionparams = missionparams with { Biome = planet.Comp.BiomeId };
            data.Missions[args.Index] = missionparams;
        }
        // End Horizon

        // Frontier: prevent expeditions if there are too many out already.
        var activeExpeditionCount = 0;
        var expeditionQuery = AllEntityQuery<SalvageExpeditionDataComponent, MetaDataComponent>();
        while (expeditionQuery.MoveNext(out var expeditionUid, out _, out _))
        {
            if (TryComp<SalvageExpeditionDataComponent>(expeditionUid, out var expeditionData) && expeditionData.Claimed)
                activeExpeditionCount++;
        }

        if (activeExpeditionCount >= _configurationManager.GetCVar(NFCCVars.SalvageExpeditionMaxActive))
        {
            PlayDenySound((uid, component));
            _popupSystem.PopupEntity(Loc.GetString("shuttle-ftl-too-many"), uid, PopupType.MediumCaution);
            UpdateConsoles((station.Value, data));
            return;
        }
        // End Frontier

        // var cdUid = Spawn(CoordinatesDisk, Transform(uid).Coordinates); // Frontier: no disk-based FTL
        // SpawnMission(missionparams, station.Value, cdUid); // Frontier: no disk-based FTL

        // Frontier: FTL travel is currently restricted to expeditions and such, and so we need to put this here
        #region Frontier FTL changes
        // until FTL changes for us in some way.

        // Run a proximity check (unless using a debug console)
        if (_salvage.ProximityCheck && !component.Debug)
        {
            if (!TryComp<StationDataComponent>(station, out var stationData)
                || _station.GetLargestGrid((station.Value, stationData)) is not { Valid: true } ourGrid
                || !TryComp<MapGridComponent>(ourGrid, out var gridComp))
            {
                PlayDenySound((uid, component));
                _popupSystem.PopupEntity(Loc.GetString("shuttle-ftl-invalid"), uid, PopupType.MediumCaution);
                UpdateConsoles((station.Value, data));
                return;
            }

            if (HasComp<FTLComponent>(ourGrid))
            {
                PlayDenySound((uid, component));
                _popupSystem.PopupEntity(Loc.GetString("shuttle-ftl-recharge"), uid, PopupType.MediumCaution);
                UpdateConsoles((station.Value, data));
                return;
            }

            var xform = Transform(ourGrid);
            var bounds = _transform.GetWorldMatrix(ourGrid).TransformBox(gridComp.LocalAABB).Enlarged(ShuttleFTLRange);
            var bodyQuery = GetEntityQuery<PhysicsComponent>();
            var otherGrids = new List<Entity<MapGridComponent>>();
            _mapManager.FindGridsIntersecting(xform.MapID, bounds, ref otherGrids);
            foreach (var otherGrid in otherGrids)
            {
                if (ourGrid == otherGrid.Owner ||
                    !bodyQuery.TryGetComponent(otherGrid.Owner, out var body) ||
                    body.Mass < ShuttleFTLMassThreshold && body.BodyType == BodyType.Dynamic)
                {
                    continue;
                }

                PlayDenySound((uid, component));
                _popupSystem.PopupEntity(Loc.GetString("shuttle-ftl-proximity"), uid, PopupType.MediumCaution);
                UpdateConsoles((station.Value, data));
                return;
            }
        }
        SpawnMission(missionparams, station.Value, null);
        #endregion Frontier FTL changes
        // End Frontier

        data.ActiveMission = args.Index;
        var mission = GetMission(missionparams.MissionType, _prototypeManager.Index<SalvageDifficultyPrototype>(missionparams.Difficulty), missionparams.Seed, missionparams.Biome); // Frontier: add MissionType; Horizon: planet biome
        // Frontier - TODO: move this to progression for secondary window timer
        data.NextOffer = _timing.CurTime + mission.Duration + TimeSpan.FromSeconds(1);
        data.CooldownTime = mission.Duration + TimeSpan.FromSeconds(1); // Frontier

        // _labelSystem.Label(cdUid, GetFTLName(_prototypeManager.Index(PlanetNames), missionparams.Seed)); // Frontier: no disc
        // _audio.PlayPvs(component.PrintSound, uid); // Frontier: no disc

        UpdateConsoles((station.Value, data));
    }

    // Frontier: early expedition end
    private void OnSalvageFinishMessage(EntityUid entity, SalvageExpeditionConsoleComponent component, FinishSalvageMessage e)
    {
        var station = _station.GetOwningStation(entity);
        if (!TryComp<SalvageExpeditionDataComponent>(station, out var data) || !data.CanFinish)
            return;

        // Based on SalvageSystem.Runner:OnConsoleFTLAttempt
        if (!TryComp(entity, out TransformComponent? xform)) // Get the console's grid (if you move it, rip you)
        {
            PlayDenySound((entity, component));
            _popupSystem.PopupEntity(Loc.GetString("salvage-expedition-shuttle-not-found"), entity, PopupType.MediumCaution);
            UpdateConsoles((station.Value, data));
            return;
        }

        // Frontier: check if any player characters or friendly ghost roles are outside
        var query = EntityQueryEnumerator<MindContainerComponent, MobStateComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out var mindContainer, out var _, out var mobXform))
        {
            if (mobXform.MapUid != xform.MapUid)
                continue;

            // Not player controlled (ghosted)
            if (!mindContainer.HasMind)
                continue;

            // NPC, definitely not a person
            if (HasComp<ActiveNPCComponent>(uid) || HasComp<NFSalvageMobRestrictionsComponent>(uid))
                continue;

            // Hostile ghost role, continue
            if (TryComp(uid, out NpcFactionMemberComponent? npcFaction))
            {
                var hostileFactions = npcFaction.HostileFactions;
                if (hostileFactions.Contains("NanoTrasen")) // TODO: move away from hardcoded faction
                    continue;
            }

            // Okay they're on salvage, so are they on the shuttle.
            if (mobXform.GridUid != xform.GridUid)
            {
                PlayDenySound((entity, component));
                _popupSystem.PopupEntity(Loc.GetString("salvage-expedition-not-everyone-aboard", ("target", uid)), entity, PopupType.MediumCaution);
                UpdateConsoles((station.Value, data));
                return;
            }
        }
        // End SalvageSystem.Runner:OnConsoleFTLAttempt

        data.CanFinish = false;
        UpdateConsoles((station.Value, data));

        var map = Transform(entity).MapUid;

        if (!TryComp<SalvageExpeditionComponent>(map, out var expedition))
            return;

        const int departTime = 20;
        var newEndTime = _timing.CurTime + TimeSpan.FromSeconds(departTime);

        if (expedition.EndTime <= newEndTime)
            return;

        expedition.Stage = ExpeditionStage.FinalCountdown;
        expedition.EndTime = newEndTime;
        Dirty(map.Value, expedition);

        Announce(map.Value, Loc.GetString("salvage-expedition-announcement-early-finish", ("departTime", departTime)));
    }
    // End Frontier: early expedition end

    private void OnSalvageConsoleInit(Entity<SalvageExpeditionConsoleComponent> console, ref ComponentInit args)
    {
        UpdateConsole(console);
    }

    private void OnSalvageConsoleParent(Entity<SalvageExpeditionConsoleComponent> console, ref EntParentChangedMessage args)
    {
        UpdateConsole(console);
    }

    private void UpdateConsoles(Entity<SalvageExpeditionDataComponent> component)
    {
        var query = AllEntityQuery<SalvageExpeditionConsoleComponent, UserInterfaceComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out var consoleComp, out var uiComp, out var xform))
        {
            var station = _station.GetOwningStation(uid, xform);

            if (station != component.Owner)
                continue;

            var state = GetState(component); // Horizon: per-console state (planet proximity differs per console)
            ApplyPlanetState(uid, consoleComp, state);
            _ui.SetUiState((uid, uiComp), SalvageConsoleUiKey.Expedition, state);
        }
    }

    private void UpdateConsole(Entity<SalvageExpeditionConsoleComponent> component)
    {
        var station = _station.GetOwningStation(component);
        SalvageExpeditionConsoleState state;

        if (TryComp<SalvageExpeditionDataComponent>(station, out var dataComponent))
        {
            state = GetState(dataComponent);
        }
        else
        {
            state = new SalvageExpeditionConsoleState(TimeSpan.Zero, false, true, 0, new List<SalvageMissionParams>(), false, TimeSpan.FromSeconds(1)); // Frontier: add false, 1 second timespan as last args (cannot finish, not on a mission)
        }

        // Frontier: if we have a lingering FTL component, we cannot start a new mission
        if (!TryComp<StationDataComponent>(station, out var stationData) ||
                _station.GetLargestGrid((station.Value, stationData)) is not { Valid: true } grid ||
                HasComp<FTLComponent>(grid))
        {
            state.Cooldown = true; //Hack: disable buttons
        }
        // End Frontier

        ApplyPlanetState(component.Owner, component.Comp, state); // Horizon
        _ui.SetUiState(component.Owner, SalvageConsoleUiKey.Expedition, state);
    }

    // Horizon: a console only works next to an expedition planet
    private void ApplyPlanetState(EntityUid console, SalvageExpeditionConsoleComponent comp, SalvageExpeditionConsoleState state)
    {
        if (comp.Debug || state.Claimed)
            return;

        if (_expeditionPlanets.TryGetPlanetNear(console, out var planet))
        {
            state.PlanetBiome = planet.Comp.BiomeId;
        }
        else
        {
            state.NoPlanet = true;
            state.Cooldown = true;
        }
    }

    /// <summary>Re-sends the UI state of a single console, e.g. after the ship moved relative to a planet.</summary>
    public void RefreshConsole(EntityUid uid)
    {
        if (TryComp<SalvageExpeditionConsoleComponent>(uid, out var console))
            UpdateConsole((uid, console));
    }
    // End Horizon

    // Frontier: deny sound
    private void PlayDenySound(Entity<SalvageExpeditionConsoleComponent> ent)
    {
        _audio.PlayPvs(_audio.ResolveSound(ent.Comp.ErrorSound), ent);
    }
    // End Frontier
}
