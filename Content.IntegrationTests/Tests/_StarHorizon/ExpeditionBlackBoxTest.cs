using System.Linq;
using System.Numerics;
using Content.Server._Horizon.SpaceHazards;
using Content.Server._StarHorizon.Expedition;
using Content.Server.Salvage;
using Content.Server.Salvage.Expeditions;
using Content.Shared._Horizon.SpaceHazards;
using Content.Shared._StarHorizon.Expedition;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Salvage;
using Content.Shared.Salvage.Expeditions;
using Robust.Shared.GameObjects;
using Robust.Shared.Log;
using Robust.Shared.Map;
using Robust.Shared.Timing;

namespace Content.IntegrationTests.Tests._StarHorizon;

/// <summary>
/// Задание «Чёрный ящик»: ящик лежит в дальней комнате данжа, подбор вызывает волну врагов, доставка на эвакуационный
/// шаттл выполняет задание; интерфейс показывает «найти» и «доставить».
/// </summary>
[TestFixture]
public sealed class ExpeditionBlackBoxTest
{
    [TestPrototypes]
    private const string Prototypes = @"
- type: entity
  id: BlackBoxTestPlayer
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

    [Test]
    public async Task BlackBoxIsFoundAndDelivered()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { FailureLogLevel = LogLevel.Fatal, Connected = true, Dirty = true });
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var salvage = server.System<SalvageSystem>();
        var timing = server.Resolve<IGameTiming>();
        var xform = server.System<SharedTransformSystem>();

        EntityUid planet = default;
        await server.WaitPost(() =>
        {
            planet = server.EntMan.SpawnEntity("ExpeditionPlanet", new MapCoordinates(new Vector2(3000, 0), map.MapId));
            var planetComp = server.EntMan.GetComponent<ExpeditionPlanetComponent>(planet);
            server.System<ExpeditionPlanetSystem>().SetupPlanet(planet, planetComp, "Grasslands", 0.5f);
            salvage.EnsurePlanetMission((planet, planetComp))!.MissionType = SalvageMissionType.Retrieval;
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
        await server.WaitPost(() => server.System<SharedMapSystem>().SetPaused(expeditionMap, false));

        // 1. Ящик в данже, далеко от посадки.
        var retrieval = server.EntMan.GetComponent<SalvageRetrievalExpeditionComponent>(expeditionMap);
        Assert.That(retrieval.Box, Is.Not.Null, "ящик положен");
        var box = retrieval.Box!.Value;
        Assert.That(server.EntMan.GetComponent<TransformComponent>(box).MapUid, Is.EqualTo(expeditionMap));
        var boxDistance = (xform.GetWorldPosition(box) - expedition.LandingCoordinates).Length();
        Assert.That(boxDistance, Is.GreaterThan(10f), "ящик в глубине данжа, не у посадки");

        // Игрок на месте посадки, интерфейс: найти и доставить.
        EntityUid player = default;
        await server.WaitPost(() =>
        {
            player = server.EntMan.SpawnEntity("BlackBoxTestPlayer", new EntityCoordinates(expeditionMap, expedition.LandingCoordinates));
            server.PlayerMan.SetAttachedEntity(server.PlayerMan.Sessions.First(), player, out _, true);
        });
        await pair.RunSeconds(2f);
        var hud = server.EntMan.GetComponent<ExpeditionHudComponent>(expeditionMap);
        Assert.That(hud.Objectives.Select(o => o.Kind), Is.EqualTo(new[] { ExpeditionObjectiveKind.Find, ExpeditionObjectiveKind.Deliver }));
        Assert.That(hud.Objectives.All(o => o.Done == 0));

        // 2. Ящик подобран: найден, сразу пришла волна.
        await server.WaitPost(() =>
        {
            xform.SetCoordinates(player, server.EntMan.GetComponent<TransformComponent>(box).Coordinates);
            Assert.That(server.System<SharedHandsSystem>().TryPickupAnyHand(player, box, checkActionBlocker: false), "ящик в руках");
        });
        await pair.RunSeconds(2f);
        Assert.That(retrieval.Found, "ящик найден");
        Assert.That(server.EntMan.Count<PursuePlayerComponent>(), Is.GreaterThan(0), "сигнал засекли: пришла волна");
        Assert.That(hud.Objectives[0].Done, Is.EqualTo(1));
        Assert.That(retrieval.Delivered, Is.False);

        // 3. Шаттл сел, игрок с ящиком на борту: задание выполнено.
        await server.WaitPost(() => expedition.EndTime = timing.CurTime + TimeSpan.FromSeconds(150));
        for (var i = 0; i < 45 && !hud.ShuttleLanded; i++)
            await pair.RunSeconds(1f);
        Assert.That(hud.ShuttleLanded, "эвакуационный шаттл сел");

        await server.WaitPost(() =>
        {
            var shuttle = server.EntMan.GetEntity(hud.Shuttle!.Value);
            xform.SetCoordinates(player, new EntityCoordinates(shuttle, Vector2.Zero));
        });
        await pair.RunSeconds(2f);
        Assert.That(retrieval.Delivered, "ящик на борту");
        Assert.That(expedition.Completed, "задание выполнено");
        Assert.That(hud.Objectives.All(o => o.Finished), "в интерфейсе обе задачи выполнены");
        Assert.That(hud.Completed);

        await pair.CleanReturnAsync();
    }
}
