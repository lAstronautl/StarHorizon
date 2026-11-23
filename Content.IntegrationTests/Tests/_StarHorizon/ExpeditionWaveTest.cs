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

        // 3. Волна приходит: мобы фракции вокруг игрока, у каждого маркер погони.
        await server.WaitPost(() => waves.NextWave = timing.CurTime);
        await pair.RunSeconds(1.5f);
        var mobs = Pursuers(server);
        Assert.That(mobs, Is.Not.Empty, "волна пришла");
        Assert.That(mobs.Count, Is.LessThanOrEqualTo(waves.MaxAliveWaveMobs));
        var playerPos = xform.GetWorldPosition(player);
        foreach (var mob in mobs)
        {
            Assert.That(server.EntMan.GetComponent<TransformComponent>(mob).MapUid, Is.EqualTo(expeditionMap));
            var dist = (xform.GetWorldPosition(mob) - playerPos).Length();
            Assert.That(dist, Is.InRange(waves.RingMin - 2f, waves.RingMax + 2f), "кольцо вокруг игрока");
        }

        // 4. Мобы знают, где игрок (FollowTarget) и сменили корень ИИ на погоню.
        await pair.RunSeconds(1.5f);
        foreach (var mob in mobs.Where(server.EntMan.EntityExists))
        {
            var htn = server.EntMan.GetComponent<HTNComponent>(mob);
            Assert.That(htn.RootTask.Task, Does.StartWith("Pursue"));
            Assert.That(htn.Blackboard.TryGetValue<EntityCoordinates>(NPCBlackboard.FollowTarget, out var target, server.EntMan), "знает цель");
            Assert.That(target.EntityId, Is.EqualTo(player));
        }

        // 5. Мобы идут к игроку: ближайший заметно приблизился.
        var startMin = mobs.Min(m => (xform.GetWorldPosition(m) - playerPos).Length());
        await pair.RunSeconds(15f);
        var endMin = mobs.Where(server.EntMan.EntityExists).Min(m => (xform.GetWorldPosition(m) - xform.GetWorldPosition(player)).Length());
        Assert.That(endMin, Is.LessThan(startMin - 3f), "мобы идут к игроку");

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
}
