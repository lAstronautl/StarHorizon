using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Content.Server._StarHorizon.Expedition;
using Content.Server.NPC.Components;
using Content.Server.NPC.HTN;
using Content.Shared.NPC;
using Content.Shared.Movement.Components;
using Content.Shared.Salvage.Expeditions;
using Robust.Shared.GameObjects;
using Robust.Shared.Log;
using Robust.Shared.Map;

namespace Content.IntegrationTests.Tests._StarHorizon;

/// <summary>
/// Все мобы всех фракций экспедиций (как их спавнит волна: спавнеры раскрываются в настоящих мобов) преследуют
/// игрока, которого не видно: он за стеной, обходить её надо через проход.
/// </summary>
[TestFixture]
public sealed class ExpeditionFactionPursuitTest
{
    private static readonly string[] Factions =
    {
        "NFZombies", "NFXenos", "NFCarps", "NFFlesh", "NFArgocytes", "NFPunks", "NFDinosaurs", "NFMercenaries",
        "NFSyndicate", "NFExplorers", "NFSilicons", "NFCultists", "InfernoSilicons",
    };

    [TestPrototypes]
    private const string Prototypes = @"
- type: entity
  id: FactionPursuitTestPlayer
  components:
  - type: Eye
  - type: ContentEye
  - type: MobState
  - type: NpcFactionMember
    factions:
    - NanoTrasen
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

    /// <summary>Список фракций теста совпадает с фракциями экспедиций в прототипах: новые не пропущены.</summary>
    [Test]
    public async Task AllFactionsAreCovered()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { FailureLogLevel = LogLevel.Fatal });
        var ids = pair.Server.ProtoMan.EnumeratePrototypes<SalvageFactionPrototype>().Select(f => f.ID).OrderBy(x => x);
        Assert.That(ids, Is.EquivalentTo(Factions.OrderBy(x => x)));
        await pair.CleanReturnAsync();
    }

    /// <summary>
    /// На карте экспедиции мобы (и не из волн) не ломают стены: NavSmash выключается; на обычной карте не трогается.
    /// </summary>
    [Test]
    public async Task MobsDoNotSmashWallsOnExpeditions()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Dirty = true, FailureLogLevel = LogLevel.Fatal });
        var server = pair.Server;
        var expeditionMap = await pair.CreateTestMap();
        var otherMap = await pair.CreateTestMap();

        EntityUid onExpedition = default, elsewhere = default;
        await server.WaitPost(() =>
        {
            var expedition = server.EntMan.AddComponent<Content.Server.Salvage.Expeditions.SalvageExpeditionComponent>(expeditionMap.MapUid);
            expedition.MissionParams = new SalvageMissionParams();
            expedition.EndTime = TimeSpan.FromHours(1);
            onExpedition = server.EntMan.SpawnEntity("NFMobXeno", expeditionMap.GridCoords);
            elsewhere = server.EntMan.SpawnEntity("NFMobXeno", otherMap.GridCoords);
        });

        bool Smash(EntityUid uid) =>
            server.EntMan.GetComponent<HTNComponent>(uid).Blackboard.TryGetValue<bool>("NavSmash", out var smash, server.EntMan) && smash;

        Assert.That(Smash(elsewhere), "у ксеноса ломание стен включено по умолчанию");
        await pair.RunSeconds(2f);
        Assert.That(Smash(onExpedition), Is.False, "на экспедиции стены не ломает");
        Assert.That(Smash(elsewhere), "вне экспедиции не трогаем");

        await pair.CleanReturnAsync();
    }

    [TestCaseSource(nameof(Factions))]
    public async Task FactionMobsPursueAPlayerBehindAWall(string factionId)
    {
        // Без клиента: множество сложных мобов клиенту теста не нужно, погоня смотрит на ActorComponent «игрока».
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Dirty = true, FailureLogLevel = LogLevel.Fatal });
        var server = pair.Server;
        var xform = server.System<SharedTransformSystem>();
        var waves = server.System<ExpeditionWaveSystem>();
        var examine = server.System<Content.Shared.Examine.ExamineSystemShared>();

        // Карта сама является гридом, как у планет (там же работают ножки пути к далёкому игроку).
        // Пол 50x17; игрок в закрытой комнате (стены x=29..39, y=-4..4), вход только с дальней стороны (x=39, y=-1..1):
        // мобы подходят к комнате и не видят игрока, обходить её надо кругом. Вход в 3 клетки, чтобы не было пробки.
        EntityUid mapUid = default;
        await server.WaitPost(() =>
        {
            var mapSys = server.System<SharedMapSystem>();
            mapUid = mapSys.CreateMap(out _);
            var grid = server.EntMan.EnsureComponent<Robust.Shared.Map.Components.MapGridComponent>(mapUid);
            var tile = new Tile(server.ResolveDependency<Robust.Shared.Map.ITileDefinitionManager>()["FloorSteel"].TileId);
            for (var x = -4; x <= 46; x++)
            {
                for (var y = -8; y <= 8; y++)
                    mapSys.SetTile(mapUid, grid, new Robust.Shared.Maths.Vector2i(x, y), tile);
            }

            for (var x = 29; x <= 39; x++)
            {
                for (var y = -4; y <= 4; y++)
                {
                    var border = x == 29 || x == 39 || y == -4 || y == 4;
                    if (!border || x == 39 && y >= -1 && y <= 1)
                        continue;
                    server.EntMan.SpawnEntity("WallSolid", new EntityCoordinates(mapUid, x + 0.5f, y + 0.5f));
                }
            }
        });
        await pair.RunSeconds(4f);

        var mobs = new List<(EntityUid Uid, string Entry)>();
        EntityUid player = default;
        await server.WaitPost(() =>
        {
            player = server.EntMan.SpawnEntity("FactionPursuitTestPlayer", new EntityCoordinates(mapUid, 34.5f, 0.5f));
            server.EntMan.AddComponent<Robust.Shared.Player.ActorComponent>(player);

            var faction = server.ProtoMan.Index<SalvageFactionPrototype>(factionId);
            var i = 0;
            foreach (var entry in faction.MobGroups)
            {
                // По одному мобу на запись фракции (толпа крупных мобов сама себе устраивает пробку в проходе).
                for (var k = 0; k < 1; k++, i++)
                {
                    var coords = new EntityCoordinates(mapUid, 0.5f + i % 4 * 1.5f, -7.5f + i / 4 % 10 * 1.5f);
                    foreach (var mob in waves.SpawnWaveMobs(entry.Proto, coords))
                    {
                        // Турели и прочее неподвижное не ходят.
                        if (!server.EntMan.HasComponent<InputMoverComponent>(mob) || !server.EntMan.HasComponent<HTNComponent>(mob))
                            continue;

                        server.EntMan.EnsureComponent<PursuePlayerComponent>(mob).StuckTime = TimeSpan.FromMinutes(10);
                        // Хрупких мобов (боты АнКо) сбивают свои же стрелки: проверяем погоню, а не дружественный огонь.
                        server.System<Content.Server.Damage.Systems.GodmodeSystem>().EnableGodmode(mob);
                        mobs.Add((mob, entry.Proto));
                    }
                }
            }
        });

        Assert.That(mobs, Is.Not.Empty, $"у фракции {factionId} есть подвижные мобы");

        // Дошёл: вплотную или видит игрока на дистанции стрельбы (стоять за стеной в 10 м не считается).
        var reached = new HashSet<EntityUid>();
        var best = mobs.ToDictionary(m => m.Uid, _ => float.MaxValue);
        for (var t = 0; t < 90 && reached.Count < mobs.Count; t++)
        {
            await pair.RunSeconds(1f);
            await server.WaitPost(() =>
            {
                var playerPos = xform.GetWorldPosition(player);
                foreach (var (mob, _) in mobs)
                {
                    if (server.EntMan.Deleted(mob) || reached.Contains(mob))
                        continue;

                    var dist = (xform.GetWorldPosition(mob) - playerPos).Length();
                    best[mob] = MathF.Min(best[mob], dist);
                    var ranged = server.EntMan.GetComponent<HTNComponent>(mob).Blackboard.GetValueOrDefault<float>("RangedRange", server.EntMan);
                    if (dist <= 3f || dist <= ranged + 1f && examine.InRangeUnOccluded(mob, player, ranged + 1f))
                        reached.Add(mob);
                }
            });
        }

        var failures = new List<string>();
        foreach (var (mob, entry) in mobs)
        {
            if (server.EntMan.Deleted(mob))
            {
                failures.Add($"{entry}: удалён");
                continue;
            }

            if (reached.Contains(mob))
                continue;

            var proto = server.EntMan.GetComponent<MetaDataComponent>(mob).EntityPrototype?.ID;
            var htn = server.EntMan.GetComponent<HTNComponent>(mob);
            var steering = server.EntMan.TryGetComponent<NPCSteeringComponent>(mob, out var s)
                ? $"{s.Status} путь {s.CurrentPath.Count} к {s.Coordinates} дальность {s.Range}"
                : "нет";
            htn.Blackboard.TryGetValue<EntityCoordinates>("FollowTarget", out var follow, server.EntMan);
            htn.Blackboard.TryGetValue<EntityUid>("Target", out var combatTarget, server.EntMan);
            steering += $", FollowTarget {follow}, Target {combatTarget}, игрок {player}";
            var plan = htn.Plan == null ? "нет плана" : string.Join(">", htn.Plan.Tasks.Select(x => x.Operator.GetType().Name));
            failures.Add($"{entry} ({proto}): не дошёл (ближе всего {best[mob]:0.0} м), корень {htn.RootTask.Task}, план {plan}, steering {steering}, " +
                         $"позиция {xform.GetWorldPosition(mob)}, активен {server.EntMan.HasComponent<ActiveNPCComponent>(mob)}");
        }

        Assert.That(failures, Is.Empty, $"{factionId}:\n" + string.Join("\n", failures));
        await pair.CleanReturnAsync();
    }
}
