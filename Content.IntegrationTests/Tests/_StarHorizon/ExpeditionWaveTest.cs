using Robust.Shared.Maths;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Content.Server._Horizon.SpaceHazards;
using Content.Server._StarHorizon.Expedition;
using Content.Server.NPC;
using Content.Server.NPC.HTN;
using Content.Server.Salvage;
using Content.Server.Salvage.Expeditions;
using Content.Shared._Horizon.SpaceHazards;
using Content.Shared.Salvage.Expeditions;
using Robust.Shared.GameObjects;
using Content.Server._StarHorizon.Expedition;
using Robust.Shared.Log;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Timing;

namespace Content.IntegrationTests.Tests._StarHorizon;

/// <summary>
/// Волны врагов на экспедиции: без игроков волн нет, с игроком приходит волна вокруг него, мобы волны всегда знают,
/// где игрок, и идут к нему; в последние секунды экспедиции волны прекращаются.
/// </summary>
[TestFixture]
public sealed class ExpeditionWaveTest
{
    [TestPrototypes]
    private const string Prototypes = @"
- type: entity
  id: PursueTestMelee
  components:
  - type: HTN
    rootTask:
      task: SimpleHostileCompound

- type: entity
  id: PursueTestRanged
  components:
  - type: HTN
    rootTask:
      task: SimpleRangedHostileCompound

- type: entity
  id: PursueTestHumanoid
  components:
  - type: HTN
    rootTask:
      task: SimpleHumanoidHostileCompound

- type: entity
  id: WaveTestPlayer
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

    private static List<EntityUid> Pursuers(Robust.UnitTesting.RobustIntegrationTest.ServerIntegrationInstance server)
    {
        var list = new List<EntityUid>();
        var query = server.EntMan.AllEntityQueryEnumerator<PursuePlayerComponent>();
        while (query.MoveNext(out var uid, out _))
            list.Add(uid);

        return list;
    }

    /// <summary>Не у всех мобов есть состояние крита: таким в тесте вместо него ставим смерть.</summary>
    private static bool CanBeCritical(Robust.UnitTesting.RobustIntegrationTest.ServerIntegrationInstance server, EntityUid mob)
    {
        return server.System<Content.Shared.Mobs.Systems.MobStateSystem>().HasState(mob, Content.Shared.Mobs.MobState.Critical);
    }

    [Test]
    public async Task WavesPursuePlayer()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { FailureLogLevel = LogLevel.Fatal, Connected = true });
        var server = pair.Server;
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
        // Генерация случайного задания изредка проваливается (планета возвращается в Idle): пробуем заново.
        for (var attempt = 0; attempt < 3 && planetComp1.Status != PlanetExpeditionStatus.Active; attempt++)
        {
            if (planetComp1.Status == PlanetExpeditionStatus.Idle && attempt > 0)
                await server.WaitPost(() => salvage.StartPlanetExpedition((planet, planetComp1)));

            for (var i = 0; i < 60 && planetComp1.Status == PlanetExpeditionStatus.Generating; i++)
                await pair.RunSeconds(2f);
        }
        Assert.That(planetComp1.Status, Is.EqualTo(PlanetExpeditionStatus.Active));
        var expeditionMap = planetComp1.ExpeditionMap!.Value;
        await server.WaitPost(() => server.System<SharedMapSystem>().SetPaused(expeditionMap, false));

        var expedition = server.EntMan.GetComponent<SalvageExpeditionComponent>(expeditionMap);

        // 1. Игроков на карте нет: волн нет.
        await pair.RunSeconds(2f);
        var waves = server.EntMan.GetComponent<ExpeditionWavesComponent>(expeditionMap);
        Assert.That(waves.NextWave, Is.Null, "без игроков таймер волны не идёт");
        Assert.That(Pursuers(server), Is.Empty);
        var before = server.EntMan.Count<Content.Shared.Mobs.Components.MobStateComponent>();

        // 2. Игрок появляется на карте: запускается таймер первой волны.
        EntityUid player = default;
        await server.WaitPost(() =>
        {
            player = server.EntMan.SpawnEntity("WaveTestPlayer", new EntityCoordinates(expeditionMap, expedition.LandingCoordinates));
            var session = server.PlayerMan.Sessions.First();
            server.PlayerMan.SetAttachedEntity(session, player, out _, true);
        });
        await pair.RunSeconds(1.5f);
        Assert.That(waves.NextWave, Is.Not.Null, "игрок на карте: таймер первой волны пошёл");
        Assert.That(waves.NextWave!.Value - timing.CurTime, Is.GreaterThan(TimeSpan.FromSeconds(30)).And.LessThan(TimeSpan.FromSeconds(80)));
        Assert.That(Pursuers(server), Is.Empty);

        // 3. Волна приходит: мобы фракции в дроп-подах DropPodspawner, у каждого маркер погони.
        Assert.That(waves.DropAnchor, Is.Not.Null, "место падения запомнено: где начал первый игрок");
        var anchor = waves.DropAnchor!.Value;
        Assert.That((anchor - xform.GetWorldPosition(player)).Length(), Is.LessThan(2f));

        await server.WaitPost(() => waves.NextWave = timing.CurTime);
        await pair.RunSeconds(1.5f);
        var mobs = Pursuers(server);
        Assert.That(mobs, Is.Not.Empty, "волна пришла");
        Assert.That(mobs.Count, Is.LessThanOrEqualTo(waves.MaxAliveWaveMobs));
        var pods = server.EntMan.AllEntities<MetaDataComponent>()
            .Count(e => e.Comp.EntityPrototype?.ID == ExpeditionWaveSystem.PodProto);
        Assert.That(pods, Is.GreaterThan(0), "мобы падают в дроп-подах");
        Assert.That(pods, Is.GreaterThanOrEqualTo((int) Math.Ceiling(mobs.Count / (double) waves.MobsPerPod)), "по MobsPerPod мобов в поде");
        Assert.That(server.EntMan.GetComponent<Content.Shared._StarHorizon.Expedition.ExpeditionWaveStateComponent>(expeditionMap).Active, "волна идёт");

        // Дроп-под падает и открывается: мобы выходят у якоря (не там, где игрок сейчас).
        await pair.RunSeconds(14f);
        var container = server.System<Robust.Shared.Containers.SharedContainerSystem>();
        foreach (var mob in mobs)
        {
            Assert.That(server.EntMan.EntityExists(mob));
            Assert.That(container.IsEntityInContainer(mob), Is.False, "вышли из пода");
            Assert.That(server.EntMan.GetComponent<TransformComponent>(mob).MapUid, Is.EqualTo(expeditionMap));
        }

        // 4. Мобы знают, где игрок (FollowTarget) и сменили корень ИИ на погоню.
        await pair.RunSeconds(1.5f);
        // Не у всех мобов фракции есть ИИ (HTN): проверяем тех, у кого есть.
        foreach (var mob in mobs.Where(m => server.EntMan.EntityExists(m) && server.EntMan.HasComponent<HTNComponent>(m)))
        {
            var htn = server.EntMan.GetComponent<HTNComponent>(mob);
            Assert.That(htn.RootTask.Task, Does.StartWith("Pursue"));
            Assert.That(htn.Blackboard.TryGetValue<EntityCoordinates>(NPCBlackboard.FollowTarget, out var target, server.EntMan), "знает цель");
            Assert.That(target.EntityId, Is.EqualTo(player));
        }

        // 5. Мобы идут к игроку: ближайший заметно приблизился.
        var playerPos = xform.GetWorldPosition(player);
        var startMin = mobs.Min(m => (xform.GetWorldPosition(m) - playerPos).Length());
        await pair.RunSeconds(15f);
        var endMin = mobs.Where(server.EntMan.EntityExists).Min(m => (xform.GetWorldPosition(m) - xform.GetWorldPosition(player)).Length());
        Assert.That(endMin, Is.LessThan(startMin).Or.LessThanOrEqualTo(11f), "мобы подошли к игроку (дальники встают на дальности стрельбы, медленные просто ближе, чем были)");

        // 5a. Музыка выключается, когда 90% мобов волны в крите или убиты (остаётся 10% и меньше в строю).
        var total = waves.WaveTotal;
        var keepFighting = (int) Math.Floor(total * 0.1f);
        // Считаются только мобы, которых можно убить (с MobState): у строений и турелей его нет.
        var all = Pursuers(server).Where(m => server.EntMan.HasComponent<Content.Shared.Mobs.Components.MobStateComponent>(m)).ToList();
        Assert.That(total, Is.EqualTo(all.Count), "в волне считаются мобы с MobState");
        Assert.That(total, Is.GreaterThan(0));
        var waveState = server.EntMan.GetComponent<Content.Shared._StarHorizon.Expedition.ExpeditionWaveStateComponent>(expeditionMap);
        await server.WaitPost(() =>
        {
            var mobState = server.System<Content.Shared.Mobs.Systems.MobStateSystem>();
            for (var i = keepFighting + 1; i < all.Count; i++)
            {
                // Половина в крите, половина убита.
                mobState.ChangeMobState(all[i], i % 2 == 0 && CanBeCritical(server, all[i])
                    ? Content.Shared.Mobs.MobState.Critical
                    : Content.Shared.Mobs.MobState.Dead);
            }
        });
        await pair.RunSeconds(1.5f);
        Assert.That(waveState.Active, $"в строю {keepFighting + 1} из {total}: больше 10%, музыка идёт");

        await server.WaitPost(() =>
            server.System<Content.Shared.Mobs.Systems.MobStateSystem>().ChangeMobState(all[keepFighting],
                CanBeCritical(server, all[keepFighting]) ? Content.Shared.Mobs.MobState.Critical : Content.Shared.Mobs.MobState.Dead));
        await pair.RunSeconds(1.5f);
        Assert.That(waveState.Active, Is.False, $"в строю {keepFighting} из {total} (мобов {all.Count}, живых {all.Count(m => server.System<Content.Shared.Mobs.Systems.MobStateSystem>().IsAlive(m))}): 90% в крите или убиты, музыка выключена");

        // 6. В последние секунды экспедиции волн нет.
        var countBefore = Pursuers(server).Count;
        await server.WaitPost(() =>
        {
            expedition.EndTime = timing.CurTime + TimeSpan.FromSeconds(20);
            waves.NextWave = timing.CurTime;
        });
        await pair.RunSeconds(2f);
        Assert.That(Pursuers(server).Count, Is.LessThanOrEqualTo(countBefore), "новых мобов нет");

        await pair.CleanReturnAsync();
    }

    /// <summary>Дальники остаются дальниками: у стрелков корень погони с дальним боем, а не только ближним.</summary>
    [Test]
    public async Task PursueRootKeepsCombatStyle()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { FailureLogLevel = LogLevel.Fatal });
        var server = pair.Server;
        var map = await pair.CreateTestMap();

        await server.WaitPost(() =>
        {
            foreach (var (proto, expected) in new[]
                     {
                         ("PursueTestMelee", "PursueMeleeCompound"),
                         ("PursueTestRanged", "PursueRangedCompound"),
                         ("PursueTestHumanoid", "PursueHumanoidCompound"),
                     })
            {
                var mob = server.EntMan.SpawnEntity(proto, map.GridCoords);
                server.EntMan.AddComponent<PursuePlayerComponent>(mob);
                Assert.That(server.EntMan.GetComponent<HTNComponent>(mob).RootTask.Task, Is.EqualTo(expected), proto);
            }
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>Замурованная породой щель не годится для падения подов, открытое место годится.</summary>
    [Test]
    public async Task ClosedPocketsAreNotDropSites()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { FailureLogLevel = LogLevel.Fatal });
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var rocks = server.System<RockClearSystem>();

        await server.WaitPost(() =>
        {
            var mapSys = server.System<SharedMapSystem>();
            var gridUid = map.Grid.Owner;
            for (var x = -8; x <= 8; x++)
            {
                for (var y = -8; y <= 8; y++)
                    mapSys.SetTile(gridUid, map.Grid.Comp, new EntityCoordinates(gridUid, x + 0.5f, y + 0.5f), map.Tile.Tile);
            }

            // Тайл (0,0) со всех восьми сторон закрыт породой.
            for (var dx = -1; dx <= 1; dx++)
            {
                for (var dy = -1; dy <= 1; dy++)
                {
                    if (dx != 0 || dy != 0)
                        server.EntMan.SpawnEntity("WallRockBasalt", new EntityCoordinates(gridUid, dx + 0.5f, dy + 0.5f));
                }
            }
        });
        await pair.RunSeconds(0.5f);

        var grid = map.Grid.Comp;
        var gridEnt = map.Grid.Owner;
        Assert.That(rocks.IsOpen(gridEnt, grid, new Vector2i(0, 0)), "сам тайл пуст");
        Assert.That(rocks.IsOpen(gridEnt, grid, new Vector2i(1, 0)), Is.False, "в тайле породы нельзя");
        Assert.That(rocks.IsOpenRegion(gridEnt, grid, new Vector2i(0, 0), 20), Is.False, "замурованная щель: выхода нет");
        Assert.That(rocks.IsOpenRegion(gridEnt, grid, new Vector2i(5, 5), 20), "открытое место годится");

        // Кольцо вокруг щели никогда не отдаёт тайл в камне или в щели.
        await server.WaitPost(() =>
        {
            var random = server.Resolve<Robust.Shared.Random.IRobustRandom>();
            for (var i = 0; i < 40; i++)
            {
                var coords = rocks.FindDropTile(gridEnt, new Vector2(0.5f, 0.5f), 0f, 3f, random, minRegion: 20);
                Assert.That(coords, Is.Not.Null);
                var tile = server.System<SharedMapSystem>().TileIndicesFor(gridEnt, grid, coords!.Value);
                Assert.That(tile, Is.Not.EqualTo(new Vector2i(0, 0)), "в щель не садимся");
                Assert.That(rocks.IsOpen(gridEnt, grid, tile), "и не в камень");
            }
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>Моб волны, который слишком долго стоит на месте и рядом нет игроков, удаляется; в контейнере (в поде) нет.</summary>
    [Test]
    public async Task StuckWaveMobsAreRemoved()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { FailureLogLevel = LogLevel.Fatal });
        var server = pair.Server;
        var map = await pair.CreateTestMap();

        EntityUid stuck = default, patient = default;
        await server.WaitPost(() =>
        {
            stuck = server.EntMan.SpawnEntity("PursueTestMelee", map.GridCoords);
            var comp = server.EntMan.AddComponent<PursuePlayerComponent>(stuck);
            comp.StuckTime = TimeSpan.FromSeconds(3);

            // У этого терпение не кончается (как будто игрок рядом).
            patient = server.EntMan.SpawnEntity("PursueTestMelee", map.GridCoords.Offset(new Vector2(0.5f, 0.5f)));
            var comp2 = server.EntMan.AddComponent<PursuePlayerComponent>(patient);
            comp2.StuckTime = TimeSpan.FromSeconds(3);
            comp2.PlayerNearDistance = float.PositiveInfinity;
        });

        await pair.RunSeconds(8f);
        Assert.That(server.EntMan.Deleted(stuck), "застрявший моб без игроков рядом удалён");
        Assert.That(server.EntMan.Deleted(patient), Is.False, "моб рядом с игроком остаётся");

        await pair.CleanReturnAsync();
    }
}
