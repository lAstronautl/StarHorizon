using System.Linq;
using System.Numerics;
using Content.Server._Horizon.SpaceHazards;
using Content.Server._StarHorizon.Expedition;
using Content.Server.Salvage;
using Content.Server.Salvage.Expeditions;
using Content.Shared._Horizon.SpaceHazards;
using Content.Shared._StarHorizon.Expedition;
using Content.Shared.Interaction;
using Content.Shared.Salvage;
using Content.Shared.Salvage.Expeditions;
using Content.Shared.Stacks;
using Robust.Shared.GameObjects;
using Robust.Shared.Log;
using Robust.Shared.Map;
using Robust.Shared.Timing;

namespace Content.IntegrationTests.Tests._StarHorizon;

/// <summary>
/// Задания «Квота добычи», «Сбор образцов» и «Ремонт шаттла» планетной экспедиции.
/// </summary>
[TestFixture]
public sealed class ExpeditionObjectivesTest
{
    [TestPrototypes]
    private const string Prototypes = @"
- type: entity
  id: ObjectivesTestPlayer
  components:
  - type: Hands
    hands:
      hand_right:
        location: Right
      hand_left:
        location: Left
    sortedHands:
    - hand_right
    - hand_left
  - type: ComplexInteraction
  - type: DoAfter
  - type: Eye
  - type: ContentEye
  - type: MobState
  - type: Physics
    bodyType: Dynamic
  - type: Fixtures
    fixtures:
      fix1:
        shape:
          !type:PhysShapeCircle
          radius: 0.35
        density: 50
        mask:
        - MobMask
        layer:
        - MobLayer
";

    private static async Task<(EntityUid Map, SalvageExpeditionComponent Expedition, EntityUid Player)> StartExpedition(
        Pair.TestPair pair, SalvageMissionType type)
    {
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var salvage = server.System<SalvageSystem>();

        EntityUid planet = default;
        await server.WaitPost(() =>
        {
            planet = server.EntMan.SpawnEntity("ExpeditionPlanet", new MapCoordinates(new Vector2(3000, 0), map.MapId));
            var planetComp = server.EntMan.GetComponent<ExpeditionPlanetComponent>(planet);
            server.System<ExpeditionPlanetSystem>().SetupPlanet(planet, planetComp, "Grasslands", 0.5f);
            salvage.EnsurePlanetMission((planet, planetComp))!.MissionType = type;
            salvage.StartPlanetExpedition((planet, planetComp));
        });

        var comp = server.EntMan.GetComponent<ExpeditionPlanetComponent>(planet);
        for (var attempt = 0; attempt < 3 && comp.Status != PlanetExpeditionStatus.Active; attempt++)
        {
            if (comp.Status == PlanetExpeditionStatus.Idle && attempt > 0)
                await server.WaitPost(() => salvage.StartPlanetExpedition((planet, comp)));

            for (var i = 0; i < 60 && comp.Status == PlanetExpeditionStatus.Generating; i++)
                await pair.RunSeconds(2f);
        }
        Assert.That(comp.Status, Is.EqualTo(PlanetExpeditionStatus.Active));
        var expeditionMap = comp.ExpeditionMap!.Value;
        var expedition = server.EntMan.GetComponent<SalvageExpeditionComponent>(expeditionMap);

        EntityUid player = default;
        await server.WaitPost(() =>
        {
            server.System<SharedMapSystem>().SetPaused(expeditionMap, false);
            player = server.EntMan.SpawnEntity("ObjectivesTestPlayer", new EntityCoordinates(expeditionMap, expedition.LandingCoordinates));
            server.PlayerMan.SetAttachedEntity(server.PlayerMan.Sessions.First(), player, out _, true);
        });

        return (expeditionMap, expedition, player);
    }

    private static void Deposit(Robust.UnitTesting.RobustIntegrationTest.ServerIntegrationInstance server, EntityUid player, EntityUid item, EntityUid depot)
    {
        var ev = new InteractUsingEvent(player, item, depot, server.EntMan.GetComponent<TransformComponent>(depot).Coordinates);
        server.EntMan.EventBus.RaiseLocalEvent(depot, ev);
        Assert.That(ev.Handled, "приёмник принял");
    }

    [Test]
    public async Task MiningQuotaIsDepositedIntoTheDroppedDepot()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { FailureLogLevel = LogLevel.Fatal, Connected = true, Dirty = true });
        var server = pair.Server;
        var (map, expedition, player) = await StartExpedition(pair, SalvageMissionType.Mining);
        var mining = server.EntMan.GetComponent<SalvageMiningExpeditionComponent>(map);
        var holder = server.EntMan.GetComponent<ExpeditionDepotHolderComponent>(map);
        Assert.That(mining.Quota, Is.GreaterThanOrEqualTo(20));

        // Приёмник падает в капсуле через ~15 с после появления игрока и выходит из неё на земле.
        await pair.RunSeconds(18f);
        Assert.That(holder.Dropped && holder.Depot != null, "приёмник сброшен");
        await pair.RunSeconds(14f);
        var depot = holder.Depot!.Value;
        Assert.That(server.EntMan.GetComponent<TransformComponent>(depot).MapUid, Is.EqualTo(map));
        Assert.That(server.EntMan.GetComponent<ExpeditionHudComponent>(map).CompassKind, Is.EqualTo(ExpeditionCompassTarget.Depot),
            "квота: компас на приёмник");
        Assert.That(server.System<Robust.Shared.Containers.SharedContainerSystem>().IsEntityInContainer(depot), Is.False, "вышел из капсулы");

        // Не руда не принимается; стак руды засчитывается целиком.
        await server.WaitPost(() =>
        {
            var junk = server.EntMan.SpawnEntity("ExpeditionSample", server.EntMan.GetComponent<TransformComponent>(player).Coordinates);
            var ev = new InteractUsingEvent(player, junk, depot, server.EntMan.GetComponent<TransformComponent>(depot).Coordinates);
            server.EntMan.EventBus.RaiseLocalEvent(depot, ev);
            Assert.That(ev.Handled, Is.False, "не руда");

            var ore = server.EntMan.SpawnEntity("SteelOre", server.EntMan.GetComponent<TransformComponent>(player).Coordinates);
            server.System<SharedStackSystem>().SetCount(ore, 10);
            Deposit(server, player, ore, depot);
        });
        await pair.RunSeconds(1.5f);
        Assert.That(mining.Collected, Is.EqualTo(10));
        Assert.That(expedition.Completed, Is.False);
        var hud = server.EntMan.GetComponent<ExpeditionHudComponent>(map);
        Assert.That(hud.Objectives.Single().Kind, Is.EqualTo(ExpeditionObjectiveKind.Quota));
        Assert.That(hud.Objectives.Single().Done, Is.EqualTo(10));

        await server.WaitPost(() =>
        {
            var ore = server.EntMan.SpawnEntity("SteelOre", server.EntMan.GetComponent<TransformComponent>(player).Coordinates);
            server.System<SharedStackSystem>().SetCount(ore, mining.Quota);
            Deposit(server, player, ore, depot);
        });
        await pair.RunSeconds(1.5f);
        Assert.That(expedition.Completed, "квота выполнена");

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task SamplesAreSpreadAndDeposited()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { FailureLogLevel = LogLevel.Fatal, Connected = true, Dirty = true });
        var server = pair.Server;
        var (map, expedition, player) = await StartExpedition(pair, SalvageMissionType.Sampling);
        var sampling = server.EntMan.GetComponent<SalvageSamplingExpeditionComponent>(map);
        Assert.That(sampling.Samples, Has.Count.GreaterThanOrEqualTo(sampling.Required + 1), "образцы с запасом");
        Assert.That(sampling.Samples.All(s => server.EntMan.GetComponent<TransformComponent>(s).MapUid == map));

        await pair.RunSeconds(32f);
        var depot = server.EntMan.GetComponent<ExpeditionDepotHolderComponent>(map).Depot!.Value;

        await server.WaitPost(() =>
        {
            foreach (var sample in sampling.Samples.Take(sampling.Required))
                Deposit(server, player, sample, depot);
        });
        await pair.RunSeconds(1.5f);
        Assert.That(sampling.Collected, Is.EqualTo(sampling.Required));
        var holder = server.EntMan.GetComponent<ExpeditionDepotHolderComponent>(map);
        Assert.That(holder.FultonReady, "образцы собраны: приёмник ждёт фултон");
        Assert.That(expedition.Completed, Is.False, "пока приёмник не улетел, задание не выполнено");
        var hud = server.EntMan.GetComponent<ExpeditionHudComponent>(map);
        Assert.That(hud.Objectives.Select(o => o.Kind), Is.EqualTo(new[] { ExpeditionObjectiveKind.Samples, ExpeditionObjectiveKind.Fulton }));
        Assert.That(hud.Objectives[0].Finished && !hud.Objectives[1].Finished);

        // У приёмника появилось действие «Привязать к фултону»; привязка занимает несколько секунд.
        await server.WaitPost(() => server.System<Robust.Shared.GameObjects.SharedTransformSystem>().SetCoordinates(player,
            server.EntMan.GetComponent<TransformComponent>(depot).Coordinates.Offset(new Vector2(0.7f, 0))));
        await pair.RunTicksSync(2);
        await server.WaitAssertion(() =>
        {
            var verbs = server.System<Content.Server.Verbs.VerbSystem>().GetLocalVerbs(depot, player, typeof(Content.Shared.Verbs.InteractionVerb));
            Assert.That(verbs.Any(v => v.Text == Robust.Shared.Localization.Loc.GetString("expedition-depot-verb-fulton")), "есть действие фултона");
            verbs.First(v => v.Text == Robust.Shared.Localization.Loc.GetString("expedition-depot-verb-fulton")).Act!.Invoke();
        });
        await pair.RunSeconds(4f);
        Assert.That(holder.FultonAttached, "приёмник на фултоне");
        Assert.That(server.EntMan.GetComponent<Content.Shared.Salvage.Fulton.FultonedComponent>(depot).Removeable, Is.False, "снять нельзя");

        // Фултон поднимает приёмник: задание выполнено, приёмник убран.
        await server.WaitPost(() => server.EntMan.GetComponent<Content.Shared.Salvage.Fulton.FultonedComponent>(depot).NextFulton = server.Resolve<IGameTiming>().CurTime);
        await pair.RunSeconds(2f);
        Assert.That(expedition.Completed, "приёмник улетел: задание выполнено");
        Assert.That(hud.Objectives.All(o => o.Finished));
        await pair.RunSeconds(4f);
        Assert.That(server.EntMan.Deleted(depot), "улетевший приёмник убран");

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task CrashedShuttleIsRepairedDefendedAndUsedForExtraction()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { FailureLogLevel = LogLevel.Fatal, Connected = true, Dirty = true });
        var server = pair.Server;
        var timing = server.Resolve<IGameTiming>();
        var (map, expedition, player) = await StartExpedition(pair, SalvageMissionType.Repair);
        var repair = server.EntMan.GetComponent<SalvageRepairExpeditionComponent>(map);
        var extraction = server.EntMan.GetComponent<ExpeditionExtractionComponent>(map);

        // 1. Разбитый шаттл садится сразу, на нём появляются узлы.
        for (var i = 0; i < 45 && !repair.PointsSpawned; i++)
            await pair.RunSeconds(1f);
        Assert.That(repair.PointsSpawned, "шаттл сел, узлы на месте");
        var shuttle = extraction.Shuttle!.Value;
        Assert.That(server.EntMan.HasComponent<ExpeditionBrokenShuttleComponent>(shuttle));
        Assert.That(repair.Points, Has.Count.EqualTo(repair.Total).And.Count.GreaterThanOrEqualTo(3));
        Assert.That(repair.Points.All(p => server.EntMan.GetComponent<TransformComponent>(p).GridUid == shuttle), "узлы на шаттле");
        Assert.That(server.EntMan.TryGetComponent<ExpeditionExtractionStateComponent>(map, out var earlyState) && earlyState.Active,
            Is.False, "эвакуация ещё не объявлена");
        await pair.RunSeconds(1f);
        Assert.That(server.EntMan.GetComponent<ExpeditionHudComponent>(map).CompassKind, Is.EqualTo(ExpeditionCompassTarget.Objective),
            "компас на разбитый шаттл: его не надо искать");

        // 2. Первый узел чинится руками (несколько секунд), остальные сразу.
        var first = repair.Points[0];
        await server.WaitPost(() =>
        {
            server.System<SharedTransformSystem>().SetCoordinates(player, server.EntMan.GetComponent<TransformComponent>(first).Coordinates.Offset(new Vector2(0.6f, 0)));
            server.EntMan.EventBus.RaiseLocalEvent(first, new InteractHandEvent(player, first));
        });
        await pair.RunSeconds(10f);
        Assert.That(server.EntMan.GetComponent<ExpeditionRepairPointComponent>(first).Repaired, "узел починен руками");

        await server.WaitPost(() =>
        {
            var system = server.System<ExpeditionObjectivesSystem>();
            foreach (var point in repair.Points.Skip(1))
                system.Repair((point, server.EntMan.GetComponent<ExpeditionRepairPointComponent>(point)));
        });
        await pair.RunSeconds(2f);

        // 3. Оборона: волны, потом шаттл готов.
        Assert.That(repair.Stage, Is.EqualTo(ExpeditionRepairStage.Defending));
        Assert.That(server.EntMan.Count<PursuePlayerComponent>(), Is.GreaterThan(0), "пришла волна");
        var hud = server.EntMan.GetComponent<ExpeditionHudComponent>(map);
        Assert.That(hud.Objectives.Select(o => o.Kind), Is.EqualTo(new[] { ExpeditionObjectiveKind.Repair, ExpeditionObjectiveKind.Defend }));
        Assert.That(hud.Objectives[0].Finished);

        await server.WaitPost(() => repair.DefenseEnd = timing.CurTime);
        await pair.RunSeconds(2f);
        Assert.That(repair.Stage, Is.EqualTo(ExpeditionRepairStage.Done));
        Assert.That(expedition.Completed, "задание выполнено");
        Assert.That(server.EntMan.HasComponent<ExpeditionBrokenShuttleComponent>(shuttle), Is.False, "шаттл исправен");

        // 4. Эвакуация с этого же шаттла, резервный не нужен.
        await server.WaitPost(() => expedition.EndTime = timing.CurTime + TimeSpan.FromSeconds(150));
        await pair.RunSeconds(2f);
        Assert.That(extraction.Called);
        Assert.That(extraction.Shuttle, Is.EqualTo(shuttle), "эвакуация с починенного шаттла");
        Assert.That(server.EntMan.GetComponent<ExpeditionExtractionStateComponent>(map).Active, "эвакуация объявлена");

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task UnrepairedShuttleGetsABackup()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { FailureLogLevel = LogLevel.Fatal, Connected = true, Dirty = true });
        var server = pair.Server;
        var timing = server.Resolve<IGameTiming>();
        var (map, expedition, _) = await StartExpedition(pair, SalvageMissionType.Repair);
        var repair = server.EntMan.GetComponent<SalvageRepairExpeditionComponent>(map);
        var extraction = server.EntMan.GetComponent<ExpeditionExtractionComponent>(map);

        for (var i = 0; i < 45 && !repair.PointsSpawned; i++)
            await pair.RunSeconds(1f);
        var crashed = extraction.Shuttle!.Value;

        await server.WaitPost(() => expedition.EndTime = timing.CurTime + TimeSpan.FromSeconds(150));
        await pair.RunSeconds(2f);
        Assert.That(extraction.Shuttle, Is.Not.EqualTo(crashed), "прислали резервный шаттл");
        Assert.That(server.EntMan.HasComponent<ExpeditionBrokenShuttleComponent>(crashed), "разбитый остаётся");
        Assert.That(server.EntMan.HasComponent<ExpeditionBrokenShuttleComponent>(extraction.Shuttle!.Value), Is.False);
        Assert.That(expedition.Completed, Is.False, "задание провалено");

        // Резервный садится и улетает в конце, разбитый остаётся.
        for (var i = 0; i < 45 && !server.EntMan.GetComponent<ExtractionShuttleComponent>(extraction.Shuttle!.Value).Arrived; i++)
            await pair.RunSeconds(1f);
        Assert.That(server.EntMan.GetComponent<ExtractionShuttleComponent>(extraction.Shuttle!.Value).Arrived, "резервный сел");

        await pair.CleanReturnAsync();
    }
}
