using System.Numerics;
using Content.Server._Horizon.SpaceHazards;
using Content.Server._StarHorizon.Expedition;
using Content.Server.Salvage;
using Content.Server.Salvage.Expeditions;
using Content.Shared._Horizon.SpaceHazards;
using Content.Shared.CCVar;
using Content.Shared.Salvage.Expeditions;
using Robust.Shared.GameObjects;
using Robust.Shared.Log;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Timing;

namespace Content.IntegrationTests.Tests._StarHorizon;

/// <summary>
/// Эвакуация с экспедиции: за 2 минуты прилетает alleycat, за 30 секунд улетает с теми, кто на борту, через 10 минут
/// после прилёта к планете корабль удаляется и люди остаются в космосе.
/// </summary>
[TestFixture]
public sealed class ExpeditionExtractionTest
{
    [TestPrototypes]
    private const string Prototypes = @"
- type: entity
  id: ExtractionTestPassenger
  components:
  - type: HumanoidAppearance
    species: Human
  - type: BankAccount
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
    public async Task ExtractionFlow()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { FailureLogLevel = LogLevel.Fatal });
        var server = pair.Server;
        server.CfgMan.SetCVar(CCVars.FTLArrivalTime, 1f);
        var map = await pair.CreateTestMap();
        var salvage = server.System<SalvageSystem>();
        var xform = server.System<SharedTransformSystem>();
        var timing = server.Resolve<IGameTiming>();

        EntityUid planet = default;
        await server.WaitPost(() =>
        {
            planet = server.EntMan.SpawnEntity("ExpeditionPlanet", new MapCoordinates(new Vector2(3000, 0), map.MapId));
            var planetComp = server.EntMan.GetComponent<ExpeditionPlanetComponent>(planet);
            server.System<ExpeditionPlanetSystem>().SetupPlanet(planet, planetComp, "Grasslands", 0.5f);
            salvage.StartPlanetExpedition((planet, planetComp));
        });

        var planetComp1 = server.EntMan.GetComponent<ExpeditionPlanetComponent>(planet);
        for (var i = 0; i < 60 && planetComp1.Status != PlanetExpeditionStatus.Active; i++)
            await pair.RunSeconds(2f);
        Assert.That(planetComp1.Status, Is.EqualTo(PlanetExpeditionStatus.Active));
        var expeditionMap = planetComp1.ExpeditionMap!.Value;

        // Карта ждёт первого прибытия (капсулы) на паузе; здесь снимаем её сами.
        await server.WaitPost(() => server.System<SharedMapSystem>().SetPaused(expeditionMap, false));

        void SetRemaining(TimeSpan remaining) =>
            server.EntMan.GetComponent<SalvageExpeditionComponent>(expeditionMap).EndTime = timing.CurTime + remaining;

        // До двух минут ничего нет.
        await pair.RunSeconds(1.5f);
        var extraction = server.EntMan.GetComponent<ExpeditionExtractionComponent>(expeditionMap);
        Assert.That(extraction.Stage, Is.EqualTo(ExtractionStage.None));
        Assert.That(extraction.Shuttle, Is.Null);

        // 1. За 2 минуты: прилетает alleycat.
        await server.WaitPost(() => SetRemaining(TimeSpan.FromMinutes(1.9)));
        await pair.RunSeconds(1.5f);
        Assert.That(extraction.Stage, Is.EqualTo(ExtractionStage.ShuttleSent));
        var shuttle = extraction.Shuttle!.Value;
        var shuttleComp = server.EntMan.GetComponent<ExtractionShuttleComponent>(shuttle);
        for (var i = 0; i < 60 && !shuttleComp.Arrived; i++)
            await pair.RunSeconds(1f);
        Assert.That(shuttleComp.Arrived, "шаттл прилетел");
        Assert.That(server.EntMan.GetComponent<TransformComponent>(shuttle).MapUid, Is.EqualTo(expeditionMap));

        // 2. Один игрок заходит на шаттл, второй остаётся на карте.
        EntityUid aboard = default, left = default;
        await server.WaitPost(() =>
        {
            var grid = server.EntMan.GetComponent<MapGridComponent>(shuttle);
            aboard = server.EntMan.SpawnEntity("ExtractionTestPassenger", new EntityCoordinates(shuttle, grid.LocalAABB.Center));
            left = server.EntMan.SpawnEntity("ExtractionTestPassenger", server.EntMan.GetComponent<TransformComponent>(shuttle).Coordinates.Offset(new Vector2(30, 0)));
        });
        Assert.That(server.EntMan.GetComponent<TransformComponent>(aboard).GridUid, Is.EqualTo(shuttle), "на борту");

        // 3. За 30 секунд: шаттл улетает (после перезарядки прыжка).
        await server.WaitPost(() => SetRemaining(TimeSpan.FromSeconds(29)));
        for (var i = 0; i < 30 && extraction.Stage != ExtractionStage.Departed; i++)
            await pair.RunSeconds(1f);
        Assert.That(extraction.Stage, Is.EqualTo(ExtractionStage.Departed));
        Assert.That(server.EntMan.GetComponent<TransformComponent>(left).MapUid, Is.EqualTo(expeditionMap), "второй остался на карте");
        await server.WaitPost(() => SetRemaining(TimeSpan.FromMinutes(5))); // карта нужна до конца теста

        // 4. Корабль в космосе у планеты, зацепившийся с ним.
        Assert.That(shuttleComp.Departed);
        var planetMap = server.EntMan.GetComponent<TransformComponent>(planet).MapUid!.Value;
        for (var i = 0; i < 60 && shuttleComp.DeleteAt == null; i++)
            await pair.RunSeconds(1f);
        Assert.That(shuttleComp.DeleteAt, Is.Not.Null, "прилетел к планете");
        Assert.That(server.EntMan.GetComponent<TransformComponent>(shuttle).MapUid, Is.EqualTo(planetMap));
        Assert.That(server.EntMan.GetComponent<TransformComponent>(aboard).MapUid, Is.EqualTo(planetMap), "пассажир долетел");
        Assert.That(server.EntMan.GetComponent<TransformComponent>(aboard).ParentUid, Is.EqualTo(shuttle));

        // 5. Через 10 минут корабль удаляется, человек остаётся в космосе.
        await server.WaitPost(() => shuttleComp.DeleteAt = timing.CurTime);
        await pair.RunSeconds(1.5f);
        Assert.That(server.EntMan.EntityExists(shuttle), Is.False, "корабль удалён");
        Assert.That(server.EntMan.EntityExists(aboard), "пассажир жив");
        Assert.That(server.EntMan.GetComponent<TransformComponent>(aboard).MapUid, Is.EqualTo(planetMap), "остался в космосе у планеты");

        // 6. Карта закрывается, не зацепившийся пропадает с ней.
        await server.WaitPost(() => SetRemaining(TimeSpan.FromSeconds(-1)));
        await pair.RunSeconds(1.5f);
        Assert.That(planetComp1.Status, Is.EqualTo(PlanetExpeditionStatus.Idle));

        await pair.CleanReturnAsync();
    }
}
