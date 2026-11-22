using System.Numerics;
using Content.Server._Horizon.SpaceHazards;
using Content.Server._StarHorizon.Expedition;
using Content.Server.Salvage;
using Content.Server.Salvage.Expeditions;
using Content.Shared._Horizon.SpaceHazards;
using Content.Shared.CCVar;
using Content.Shared.Salvage.Expeditions;
using Content.Shared.Weapons.Ranged.Components;
using Robust.Shared.GameObjects;
using Robust.Shared.Log;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Timing;

namespace Content.IntegrationTests.Tests._StarHorizon;

/// <summary>
/// Эвакуация с экспедиции: за 5 минут маяк и крюки, за 2 прилетает alleycat, за 1 он улетает с теми, кто зацепился
/// за маяк, через 10 минут после прилёта к планете корабль удаляется и люди остаются в космосе.
/// </summary>
[TestFixture]
public sealed class ExpeditionExtractionTest
{
    [TestPrototypes]
    private const string Prototypes = @"
- type: entity
  id: ExtractionTestHook

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

        // До пяти минут ничего нет.
        await pair.RunSeconds(1.5f);
        var extraction = server.EntMan.GetComponent<ExpeditionExtractionComponent>(expeditionMap);
        Assert.That(extraction.Stage, Is.EqualTo(ExtractionStage.None));

        // 1. За 5 минут: маяк и крюки.
        await server.WaitPost(() => SetRemaining(TimeSpan.FromMinutes(4.9)));
        await pair.RunSeconds(1.5f);
        Assert.That(extraction.Stage, Is.EqualTo(ExtractionStage.BeaconPlaced));
        var beacon = extraction.Beacon!.Value;
        Assert.That(server.EntMan.GetComponent<TransformComponent>(beacon).MapUid, Is.EqualTo(expeditionMap));

        // Игрок с крюком, который зацепился за маяк, и игрок, который не зацепился.
        EntityUid hooked = default, free = default, gun = default, hook = default;
        await server.WaitPost(() =>
        {
            var beaconCoords = server.EntMan.GetComponent<TransformComponent>(beacon).Coordinates;
            hooked = server.EntMan.SpawnEntity("ExtractionTestPassenger", beaconCoords.Offset(new Vector2(0, -3)));
            free = server.EntMan.SpawnEntity("ExtractionTestPassenger", beaconCoords.Offset(new Vector2(3, 3)));
            gun = server.EntMan.SpawnEntity("WeaponGrapplingGun", beaconCoords.Offset(new Vector2(0, -3)));
            xform.SetParent(gun, hooked);
            hook = server.EntMan.SpawnEntity("ExtractionTestHook", beaconCoords);
            xform.SetParent(hook, beacon);
            var gunComp = server.EntMan.GetComponent<GrapplingGunComponent>(gun);
            gunComp.Projectile = hook;
        });

        // 2. За 2 минуты: прилетает alleycat, берёт маяк.
        await server.WaitPost(() => SetRemaining(TimeSpan.FromMinutes(1.9)));
        await pair.RunSeconds(1.5f);
        Assert.That(extraction.Stage, Is.EqualTo(ExtractionStage.ShuttleSent));
        var shuttle = extraction.Shuttle!.Value;
        var shuttleComp = server.EntMan.GetComponent<ExtractionShuttleComponent>(shuttle);
        for (var i = 0; i < 60 && !shuttleComp.Arrived; i++)
            await pair.RunSeconds(1f);
        Assert.That(shuttleComp.Arrived, "корабль прилетел");
        Assert.That(server.EntMan.GetComponent<TransformComponent>(shuttle).MapUid, Is.EqualTo(expeditionMap));
        await pair.RunSeconds(0.5f);
        Assert.That(server.EntMan.HasComponent<ExtractionPassengerComponent>(beacon), "beacon passenger comp");
        var bpass = server.EntMan.GetComponent<ExtractionPassengerComponent>(beacon);
        Assert.That(bpass.Grid, Is.EqualTo(shuttle), $"grid {bpass.Grid} local {bpass.Local}");
        Assert.That(server.EntMan.GetComponent<TransformComponent>(beacon).ParentUid, Is.EqualTo(shuttle), "маяк на корабле");

        await pair.RunSeconds(1f);

        // 3. За минуту: корабль улетает вместе с зацепившимся, второй остаётся.
        await server.WaitPost(() => SetRemaining(TimeSpan.FromSeconds(50)));
        // Сначала у корабля идёт перезарядка прыжка после прилёта, потом он улетает.
        for (var i = 0; i < 30 && extraction.Stage != ExtractionStage.Departed; i++)
            await pair.RunSeconds(1f);
        Assert.That(extraction.Stage, Is.EqualTo(ExtractionStage.Departed));
        Assert.That(server.EntMan.GetComponent<TransformComponent>(hooked).ParentUid, Is.EqualTo(shuttle), "висит снаружи корабля");
        Assert.That(server.EntMan.GetComponent<TransformComponent>(free).MapUid, Is.EqualTo(expeditionMap));

        // 4. Корабль в космосе у планеты, зацепившийся с ним.
        Assert.That(shuttleComp.Departed);
        var planetMap = server.EntMan.GetComponent<TransformComponent>(planet).MapUid!.Value;
        for (var i = 0; i < 60 && shuttleComp.DeleteAt == null; i++)
            await pair.RunSeconds(1f);
        Assert.That(shuttleComp.DeleteAt, Is.Not.Null, "прилетел к планете");
        Assert.That(server.EntMan.GetComponent<TransformComponent>(shuttle).MapUid, Is.EqualTo(planetMap));
        Assert.That(server.EntMan.GetComponent<TransformComponent>(hooked).MapUid, Is.EqualTo(planetMap), "пассажир долетел");
        Assert.That(server.EntMan.GetComponent<TransformComponent>(hooked).ParentUid, Is.EqualTo(shuttle));

        // 5. Через 10 минут корабль удаляется, человек остаётся в космосе.
        await server.WaitPost(() => shuttleComp.DeleteAt = timing.CurTime);
        await pair.RunSeconds(1.5f);
        Assert.That(server.EntMan.EntityExists(shuttle), Is.False, "корабль удалён");
        Assert.That(server.EntMan.EntityExists(hooked), "пассажир жив");
        Assert.That(server.EntMan.GetComponent<TransformComponent>(hooked).MapUid, Is.EqualTo(planetMap), "остался в космосе у планеты");
        Assert.That(server.EntMan.HasComponent<ExtractionPassengerComponent>(hooked), Is.False);

        // 6. Карта закрывается, не зацепившийся пропадает с ней.
        await server.WaitPost(() => SetRemaining(TimeSpan.FromSeconds(-1)));
        await pair.RunSeconds(1.5f);
        Assert.That(planetComp1.Status, Is.EqualTo(PlanetExpeditionStatus.Idle));

        await pair.CleanReturnAsync();
    }
}
