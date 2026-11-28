using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Content.Server._StarHorizon.Expedition;
using Content.Shared.Examine;
using Robust.Shared.GameObjects;
using Robust.Shared.Log;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Maths;

namespace Content.IntegrationTests.Tests._StarHorizon;

/// <summary>
/// Капсулы волн падают туда, откуда до игрока точно есть путь (если игрок в комплексе, то в комплекс), а мобы,
/// не нашедшие путь, идут, когда он освобождается.
/// </summary>
[TestFixture]
public sealed class ExpeditionDropSiteTest
{
    [TestPrototypes]
    private const string Prototypes = @"
- type: entity
  id: DropSiteTestPlayer
  components:
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

    /// <summary>
    /// Карта-грид как у планет: пол 40x30, закрытая комната комплекса x=10..20, y=-5..5 (стены по краю), игрок в ней.
    /// </summary>
    private static async Task<(EntityUid Map, HashSet<Vector2i> Room)> BuildRoom(Pair.TestPair pair, bool door = false)
    {
        var server = pair.Server;
        EntityUid mapUid = default;
        var room = new HashSet<Vector2i>();
        await server.WaitPost(() =>
        {
            var mapSys = server.System<SharedMapSystem>();
            mapUid = mapSys.CreateMap(out _);
            var grid = server.EntMan.EnsureComponent<MapGridComponent>(mapUid);
            var tile = new Tile(server.ResolveDependency<ITileDefinitionManager>()["FloorSteel"].TileId);
            for (var x = -10; x <= 30; x++)
            {
                for (var y = -15; y <= 15; y++)
                    mapSys.SetTile(mapUid, grid, new Vector2i(x, y), tile);
            }

            for (var x = 10; x <= 20; x++)
            {
                for (var y = -5; y <= 5; y++)
                {
                    room.Add(new Vector2i(x, y));
                    if (x == 10 || x == 20 || y == -5 || y == 5)
                        server.EntMan.SpawnEntity("WallSolid", new EntityCoordinates(mapUid, x + 0.5f, y + 0.5f));
                }
            }

            server.EntMan.EnsureComponent<ExpeditionDungeonTilesComponent>(mapUid).Tiles.UnionWith(room);
        });
        await pair.RunTicksSync(2);
        return (mapUid, room);
    }

    [Test]
    public async Task PodsLandInsideTheComplexWhereThePlayerIs()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Dirty = true, FailureLogLevel = LogLevel.Fatal });
        var server = pair.Server;
        var (map, room) = await BuildRoom(pair);
        var waves = server.System<ExpeditionWaveSystem>();

        await server.WaitAssertion(() =>
        {
            var grid = server.EntMan.GetComponent<MapGridComponent>(map);
            var mapSys = server.System<SharedMapSystem>();
            var taken = new HashSet<Vector2i>();
            for (var i = 0; i < 12; i++)
            {
                var coords = waves.FindReachableDropTile(map, new Vector2(15.5f, 0.5f), taken);
                Assert.That(coords, Is.Not.Null, "место нашлось");
                var tile = mapSys.TileIndicesFor(map, grid, coords!.Value);
                Assert.That(room.Contains(tile), $"капсула {tile} в комплексе, где игрок");
                Assert.That(tile.X > 10 && tile.X < 20 && tile.Y > -5 && tile.Y < 5, $"капсула {tile} внутри комнаты, а не в стене");
            }

            Assert.That(taken, Has.Count.EqualTo(12), "капсулы не падают друг на друга");
        });

        // Маркер в атласе: капсула падает на него.
        await server.WaitPost(() => server.EntMan.SpawnEntity("ExpeditionDropSiteMarker", new EntityCoordinates(map, 12.5f, 3.5f)));
        await server.WaitAssertion(() =>
        {
            var grid = server.EntMan.GetComponent<MapGridComponent>(map);
            var coords = waves.FindReachableDropTile(map, new Vector2(15.5f, 0.5f));
            Assert.That(server.System<SharedMapSystem>().TileIndicesFor(map, grid, coords!.Value), Is.EqualTo(new Vector2i(12, 3)), "на маркер");
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>Пути к игроку нет (сплошная стена), потом стена убрана: моб не стоит, а доходит.</summary>
    [Test]
    public async Task MobWalksOnceThePathIsFreed()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Dirty = true, FailureLogLevel = LogLevel.Fatal });
        var server = pair.Server;
        var xform = server.System<SharedTransformSystem>();
        var (map, _) = await BuildRoom(pair);

        EntityUid mob = default, player = default, gate = default;
        await server.WaitPost(() =>
        {
            // Игрок в закрытой комнате, моб снаружи; одна стена комнаты потом исчезнет.
            gate = server.EntMan.AllEntities<MetaDataComponent>().Select(e => e.Owner)
                .First(e => server.EntMan.GetComponent<MetaDataComponent>(e).EntityPrototype?.ID == "WallSolid"
                            && server.EntMan.GetComponent<TransformComponent>(e).MapUid == map
                            && (xform.GetWorldPosition(e) - new Vector2(10.5f, 0.5f)).Length() < 0.1f);
            player = server.EntMan.SpawnEntity("DropSiteTestPlayer", new EntityCoordinates(map, 15.5f, 0.5f));
            server.EntMan.AddComponent<Robust.Shared.Player.ActorComponent>(player);
            mob = server.EntMan.SpawnEntity("NFMobXeno", new EntityCoordinates(map, 2.5f, 0.5f));
            server.EntMan.EnsureComponent<PursuePlayerComponent>(mob).StuckTime = TimeSpan.FromMinutes(10);
            server.System<Content.Server.Damage.Systems.GodmodeSystem>().EnableGodmode(mob);
        });

        // Пути нет: моб доходит до стены и стоит.
        await pair.RunSeconds(8f);
        Assert.That((xform.GetWorldPosition(mob) - xform.GetWorldPosition(player)).Length(), Is.GreaterThan(4f), "пока стена, к игроку не пройти");

        // Стену убрали: моб должен дойти.
        await server.WaitPost(() => server.EntMan.DeleteEntity(gate));
        var reached = false;
        for (var i = 0; i < 30 && !reached; i++)
        {
            await pair.RunSeconds(1f);
            reached = (xform.GetWorldPosition(mob) - xform.GetWorldPosition(player)).Length() < 2.5f;
        }

        Assert.That(reached, $"моб дошёл после освобождения пути (позиция {xform.GetWorldPosition(mob)})");
        await pair.CleanReturnAsync();
    }
    /// <summary>
    /// Игрок далеко (дальше прямого поиска пути) в комнате комплекса со входом с дальней стороны: моб по ножкам пути
    /// обходит комнату и доходит, а не упирается в стену комнаты, стоящую на прямой к игроку.
    /// </summary>
    [Test]
    public async Task FarMobFindsTheWayIntoTheComplex()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Dirty = true, FailureLogLevel = LogLevel.Fatal });
        var server = pair.Server;
        var xform = server.System<SharedTransformSystem>();

        EntityUid mapUid = default, mob = default, player = default;
        await server.WaitPost(() =>
        {
            var mapSys = server.System<SharedMapSystem>();
            mapUid = mapSys.CreateMap(out _);
            var grid = server.EntMan.EnsureComponent<MapGridComponent>(mapUid);
            var tile = new Tile(server.ResolveDependency<ITileDefinitionManager>()["FloorSteel"].TileId);
            for (var x = -4; x <= 80; x++)
            {
                for (var y = -14; y <= 14; y++)
                    mapSys.SetTile(mapUid, grid, new Vector2i(x, y), tile);
            }

            // Комплекс x=40..64, y=-10..10, вход только с восточной стороны (x=64, y=-1..1); внутри перегородка.
            for (var x = 40; x <= 64; x++)
            {
                for (var y = -10; y <= 10; y++)
                {
                    var border = x == 40 || x == 64 || y == -10 || y == 10;
                    var inner = x == 52 && y <= 6;
                    if (!border && !inner || x == 64 && y >= -1 && y <= 1)
                        continue;
                    server.EntMan.SpawnEntity("WallSolid", new EntityCoordinates(mapUid, x + 0.5f, y + 0.5f));
                }
            }
        });
        await pair.RunSeconds(3f);

        await server.WaitPost(() =>
        {
            player = server.EntMan.SpawnEntity("DropSiteTestPlayer", new EntityCoordinates(mapUid, 46.5f, 0.5f));
            server.EntMan.AddComponent<Robust.Shared.Player.ActorComponent>(player);
            mob = server.EntMan.SpawnEntity("NFMobXeno", new EntityCoordinates(mapUid, 0.5f, 0.5f));
            server.EntMan.EnsureComponent<PursuePlayerComponent>(mob).StuckTime = TimeSpan.FromMinutes(10);
            server.System<Content.Server.Damage.Systems.GodmodeSystem>().EnableGodmode(mob);
        });

        var reached = false;
        for (var i = 0; i < 120 && !reached; i++)
        {
            await pair.RunSeconds(1f);
            reached = (xform.GetWorldPosition(mob) - xform.GetWorldPosition(player)).Length() < 2.5f;
        }

        Assert.That(reached, $"моб дошёл до игрока в комплексе (позиция {xform.GetWorldPosition(mob)})");
        await pair.CleanReturnAsync();
    }
    /// <summary>Игрок на севшем шаттле (другом гриде): капсулы врагов падают не на шаттл, а снаружи возле него.</summary>
    [Test]
    public async Task PodsLandNextToTheShuttleNotOnIt()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Dirty = true, FailureLogLevel = LogLevel.Fatal });
        var server = pair.Server;
        var xform = server.System<SharedTransformSystem>();
        var waves = server.System<ExpeditionWaveSystem>();

        EntityUid mapUid = default, shuttle = default;
        await server.WaitPost(() =>
        {
            var mapSys = server.System<SharedMapSystem>();
            mapUid = mapSys.CreateMap(out var mapId);
            var grid = server.EntMan.EnsureComponent<MapGridComponent>(mapUid);
            var tile = new Tile(server.ResolveDependency<ITileDefinitionManager>()["FloorSteel"].TileId);
            for (var x = -10; x <= 50; x++)
            {
                for (var y = -25; y <= 25; y++)
                    mapSys.SetTile(mapUid, grid, new Vector2i(x, y), tile);
            }

            // Шаттл 11x11 у (25..36, -5..6), вход с востока.
            var grid2 = server.ResolveDependency<IMapManager>().CreateGridEntity(mapId);
            shuttle = grid2.Owner;
            xform.SetWorldPosition(shuttle, new Vector2(25f, -5f));
            for (var x = 0; x <= 10; x++)
            {
                for (var y = 0; y <= 10; y++)
                {
                    mapSys.SetTile(shuttle, grid2.Comp, new Vector2i(x, y), tile);
                    var border = x == 0 || x == 10 || y == 0 || y == 10;
                    if (border && !(x == 10 && y >= 4 && y <= 6))
                        server.EntMan.SpawnEntity("WallSolid", new EntityCoordinates(shuttle, x + 0.5f, y + 0.5f));
                }
            }
        });
        await pair.RunTicksSync(3);

        await server.WaitAssertion(() =>
        {
            var taken = new HashSet<Vector2i>();
            var shuttleBox = new Box2(25f, -5f, 36f, 6f);
            for (var i = 0; i < 12; i++)
            {
                // Игрок внутри шаттла.
                var coords = waves.FindReachableDropTile(mapUid, new Vector2(28.5f, 0.5f), taken);
                Assert.That(coords, Is.Not.Null, "место нашлось");
                var world = xform.ToMapCoordinates(coords!.Value).Position;
                Assert.That(shuttleBox.Contains(world), Is.False, $"капсула {world} не на шаттле");
                Assert.That((world - new Vector2(30.5f, 0.5f)).Length(), Is.LessThan(25f), $"капсула {world} возле шаттла");
            }
        });

        await pair.CleanReturnAsync();
    }
    /// <summary>Лава (и пропасть) не место для капсулы: на ней мобы сгорают.</summary>
    [Test]
    public async Task PodsDoNotLandOnLava()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Dirty = true, FailureLogLevel = LogLevel.Fatal });
        var server = pair.Server;
        var xform = server.System<SharedTransformSystem>();
        var waves = server.System<ExpeditionWaveSystem>();

        EntityUid mapUid = default;
        await server.WaitPost(() =>
        {
            var mapSys = server.System<SharedMapSystem>();
            mapUid = mapSys.CreateMap(out _);
            var grid = server.EntMan.EnsureComponent<MapGridComponent>(mapUid);
            var tile = new Tile(server.ResolveDependency<ITileDefinitionManager>()["FloorBasalt"].TileId);
            for (var x = 0; x <= 30; x++)
            {
                for (var y = -15; y <= 15; y++)
                {
                    mapSys.SetTile(mapUid, grid, new Vector2i(x, y), tile);
                    // Лава всюду, кроме узкой полосы x=10..13 вокруг игрока.
                    if (x < 10 || x > 13)
                        server.EntMan.SpawnEntity("FloorLavaEntity", new EntityCoordinates(mapUid, x + 0.5f, y + 0.5f));
                }
            }
        });
        await pair.RunTicksSync(3);

        await server.WaitAssertion(() =>
        {
            var taken = new HashSet<Vector2i>();
            for (var i = 0; i < 15; i++)
            {
                var coords = waves.FindReachableDropTile(mapUid, new Vector2(11.5f, 0.5f), taken);
                Assert.That(coords, Is.Not.Null, "место нашлось");
                var x = (int) MathF.Floor(xform.ToMapCoordinates(coords!.Value).Position.X);
                Assert.That(x, Is.InRange(10, 13), $"капсула не на лаве (x={x})");
            }
        });

        await pair.CleanReturnAsync();
    }
}
