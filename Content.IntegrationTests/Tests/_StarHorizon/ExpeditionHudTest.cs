using System.Linq;
using System.Numerics;
using Content.Client._StarHorizon.Expedition;
using Content.Server._NF.Salvage.Expeditions;
using Content.Server._NF.Salvage.Expeditions.Structure;
using Content.Server._Horizon.SpaceHazards;
using Content.Server.Salvage;
using Content.Server.Salvage.Expeditions;
using Content.Shared._Horizon.SpaceHazards;
using Content.Shared._StarHorizon.Expedition;
using Content.Shared.Salvage.Expeditions;
using Robust.Shared.GameObjects;
using Robust.Shared.Log;
using Robust.Shared.Map;
using Robust.Shared.Timing;

namespace Content.IntegrationTests.Tests._StarHorizon;

/// <summary>
/// Интерфейс экспедиции в стиле DRG: виден на карте экспедиции, показывает задачи и их прогресс, время до эвакуации,
/// затем время до вылета, расстояние до севшего шаттла и «на борту».
/// </summary>
[TestFixture]
public sealed class ExpeditionHudTest
{
    [TestPrototypes]
    private const string Prototypes = @"
- type: entity
  id: HudTestPlayer
  components:
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
    public async Task HudShowsObjectivesAndExtraction()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { FailureLogLevel = LogLevel.Fatal, Connected = true, Dirty = true });
        var server = pair.Server;
        var client = pair.Client;
        var map = await pair.CreateTestMap();
        var salvage = server.System<SalvageSystem>();
        var timing = server.Resolve<IGameTiming>();
        var hudSystem = client.System<ExpeditionHudSystem>();

        EntityUid planet = default;
        await server.WaitPost(() =>
        {
            planet = server.EntMan.SpawnEntity("ExpeditionPlanet", new MapCoordinates(new Vector2(3000, 0), map.MapId));
            var planetComp = server.EntMan.GetComponent<ExpeditionPlanetComponent>(planet);
            server.System<ExpeditionPlanetSystem>().SetupPlanet(planet, planetComp, "Grasslands", 0.5f);
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

        // Не на карте экспедиции: интерфейса нет.
        await pair.RunSeconds(1f);
        Assert.That(hudSystem.TryGetView(out _), Is.False, "игрок не на экспедиции");

        EntityUid player = default;
        await server.WaitPost(() =>
        {
            player = server.EntMan.SpawnEntity("HudTestPlayer", new EntityCoordinates(expeditionMap, expedition.LandingCoordinates));
            server.PlayerMan.SetAttachedEntity(server.PlayerMan.Sessions.First(), player, out _, true);
        });
        await pair.RunSeconds(2f);

        // 1. Задачи и время до эвакуации.
        var hud = server.EntMan.GetComponent<ExpeditionHudComponent>(expeditionMap);
        Assert.That(hud.Objectives, Is.Not.Empty, "у задания есть задачи");
        Assert.That(hud.ExtractionAt, Is.EqualTo(expedition.EndTime - TimeSpan.FromMinutes(3)));

        await client.WaitAssertion(() =>
        {
            Assert.That(hudSystem.TryGetView(out var view), "игрок на экспедиции: интерфейс виден");
            Assert.That(view.Phase, Is.EqualTo(ExpeditionHudPhase.Countdown));
            Assert.That(view.Objectives, Has.Count.EqualTo(hud.Objectives.Count));
            Assert.That(view.Objectives.All(o => o.Total > 0 && o.Done == 0 && !o.Finished));
            Assert.That(view.Timer.TotalSeconds, Is.GreaterThan(60));
        });

        // 2. Одна цель уничтожена: прогресс растёт.
        var kind = hud.Objectives[0].Kind;
        await server.WaitPost(() =>
        {
            EntityUid target = kind == ExpeditionObjectiveKind.Destroy
                ? server.EntMan.GetComponent<SalvageDestructionExpeditionComponent>(expeditionMap).Structures[0]
                : server.EntMan.GetComponent<SalvageEliminationExpeditionComponent>(expeditionMap).Megafauna[0];
            server.EntMan.DeleteEntity(target);
        });
        await pair.RunSeconds(2f);
        await client.WaitAssertion(() =>
        {
            Assert.That(hudSystem.TryGetView(out var view));
            Assert.That(view.Objectives[0].Done, Is.EqualTo(1), "одна цель выполнена");
        });

        // 3. Шаттл вызван: время до вылета; сел: расстояние до шаттла.
        await server.WaitPost(() => expedition.EndTime = timing.CurTime + TimeSpan.FromSeconds(150));
        await pair.RunSeconds(2f);
        await client.WaitAssertion(() =>
        {
            Assert.That(hudSystem.TryGetView(out var view));
            Assert.That(view.Phase, Is.EqualTo(ExpeditionHudPhase.ShuttleIncoming).Or.EqualTo(ExpeditionHudPhase.Return));
        });

        for (var i = 0; i < 40 && !hud.ShuttleLanded; i++)
            await pair.RunSeconds(1f);
        Assert.That(hud.ShuttleLanded, "эвакуационный шаттл сел");
        await pair.RunSeconds(1f);
        await client.WaitAssertion(() =>
        {
            Assert.That(hudSystem.TryGetView(out var view));
            Assert.That(view.Phase, Is.EqualTo(ExpeditionHudPhase.Return));
            Assert.That(view.ToShuttle, Is.Not.Null, "известно, где шаттл");
            Assert.That(view.ToShuttle!.Value.Length(), Is.GreaterThan(3f));
        });

        // 4. Игрок на борту.
        await server.WaitPost(() =>
        {
            var shuttle = server.EntMan.GetEntity(hud.Shuttle!.Value);
            server.System<SharedTransformSystem>().SetCoordinates(player, new EntityCoordinates(shuttle, Vector2.Zero));
        });
        await pair.RunSeconds(1f);
        await client.WaitAssertion(() =>
        {
            Assert.That(hudSystem.TryGetView(out var view));
            Assert.That(view.Phase, Is.EqualTo(ExpeditionHudPhase.Aboard));
        });

        await pair.CleanReturnAsync();
    }
}
