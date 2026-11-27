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
using Robust.Shared.GameObjects;
using Robust.Shared.Log;
using Robust.Shared.Map;
using Robust.Shared.Timing;

namespace Content.IntegrationTests.Tests._StarHorizon;

/// <summary>
/// Задание «Чёрный ящик»: ящик стоит в дальней комнате данжа, активация вызывает волну, данные качаются, только пока
/// игрок в зоне ящика; интерфейс показывает «активировать» и процент скачивания с паузой.
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
    public async Task BlackBoxDataIsDownloadedInTheZone()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { FailureLogLevel = LogLevel.Fatal, Connected = true, Dirty = true });
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var salvage = server.System<SalvageSystem>();
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

        // 1. Ящик стоит в данже (закреплён), далеко от посадки; зона ещё не активна.
        var retrieval = server.EntMan.GetComponent<SalvageRetrievalExpeditionComponent>(expeditionMap);
        Assert.That(retrieval.Box, Is.Not.Null, "ящик поставлен");
        var box = retrieval.Box!.Value;
        var boxXform = server.EntMan.GetComponent<TransformComponent>(box);
        Assert.That(boxXform.MapUid, Is.EqualTo(expeditionMap));
        Assert.That(boxXform.Anchored, "ящик: структура, закреплён");
        var boxPos = xform.GetWorldPosition(box);
        Assert.That((boxPos - expedition.LandingCoordinates).Length(), Is.GreaterThan(10f), "ящик в глубине данжа, не у посадки");
        var zone = server.EntMan.GetComponent<ExpeditionBlackBoxZoneComponent>(box);
        Assert.That(zone.Active, Is.False);

        EntityUid player = default;
        await server.WaitPost(() =>
        {
            player = server.EntMan.SpawnEntity("BlackBoxTestPlayer", new EntityCoordinates(expeditionMap, expedition.LandingCoordinates));
            server.PlayerMan.SetAttachedEntity(server.PlayerMan.Sessions.First(), player, out _, true);
        });
        await pair.RunSeconds(2f);
        var hud = server.EntMan.GetComponent<ExpeditionHudComponent>(expeditionMap);
        Assert.That(hud.Objectives.Select(o => o.Kind), Is.EqualTo(new[] { ExpeditionObjectiveKind.Activate, ExpeditionObjectiveKind.Download }));
        Assert.That(hud.Objectives.All(o => o.Done == 0));

        // 2. Игрок рядом активирует ящик: зона включилась, сразу пришла волна, данные качаются.
        await server.WaitPost(() =>
        {
            xform.SetWorldPosition(player, boxPos + new Vector2(1f, 0));
            server.EntMan.EventBus.RaiseLocalEvent(box, new InteractHandEvent(player, box));
        });
        await pair.RunSeconds(3f);
        Assert.That(retrieval.Started, "ящик активирован");
        Assert.That(zone.Active, "зона включена");
        Assert.That(zone.Occupied, "игрок в зоне");
        Assert.That(server.EntMan.Count<PursuePlayerComponent>(), Is.GreaterThan(0), "сигнал засекли: пришла волна");
        Assert.That(retrieval.Progress, Is.GreaterThan(TimeSpan.Zero), "данные качаются");
        Assert.That(hud.Objectives[0].Finished);
        Assert.That(hud.Objectives[1].Done, Is.GreaterThan(0));

        // 3. Игрок ушёл из зоны: пауза, прогресс стоит.
        await server.WaitPost(() => xform.SetWorldPosition(player, boxPos + new Vector2(zone.Radius + 4f, 0)));
        await pair.RunSeconds(1.5f);
        var paused = retrieval.Progress;
        await pair.RunSeconds(3f);
        Assert.That(retrieval.Paused, "в зоне никого: пауза");
        Assert.That(retrieval.Progress, Is.EqualTo(paused), "прогресс стоит");
        Assert.That(hud.Objectives[1].Paused, "интерфейс показывает паузу");
        Assert.That(zone.Occupied, Is.False);

        // 4. Вернулся: скачивание продолжается и заканчивается, задание выполнено.
        await server.WaitPost(() =>
        {
            xform.SetWorldPosition(player, boxPos + new Vector2(1f, 0));
            retrieval.Progress = retrieval.Duration - TimeSpan.FromSeconds(1);
        });
        await pair.RunSeconds(3f);
        Assert.That(retrieval.Done, "данные скачаны");
        Assert.That(expedition.Completed, "задание выполнено");
        Assert.That(zone.Active, Is.False, "зона выключена");
        Assert.That(hud.Objectives.All(o => o.Finished), "в интерфейсе обе задачи выполнены");

        await pair.CleanReturnAsync();
    }
}
