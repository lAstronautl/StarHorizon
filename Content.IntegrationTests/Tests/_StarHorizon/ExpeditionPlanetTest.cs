using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Content.Server._Horizon.SpaceHazards;
using Content.Server.Salvage;
using Content.Server.Salvage.Expeditions;
using Content.Server.Storage.EntitySystems;
using Content.Shared._Horizon.SpaceHazards;
using Content.Shared._StarHorizon.DropPod;
using Content.Shared.CCVar;
using Content.Shared.DeviceLinking;
using Content.Shared.Salvage.Expeditions;
using Content.Shared.Storage.Components;
using Robust.Shared.GameObjects;
using Robust.Shared.Log;
using Robust.Shared.Timing;

namespace Content.IntegrationTests.Tests._StarHorizon;

/// <summary>
/// Экспедиция планеты: одно задание, карта создаётся только когда капсулу сбросили, идёт общее время для всех,
/// после закрытия планета получает новое задание.
/// </summary>
[TestFixture]
public sealed class ExpeditionPlanetTest
{
    [TestPrototypes]
    private const string Prototypes = @"
- type: entity
  id: ExpeditionTestPassenger
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

    private static int CountExpeditionMaps(Robust.UnitTesting.RobustIntegrationTest.ServerIntegrationInstance server)
    {
        var count = 0;
        var query = server.EntMan.AllEntityQueryEnumerator<SalvageExpeditionComponent>();
        while (query.MoveNext(out _))
            count++;

        return count;
    }

    private static void Fast(DropPodComponent pod)
    {
        pod.CountdownTime = TimeSpan.FromSeconds(1);
        pod.ZoomOutTime = TimeSpan.FromSeconds(1);
        pod.FlightTime = TimeSpan.FromSeconds(1);
        pod.ArrivalViewTime = TimeSpan.FromSeconds(1);
        pod.VignetteDelay = TimeSpan.FromSeconds(0.3);
    }

    [Test]
    public async Task PlanetExpeditionLifecycle()
    {
        // Fatal: при удалении карты экспедиции движок может (в зависимости от случайного лута) залогировать
        // известную ошибку рекурсивного удаления частей тела трупов; к этой механике она не относится.
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { FailureLogLevel = LogLevel.Fatal });
        var server = pair.Server;
        server.CfgMan.SetCVar(CCVars.FTLArrivalTime, 1f);
        var map = await pair.CreateTestMap();
        var storage = server.System<EntityStorageSystem>();
        var salvage = server.System<SalvageSystem>();
        var xform = server.System<SharedTransformSystem>();
        var links = server.System<SharedDeviceLinkSystem>();

        // Планета вдали от станции, консоль и две капсулы на станции.
        EntityUid planet = default, console = default, pod1 = default, pod2 = default, button = default;
        await server.WaitPost(() =>
        {
            planet = server.EntMan.SpawnEntity("ExpeditionPlanet", new Robust.Shared.Map.MapCoordinates(new Vector2(3000, 0), map.MapId));
            var planetComp = server.EntMan.GetComponent<ExpeditionPlanetComponent>(planet);
            server.System<ExpeditionPlanetSystem>().SetupPlanet(planet, planetComp, "Grasslands", 0.5f);

            console = server.EntMan.SpawnEntity("StarHorizonDropPodConsole", map.GridCoords);
            pod1 = server.EntMan.SpawnEntity("StarHorizonDropPod", map.GridCoords);
            pod2 = server.EntMan.SpawnEntity("StarHorizonDropPod", map.GridCoords);
            Fast(server.EntMan.GetComponent<DropPodComponent>(pod1));
            Fast(server.EntMan.GetComponent<DropPodComponent>(pod2));
            storage.CloseStorage(pod1);
            storage.CloseStorage(pod2);
            button = server.EntMan.SpawnEntity("SignalButton", map.GridCoords);
        });

        // 1. У планеты одно задание, статус Idle, карты экспедиции нет.
        var planetComp1 = server.EntMan.GetComponent<ExpeditionPlanetComponent>(planet);
        var infos = salvage.GetPlanetInfos();
        Assert.That(infos, Has.Count.EqualTo(1));
        Assert.That(infos[0].Status, Is.EqualTo(PlanetExpeditionStatus.Idle));
        Assert.That(planetComp1.Mission, Is.Not.Null, "задание выбрано сразу");
        Assert.That(planetComp1.ExpeditionMap, Is.Null);
        Assert.That(CountExpeditionMaps(server), Is.EqualTo(0), "карта не создаётся, пока не сбросили капсулу");
        var firstMission = planetComp1.Mission!;

        // 2. Капсулы привязаны к консоли и к кнопке; консоль задаёт цель - планету.
        await server.WaitPost(() =>
        {
            links.LinkDefaults(null, console, pod1);
            links.LinkDefaults(null, button, pod1);
            server.EntMan.EventBus.RaiseLocalEvent(console, (object) new DropPodArmPlanetMessage(server.EntMan.GetNetEntity(planet)));
        });
        await pair.RunSeconds(0.3f);
        Assert.That(server.EntMan.GetComponent<DropPodComponent>(pod1).Plan?.Planet, Is.EqualTo(planet), "цель - планета");
        Assert.That(CountExpeditionMaps(server), Is.EqualTo(0), "выбор цели карту не создаёт");

        // 3. Триггер от кнопки: идёт обратный отсчёт, карты всё ещё нет.
        await server.WaitPost(() => links.InvokePort(button, "Pressed"));
        await pair.RunSeconds(0.4f);
        Assert.That(server.EntMan.GetComponent<DropPodComponent>(pod1).State, Is.EqualTo(DropPodState.Countdown));
        Assert.That(CountExpeditionMaps(server), Is.EqualTo(0), "пока капсула не взлетела, карты нет");

        // 4. Капсула взлетела: экспедиция создаётся; ждём, пока job закончит.
        await pair.RunSeconds(1.0f);
        Assert.That(planetComp1.ExpeditionMap, Is.Not.Null, "карта создана при взлёте");

        for (var i = 0; i < 60 && planetComp1.Status != PlanetExpeditionStatus.Active; i++)
            await pair.RunSeconds(2f);

        Assert.That(planetComp1.Status, Is.EqualTo(PlanetExpeditionStatus.Active), "экспедиция создана и идёт");
        var expeditionMap = planetComp1.ExpeditionMap!.Value;
        Assert.That(CountExpeditionMaps(server), Is.EqualTo(1));

        var now = server.Resolve<IGameTiming>().CurTime;
        var duration = TimeSpan.FromSeconds(server.CfgMan.GetCVar(CCVars.SalvageExpeditionDuration));
        Assert.That(planetComp1.EndTime - now, Is.GreaterThan(duration - TimeSpan.FromMinutes(5)).And.LessThanOrEqualTo(duration),
            "идёт обычное время экспедиции");

        // 5. Капсула садится на карту экспедиции.
        var podComp1 = server.EntMan.GetComponent<DropPodComponent>(pod1);
        for (var i = 0; i < 30 && !podComp1.Used; i++)
            await pair.RunSeconds(1f);

        Assert.That(podComp1.Used, "капсула приземлилась");
        Assert.That(server.EntMan.GetComponent<TransformComponent>(pod1).MapUid, Is.EqualTo(expeditionMap), "на карте экспедиции");

        // Приземлившаяся капсула сама открывается с задержкой.
        Assert.That(server.EntMan.GetComponent<EntityStorageComponent>(pod1).Open, Is.False, "сразу после посадки закрыта");
        await pair.RunSeconds(4f);
        Assert.That(server.EntMan.GetComponent<EntityStorageComponent>(pod1).Open, Is.True, "через задержку открылась сама");

        // 6. Вторая капсула присоединяется к той же экспедиции: новая карта не создаётся.
        await server.WaitPost(() =>
        {
            links.LinkDefaults(null, console, pod2);
            server.EntMan.EventBus.RaiseLocalEvent(console, (object) new DropPodLaunchToPlanetMessage(server.EntMan.GetNetEntity(planet)));
        });
        var podComp2 = server.EntMan.GetComponent<DropPodComponent>(pod2);
        for (var i = 0; i < 40 && !podComp2.Used; i++)
            await pair.RunSeconds(1f);

        Assert.That(podComp2.Used, "вторая капсула приземлилась");
        Assert.That(planetComp1.ExpeditionMap, Is.EqualTo(expeditionMap), "экспедиция та же");
        Assert.That(CountExpeditionMaps(server), Is.EqualTo(1), "вторая карта не создана");
        Assert.That(server.EntMan.GetComponent<TransformComponent>(pod2).MapUid, Is.EqualTo(expeditionMap));

        // 7. Экспедиция закрывается по таймеру: карты нет, у планеты НОВОЕ задание.
        await server.WaitPost(() =>
        {
            var expedition = server.EntMan.GetComponent<SalvageExpeditionComponent>(expeditionMap);
            expedition.EndTime = server.Resolve<IGameTiming>().CurTime - TimeSpan.FromSeconds(1);
        });
        await pair.RunSeconds(1.0f);

        Assert.That(planetComp1.Status, Is.EqualTo(PlanetExpeditionStatus.Idle), "экспедиция закрыта");
        Assert.That(planetComp1.ExpeditionMap, Is.Null);
        Assert.That(CountExpeditionMaps(server), Is.EqualTo(0));
        Assert.That(planetComp1.Mission, Is.Not.Null.And.Not.EqualTo(firstMission), "после закрытия брошено новое задание");
        Assert.That(salvage.GetPlanetInfos()[0].Mission.Index, Is.Not.EqualTo(firstMission.Index));

        await pair.CleanReturnAsync();
    }

    /// <summary>Таймер планеты идёт всегда: без карты задание само меняется по его концу; в последние 5 минут капсулы не принимаются.</summary>
    [Test]
    public async Task PlanetTimerRunsAlways()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { FailureLogLevel = LogLevel.Fatal });
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var salvage = server.System<SalvageSystem>();
        var timing = server.Resolve<IGameTiming>();

        EntityUid planet = default, console = default, pod = default;
        await server.WaitPost(() =>
        {
            planet = server.EntMan.SpawnEntity("ExpeditionPlanet", new Robust.Shared.Map.MapCoordinates(new Vector2(3000, 0), map.MapId));
            var planetComp = server.EntMan.GetComponent<ExpeditionPlanetComponent>(planet);
            server.System<ExpeditionPlanetSystem>().SetupPlanet(planet, planetComp, "Grasslands", 0.5f);
            console = server.EntMan.SpawnEntity("StarHorizonDropPodConsole", map.GridCoords);
            pod = server.EntMan.SpawnEntity("StarHorizonDropPod", map.GridCoords);
            server.System<EntityStorageSystem>().CloseStorage(pod);
            server.System<SharedDeviceLinkSystem>().LinkDefaults(null, console, pod);
        });
        await pair.RunSeconds(1f);

        var comp = server.EntMan.GetComponent<ExpeditionPlanetComponent>(planet);
        var duration = TimeSpan.FromSeconds(server.CfgMan.GetCVar(CCVars.SalvageExpeditionDuration));
        Assert.That(salvage.GetPlanetInfos(), Has.Count.EqualTo(1));
        Assert.That(comp.EndTime - timing.CurTime, Is.GreaterThan(duration - TimeSpan.FromMinutes(1)),
            "таймер идёт у задания без карты");

        // Меньше 5 минут: капсулы не принимаются.
        await server.WaitPost(() =>
        {
            comp.EndTime = timing.CurTime + TimeSpan.FromMinutes(4);
            server.EntMan.EventBus.RaiseLocalEvent(console, (object) new DropPodArmPlanetMessage(server.EntMan.GetNetEntity(planet)));
        });
        await pair.RunSeconds(0.3f);
        Assert.That(server.EntMan.GetComponent<DropPodComponent>(pod).Plan?.Planet, Is.Null, "в последние 5 минут не пускаем");

        // Таймер кончился, карты нет: задание сменилось, время уже идёт.
        var first = comp.Mission!;
        await server.WaitPost(() => comp.EndTime = timing.CurTime - TimeSpan.FromSeconds(1));
        await pair.RunSeconds(1f);
        Assert.That(comp.Mission, Is.Not.EqualTo(first), "задание сменилось");
        Assert.That(comp.EndTime - timing.CurTime, Is.GreaterThan(duration - TimeSpan.FromMinutes(1)), "у нового задания время уже идёт");
        Assert.That(comp.Status, Is.EqualTo(PlanetExpeditionStatus.Idle));
        Assert.That(CountExpeditionMaps(server), Is.EqualTo(0));

        await pair.CleanReturnAsync();
    }
}
