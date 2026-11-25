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

    /// <summary>Длительность задания планеты (случайная у биомов вроде Анко, плюс редкие модификаторы времени).</summary>
    private static TimeSpan MissionDuration(Robust.UnitTesting.RobustIntegrationTest.ServerIntegrationInstance server,
        SalvageSystem salvage, Content.Shared.Salvage.Expeditions.SalvageMissionParams mission)
    {
        return salvage.GetMission(mission.MissionType,
            server.ProtoMan.Index<Content.Shared.Procedural.SalvageDifficultyPrototype>(mission.Difficulty), mission.Seed, mission.Biome).Duration;
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

        // Пока экспедиция генерируется, БСС удлиняется: капсула не прилетает на недостроенную карту.
        var podState1 = server.EntMan.GetComponent<DropPodComponent>(pod1);
        var generatingBeyondFlight = 0;
        for (var i = 0; i < 120 && planetComp1.Status != PlanetExpeditionStatus.Active; i++)
        {
            await pair.RunSeconds(1f);
            // Полёт (1 с) и прибытие (5 с) уже закончились бы, а карта ещё строится.
            if (i >= 9 && planetComp1.Status == PlanetExpeditionStatus.Generating)
            {
                generatingBeyondFlight++;
                Assert.That(podState1.State, Is.EqualTo(DropPodState.InBluespace), "карта строится: капсула ещё в БСС");
            }
        }

        TestContext.WriteLine($"Ticks of extended bluespace while generating: {generatingBeyondFlight}");
        Assert.That(planetComp1.Status, Is.EqualTo(PlanetExpeditionStatus.Active), "экспедиция создана и идёт");
        var expeditionMap = planetComp1.ExpeditionMap!.Value;
        Assert.That(CountExpeditionMaps(server), Is.EqualTo(1));

        var now = server.Resolve<IGameTiming>().CurTime;
        var duration = MissionDuration(server, salvage, planetComp1.Mission!);
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

        var baselineMaps = CountExpeditionMaps(server); // карты от предыдущих тестов пула не считаем
        var comp = server.EntMan.GetComponent<ExpeditionPlanetComponent>(planet);
        var duration = MissionDuration(server, salvage, comp.Mission!);
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
        var newDuration = MissionDuration(server, salvage, comp.Mission!);
        Assert.That(comp.EndTime - timing.CurTime, Is.GreaterThan(newDuration - TimeSpan.FromMinutes(1)), "у нового задания время уже идёт");
        Assert.That(comp.Status, Is.EqualTo(PlanetExpeditionStatus.Idle));
        Assert.That(CountExpeditionMaps(server), Is.EqualTo(baselineMaps));

        await pair.CleanReturnAsync();
    }

    /// <summary>
    /// БСС капсулы полный (15 с полёта + 5 с прибытия), а пока экспедиция генерируется, он удлиняется: капсула не прилетает
    /// на недостроенную карту и садится, только когда экспедиция идёт.
    /// </summary>
    [Test]
    public async Task BluespaceExtendsWhileGenerating()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { FailureLogLevel = LogLevel.Fatal });
        var server = pair.Server;
        server.CfgMan.SetCVar(CCVars.FTLArrivalTime, 1f);
        var map = await pair.CreateTestMap();
        var storage = server.System<EntityStorageSystem>();
        var links = server.System<SharedDeviceLinkSystem>();

        // Полный БСС по умолчанию, как у команды ftlentity: полёт не короче 15 с.
        Assert.That(new DropPodComponent().FlightTime, Is.GreaterThanOrEqualTo(TimeSpan.FromSeconds(15)));

        EntityUid planet = default, console = default, pod = default;
        await server.WaitPost(() =>
        {
            planet = server.EntMan.SpawnEntity("ExpeditionPlanet", new Robust.Shared.Map.MapCoordinates(new Vector2(3000, 0), map.MapId));
            var planetComp = server.EntMan.GetComponent<ExpeditionPlanetComponent>(planet);
            server.System<ExpeditionPlanetSystem>().SetupPlanet(planet, planetComp, "Grasslands", 0.5f);
            console = server.EntMan.SpawnEntity("StarHorizonDropPodConsole", map.GridCoords);
            pod = server.EntMan.SpawnEntity("StarHorizonDropPod", map.GridCoords);
            var comp = server.EntMan.GetComponent<DropPodComponent>(pod);
            Fast(comp);
            comp.FlightTime = TimeSpan.FromSeconds(1);
            storage.CloseStorage(pod);
            links.LinkDefaults(null, console, pod);
        });

        // Планета бросает задание и запускает таймер цикла (иначе капсулы не принимаются).
        await pair.RunSeconds(1.5f);
        await server.WaitPost(() =>
            server.EntMan.EventBus.RaiseLocalEvent(console, (object) new DropPodLaunchToPlanetMessage(server.EntMan.GetNetEntity(planet))));

        var planetState = server.EntMan.GetComponent<ExpeditionPlanetComponent>(planet);
        var podState = server.EntMan.GetComponent<DropPodComponent>(pod);

        // Капсула в БСС: притворяемся, что генерация идёт всё время (настоящая успевает за пару секунд).
        for (var i = 0; i < 40 && podState.State != DropPodState.InBluespace; i++)
            await pair.RunSeconds(0.5f);
        Assert.That(podState.State, Is.EqualTo(DropPodState.InBluespace));

        for (var i = 0; i < 40; i++)
        {
            await server.WaitPost(() => planetState.Status = PlanetExpeditionStatus.Generating);
            await pair.RunSeconds(0.5f);
            Assert.That(podState.State, Is.EqualTo(DropPodState.InBluespace), $"генерация идёт ({i * 0.5f} с): БСС удлиняется");
        }

        // Экспедиция построена: капсула прилетает и садится.
        await server.WaitPost(() => planetState.Status = PlanetExpeditionStatus.Active);
        for (var i = 0; i < 60 && !podState.Used; i++)
            await pair.RunSeconds(1f);
        Assert.That(podState.Used, "экспедиция готова: капсула приземлилась");

        await pair.CleanReturnAsync();
    }

    /// <summary>
    /// У биома Анко длительность случайная (20-50 минут по биому), у остальных 30 минут; редкие модификаторы времени
    /// (-5 и +10 минут) выпадают по сиду и показываются в списке модификаторов.
    /// </summary>
    [Test]
    public async Task RandomDurationAndRareTimeMods()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { FailureLogLevel = LogLevel.Fatal });
        var server = pair.Server;
        var salvage = server.System<SalvageSystem>();
        var cvar = TimeSpan.FromSeconds(server.CfgMan.GetCVar(CCVars.SalvageExpeditionDuration));
        var inferno = server.ProtoMan.Index<Content.Shared.Procedural.SalvageDifficultyPrototype>("Inferno");
        var moderate = server.ProtoMan.Index<Content.Shared.Procedural.SalvageDifficultyPrototype>("NFModerate");
        var anco = new List<TimeSpan>();
        var shortCount = 0;
        var longCount = 0;
        const int samples = 1500;

        await server.WaitPost(() =>
        {
            for (var seed = 1; seed <= samples; seed++)
            {
                var a = salvage.GetMission(Content.Shared.Salvage.SalvageMissionType.Combined, inferno, seed, "ArdoniumWastes");
                anco.Add(a.Duration);

                var m = salvage.GetMission(Content.Shared.Salvage.SalvageMissionType.Destruction, moderate, seed, "Grasslands");
                if (m.Duration == cvar - TimeSpan.FromMinutes(5))
                    shortCount++;
                else if (m.Duration == cvar + TimeSpan.FromMinutes(10))
                    longCount++;
                else
                    Assert.That(m.Duration, Is.EqualTo(cvar), "без модификатора времени: стандартные 30 минут");
            }
        });

        // Анко: случайное время 20-50 минут по биому (плюс возможный сдвиг модификатором).
        Assert.That(anco.Distinct().Count(), Is.GreaterThan(50), "время у Анко случайное");
        Assert.That(anco.Min(), Is.GreaterThanOrEqualTo(TimeSpan.FromSeconds(1200) - TimeSpan.FromMinutes(5)));
        Assert.That(anco.Max(), Is.LessThanOrEqualTo(TimeSpan.FromSeconds(3000) + TimeSpan.FromMinutes(10)));

        // Модификаторы редкие: примерно 7% каждого.
        Assert.That(shortCount, Is.InRange(samples * 0.03, samples * 0.12), $"-5 минут: {shortCount} из {samples}");
        Assert.That(longCount, Is.InRange(samples * 0.03, samples * 0.12), $"+10 минут: {longCount} из {samples}");

        await pair.CleanReturnAsync();
    }
}
